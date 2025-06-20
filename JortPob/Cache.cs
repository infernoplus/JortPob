﻿using HKLib.hk2018;
using HKX2;
using JortPob.Common;
using JortPob.Model;
using JortPob.Worker;
using SharpAssimp;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static HKLib.hk2018.hkaSkeleton;

namespace JortPob
{
    public class Cache
    {
        public List<TerrainInfo> terrains;
        public List<ModelInfo> maps;        // Map pieces
        public List<ModelInfo> assets;
        public List<ObjectInfo> objects;

        public Cache()
        {
            maps = new();     /// @TODO: deelte deprecated
            assets = new();
            objects = new();
            terrains = new();
        }

        /* Get a terrain by coordinate */
        public TerrainInfo GetTerrain(Int2 coordinate)
        {
            foreach(TerrainInfo terrain in terrains)
            {
                if(terrain.coordinate == coordinate)
                {
                    return terrain;
                }
            }
            return null;
        }

        /* Get a modelinfo by the nif name and scale */
        public ModelInfo GetModel(string name)
        {
            return GetModel(name, 100);
        }

        public ModelInfo GetModel(string name, int scale)
        {
            /* If the model doesn't have collision it's static scaleable so we return scale 100 as that's the only version of it */
            if(!ModelHasCollision(name))
            {
                foreach (ModelInfo model in assets)
                {
                    if (model.name == name) { return model; }
                }
                return null;
            }

            /* Otherwise... */
            /* First look for one with a matched scale */
            foreach(ModelInfo model in assets)
            {
                if(model.name == name && model.scale == scale) { return model; }
            }

            /* If not found then we look a dynamic asset */
            foreach (ModelInfo model in assets)
            {
                if (model.name == name && model.scale == Const.DYNAMIC_ASSET) { return model; }
            }

            /* Oh dear.. return null I guess! */
            return null;
        }

        public bool ModelHasCollision(string name)
        {
            foreach(ModelInfo model in assets)
            {
                if (model.name == name) { return model.HasCollision(); }
            }
            return false;
        }

        /* Big stupid load function */
        public static Cache Load(ESM esm)
        {
            string manifestPath = Const.CACHE_PATH + @"cache.json";

            /* Cache Exists ? */
            if (File.Exists(manifestPath))
            {
                Lort.Log($"Using cache: {manifestPath}", Lort.Type.Main);
                Lort.Log($"Delete this file if you want to regenerate models/textures/collision and cache!", Lort.Type.Main);
            }
            /* Generate new cache! */
            else
            {
                /* Grab all the models we want to convert */
                List<PreModel> meshes = new();
                PreModel GetMesh(Content content)
                {
                    foreach (PreModel model in meshes)
                    {
                        if(model.mesh == content.mesh)
                        {
                            if (content.type == ESM.Type.Static) { model.forceCollision = true; }
                            return model;
                        }
                    }
                    PreModel m = new(content.mesh, content.type == ESM.Type.Static);
                    meshes.Add(m);
                    return m;
                }

                void ScoopEmUp(List<Cell> cells)
                {
                    foreach (Cell cell in cells)
                    {
                        void Scoop(List<Content> contents)
                        {
                            foreach (Content content in contents)
                            {
                                if(content.mesh == null) { continue; }  // skip content with no mesh
                                PreModel model = GetMesh(content);
                                int i = model.scales.ContainsKey(content.scale)? model.scales[content.scale]:0;
                                model.scales.Remove(content.scale);
                                model.scales.Add(content.scale, ++i);  // ?? i guess this works. dumbass solution though tbh
                            }
                        }
                        Scoop(cell.contents);
                    }
                }
                ScoopEmUp(esm.exterior);
                if (!Const.DEBUG_SKIP_INTERIOR) { ScoopEmUp(esm.interior); }

                Lort.Log($"Generating new cache...", Lort.Type.Main);

                Cache nu = new();

                AssimpContext assimpContext = new();
                MaterialContext materialContext = new();

                /* Convert models/textures for models */
                nu.assets = FlverWorker.Go(materialContext, meshes);

                /* Convert models/textures for terrain */
                nu.terrains = LandscapeWorker.Go(materialContext, esm);

                /* Write textures */
                materialContext.WriteAll();
                assimpContext.Dispose();

                /* Convert collision */
                List<CollisionInfo> collisions = new();
                foreach (ModelInfo modelInfo in nu.assets)
                {
                    if (modelInfo.collision == null) { continue; }
                    collisions.Add(modelInfo.collision);
                }
                foreach (TerrainInfo terrain in nu.terrains)
                {
                    collisions.Add(terrain.collision);
                }
                HkxWorker.Go(collisions);

                /* Assign resource ID numbers */
                int nextM = 0, nextA = 0, nextO = 5000;
                foreach (TerrainInfo terrainInfo in nu.terrains)
                {
                    terrainInfo.id = nextM++;
                    //terrainInfo.collision.id = -1;
                }
                foreach (ModelInfo modelInfo in nu.maps)
                {
                    modelInfo.id = nextM++;
                }
                foreach (ModelInfo modelInfo in nu.assets)
                {
                    modelInfo.id = nextA++;
                }
                foreach (ObjectInfo objectInfo in nu.objects)
                {
                    objectInfo.id = nextO++;
                }

                /* Write new cache file */
                string jsonOutput = JsonSerializer.Serialize<Cache>(nu, new JsonSerializerOptions { IncludeFields = true });
                File.WriteAllText(manifestPath, jsonOutput);
                Lort.Log($"Generated new cache: {Const.CACHE_PATH}", Lort.Type.Main);
            }

            /* Load cache manifest */
            string tempRawJson = File.ReadAllText(manifestPath);
            Cache cache = JsonSerializer.Deserialize<Cache>(tempRawJson, new JsonSerializerOptions { IncludeFields = true });
            return cache;
        }
    }

    public class TerrainInfo
    {
        public readonly Int2 coordinate;   // Location in world cell grid
        public readonly string path;
        public CollisionInfo collision;
        public List<TextureInfo> textures; // All generated tpf files

        public int id;
        public TerrainInfo(Int2 coordinate, string path)
        {
            this.coordinate = coordinate;
            this.path = path;
            textures = new();

            id = -1;
        }
    }

    public class ObjectInfo
    {
        public string name; // Original esm ref id
        public ModelInfo model;

        public int id;
        public ObjectInfo(string name, ModelInfo model)
        {
            this.name = name.ToLower();
            this.model = model;
        }
    }

    public class ModelInfo
    {
        public string name; // Original nif name, for lookup from ESM records
        public readonly string path; // Relative path from the 'cache' folder to the converted flver file
        public CollisionInfo collision; // Generated HKX file or null if no collision exists
        public List<TextureInfo> textures; // All generated tpf files

        public Dictionary<string, short> dummies; // Dummies and their ids

        public int id;  // Model ID number, the last 6 digits in a model filename. EXAMPLE: m30_00_00_00_005521.mapbnd.dcx
        public int scale;  // clamped to an int. 1 = 1%. 100 is 1f. int.MAX_VALUE means it's dynamic

        public float size; // Bounding radius, for use in distant LOD generation

        public ModelInfo(string name, string path, int scale)
        {
            this.name = name.ToLower();
            this.path = path;
            textures = new();
            dummies = new();

            id = -1;
            this.scale = scale;
            size = -1f;
        }

        public string AssetPath()
        {
            int v1 = 900 + (int)(id / 1000);
            int v2 = id % 1000;
            return $"aeg{v1.ToString("D3")}\\aeg{v1.ToString("D3")}_{v2.ToString("D3")}";
        }

        public string AssetName()
        {
            int v1 = 900 + (int)(id / 1000);
            int v2 = id % 1000;
            return $"aeg{v1.ToString("D3")}_{v2.ToString("D3")}";
        }

        public int AssetRow()
        {
            int v1 = 900 + (int)(id / 1000);
            int v2 = id % 1000;
            return int.Parse($"{v1.ToString("D3")}{v2.ToString("D3")}");  // yes i know this the wrong way to do this but guh
        }

        public bool HasCollision()
        {
            return collision != null;
        }

        public bool IsDynamic()
        {
            return scale == Const.DYNAMIC_ASSET;
        }

        public bool UseScale()
        {
            return !HasCollision() || IsDynamic();
        }
    }

    public class CollisionInfo
    {
        public string name; // Original nif name, for lookup from ESM records
        public string obj;  // Relative path from the 'cache' folder to the converted obj file
        public string path; // Relative path from the 'cache' folder to the converted hkx file

        public CollisionInfo(string name, string path)
        {
            this.name = name.ToLower();
            this.obj = path;
            this.path = path.Replace(".obj", ".hkx");
        }
    }

    public class TextureInfo
    {
        public readonly string name; // Original dds texture name for lookup
        public readonly string path; // Relative path from the 'cache' folder to the converted tpf file
        public TextureInfo(string name, string path)
        {
            this.name = name.ToLower();
            this.path = path;
        }
    }

    // little class i'm using for preprocessing scale info on meshes that will become assets.
    // for assets that have a lot of scaled versions placed down we make baked scaled versions
    // for 1 off scales we use dynamic assets instead
    public class PreModel
    {
        public string mesh;
        public Dictionary<int, int> scales;
        public bool forceCollision;           // some morrowind nifs dont have collision meshes despite needing them. in SOME cases we use the visual mesh as collision

        public PreModel(string mesh, bool forceCollision)
        {
            this.mesh = mesh.Trim().ToLower();
            scales = new();
            this.forceCollision = forceCollision;
        }
    }
}
