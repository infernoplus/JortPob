using JortPob.Common;
using SoulsFormats;
using SoulsFormats.KF4;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace JortPob.Model
{
    public class MaterialContext
    {

        public XmlDocument xmlMaterialInfo;
        public XmlNodeList xmlMaterials, xmlExamples;

        public MaterialContext()
        {
            xmlMaterialInfo = new();
            xmlMaterialInfo.Load(Utility.ResourcePath(@"matbins\MaterialInfo.xml"));

            xmlMaterials = xmlMaterialInfo.ChildNodes[1].ChildNodes[0].ChildNodes;
            xmlExamples = xmlMaterialInfo.ChildNodes[1].ChildNodes[2].ChildNodes;

            genTextures = new();
            genMATBINs = new();
        }

        /* If isStatic is true we will skip any bufferlayouts with boneweights/indicies in them */
        private FLVER2.BufferLayout GetLayout(string mtd, bool isStatic)
        {
            string Standardize(string guh)
            {
                return guh.ToLower().Replace(".mtd", "").Replace(".matxml", "");
            }

            foreach (XmlElement xmlElement in xmlMaterials)
            {
                string attrMtd = xmlElement.GetAttribute("mtd");
                if (Standardize(attrMtd) == Standardize(mtd))
                {
                    foreach(XmlElement xmlBuffer in xmlElement.ChildNodes[0].ChildNodes)
                    {
                        if (isStatic && (xmlBuffer.InnerXml.Contains("BoneIndices") || xmlBuffer.InnerXml.Contains("BoneWeights"))) { continue; }
                        FLVER2.BufferLayout layout = new();

                        foreach(XmlElement xmlBufferValue in xmlBuffer.ChildNodes[0].ChildNodes)
                        {
                            string type = xmlBufferValue.Name;
                            string semantic = xmlBufferValue.InnerText;
                            int index = 0;
                            if(semantic.EndsWith("]")) {
                                index = int.Parse(semantic.Substring(semantic.Length - 2, 1));
                                semantic = semantic.Substring(0, semantic.Length - 3);
                            }

                            FLVER.LayoutType layoutType = Enum.Parse<FLVER.LayoutType>(type);
                            FLVER.LayoutSemantic layoutSemantic = Enum.Parse<FLVER.LayoutSemantic>(semantic);

                            FLVER.LayoutMember member = new(layoutType, layoutSemantic, index, 0, 0);
                            layout.Add(member);
                        }

                        return layout;
                    }
                }
            }

            Lort.Log($"## ERROR ## Failed to find bufferlayout for {mtd}", Lort.Type.Debug);
            return null;
        }

        /* Currently selects first example of a valid GXList for a given material. It might be a good idea to build a better system for this. */
        private FLVER2.GXList GetGXList(string mtd)
        {
            string Standardize(string guh)
            {
                return guh.ToLower().Replace(".mtd", "").Replace(".matxml", "");
            }

            foreach (XmlElement xmlElement in xmlExamples)
            {
                string attrMtd = xmlElement.GetAttribute("mtd");
                if (Standardize(attrMtd) == Standardize(mtd))
                {

                    FLVER2.GXList gxlist = new();

                    foreach(XmlElement xmlItem in xmlElement.ChildNodes[0].ChildNodes[0].ChildNodes)
                    {
                        string id = xmlItem.GetAttribute("id");
                        string unk04 = xmlItem.GetAttribute("unk04");
                        byte[] data = xmlItem.InnerText.Split().Select(t => byte.Parse(t, NumberStyles.AllowHexSpecifier)).ToArray();

                        FLVER2.GXItem gxitem = new();
                        gxitem.ID = id;
                        gxitem.Unk04 = int.Parse(unk04);
                        gxitem.Data = data;

                        gxlist.Add(gxitem);
                    }

                    return gxlist;
                }
            }

            Lort.Log($"## ERROR ## Failed to find gxlists for {mtd}", Lort.Type.Debug);
            return null;
        }

        /* Currently copies the first example of the given material. It might be a good idea to build a better system for this. */
        private FLVER2.Material GetMaterial(string mtd, int index)
        {
            string Standardize(string guh)
            {
                return guh.ToLower().Replace(".mtd", "").Replace(".matxml", "");
            }

            foreach (XmlElement xmlElement in xmlExamples)
            {
                string attrMtd = xmlElement.GetAttribute("mtd");
                if (Standardize(attrMtd) == Standardize(mtd))
                {
                    XmlElement xmlMaterial = (XmlElement)xmlElement.ChildNodes[0];
                    FLVER2.Material material = new();

                    material.MTD = mtd;
                    material.Name = xmlMaterial.GetAttribute("name");
                    material.Index = index;
                    material.GXIndex = index;

                    foreach(XmlElement xmlTexture in xmlMaterial.ChildNodes[1].ChildNodes)
                    {
                        FLVER2.Texture texture = new();

                        string unk10 = xmlTexture.GetAttribute("unk10");
                        string unk11 = xmlTexture.GetAttribute("unk11");
                        string unk14 = xmlTexture.GetAttribute("unk14");
                        string unk18 = xmlTexture.GetAttribute("unk18");
                        string unk1c = xmlTexture.GetAttribute("unk1C");

                        string[] scale = xmlTexture.GetAttribute("scale").Split(",");

                        texture.Type = xmlTexture.GetAttribute("type");
                        texture.Path = xmlTexture.GetAttribute("path");
                        texture.Unk10 = byte.Parse(unk10);
                        texture.Unk11 = bool.Parse(unk11);
                        texture.Unk14 = float.Parse(unk14);
                        texture.Unk18 = float.Parse(unk18);
                        texture.Unk1C = float.Parse(unk1c);
                        texture.Scale = new System.Numerics.Vector2(float.Parse(scale[0]), float.Parse(scale[1]));

                        material.Textures.Add(texture);
                    }

                    return material;
                }

            }

            Lort.Log($"## ERROR ## Failed to find {mtd}", Lort.Type.Debug);
            return null;
        }

        /* For the materialcontexts lifetime it collects a list of textures and matbins used by models being converted. */
        /* Once we are done converting models the write() method for the material context should be called and it will write all tpfs and matbins and bind them up into the appropriate bnds */
        /* The reason we collect everything and then write at the end is so we can reuse tps/matbins easily and so we can easily make the bnds at the end of the process */
        public ConcurrentDictionary<string, MATBIN> genMATBINs;   // template id + texture, matbin
        public ConcurrentDictionary<string, string> genTextures;  // original texture path, tpf output

        /* List of template matbins which we use as a base for our custom materials */
        /* filenames for templates stored without extension or path. files are in Resources/matbins and the extension is .matbin but is stored as.matxml in flvers so guh */
        /* template matbins are stored with their original filename so that the lookup through the MaterialInfo.xml still matches to it correctly. makes life easier! */
        public Dictionary<string, string> matbinTemplates = new() {
            { "static[a]opaque", "AEG006_030_ID001" },      // simple opaque albedo material
            { "static[a]alpha", null },                     // same but alpha tested
            { "static[a]transparent", null },               // same but transparent
            { "static[a]multi[2]", "m10_00_027" },          // blendy multimaterial for terrain
            { "static[a]multi[3]", "m10_00_022" },          // VERY blendy multimaterial for terrain m10_00_022 best so far
            { "static[a]overlay", "m10_00_311" },      // used for terrain vertex color masking m10_00_003 best so far
            { "static[x]water", "Field_sea_05"}          // water shader, does not take any input textures
        };

        /* Create a full suite of a custom matbin, textures, and layout/gx/material info for a flver and return them all in a container */
        /* This method will make guesstimations based on the texture files about what type of material to use. For example, if the material has an alpha channel we will make it transparent */
        public List<MaterialInfo> GenerateMaterials(List<SharpAssimp.Material> sourceMaterials)
        {
            int index = 0;
            List<MaterialInfo> materialInfo = new();
            foreach (SharpAssimp.Material sourceMaterial in sourceMaterials)
            {
                // @TOOD: currently writing materials 1 to 1 but should probably check material usage and cull materials that are not needed like collision material
                string diffuseTextureSourcePath = sourceMaterial.TextureDiffuse.FilePath != null ? sourceMaterial.TextureDiffuse.FilePath : Utility.ResourcePath(@"textures\tx_missing.dds");
                string diffuseTexture;
                if (genTextures.ContainsKey(diffuseTextureSourcePath))
                {
                    diffuseTexture = genTextures.GetValueOrDefault(diffuseTextureSourcePath);
                }
                else
                {
                    diffuseTexture = Utility.PathToFileName(diffuseTextureSourcePath);
                    genTextures.TryAdd(diffuseTextureSourcePath, diffuseTexture);
                }

                string matbinTemplate = matbinTemplates["static[a]opaque"];   // @TODO: actually look at values of textures and determine template type
                string matbinName = diffuseTexture.StartsWith("tx_") ? $"mat_{diffuseTexture.Substring(3)}" : $"mat_{diffuseTexture}";

                MATBIN matbin;
                string matbinkey = $"{matbinTemplate}::{matbinName}";
                if (genMATBINs.ContainsKey(matbinkey))
                {
                    matbin = genMATBINs.GetValueOrDefault(matbinkey);
                }
                else
                {
                    matbin = MATBIN.Read(Utility.ResourcePath($"matbins\\{matbinTemplate}.matbin"));
                    matbin.Samplers[0].Path = $"{diffuseTexture}";
                    matbin.SourcePath = $"{matbinName}.matxml";
                    genMATBINs.TryAdd(matbinkey, matbin);
                }

                FLVER2.BufferLayout layout = GetLayout($"{matbinTemplate}.matxml", true);
                FLVER2.GXList gx = GetGXList($"{matbinTemplate}.matxml");
                FLVER2.Material material = GetMaterial($"{matbinTemplate}.matxml", index++);
                material.MTD = matbin.SourcePath;
                material.Name = $"{matbinName}";

                List<TextureInfo> info = new();
                info.Add(new(diffuseTexture, $"textures\\{diffuseTexture}.tpf.dcx"));

                materialInfo.Add(new MaterialInfo(material, gx, layout, matbin, info));
            }
            return materialInfo;
        }

        /* Same as above but for terrain */
        public List<MaterialInfo> GenerateMaterials(Landscape landscape)
        {
            int index = 0;
            List<MaterialInfo> materialInfo = new();
            foreach (Landscape.Mesh mesh in landscape.meshes)
            {
                switch (mesh.shader)
                {
                    case "static[a]opaque":
                        materialInfo.Add(GenerateMaterialSingle(mesh, index++));
                        break;
                    case "static[a]multi[2]":
                        materialInfo.Add(GenerateMaterialMulti2(mesh, index++));
                        break;
                    case "static[a]multi[3]":
                        materialInfo.Add(GenerateMaterialMulti3(mesh, index++));
                        break;
                    case "static[a]overlay":
                        materialInfo.Add(GenerateMaterialOverlay(mesh, index++));
                        break;
                    default:
                        Lort.Log($" ## ERROR ## INVALID MATERIAL TYPE DESIGNATOR ??? [{mesh.shader}] ", Lort.Type.Debug);
                        break;
                }
            }
            return materialInfo;
        }

        private MaterialInfo GenerateMaterialSingle(Landscape.Mesh mesh, int index)
        {
            string diffuseTextureSourcePathA = mesh.textures[0].path;
            string diffuseTextureA;
            string AddTexture(string diffuseTextureSourcePath)
            {
                if (genTextures.ContainsKey(diffuseTextureSourcePath))
                {
                    return genTextures.GetValueOrDefault(diffuseTextureSourcePath);
                }
                else
                {
                    string n = Utility.PathToFileName(diffuseTextureSourcePath);
                    genTextures.TryAdd(diffuseTextureSourcePath, n);
                    return n;
                }
            }
            diffuseTextureA = AddTexture(diffuseTextureSourcePathA);

            string matbinTemplate = matbinTemplates["static[a]opaque"];
            string matbinName = "mat_landscape_";
            matbinName += diffuseTextureA.StartsWith("tx_") ? diffuseTextureA.Substring(3) : diffuseTextureA;

            MATBIN matbin;
            string matbinkey = $"{matbinTemplate}::{matbinName}";
            if (genMATBINs.ContainsKey(matbinkey))
            {
                matbin = genMATBINs.GetValueOrDefault(matbinkey);
            }
            else
            {
                matbin = MATBIN.Read(Utility.ResourcePath($"matbins\\{matbinTemplate}.matbin"));
                matbin.Params[10].Value = false;   // "Enable SSR" -- turning this off as we dont want reflections and i assume its screen space reflections (?)
                matbin.Params[13].Value = new int[] { 0, 0 };  // "AlphaRef" -- dont know what this does but I'm gonna guess transparency control of some kind
                matbin.Samplers[0].Path = diffuseTextureA;
                matbin.Samplers[0].Unk14 = new Vector2(Const.TERRAIN_UV_SCALE);
                matbin.SourcePath = $"{matbinName}.matxml";
                genMATBINs.TryAdd(matbinkey, matbin);
            }

            FLVER2.BufferLayout layout = GetLayout($"{matbinTemplate}.matxml", true);
            FLVER2.GXList gx = GetGXList($"{matbinTemplate}.matxml");
            FLVER2.Material material = GetMaterial($"{matbinTemplate}.matxml", index);
            material.MTD = matbin.SourcePath;
            material.Name = $"{matbinName}";

            List<TextureInfo> info = new();
            info.Add(new(diffuseTextureA, $"textures\\{diffuseTextureA}.tpf.dcx"));

            return new MaterialInfo(material, gx, layout, matbin, info);
        }

        private MaterialInfo GenerateMaterialOverlay(Landscape.Mesh mesh, int index)
        {
            string diffuseTextureSourcePath = mesh.textures[0].path;
            string diffuseTexture;
            string AddTexture(string diffuseTextureSourcePath)
            {
                if (genTextures.ContainsKey(diffuseTextureSourcePath))
                {
                    return genTextures.GetValueOrDefault(diffuseTextureSourcePath);
                }
                else
                {
                    string n = Utility.PathToFileName(diffuseTextureSourcePath);
                    genTextures.TryAdd(diffuseTextureSourcePath, n);
                    return n;
                }
            }
            diffuseTexture = AddTexture(diffuseTextureSourcePath);

            string matbinTemplate = matbinTemplates["static[a]overlay"];
            string matbinName = $"mat_{mesh.textures[0].name}";

            MATBIN matbin;
            string matbinkey = $"{matbinTemplate}::{matbinName}";
            if (genMATBINs.ContainsKey(matbinkey))
            {
                matbin = genMATBINs.GetValueOrDefault(matbinkey);
            }
            else
            {
                var test = MATBIN.Read(Utility.ResourcePath($"matbins\\m10_00_003.matbin"));
                matbin = MATBIN.Read(Utility.ResourcePath($"matbins\\{matbinTemplate}.matbin"));
                matbin.Params[1].Value = true;  // decal mode (?) - not functional with the nolight material i guess'
                matbin.Params[4].Value = true;  // no shadowcast
                matbin.Params[8].Value = false; // disable decals on this material (???)
                matbin.Params[9].Value = true;  // no depth write
                matbin.Params[10].Value = false;  // SSR ??
                matbin.Params[12].Value = 3;    // Multiply/Overlay composite mode. Not sure which hard to tell difference.
                matbin.Params[13].Value = new int[] {1,0 }; //alpha ref

                MATBIN.Param invalidLight = new MATBIN.Param();
                invalidLight.Key = 2539390870;
                invalidLight.Name = "GXFT_InvalidLighting";
                invalidLight.Type = MATBIN.ParamType.Bool;
                invalidLight.Value = true;
                matbin.Params.Add(invalidLight);

                MATBIN.Param ssssw = new MATBIN.Param();
                ssssw.Key = 672269248;
                ssssw.Name = "g_SSSWidth";
                ssssw.Type = MATBIN.ParamType.Float;
                ssssw.Value = 0f;
                matbin.Params.Add(ssssw);

                MATBIN.Param ssss = new MATBIN.Param();
                ssss.Key = 1144521999;
                ssss.Name = "g_SSSStrength";
                ssss.Type = MATBIN.ParamType.Float3;
                ssss.Value = new float[] {0f, 0f, 0f};
                matbin.Params.Add(ssss);

                MATBIN.Param ssssenable = new MATBIN.Param();
                ssssenable.Key = 3802007824;
                ssssenable.Name = "g_SSSStrength_UseCAParam";
                ssssenable.Type = MATBIN.ParamType.Bool;
                ssssenable.Value = false;
                matbin.Params.Add(ssssenable);

                MATBIN.Param forward = new MATBIN.Param();
                forward.Key = 2948204600;
                forward.Name = "g_GX_ForwardRendering";
                forward.Type = MATBIN.ParamType.Bool;
                forward.Value = true;
                matbin.Params.Add(forward);

                MATBIN.Param force = new MATBIN.Param();
                force.Key = 1810630237;
                force.Name = "GXFT_ForceForward";
                force.Type = MATBIN.ParamType.Bool;
                force.Value = true;
                matbin.Params.Add(force);

                MATBIN.Param depth = new MATBIN.Param();
                depth.Key = 839124027;
                depth.Name = "g_DepthBias";
                depth.Type = MATBIN.ParamType.Int;
                depth.Value = 64;
                matbin.Params.Add(depth);


                MATBIN.Param emissive = new MATBIN.Param();
                emissive.Key = 1060242654;
                emissive.Name = "GXFT_Emissive";
                emissive.Type = MATBIN.ParamType.Bool;
                emissive.Value = true;
                matbin.Params.Add(emissive);

                /*MATBIN.Param meshDecal = new();
                meshDecal.Key = 1463682445;
                meshDecal.Name = "g_GX_bMeshDecal";
                meshDecal.Type = MATBIN.ParamType.Bool;
                meshDecal.Value = true;
                matbin.Params.Add(meshDecal);*/
                //matbin.Params[15].Value = false; // forward rendering ?? def true
                //matbin.Params[16].Value = false; // emmissvie?? def true
                //matbin.Params[17].Value = false; // forceforward?? def true
                matbin.Samplers[0].Path = diffuseTexture;
                matbin.Samplers[0].Unk14 = new Vector2(1, 1);
                matbin.Samplers[1].Path = "tx_flat";
                matbin.Samplers[1].Unk14 = new Vector2(1, 1);
                matbin.SourcePath = $"{matbinName}.matxml";

                /*matbin.Params[1].Value = true;
                matbin.Params[2].Value = true;
                matbin.Params[6].Value = 3;
                matbin.Params[9].Value = 1f;
                matbin.Samplers[0].Path = diffuseTexture;
                matbin.Samplers[0].Unk14 = new Vector2(1, 1);
                matbin.SourcePath = $"{matbinName}.matxml";*/
                genMATBINs.TryAdd(matbinkey, matbin);
            }

            FLVER2.BufferLayout layout = GetLayout($"m10_00_003.matxml", true);
            FLVER2.GXList gx = GetGXList($"m10_00_003.matxml");
            FLVER2.Material material = GetMaterial($"m10_00_003.matxml", index);
            material.MTD = matbin.SourcePath;
            material.Name = $"{matbinName}";

            List<TextureInfo> info = new();
            info.Add(new(diffuseTexture, $"textures\\{diffuseTexture}.tpf.dcx"));

            return new MaterialInfo(material, gx, layout, matbin, info);
        }

        private MaterialInfo GenerateMaterialMulti2(Landscape.Mesh mesh, int index)
        {
            string diffuseTextureSourcePathA = mesh.textures[0].path;
            string diffuseTextureSourcePathB = mesh.textures[1].path;
            string normalTextureSourcePath = Utility.ResourcePath(@"textures\tx_flat.dds");
            string blendTextureSourcePath = Utility.ResourcePath(@"textures\tx_grey.dds");
            string diffuseTextureA, diffuseTextureB, normalTexture, blendTexture;
            string AddTexture(string diffuseTextureSourcePath)
            {
                if (genTextures.ContainsKey(diffuseTextureSourcePath))
                {
                    return genTextures.GetValueOrDefault(diffuseTextureSourcePath);
                }
                else
                {
                    string n = Utility.PathToFileName(diffuseTextureSourcePath);
                    genTextures.TryAdd(diffuseTextureSourcePath, n);
                    return n;
                }
            }
            diffuseTextureA = AddTexture(diffuseTextureSourcePathA);
            diffuseTextureB = AddTexture(diffuseTextureSourcePathB);
            normalTexture = AddTexture(normalTextureSourcePath);
            blendTexture = AddTexture(blendTextureSourcePath);

            string matbinTemplate = matbinTemplates["static[a]multi[2]"];
            string matbinName = "mat_landscape_";
            matbinName += diffuseTextureA.StartsWith("tx_") ? diffuseTextureA.Substring(3) : diffuseTextureA;
            matbinName += "-";
            matbinName += diffuseTextureB.StartsWith("tx_") ? diffuseTextureB.Substring(3) : diffuseTextureB;

            MATBIN matbin;
            string matbinkey = $"{matbinTemplate}::{matbinName}";
            if (genMATBINs.ContainsKey(matbinkey))
            {
                matbin = genMATBINs.GetValueOrDefault(matbinkey);
            }
            else
            {
                matbin = MATBIN.Read(Utility.ResourcePath($"matbins\\{matbinTemplate}.matbin"));
                matbin.Params[10].Value = false;   // "Enable SSR" -- turning this off as we dont want reflections and i assume its screen space reflections (?)
                matbin.Params[15].Value = 0.36f;   // blend settings, same settings at multi3
                matbin.Params[16].Value = 0.36f;
                matbin.Samplers[0].Path = diffuseTextureA;
                matbin.Samplers[0].Unk14 = new Vector2(Const.TERRAIN_UV_SCALE);
                matbin.Samplers[1].Path = diffuseTextureB;
                matbin.Samplers[1].Unk14 = new Vector2(Const.TERRAIN_UV_SCALE);
                matbin.Samplers[2].Path = normalTexture;
                matbin.Samplers[2].Unk14 = new Vector2(0f, 0f);
                matbin.Samplers[3].Path = normalTexture;
                matbin.Samplers[3].Unk14 = new Vector2(0f, 0f);
                matbin.Samplers[4].Path = normalTexture;
                matbin.Samplers[4].Unk14 = new Vector2(0f, 0f);
                matbin.Samplers[5].Path = blendTexture;
                matbin.Samplers[5].Unk14 = new Vector2(0f, 0f);
                matbin.SourcePath = $"{matbinName}.matxml";
                genMATBINs.TryAdd(matbinkey, matbin);
            }

            FLVER2.BufferLayout layout = GetLayout($"{matbinTemplate}.matxml", true);
            FLVER2.GXList gx = GetGXList($"{matbinTemplate}.matxml");
            FLVER2.Material material = GetMaterial($"{matbinTemplate}.matxml", index);
            material.MTD = matbin.SourcePath;
            material.Name = $"{matbinName}";

            List<TextureInfo> info = new();
            info.Add(new(diffuseTextureA, $"textures\\{diffuseTextureA}.tpf.dcx"));
            info.Add(new(diffuseTextureB, $"textures\\{diffuseTextureB}.tpf.dcx"));
            info.Add(new(normalTexture, $"textures\\{normalTexture}.tpf.dcx"));
            info.Add(new(blendTexture, $"textures\\{blendTexture}.tpf.dcx"));

            return new MaterialInfo(material, gx, layout, matbin, info);
        }

        private MaterialInfo GenerateMaterialMulti3(Landscape.Mesh mesh, int index)
        {
            string diffuseTextureSourcePathA = mesh.textures[0].path;
            string diffuseTextureSourcePathB = mesh.textures[1].path;
            string diffuseTextureSourcePathC = mesh.textures[2].path;
            string normalTextureSourcePath = Utility.ResourcePath(@"textures\tx_flat.dds");
            string blendTextureSourcePath = Utility.ResourcePath(@"textures\tx_grey.dds");
            string diffuseTextureA, diffuseTextureB, diffuseTextureC, normalTexture, blendTexture;
            string AddTexture(string diffuseTextureSourcePath)
            {
                if (genTextures.ContainsKey(diffuseTextureSourcePath))
                {
                    return genTextures.GetValueOrDefault(diffuseTextureSourcePath);
                }
                else
                {
                    string n = Utility.PathToFileName(diffuseTextureSourcePath);
                    genTextures.TryAdd(diffuseTextureSourcePath, n);
                    return n;
                }
            }
            diffuseTextureA = AddTexture(diffuseTextureSourcePathA);
            diffuseTextureB = AddTexture(diffuseTextureSourcePathB);
            diffuseTextureC = AddTexture(diffuseTextureSourcePathC);
            normalTexture = AddTexture(normalTextureSourcePath);
            blendTexture = AddTexture(blendTextureSourcePath);

            string matbinTemplate = matbinTemplates["static[a]multi[3]"];
            string matbinName = "mat_landscape_";
            matbinName += diffuseTextureA.StartsWith("tx_") ? diffuseTextureA.Substring(3) : diffuseTextureA;
            matbinName += "-";
            matbinName += diffuseTextureB.StartsWith("tx_") ? diffuseTextureB.Substring(3) : diffuseTextureB;
            matbinName += "-";
            matbinName += diffuseTextureC.StartsWith("tx_") ? diffuseTextureC.Substring(3) : diffuseTextureC;

            MATBIN matbin;
            string matbinkey = $"{matbinTemplate}::{matbinName}";
            if (genMATBINs.ContainsKey(matbinkey))
            {
                matbin = genMATBINs.GetValueOrDefault(matbinkey);
            }
            else
            {
                matbin = MATBIN.Read(Utility.ResourcePath($"matbins\\{matbinTemplate}.matbin"));
                matbin.Params[10].Value = false;   // "Enable SSR" -- turning this off as we dont want reflections and i assume its screen space reflections (?)
                matbin.Params[15].Value = 0.36f;   // blend settings, these values result in a very normal linear 3 way blend
                matbin.Params[16].Value = 0.36f;
                matbin.Params[17].Value = 0f;
                matbin.Params[18].Value = new float[] { 1f, 1f };  // set of uv params. I'm not sure what these do but im setting them all to 1f
                matbin.Params[19].Value = new float[] { 1f, 1f };
                matbin.Params[20].Value = new float[] { 1f, 1f };
                matbin.Params[21].Value = new float[] { 1f, 1f };
                matbin.Samplers[0].Path = diffuseTextureA;
                matbin.Samplers[0].Unk14 = new Vector2(Const.TERRAIN_UV_SCALE);
                matbin.Samplers[1].Path = diffuseTextureB;
                matbin.Samplers[1].Unk14 = new Vector2(Const.TERRAIN_UV_SCALE);
                matbin.Samplers[2].Path = diffuseTextureC;
                matbin.Samplers[2].Unk14 = new Vector2(Const.TERRAIN_UV_SCALE);
                matbin.Samplers[3].Path = normalTexture;
                matbin.Samplers[3].Unk14 = new Vector2(0f, 0f);
                matbin.Samplers[4].Path = normalTexture;
                matbin.Samplers[4].Unk14 = new Vector2(0f, 0f);
                matbin.Samplers[5].Path = normalTexture;
                matbin.Samplers[5].Unk14 = new Vector2(0f, 0f);
                matbin.Samplers[6].Path = normalTexture;
                matbin.Samplers[6].Unk14 = new Vector2(0f, 0f);
                matbin.Samplers[7].Path = blendTexture;
                matbin.Samplers[7].Unk14 = new Vector2(0f, 0f);
                matbin.SourcePath = $"{matbinName}.matxml";
                genMATBINs.TryAdd(matbinkey, matbin);
            }

            FLVER2.BufferLayout layout = GetLayout($"{matbinTemplate}.matxml", true);
            FLVER2.GXList gx = GetGXList($"{matbinTemplate}.matxml");
            FLVER2.Material material = GetMaterial($"{matbinTemplate}.matxml", index);
            material.MTD = matbin.SourcePath;
            material.Name = $"{matbinName}";

            List<TextureInfo> info = new();
            info.Add(new(diffuseTextureA, $"textures\\{diffuseTextureA}.tpf.dcx"));
            info.Add(new(diffuseTextureB, $"textures\\{diffuseTextureB}.tpf.dcx"));
            info.Add(new(diffuseTextureC, $"textures\\{diffuseTextureC}.tpf.dcx"));
            info.Add(new(normalTexture, $"textures\\{normalTexture}.tpf.dcx"));
            info.Add(new(blendTexture, $"textures\\{blendTexture}.tpf.dcx"));

            return new MaterialInfo(material, gx, layout, matbin, info);
        }

        public MaterialInfo GenerateMaterialWater(int index)
        {
            string matbinTemplate = matbinTemplates["static[x]water"];
            string matbinName = "mat_water";

            MATBIN matbin;
            string matbinkey = $"{matbinTemplate}::{matbinName}";
            if (genMATBINs.ContainsKey(matbinkey))
            {
                matbin = genMATBINs.GetValueOrDefault(matbinkey);
            }
            else
            {
                matbin = MATBIN.Read(Utility.ResourcePath($"matbins\\{matbinTemplate}.matbin"));
                matbin.SourcePath = $"{matbinName}.matxml";
                genMATBINs.TryAdd(matbinkey, matbin);
            }

            FLVER2.BufferLayout layout = GetLayout($"{matbinTemplate}.matxml", true);
            FLVER2.GXList gx = GetGXList($"{matbinTemplate}.matxml");
            FLVER2.Material material = GetMaterial($"{matbinTemplate}.matxml", index);
            material.MTD = matbin.SourcePath;
            material.Name = $"{matbinName}";

            List<TextureInfo> info = new();
            return new MaterialInfo(material, gx, layout, matbin, info);
        }

        /* Write all tpfs and matbins generated by above methods in this context and bnd them appropriately */
        public void WriteAll()
        {
            Lort.NewTask("Writing matbins", genMATBINs.Count());
            foreach(KeyValuePair<string, MATBIN> kvp in genMATBINs)
            {
                string outFileName = $"{Utility.PathToFileName(kvp.Value.SourcePath)}.matbin";
                kvp.Value.Write($"{Const.CACHE_PATH}materials\\{outFileName}");
                Lort.TaskIterate();
            }

            Lort.NewTask("Writing tpfs", genTextures.Count());
            foreach (KeyValuePair<string, string> kvp in genTextures)
            {
                TPF tpf = new TPF();
                tpf.Encoding = 1;
                tpf.Flag2 = 3;
                tpf.Platform = TPF.TPFPlatform.PC;
                tpf.Compression = DCX.Type.DCX_KRAK;

                byte[] data = File.ReadAllBytes(kvp.Key);
                int format = JortPob.Common.DDS.GetTpfFormatFromDdsBytes(data);

                TPF.Texture tex = new($"{kvp.Value}", (byte)format, 0, data, TPF.TPFPlatform.PC);
                tpf.Textures.Add(tex);

                tpf.Write($"{Const.CACHE_PATH}textures\\{kvp.Value}.tpf.dcx");
                Lort.TaskIterate();
            }
        }

        /* Container class for returning a bunch of stuff at once */
        public class MaterialInfo
        {
            public FLVER2.Material material;
            public FLVER2.GXList gx;
            public FLVER2.BufferLayout layout;
            public MATBIN matbin;
            public List<TextureInfo> info; // used by cache

            public MaterialInfo(FLVER2.Material material, FLVER2.GXList gx, FLVER2.BufferLayout layout, MATBIN matbin, List<TextureInfo> info)
            {
                this.material = material;
                this.gx = gx;
                this.layout = layout;
                this.matbin = matbin;
                this.info = info;
            }
        }
    }
}
