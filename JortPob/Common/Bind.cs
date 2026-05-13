using SoulsFormats;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JortPob.Common
{
    public class Bind
    {
        public static void BindMaterials(string outPath)
        {
            BND4 bnd = BND4.Read(Utility.ResourcePath(@$"matbins\allmaterial.matbinbnd.dcx"));

            /* Grab all matbin files */
            string[] fileList = Directory.GetFiles(Path.Combine(Const.CACHE_PATH, "materials"));
            int i = 15102; // appending our new file indexes after all the base game ones

            Lort.Log($"Binding {fileList.Length} materials...", Lort.Type.Main);
            Lort.NewTask("Binding Materials", fileList.Length);

            foreach (string file in fileList)
            {
                MATBIN matbin = MATBIN.Read(file);
                BinderFile bind = new();
                bind.ID = ++i;
                bind.Name = @$"N:\GR\data\INTERROOT_win64\material\matbin\Morrowind\matxml\{Path.GetFileNameWithoutExtension(file)}.matbin";
                bind.Bytes = matbin.Write();
                bnd.Files.Add(bind);
                Lort.TaskIterate();
            }

            bnd.Write(outPath);
        }

        /* Binds all assets to correct asset directories */
        public static void BindAssets(Cache cache)
        {
            cache.assets.AsParallel()
                .WithDegreeOfParallelism(Const.THREAD_COUNT)
                .ForAll(modelInfo =>
                {
                    BindAsset(modelInfo, Path.Combine(Const.OUTPUT_PATH, $@"asset\aeg\{modelInfo.AssetPath()}.geombnd.dcx"));
                    Lort.TaskIterate(); // Progress bar update
                });
        }

        /* Bind all emitter assets */
        public static void BindPickables(Cache cache)
        {
            foreach (PickableInfo pickable in cache.GetPickables())
            {
                string outPath = Path.Combine(Const.OUTPUT_PATH, $@"asset\aeg\{pickable.AssetPath()}.geombnd.dcx");

                // Bind up emitter asset flver
                {
                    BND4 bnd = new();
                    bnd.Compression = Compression.KRAK();
                    bnd.Version = "07D7R6";

                    FLVER2 flver = FLVER2.Read(Path.Combine(Const.CACHE_PATH, pickable.model.path));

                    BinderFile file = new();
                    file.ID = 200;
                    file.Name = $@"N:\GR\data\INTERROOT_win64\asset\aeg\{pickable.AssetPath()}\sib\{pickable.AssetName()}.flver";
                    file.Bytes = flver.Write();

                    bnd.Files.Add(file);
                    bnd.Write(outPath);
                }
            }
        }

        /* Bind all emitter assets */
        public static void BindEmitters(Cache cache)
        {
            foreach(EmitterInfo emitterInfo in cache.emitters)
            {
                string outPath = Path.Combine(Const.OUTPUT_PATH, @$"asset\aeg\{emitterInfo.AssetPath()}.geombnd.dcx");

                // Bind up emitter asset flver
                {
                    BND4 bnd = new();
                    bnd.Compression = Compression.KRAK();
                    bnd.Version = "07D7R6";

                    FLVER2 flver = FLVER2.Read(Path.Combine(Const.CACHE_PATH, emitterInfo.model.path));

                    BinderFile file = new();
                    file.ID = 200;
                    file.Name = @$"N:\GR\data\INTERROOT_win64\asset\aeg\{emitterInfo.AssetPath()}\sib\{emitterInfo.AssetName()}.flver";
                    file.Bytes = flver.Write();

                    bnd.Files.Add(file);
                    bnd.Write(outPath);
                }
            }
        }

        public static void BindAsset(ModelInfo modelInfo, string outPath)
        {
            // Bind up asset flver
            {
                BND4 bnd = new();
                bnd.Compression = Compression.KRAK();
                bnd.Version = "07D7R6";

                FLVER2 flver = FLVER2.Read(Path.Combine(Const.CACHE_PATH, modelInfo.path));

                BinderFile file = new();
                file.ID = 200;
                file.Name = @$"N:\GR\data\INTERROOT_win64\asset\aeg\{modelInfo.AssetPath()}\sib\{modelInfo.AssetName()}.flver";
                file.Bytes = flver.Write();

                bnd.Files.Add(file);
                bnd.Write(outPath);
            }

            // If this asset has collision we bind that up as well
            if(modelInfo.collision != null)
            {
                BND4 bnd = new();
                bnd.Compression = Compression.KRAK();
                bnd.Version = "07D7R6";

                BinderFile file = new();
                file.ID = 300;
                file.Name = @$"N:\GR\data\INTERROOT_win64\asset\aeg\{modelInfo.AssetPath().ToUpper()}\hkx_L\{modelInfo.AssetName().ToUpper()}_L.hkx";
                file.Bytes = File.ReadAllBytes(Path.Combine(Const.CACHE_PATH, modelInfo.collision.hkx));

                bnd.Files.Add(file);
                bnd.Write(outPath.Replace(".geombnd.dcx", "_l.geomhkxbnd.dcx"));
            }
        }

        public static void BindAsset(LiquidInfo waterInfo, string outPath)
        {
            // Bind up asset flver
            {
                BND4 bnd = new();
                bnd.Compression = Compression.KRAK();
                bnd.Version = "07D7R6";

                FLVER2 flver = FLVER2.Read(Path.Combine(Const.CACHE_PATH, waterInfo.path));

                BinderFile file = new();
                file.ID = 200;
                file.Name = @$"N:\GR\data\INTERROOT_win64\asset\aeg\{waterInfo.AssetPath()}\sib\{waterInfo.AssetName()}.flver";
                file.Bytes = flver.Write();

                bnd.Files.Add(file);
                bnd.Write(outPath);
            }
        }
        public static void BindTPF(Cache cache, List<int> commons)
        {
            /* Collect all textures, kind of brute force, could optimize later */
            List<TextureInfo> textures = new();
            bool TextureExists(TextureInfo t)
            {
                foreach (TextureInfo tex in textures)
                {
                    if (t.name == tex.name) { return true; }
                }
                return false;
            }

            foreach (ModelInfo mod in cache.assets)
            {
                foreach(TextureInfo tex in mod.textures)
                {
                    if (TextureExists(tex)) { continue; }
                    textures.Add(tex);
                }
            }

            foreach(TerrainInfo terrain in cache.terrains)
            {
                foreach(TextureInfo tex in terrain.textures)
                {
                    if (TextureExists(tex)) { continue; }
                    textures.Add(tex);
                }
            }

            Lort.Log($"Binding {textures.Count()} textures...", Lort.Type.Main);
            Lort.NewTask("Binding Textures", textures.Count());

            /* Bind all textures */
            BXF4 tpfbdt = new();
            tpfbdt.Version = "07D7R6";
            int index = 0;
            foreach (TextureInfo tex in textures)
            {
                // tex file
                {
                    TPF tpf = TPF.Read(Path.Combine(Const.CACHE_PATH, tex.path));
                    BinderFile bf = new();
                    bf.CompressionInfo = Compression.NONE();
                    bf.ID = index++;
                    bf.Name = $"{tex.name}.tpf.dcx";
                    bf.Bytes = tpf.Write();
                    tpfbdt.Files.Add(bf);
                }
                // low detail tex file
                {
                    TPF tpf = TPF.Read(Path.Combine(Const.CACHE_PATH, tex.low));
                    BinderFile bf = new();
                    bf.CompressionInfo = Compression.NONE();
                    bf.ID = index++;
                    bf.Name = $"{tex.name}_l.tpf.dcx";
                    bf.Bytes = tpf.Write();
                    tpfbdt.Files.Add(bf);
                }
                Lort.TaskIterate();
            }

            /* Write bind */
            foreach (int common in commons) {
                string outPath = Path.Combine(Const.OUTPUT_PATH, @$"map\m{common.ToString("D2")}\common\m{common.ToString("D2")}_0000");
                tpfbdt.Write($"{outPath}.tpfbhd", $"{outPath}.tpfbdt");
            }
        }
    }
}
