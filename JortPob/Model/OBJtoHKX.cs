using HKLib.hk2018;
using HKLib.Reflection.hk2018;
using HKLib.Serialization.hk2018.Binary;
using HKLib.Serialization.hk2018.Xml;
using JortPob.Common;
using SoulsFormats;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

/* Code here is courtesy of Dropoff */
/* Also uses some stuff by Hork & 12th I think */
/* This is a modified version of ER_OBJ2HKX */
namespace JortPob.Model
{
    partial class ModelConverter
    {
        private static readonly ConcurrentDictionary<string, HavokTypeRegistry> registryCache = new();

        public static void OBJtoHKX(string objPath, string hkxPath)
        {
            string toolsDir = $"{AppDomain.CurrentDomain.BaseDirectory}Resources\\tools\\ER_OBJ2HKX\\";
            DirectoryInfo tempDir = CreateTempDirectory(toolsDir); // directory for all the intermediate files

            /* Convert obj to hkx */
            byte[] hkx = ObjToHkx(toolsDir, tempDir.FullName, objPath);
            hkx = UpgradeHKX(toolsDir, hkx, objPath);
            File.WriteAllBytes(hkxPath, hkx);
            
            /* Delete temp files */
            tempDir.Delete(true);
        }

        private static byte[] ObjToHkx(string toolsDir, string tempDir, string objPath)
        {
            string fName = Path.GetFileNameWithoutExtension(objPath);

            File.Copy(objPath, @$"{tempDir}\{fName}.obj", true);

            string srcDir = Path.GetDirectoryName(objPath);
            File.Copy(Utility.ResourcePath("misc\\havok.mtl"), @$"{tempDir}\{fName}.mtl", true);

            ProcessStartInfo startInfo = new(@$"{toolsDir}\obj2fsnp.exe", $"\"{tempDir}\\{fName}.obj\"")
            {
                WorkingDirectory = @$"{tempDir}\",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            Utility.ExecuteProcess(startInfo, 60000);

            startInfo = new(@$"{toolsDir}\AssetCc2_fixed.exe", $"--strip \"{tempDir}\\{fName}.obj.o2f\" \"{tempDir}\\{fName}.1\"")
            {
                WorkingDirectory = @$"{tempDir}\",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            Utility.ExecuteProcess(startInfo, 60000);

            startInfo = new(@$"{toolsDir}\hknp2fsnp.exe", $"\"{tempDir}\\{fName}.1\"")
            {
                WorkingDirectory = @$"{tempDir}\",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            Utility.ExecuteProcess(startInfo, 60000);

            return File.ReadAllBytes($@"{tempDir}\{fName}.1.hkx");  
        }

        private static byte[] UpgradeHKX(string toolsDir, byte[] bytes, string objPath)
        {
            var des = new HKX2.PackFileDeserializer();
            var root = (HKX2.hkRootLevelContainer)des.Deserialize(new BinaryReaderEx(false, bytes));

            hkRootLevelContainer hkx = HkxUpgrader.UpgradehkRootLevelContainer(root);
            HavokTypeRegistry registry = GetTypeRegistryForDirectory(toolsDir);

            /* Absolute garbage code fix for materials */
            /* Somewhere in the process of dropoff -> 12av -> hork code chain the material ids get mutilated and so I have to repair them at the end */
            /* This sucks but it is what it is. Hork code is a black box so I can't debug it. */
            List<Obj.CollisionMaterial> source = Obj.GetMaterials(objPath);  // grab source materials from obj file
            List<HKLib.hk2018.fsnpCustomMeshParameter.PrimitiveData> mats =
                ((HKLib.hk2018.fsnpCustomParamCompressedMeshShape)((HKLib.hk2018.hknpPhysicsSceneData)hkx.m_namedVariants[0].m_variant).m_systemDatas[0].m_bodyCinfos[0].m_shape).m_pParam.m_primitiveDataArray;
            if (mats.Count > source.Count) { Lort.Log($"Mismatch in HKX hitmrtl repair: {Path.GetFileNameWithoutExtension(objPath)}.obj", Lort.Type.Debug); }
            for(int i=0;i<mats.Count;i++)
            {
                mats[i].m_materialNameData = ((uint)source[i]); // fixed i guess!
            }

            /* Vanilla asset collision materials are shared (m_isExclusive == 0). The obj2fsnp -> AssetCc2 -> hknp2fsnp chain */
            /* emits m_isExclusive == 1, which makes the engine allocate a fresh hknpMaterialLibrary entry for every asset */
            /* instance and never free it. The library uses 16-bit ids (fuck u FromSoft), so after 65536 instances it corrupts */
            /* itself and the game crashes in hknpMaterialLibrary::addEntry (strcmp on a garbage entry). Clearing the flag lets */
            /* identical materials dedupe into one shared library entry. */
            HKLib.hk2018.hknpPhysicsSceneData sceneData = (HKLib.hk2018.hknpPhysicsSceneData)hkx.m_namedVariants[0].m_variant;
            foreach (HKLib.hk2018.hknpPhysicsSystemData systemData in sceneData.m_systemDatas)
            {
                foreach (HKLib.hk2018.hknpMaterial material in systemData.m_materials)
                {
                    material.m_isExclusive = 0;
                }
            }

            HavokBinarySerializer binarySerializer = new(registry);
            HavokXmlSerializer xmlSerializer = new(registry);
            using (MemoryStream ms = new MemoryStream())
            {
                if(Const.DEBUG_HKX_FORCE_BINARY)
                {
                    binarySerializer.Write(hkx, ms);  // bad ending
                    bytes = ms.ToArray();
                }
                else
                {
                    xmlSerializer.Write(hkx, ms);   // good ending
                    bytes = ms.ToArray();
                }
            }
            return bytes;
        }

        /**
         * Helper to ensure we only need to initialize the Havok registry once per temp directory.
         */
        private static HavokTypeRegistry GetTypeRegistryForDirectory(string tempDir)
        {
            return registryCache.GetOrAdd(
                Path.Combine(tempDir, "HavokTypeRegistry20180100.xml"),
                HavokTypeRegistry.Load
            );
        }

        /**
         * Helper to generate a reasonably unique temp directory nested in a given directory.
         * We can probably switch to using the built-in `GetTempPath` in the future, but
         * this allows us to specify a parent.
         *
         * Returns a reference to the created directory.
         */
        private static DirectoryInfo CreateTempDirectory(string parentDir)
        {
            const int maxRetries = 10;
            int attempt = 0;
            string tempPath;

            do
            {
                if (attempt >= maxRetries)
                {
                    throw new ApplicationException("Exceeded maximum number of retries creating a temp directory.");
                }

                string randomId = System.Guid.NewGuid().ToString();
                tempPath = Path.Combine(parentDir, randomId);
                ++attempt;
            } while (Path.Exists(tempPath));

            return Directory.CreateDirectory(tempPath);
        }
    }
}
