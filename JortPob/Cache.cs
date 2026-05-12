using JortPob.Common;
using JortPob.Model;
using JortPob.Worker;
using SharpAssimp;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace JortPob
{
    public class Cache
    {
        public List<TerrainInfo> terrains;
        public List<ModelInfo> maps;        // Map pieces (deprecated)
        public List<ModelInfo> assets;
        public List<EmitterInfo> emitters;
        public List<ObjectInfo> objects;
        public List<LiquidInfo> liquids;
        public List<CutoutInfo> cutouts; // defines collision planes for swamps/lava

        public CollisionInfo? defaultCollision { get; set; } = null; // used by interior cells as a base. needed because of engine being weird

        /*
         * These fields are marked as "ignore" because they are dynamic/used for caching.
         */
        [JsonIgnore]
        public Dictionary<string, PickableInfo> pickablesByRecord;  // models for harvestable plants. seperated because they have very different params than most assets

        [JsonIgnore]
        private bool hasInitializedLookups;

        [JsonIgnore]
        private Dictionary<string, bool> assetCollisionByName; // caches whether an asset name has collision

        [JsonIgnore]
        private Dictionary<string, List<ModelInfo>> assetsByName; // we use a list because a single asset name can have multiple models with different scales

        public Cache()
        {
            maps = new();     /// @TODO: deelte deprecated
            assets = new();
            assetCollisionByName = new();
            pickablesByRecord = new();
            assetsByName = new();
            emitters = new();
            objects = new();
            terrains = new();
            liquids = new();
            cutouts = new();
        }

        /**
         * Populates the various lookups/caches on-demand at most one time.
         * Subsequent calls are no-ops.
         */
        private void InitializeLookups()
        {
            lock (assetsByName)
            lock (assetCollisionByName)
            {
                if (hasInitializedLookups)
                {
                    return;
                }

                foreach (ModelInfo model in assets)
                {
                    // initialize the dictionary as we go, now that everything is settled
                    if (assetsByName.TryGetValue(model.name, out var matchingAssets))
                    {
                        matchingAssets.Add(model);
                    }
                    else
                    {
                        assetsByName[model.name] = [model];
                    }

                    assetCollisionByName[model.name] = model.HasCollision();
                }

                hasInitializedLookups = true;
            }
        }

        /* Get a terrain by coordinate */
        public TerrainInfo? GetTerrain(Int2 coordinate)
        {
            return terrains.FirstOrDefault(t => t.coordinate == coordinate);
        }

        /* Get a cutout by coordinate */
        public CutoutInfo? GetCutout(Int2 coordinate)
        {
            return cutouts.FirstOrDefault(c => c.coordinate == coordinate);
        }

        public List<PickableInfo> GetPickables()
        {
            return pickablesByRecord.Values.ToList();
        }

        /* Get a pickable modelinfo. These are generated on the fly. Pickables are always dynamic. */
        public PickableInfo? GetPickableModel(PickableContent content)
        {
            /* If pickable exists return it */
            if (pickablesByRecord.TryGetValue(content.id.ToLower(), out var pickable))
            {
                return pickable;
            }

            /* If it doesn't exist we look for the asset model of it and then create a pickable from that */
            var modelInfo = GetModel(content.mesh);
            if (modelInfo == null)
                return null;
            PickableInfo p = new(content, modelInfo);
            p.id = pickablesByRecord.Count;
            pickablesByRecord.Add(p.record, p);

            return p;
        }
        
        /**
         * Cached lookup of whether a model name has collision.
         * Returns "false" if we've never seen the model before, otherwise
         * returns its actual `HasCollision` result after caching it.
         */
        private bool AssetHasCollision(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            InitializeLookups();
            lock (assetCollisionByName)
            {
                if (assetCollisionByName.TryGetValue(name, out var collision))
                {
                    return collision;
                }
            }

            return false;
        }

        private List<ModelInfo> GetAssetsByName(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return [];

            InitializeLookups();
            lock (assetsByName)
            {
                return assetsByName.TryGetValue(name, out var matchingAssets) ? matchingAssets : [];
            }
        }

        /* Get a modelinfo by the nif name and scale */
        public ModelInfo? GetModel(string? name)
        {
            return GetModel(name, 100);
        }

        public ModelInfo? GetModel(string? name, int scale)
        {
            var matchingAssets = GetAssetsByName(name);

            // If we have nothing for the asset, return early
            if (matchingAssets.Count == 0)
            {
                return null;
            }

            /* If the model doesn't have collision it's static scaleable so we return scale 100 as that's the only version of it */
            if(!AssetHasCollision(name))
            {
                return matchingAssets.FirstOrDefault();
            }

            /* Otherwise... */
            /* First look for one with a matched scale */
            return matchingAssets.Find(m => m.scale == scale) ??
                   /* If not found then we look a dynamic asset, else return null */
                   matchingAssets.Find(m => m.scale == Const.DYNAMIC_ASSET);
        }

        public EmitterInfo? GetEmitter(string record)
        {
            return emitters.FirstOrDefault(e => e.record == record);
        }

        public LiquidInfo GetWater()
        {
            return liquids[0];
        }

        public LiquidInfo GetSwamp()
        {
            return liquids[1];
        }

        public LiquidInfo GetLava()
        {
            return liquids[2];
        }

        public void AddConvertedEmitter(EmitterContent emitterContent)
        {
            var modelInfo = GetModel(emitterContent.mesh);
            if (modelInfo == null)
                return;

            if (GetEmitter(emitterContent.id) == null)
            {
                EmitterInfo emitterInfo = new();
                emitterInfo.record = emitterContent.id;
                emitterInfo.model = modelInfo;

                emitterInfo.color = new byte[] { 0, 0, 0, 0 }; // no light

                emitterInfo.radius = 0f;
                emitterInfo.weight = 0f;

                emitterInfo.value = 0;
                emitterInfo.time = 0;

                emitterInfo.dynamic = false;
                emitterInfo.fire = false;
                emitterInfo.negative = false;
                emitterInfo.defaultOff = true;
                emitterInfo.mode = LightContent.Mode.Default;

                emitterInfo.id = emitters.Last().id + 1;

                Lort.Log($"## INFO ##  AssetContent '{emitterContent.id}' was converted to an EmitterContent!", Lort.Type.Debug);
                emitters.Add(emitterInfo);
            }
        }

        /* Big stupid load function */
        public static Cache Load(ESM esm)
        {
            string manifestPath = Path.Combine(Const.CACHE_PATH, "cache.json");

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
                PreModel? GetMesh(Content content)
                {
                    if (string.IsNullOrEmpty(content.mesh))
                        return null;
                    foreach (PreModel model in meshes)
                    {
                        if(model.mesh == content.mesh)
                        {
                            if (content.type == ESM.Type.Static) { model.forceCollision = true; }
                            if (content is ItemContent || content is ContainerContent) { model.forceDynamic = true; }
                            return model;
                        }
                    }
                    PreModel m = new(content.mesh, content.type == ESM.Type.Static, content is ItemContent || content is ContainerContent);
                    meshes.Add(m);
                    return m;
                }

                void ScoopEmUp(List<Cell> cells, bool addCutouts)
                {
                    foreach (Cell cell in cells)
                    {
                        void Scoop(List<Content> contents)
                        {
                            foreach (Content content in contents)
                            {
                                if(String.IsNullOrEmpty(content.mesh)) { continue; }  // skip content with no mesh
                                if (addCutouts) { LiquidManager.AddCutout(content); } // check if this is a lava or swamp mesh and add it to cutouts if it is
                                var model = GetMesh(content);
                                if (model == null)
                                {
                                    Lort.Log($" ## WARNING ## Content with id {content.id} does not have an associated model, skipping.", Lort.Type.Debug);
                                    continue;
                                }    
                                int i = model.scales.ContainsKey(content.scale) ? model.scales[content.scale] : 0;
                                model.scales.Remove(content.scale);
                                model.scales.Add(content.scale, ++i);  // ?? i guess this works. dumbass solution though tbh
                            }
                        }
                        Scoop(cell.contents);
                    }
                }
                ScoopEmUp(esm.exterior, true);
                ScoopEmUp(esm.interior, false);

                Lort.Log($"Generating new cache...", Lort.Type.Main);

                Cache nu = new();

                AssimpContext? assimpContext = new();
                MaterialContext? materialContext = new();

                /* Convert models/textures for terrain */
                LandscapeWorker landscapeWorker = new LandscapeWorker(materialContext, esm);
                nu.terrains = landscapeWorker.Go();

                /* Generate stuff for cutouts */
                nu.cutouts = LiquidManager.GenerateCutouts(esm);

                /* Convert models/textures for models */
                FlverWorker flverWorker = new FlverWorker(materialContext, meshes);
                nu.assets = flverWorker.Go();

                /* Generate stuff for water */
                nu.liquids = LiquidManager.GenerateLiquids(esm, materialContext);

                /* Add some pregen assets */
                ModelInfo boxModelInfo = new("InteriorShadowBox", $"meshes\\interior_shadow_box.flver", 100);
                ModelConverter.NIFToFLVER(materialContext, boxModelInfo, false, Utility.ResourcePath(@"mesh\box.nif"), Path.Combine(Const.CACHE_PATH, boxModelInfo.path));
                FLVER2 boxFlver = FLVER2.Read(Path.Combine(Const.CACHE_PATH, boxModelInfo.path)); // we need this box to be exactly 1 unit in each direction no matter what so we just edit it real quick
                foreach (FLVER.Vertex v in boxFlver.Meshes[0].Vertices)
                {
                    float x = v.Position.X > 0f ? .5f : -.5f;
                    float y = v.Position.Y > 0f ? .5f : -.5f;
                    float z = v.Position.Z > 0f ? .5f : -.5f;
                    v.Position = new Vector3(x, y, z);
                }
                BoundingBoxSolver.FLVER(boxFlver); // redo bounding box since we edited the mesh
                boxFlver.Write(Path.Combine(Const.CACHE_PATH, boxModelInfo.path));
                nu.assets.Add(boxModelInfo);

                string defaultCollisionObjPath = "meshes\\default_collision_plane.obj";
                if(!File.Exists(Path.Combine(Const.CACHE_PATH, defaultCollisionObjPath))) 
                {
                    File.Copy(Utility.ResourcePath(@"mesh\plane.obj.file"), Path.Combine(Const.CACHE_PATH, defaultCollisionObjPath)); 
                }
                nu.defaultCollision = new("DefaultCollisionPlane", defaultCollisionObjPath);

                /* Write textures */
                Lort.Log($"Writing matbins & tpfs...", Lort.Type.Main);
                materialContext.WriteAll();
                assimpContext.Dispose();

                /* Garbage collect after writing material data to file */
                materialContext = null;
                assimpContext = null;
                GC.Collect();

                /* Create emitterinfos */
                foreach(JsonNode json in esm.GetAllRecordsByType(ESM.Type.Light))
                {
                    if (string.IsNullOrEmpty(json["mesh"]?.ToString())) { continue; }

                    EmitterInfo emitterInfo = new();
                    emitterInfo.record = json["id"]!.ToString();
                    string mesh = json["mesh"]!.ToString().ToLower();
                    emitterInfo.model = nu.GetModel(mesh);

                    if(emitterInfo.model == null) { continue; } // discard if we don't find a model for this. should only happen when debug stuff is enabled for cell building

                    int r = int.Parse(json["data"]!["color"]![0]!.ToString());
                    int g = int.Parse(json["data"]!["color"]![1]!.ToString());
                    int b = int.Parse(json["data"]!["color"]![2]!.ToString());
                    int a = int.Parse(json["data"]!["color"]![3]!.ToString());
                    emitterInfo.color = [(byte)r, (byte)g, (byte)b, (byte)a];  // 0 -> 255 colors

                    emitterInfo.radius = float.Parse(json["data"]!["radius"]!.ToString()) * Const.GLOBAL_SCALE;
                    emitterInfo.weight = float.Parse(json["data"]!["weight"]!.ToString());

                    emitterInfo.value = int.Parse(json["data"]!["value"]!.ToString());
                    emitterInfo.time = int.Parse(json["data"]!["time"]!.ToString());

                    string flags = json["data"]!["flags"]!.ToString();

                    emitterInfo.dynamic = flags.Contains("DYNAMIC");
                    emitterInfo.fire = flags.Contains("FIRE");
                    emitterInfo.negative = flags.Contains("NEGATIVE");
                    emitterInfo.defaultOff = flags.Contains("OFF_BY_DEFAULT");

                    if (flags.Contains("FLICKER_SLOW")) { emitterInfo.mode = LightContent.Mode.FlickerSlow; }
                    else if (flags.Contains("FLICKER")) { emitterInfo.mode = LightContent.Mode.Flicker; }
                    else if (flags.Contains("PULSE_SLOW")) { emitterInfo.mode = LightContent.Mode.PulseSlow; }
                    else if (flags.Contains("PULSE")) { emitterInfo.mode = LightContent.Mode.Pulse; }
                    else { emitterInfo.mode = LightContent.Mode.Default; }

                    nu.emitters.Add(emitterInfo);
                }

                /* Convert collision */
                List<CollisionInfo> collisions = new();
                collisions.Add(nu.defaultCollision);
                foreach (ModelInfo modelInfo in nu.assets)
                {
                    if (modelInfo.collision == null) { continue; }
                    collisions.Add(modelInfo.collision);
                }
                foreach (TerrainInfo terrain in nu.terrains)
                {
                    foreach(CollisionInfo collision in terrain.collision)
                    {
                        collisions.Add(collision);
                    }
                }
                foreach(LiquidInfo water in nu.liquids)
                {
                    collisions.AddRange(water.GetCollision());
                }
                foreach (CutoutInfo cutout in nu.cutouts)
                {
                    collisions.Add(cutout.collision);
                }

                HkxWorker hkxWorker = new HkxWorker(collisions);
                hkxWorker.Go();

                /* Assign resource ID numbers */
                int nextM = 0, nextA = 0, nextO = 5000;
                foreach (TerrainInfo terrainInfo in nu.terrains)
                {
                    terrainInfo.id = nextM++;
                }
                foreach (ModelInfo modelInfo in nu.maps)
                {
                    modelInfo.id = nextM++;
                }
                foreach (ModelInfo modelInfo in nu.assets)
                {
                    modelInfo.id = nextA++;
                }
                foreach(EmitterInfo emitterInfo in nu.emitters)
                {
                    emitterInfo.id = nextA++;
                }
                foreach (ObjectInfo objectInfo in nu.objects)
                {
                    objectInfo.id = nextO++;
                }

                /* Write new cache file */
                string jsonOutput = JsonSerializer.Serialize(nu, new JsonSerializerOptions { IncludeFields = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals });
                File.WriteAllText(manifestPath, jsonOutput);
                Lort.Log($"Generated new cache: {Const.CACHE_PATH}", Lort.Type.Main);
            }

            /* Load cache manifest */
            string tempRawJson = File.ReadAllText(manifestPath);
            Cache cache = JsonSerializer.Deserialize<Cache>(tempRawJson, new JsonSerializerOptions { IncludeFields = true, NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals })!;
            return cache!;
        }
    }

    public class TerrainInfo
    {
        public readonly Int2 coordinate;   // Location in world cell grid
        public readonly string path, obj;  // path goes to flver, obj goes to full obj
        public List<CollisionInfo> collision;
        public List<TextureInfo> textures; // All generated tpf files
        public bool hasWater, hasLava, hasSwamp; // caching these flags so we dont have to load the landscape to find out

        public int id;
        public TerrainInfo(Int2 coordinate, string path)
        {
            this.coordinate = coordinate;
            this.path = path;
            obj = Path.ChangeExtension(path, ".obj");
            textures = new();
            collision = new();

            id = -1;

            hasWater = false;
            hasLava = false;
            hasSwamp = false;
        }
    }

    public class ObjectInfo
    {
        public string name { get; set; } // Original esm ref id
        public ModelInfo model { get; set; }
        public int id { get; set; }

        public ObjectInfo(string name, ModelInfo model)
        {
            this.name = name.ToLower();
            this.model = model;
        }
    }

    public class EmitterInfo
    {
        public string record { get; set; } = ""; // record ID
        public ModelInfo? model { get; set; }

        public float radius { get; set; }
        public float weight { get; set; }
        public int time { get; set; }
        public int value { get; set; }

        public byte[] color { get; set; } = [];

        public bool dynamic { get; set; }
        public bool fire { get; set; } 
        public bool negative { get; set; }
        public bool defaultOff { get; set; }
        public LightContent.Mode mode { get; set; }

        public int id { get; set; } // asset id

        public EmitterInfo()
        {

        }

        public string AssetPath()
        {
            int v1 = Const.ASSET_GROUP + (int)(id / 1000);
            int v2 = id % 1000;
            return $"aeg{v1.ToString("D3")}\\aeg{v1.ToString("D3")}_{v2.ToString("D3")}";
        }

        public string AssetName()
        {
            int v1 = Const.ASSET_GROUP + (int)(id / 1000);
            int v2 = id % 1000;
            return $"aeg{v1.ToString("D3")}_{v2.ToString("D3")}";
        }

        public int AssetRow()
        {
            int v1 = Const.ASSET_GROUP + (int)(id / 1000);
            int v2 = id % 1000;
            return int.Parse($"{v1.ToString("D3")}{v2.ToString("D3")}");  // yes i know this the wrong way to do this but guh
        }

        public bool HasEmitter()
        {
            foreach (KeyValuePair<string, short> kvp in model?.dummies ?? [])
            {
                if (kvp.Key.Contains("emitter")) { return true; }
            }
            return false;
        }

        // returns false if the light data in this class would effectively produce no light. for example a light color of 0 0 0 or a radius of 0
        public bool HasLight()
        {
            if (radius <= 0) { return false; }
            if (color[0] + color[1] + color[2] <= 0) { return false; }
            return true;
        }

        public short GetAttachLight()
        {
            short rootDmyId = -1;
            foreach (KeyValuePair<string, short> kvp in model?.dummies ?? [])
            {
                if (kvp.Key.Contains("attachlight")) { return kvp.Value; }
                if (kvp.Key.Contains("root")) { rootDmyId = kvp.Value; }
            }

            return rootDmyId; // if we don't find an explicit attachlight dmy we instead return the root dmy
        }
    }

    public class ModelInfo
    {
        public string name { get; init; } // Original nif name, for lookup from ESM records
        public string path { get; init; } // Relative path from the 'cache' folder to the converted flver file
        public CollisionInfo? collision { get; set; } // Generated HKX file or null if no collision exists
        public List<TextureInfo> textures { get; init; } // All generated tpf files

        public Dictionary<string, short> dummies { get; init; } // Dummies and their ids

        public int id { get; set; }  // Model ID number, the last 6 digits in a model filename. EXAMPLE: m30_00_00_00_005521.mapbnd.dcx
        public int scale { get; init; }  // clamped to an int. 1 = 1%. 100 is 1f. int.MAX_VALUE means it's dynamic

        public float size { get; set; } // Bounding radius, for use in distant LOD generation

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

        public virtual string AssetPath()
        {
            int v1 = Const.ASSET_GROUP + (int)(id / 1000);
            int v2 = id % 1000;
            return $"aeg{v1.ToString("D3")}\\aeg{v1.ToString("D3")}_{v2.ToString("D3")}";
        }

        public virtual string AssetName()
        {
            int v1 = Const.ASSET_GROUP + (int)(id / 1000);
            int v2 = id % 1000;
            return $"aeg{v1.ToString("D3")}_{v2.ToString("D3")}";
        }

        public virtual int AssetRow()
        {
            int v1 = Const.ASSET_GROUP + (int)(id / 1000);
            int v2 = id % 1000;
            return int.Parse($"{v1.ToString("D3")}{v2.ToString("D3")}");  // yes i know this the wrong way to do this but guh
        }

        public bool HasCollision()
        {
            return collision != null;
        }

        public virtual bool IsDynamic()
        {
            return scale == Const.DYNAMIC_ASSET;
        }

        public virtual bool UseScale()
        {
            return !HasCollision() || IsDynamic();
        }

        public bool HasEmitter()
        {
            foreach (KeyValuePair<string, short> kvp in dummies)
            {
                if (kvp.Key.Contains("emitter")) { return true; }
            }
            return false;
        }
    }

    public class PickableInfo
    {
        public int id { get; set; }             // AEG id
        public string record { get; init; }     // record id from the container we are creating this pickable of
        public string name { get; init; }       // Actual name of the object, for display to the player
        public List<(string id, int quantity)> inventory { get; init; }

        public ModelInfo model { get; init; }

        public PickableInfo(PickableContent pickable, ModelInfo model)
        {
            record = pickable.id.ToLower();
            name = pickable.name ?? throw new Exception("Name cannot be null");
            inventory = pickable.inventory;

            this.model = model;
        }

        public string ActionText()
        {
            return $"Harvest {name}";
        }

        public string AssetPath()
        {
            int v1 = Const.PICKABLE_ASSET_GROUP + (int)(id / 1000);
            int v2 = id % 1000;
            return $"aeg{v1.ToString("D3")}\\aeg{v1.ToString("D3")}_{v2.ToString("D3")}";
        }

        public string AssetName()
        {
            int v1 = Const.PICKABLE_ASSET_GROUP + (int)(id / 1000);
            int v2 = id % 1000;
            return $"aeg{v1.ToString("D3")}_{v2.ToString("D3")}";
        }

        public int AssetRow()
        {
            int v1 = Const.PICKABLE_ASSET_GROUP + (int)(id / 1000);
            int v2 = id % 1000;
            return int.Parse($"{v1.ToString("D3")}{v2.ToString("D3")}");  // yes i know this the wrong way to do this but guh
        }

        public bool IsDynamic() { return true; }  // pickables are always dynamic
        public bool UseScale() { return true; }
    }

    /* contains info on type of water and it's files and filepaths */
    public class LiquidInfo
    {
        public int id { get; set; }
        public string path { get; init; }
        public List<Tuple<Int2, CollisionInfo>> collision { get; init; }   // Not true collision, just used for the water plane to have splashy splashers when you splash through it

        public LiquidInfo(int id, string path)
        {
            this.id = id;
            this.path = path;
            collision = new();
        }

        public string AssetPath()
        {
            int v1 = Const.WATER_ASSET_GROUP + (int)(id / 1000);
            int v2 = id % 1000;
            return $"aeg{v1.ToString("D3")}\\aeg{v1.ToString("D3")}_{v2.ToString("D3")}";
        }

        public string AssetName()
        {
            int v1 = Const.WATER_ASSET_GROUP + (int)(id / 1000);
            int v2 = id % 1000;
            return $"aeg{v1.ToString("D3")}_{v2.ToString("D3")}";
        }

        public int AssetRow()
        {
            int v1 = Const.WATER_ASSET_GROUP + (int)(id / 1000);
            int v2 = id % 1000;
            return int.Parse($"{v1.ToString("D3")}{v2.ToString("D3")}");  // yes i know this the wrong way to do this but guh
        }

        public void AddCollision(Int2 coordinate, CollisionInfo collisionInfo)
        {
            collision.Add(new(coordinate, collisionInfo));
        }

        public List<CollisionInfo> GetCollision()
        {
            List<CollisionInfo> ret = new();
            foreach(Tuple<Int2, CollisionInfo> tuple in collision)
            {
                ret.Add(tuple.Item2);
            }
            return ret;
        }

        public CollisionInfo GetCollision(Int2 coordinate)
        {
            foreach (Tuple<Int2, CollisionInfo> tuple in collision)
            {
                if (tuple.Item1 == coordinate)
                {
                    return tuple.Item2;
                }
            }
            return GetCollision(new Int2(0, 0)); // we use 0,0 as the default no cutout water plane collision. if this is missing it will stack overflow and crash. should never happen but lol
        }
    }

    public class CutoutInfo
    {
        public Int2 coordinate { get; init; }
        public CollisionInfo collision { get; init; }

        public CutoutInfo(Int2 coordinate, CollisionInfo collision)
        {
            this.coordinate = coordinate;
            this.collision = collision;
        }
    }

    public class CollisionInfo
    {
        public string name { get; init; } // Original nif name, for lookup from ESM records
        public string obj { get; init; }  // Relative path from the 'cache' folder to the converted obj file
        public string hkx { get; init; } // Relative path from the 'cache' folder to the converted hkx file

        public CollisionInfo(string name, string obj)
        {
            this.name = name.ToLower();
            this.obj = obj;
            this.hkx = obj.Replace(".obj", ".hkx");
        }
    }

    public class TextureInfo
    {
        public string name { get; init; } // Original dds texture name for lookup
        public string path { get; init; } // Relative path from the 'cache' folder to the converted tpf file
        public string low { get; init; }  // same as above but points to low detail texture

        public TextureInfo(string name, string path)
        {
            this.name = name.ToLower();
            this.path = path;
            this.low = path.Replace(".tpf.dcx", "_l.tpf.dcx");
        }
    }

    // little class i'm using for preprocessing scale info on meshes that will become assets.
    // for assets that have a lot of scaled versions placed down we make baked scaled versions
    // for 1 off scales we use dynamic assets instead
    public class PreModel
    {
        public string mesh { get; init; }
        public Dictionary<int, int> scales { get; init; }
        public bool forceCollision { get; set; }           // some morrowind nifs dont have collision meshes despite needing them. in SOME cases we use the visual mesh as collision
        public bool forceDynamic { get; set; }

        public PreModel(string mesh, bool forceCollision, bool forceDynamic)
        {
            this.mesh = mesh.Trim().ToLower();
            scales = new();
            this.forceCollision = forceCollision;
            this.forceDynamic = forceDynamic;
        }
    }
}
