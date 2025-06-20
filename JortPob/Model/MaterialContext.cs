﻿using JortPob.Common;
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
            foreach (XmlElement xmlElement in xmlMaterials)
            {
                string attrMtd = xmlElement.GetAttribute("mtd");
                if(attrMtd == mtd)
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
            foreach (XmlElement xmlElement in xmlExamples)
            {
                string attrMtd = xmlElement.GetAttribute("mtd");
                if (attrMtd == mtd)
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
            foreach (XmlElement xmlElement in xmlExamples)
            {
                string attrMtd = xmlElement.GetAttribute("mtd");
                if (attrMtd == mtd)
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
            { "static[a]multi[2]", "m10_00_027" }           // blendy multimaterial for terrain
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
                    matbin.Samplers[0].Path = diffuseTextureA;
                    matbin.Samplers[0].Unk14 = new Vector2(13f, 13f);
                    matbin.Samplers[1].Path = diffuseTextureB;
                    matbin.Samplers[1].Unk14 = new Vector2(13f, 13f);
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
                FLVER2.Material material = GetMaterial($"{matbinTemplate}.matxml", index++);
                material.MTD = matbin.SourcePath;
                material.Name = $"{matbinName}";

                List<TextureInfo> info = new();
                info.Add(new(diffuseTextureA, $"textures\\{diffuseTextureA}.tpf.dcx"));
                info.Add(new(diffuseTextureB, $"textures\\{diffuseTextureB}.tpf.dcx"));
                info.Add(new(normalTexture, $"textures\\{normalTexture}.tpf.dcx"));
                info.Add(new(blendTexture, $"textures\\{blendTexture}.tpf.dcx"));

                materialInfo.Add(new MaterialInfo(material, gx, layout, matbin, info));
            }
            return materialInfo;
        }

        /* Write all tpfs and matbins generated by above methods in this context and bnd them appropriately */
        public void WriteAll()
        {
            foreach(KeyValuePair<string, MATBIN> kvp in genMATBINs)
            {
                string outFileName = $"{Utility.PathToFileName(kvp.Value.SourcePath)}.matbin";
                kvp.Value.Write($"{Const.CACHE_PATH}materials\\{outFileName}");
            }

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
