using JortPob.Common;
using JortPob.Scripts;
using JortPob.Worker;
using PortJob;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using static JortPob.Scripts.Script;

namespace JortPob
{
    public class Main
    {
        public static void Convert()
        {
            /* Init */
            Lort.Initialize(); // startup logging
            Override.Initialize(); // load all override jsons
            Utility.InitSRGBCache();
            Oodler.Initialize();


            /* Loading stuff */
            ScriptManager scriptManager = new();                                              // Manages EMEVD scripts
            ESM esm = new ESM(scriptManager);                                                // Morrowind ESM parse and partial serialization
            Cache cache = Cache.Load(esm);                                                  // Load existing cache (FAST!) or generate a new one (SLOW!)
            TextManager text = new();                                                      // Manages FMG text files
            MenuTextureManager texManager = new(esm);                                     // Manages menu textures for things like inventory icons and loading screens
            Paramanager param = new(cache, text);                                        // Class for managing PARAM files
            SpeffManager speff = new(esm, param, scriptManager, texManager, text);      // Manages speff params, primarily for magic effects like potions and enchanted gear. NOT SPELLS!
            ItemManager item = new(esm, param, scriptManager, speff, texManager, text);                         // Handles generation and reampping of items
            Layout layout = new(cache, esm, param, text, scriptManager);                                       // Subdivides all content data from ESM into a more elden ring friendly format
            SoundManager sound = new();                                                                       // Manages vcbanks
            NpcManager character = new(esm, layout, sound, param, text, item, speff, scriptManager);         // Manages dialog esd

            /* Some quick setup stuff */
            scriptManager.SetupSpecialFlags(esm);

            /* Create some needed text data that is ref'd later */
            for (int i = 0; i <= 100; i++) { text.AddTopic($"Disposition: {i}"); }

            /* Write custom map */
            MapWorker.Go();

            /* replace the maptexinfo responsible for weather functions, sourced from Resources/other/mapinfotex.png */
            MapInfoTexWorker.Go(); // @TODO: this is like... designed as a worker but runs on 1 thread lol

            /* Replace openign cutscene */
            if(!Const.DEBUG_SKIP_CUTSCENES) { Cutscener.Create(Path.Combine(Const.MORROWIND_PATH, @"Data Files\video\mw_intro.bik"), 0040); }

            /* Collect content that needs script compiling for post MSB generation */
            List<PleaseCompile> contentToCompile = new();

            /* Generate exterior msbs from layout */
            List<ResourcePool> msbs = new();

            void GenerateMSB(IMSBCompilableGroup group)
            {
                // Just write empty tiles as empty msbs and scripts to prevent base game stuff from loading in the distance. Debug flag if you want to skip this behaviour
                if (group.IsEmpty && (Const.DEBUG_DONT_WRITE_BLANK_MSBS || group.IsInterior)) { return; }

                /* Generate msb from group */
                MSBE msb = new()
                {
                    Compression = Compression.KRAK()
                };

                BaseScript script = scriptManager.GetScript(group);
                LightManager lightManager = new(group.map, group.coordinate.x, group.coordinate.y, group.block);
                ResourcePool pool = new(group, msb, lightManager, script);

                /* Misc Indices */
                int nextC = 0, nextMPR = 0;

                string NewCollisionIndex() => $"{group.coordinate.x:D2}{group.coordinate.y:D2}{nextC++:D2}";

                /* Handle chunks */
                List<IMSBCompilableChunk> chunks = group.Chunks;
                for (int i = 0; i < chunks.Count; i++)
                {
                    IMSBCompilableChunk chunk = chunks[i];
                    MSBE.Part.Collision rootCollision = MakePart.Collision();
                    if (group.IsInterior)
                    {
                        /* Interior MSB drawgroup */
                        uint chunkDrawGroup = (uint)0 | ((uint)1 << i);

                        /* Interior MSB chunk collision root */
                        string collisionIndex = NewCollisionIndex();
                        rootCollision.Name = $"h{collisionIndex}_0000";
                        rootCollision.ModelName = $"h{collisionIndex}";
                        rootCollision.Position = chunk.root + Const.MSB_OFFSET - new Vector3(0f, chunk.bounds.Z, 0f);
                        rootCollision.Unk1.DisplayGroups[0] = 0;
                        rootCollision.Unk1.DisplayGroups[1] = chunkDrawGroup;
                        rootCollision.Unk1.CollisionMask[0] = 0;
                        rootCollision.Unk1.CollisionMask[1] = chunkDrawGroup;
                        msb.Parts.Collisions.Add(rootCollision);
                        pool.collisionIndices.Add(new Tuple<string, CollisionInfo>(collisionIndex, cache.defaultCollision));

                        /* Interior MSB shadow box */
                        ModelInfo shadowBoxModelInfo = cache.GetModel("interiorshadowbox");
                        MSBE.Part.Asset shadowBoxAsset = MakePart.Asset(shadowBoxModelInfo);
                        shadowBoxAsset.Position = chunk.root + Const.MSB_OFFSET;
                        shadowBoxAsset.Rotation = Vector3.Zero;
                        shadowBoxAsset.Scale = chunk.bounds;
                        shadowBoxAsset.Unk1.DisplayGroups[0] = 0;
                        shadowBoxAsset.UnkPartNames[1] = rootCollision.Name;
                        shadowBoxAsset.UnkPartNames[3] = rootCollision.Name;
                        shadowBoxAsset.UnkPartNames[5] = rootCollision.Name;
                        msb.Parts.Assets.Add(shadowBoxAsset);
                    }
                    else if (group is Tile tile)
                    {
                        List<Tuple<Vector3, TerrainInfo>> terrains = tile.terrain;

                        /* Add terrain */
                        foreach ((Vector3 position, TerrainInfo terrainInfo) in terrains)
                        {
                            /* Terrain and terrain collision */
                            // Render goes in superoverworld for long view distance. Collision goes in tile for optimization
                            // superoverworld msb is  handled by its own class -> OverworldManager
                            foreach (CollisionInfo collisionInfo in terrainInfo.collision)
                            {
                                string collisionIndex = NewCollisionIndex();

                                MSBE.Part.Collision collision = MakePart.Collision();
                                collision.Name = $"h{collisionIndex}_0000";
                                collision.ModelName = $"h{collisionIndex}";
                                collision.Position = position + Const.MSB_OFFSET;

                                msb.Parts.Collisions.Add(collision);
                                pool.collisionIndices.Add(new Tuple<string, CollisionInfo>(collisionIndex, collisionInfo));
                            }

                            /* Add water collision if terrain 'hasWater' */
                            /* Water is done on a cell by cell basis, so I am simply tying it to the terrain data here */
                            if (terrainInfo.hasWater)
                            {
                                LiquidInfo waterInfo = cache.GetWater();
                                CollisionInfo waterCollisionInfo = waterInfo.GetCollision(terrainInfo.coordinate);

                                /* Make collision for water splashing */
                                string collisionIndex = NewCollisionIndex();
                                MSBE.Part.Collision collision = MakePart.WaterCollision();
                                collision.Name = $"h{collisionIndex}_0000";
                                collision.ModelName = $"h{collisionIndex}";
                                collision.Position = position + Const.MSB_OFFSET;

                                msb.Parts.Collisions.Add(collision);
                                pool.collisionIndices.Add(new Tuple<string, CollisionInfo>(collisionIndex, waterCollisionInfo));
                            }

                            /* Add collision for cutouts. */
                            if (terrainInfo.hasSwamp || terrainInfo.hasLava) // minor hack, no cell has both swamp and lava so we don't actually differentiate here
                            {
                                CutoutInfo cutoutInfo = cache.GetCutout(terrainInfo.coordinate);
                                if (cutoutInfo != null)
                                {
                                    /* Make collision for swamp or lava splashy splashing, surface collision */
                                    string collisionIndex = NewCollisionIndex();
                                    MSBE.Part.Collision collision = MakePart.WaterCollision(); // also works for lava and poison
                                    collision.Name = $"h{collisionIndex}_0000";
                                    collision.ModelName = $"h{collisionIndex}";
                                    collision.Position = position + Const.MSB_OFFSET;
                                    msb.Parts.Collisions.Add(collision);
                                    pool.collisionIndices.Add(new Tuple<string, CollisionInfo>(collisionIndex, cutoutInfo.collision));

                                    /* Make collision for swamp or lava floor, caps the depth of pools so they arent super deep */
                                    collision = MakePart.Collision(); // also works for lava and poison
                                    collision.Name = $"h{collisionIndex}_0001";
                                    collision.ModelName = $"h{collisionIndex}";
                                    collision.Position = position + Const.MSB_OFFSET + new Vector3(0f, terrainInfo.hasLava ? Const.LAVA_FLOOR_DEPTH : Const.SWAMP_FLOOR_DEPTH, 0f);
                                    msb.Parts.Collisions.Add(collision);
                                }
                            }
                        }
                    }

                    /* Add assets */
                    foreach (AssetContent content in chunk.assets)
                    {
                        if (Override.CheckDoNotPlace(content.mesh)) { continue; } // skip any meshes listed in the do_not_place override json

                        /* Grab ModelInfo */
                        ModelInfo modelInfo = cache.GetModel(content.mesh, content.scale);

                        /* Make part */
                        MSBE.Part.Asset asset = MakePart.Asset(modelInfo);
                        asset.Position = content.relative + Const.MSB_OFFSET;
                        asset.Rotation = content.rotation;
                        asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);

                        if (group.IsInterior)
                        {
                            asset.Unk1.DisplayGroups[0] = 0;
                            asset.UnkPartNames[1] = rootCollision.Name;
                            asset.UnkPartNames[3] = rootCollision.Name;
                            asset.UnkPartNames[5] = rootCollision.Name;
                        }
                        /* Asset tileload config */
                        else if (group is HugeTile || group is BigTile)
                        {
                            asset.TileLoad.MapID = [(byte)0, (byte)content.load.y, (byte)content.load.x, (byte)group.map];
                            asset.TileLoad.Unk04 = 13;
                            asset.TileLoad.CullingHeightBehavior = -1;
                        }

                        /* Objects with script references added to PleaseCompile list */
                        if (content.entity > 0)
                        {
                            asset.EntityID = content.entity;
                            contentToCompile.Add(new PleaseCompileGroup(group, msb, script, content, asset));
                        }

                        /* If bed... */
                        if (content is BedContent bedContent)
                        {
                            (uint bed, uint respawn) entityIds = script.RegisterBed();

                            MSBE.Part.Enemy bed = MakePart.Bed();
                            bed.Position = asset.Position;
                            bed.Unk1.DisplayGroups[0] = 0;
                            bed.EntityID = entityIds.bed;
                            bed.TalkID = character.GetESD(group.IdList(), msb, bedContent);

                            if (group.IsInterior) {
                                bed.CollisionPartName = rootCollision.Name;   
                            }
                            msb.Parts.Enemies.Add(bed);

                            MSBE.Part.Player respawn = MakePart.Player();
                            respawn.Position = asset.Position;
                            respawn.EntityID = entityIds.respawn;
                            msb.Parts.Players.Add(respawn);
                        }

                        /* Add to msb */
                        msb.Parts.Assets.Add(asset);
                    }

                    /* Add doors */
                    foreach (DoorContent content in chunk.doors)
                    {
                        if (content.warp == null && Const.DEBUG_DISCARD_ANIMATED_DOORS) { continue; } // if the debug flag is set, skip any doors that are NOT load doors. useful for debugging until we get animated doors working

                        /* Grab ModelInfo */
                        ModelInfo modelInfo = cache.GetModel(content.mesh, content.scale);

                        /* Make part */
                        MSBE.Part.Asset asset = MakePart.Asset(modelInfo);
                        asset.Position = content.relative + Const.MSB_OFFSET;
                        asset.Rotation = content.rotation;
                        asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);

                        if (group.IsInterior)
                        {
                            asset.Unk1.DisplayGroups[0] = 0;
                            asset.UnkPartNames[1] = rootCollision.Name;
                            asset.UnkPartNames[3] = rootCollision.Name;
                            asset.UnkPartNames[5] = rootCollision.Name;
                        }

                        /* Objects with script references added to PleaseCompile list */
                        if (content.entity > 0)
                        {
                            asset.EntityID = content.entity;
                            contentToCompile.Add(new PleaseCompileGroup(group, msb, script, content, asset));
                        }

                        /* Add to msb */
                        msb.Parts.Assets.Add(asset);
                    }

                    /* Add warp destinations for load doors */
                    foreach (Layout.WarpDestination warp in chunk.warps)
                    {
                        MSBE.Part.Player player = MakePart.Player();
                        player.Position = warp.position + Const.MSB_OFFSET;
                        player.Rotation = warp.rotation;
                        player.EntityID = warp.id;
                        msb.Parts.Players.Add(player);
                    }

                    /* Add emitters */
                    foreach (EmitterContent content in chunk.emitters)
                    {
                        /* Grab ModelInfo */
                        EmitterInfo emitterInfo = cache.GetEmitter(content.id);
                        if (emitterInfo == null)
                        {
                            Lort.Log($" ## WARNING ## Skipping EmitterContent with id {content.id} as it has no associated EmitterInfo!", Lort.Type.Debug);
                            continue;
                        }

                        /* Make part */
                        MSBE.Part.Asset asset = MakePart.Asset(emitterInfo);
                        asset.Position = content.relative + Const.MSB_OFFSET;
                        asset.Rotation = content.rotation;
                        asset.Scale = new Vector3(content.scale * 0.01f); // converting from 100-0 int scale to 1f-0f scale

                        if (group.IsInterior)
                        {
                            asset.Unk1.DisplayGroups[0] = 0;
                            asset.UnkPartNames[1] = rootCollision.Name;
                            asset.UnkPartNames[3] = rootCollision.Name;
                            asset.UnkPartNames[5] = rootCollision.Name;
                        }

                        /* Objects with script references added to PleaseCompile list */
                        if (content.entity > 0)
                        {
                            asset.EntityID = content.entity;
                            contentToCompile.Add(new PleaseCompileGroup(group, msb, script, content, asset));
                        }

                        /* Add to msb */
                        msb.Parts.Assets.Add(asset);
                    }

                    /* Add lights */
                    foreach (LightContent light in chunk.lights)
                    {
                        lightManager.CreateLight(light);
                    }

                    /* Create humanoid NPCs (c0000) */
                    foreach (NpcContent npc in chunk.npcs)
                    {
                        MSBE.Part.Enemy enemy = MakePart.Npc();
                        enemy.Position = npc.relative + Const.MSB_OFFSET;
                        enemy.Rotation = npc.rotation;

                        if (group.IsInterior)
                        {
                            enemy.Unk1.DisplayGroups[0] = 0;
                            enemy.CollisionPartName = rootCollision.Name;
                        }

                        // If the npc is a deadbody we create a treasure on their body
                        List<(ItemManager.ItemInfo item, int quantity)> inventory = item.ResolveInventory(npc);
                        if (npc.dead && inventory.Count > 0)
                        {
                            MSBE.Event.Treasure treasure = MakePart.Treasure();
                            treasure.ItemLotID = param.GenerateDeadBodyItemLot(script, npc, inventory);

                            MSBE.Part.Asset treasurePart = MakePart.TreasureAsset();
                            treasurePart.Position = enemy.Position;

                            treasure.Name = $"DeadBodyTreaure->{npc.id}";
                            treasure.ActionButtonID = param.GenerateActionButtonItemParam($"Loot {npc.name}");
                            treasure.TreasurePartName = treasurePart.Name;

                            msb.Parts.Assets.Add(treasurePart);
                            msb.Events.Treasures.Add(treasure);
                        }

                        (int npc, int think, int init) paramRows = character.GetParams(item, script, npc); // creates/gets and returns NpcParam, NpcThinkParam, and CharInitParam
                        enemy.NPCParamID = paramRows.npc;
                        enemy.ThinkParamID = paramRows.think;
                        enemy.CharaInitID = paramRows.init;

                        if (!group.IsInterior)
                        {
                            /* Asset tileload config */
                            if (chunk.GetType() == typeof(HugeTile) || chunk.GetType() == typeof(BigTile))
                            {
                                enemy.TileLoad.MapID = [(byte)0, (byte)npc.load.y, (byte)npc.load.x, (byte)group.map];
                                //enemy.TileLoad.Unk04 = 13;
                            }
                        }

                        /* Objects with script references added to PleaseCompile list */
                        if (npc.entity > 0)
                        {
                            enemy.EntityID = npc.entity;
                            contentToCompile.Add(new PleaseCompileGroup(group, msb, script, npc, enemy));
                        }

                        /* Add to msb */
                        msb.Parts.Enemies.Add(enemy);
                    }

                    /* Creatures */
                    foreach (CreatureContent creature in chunk.creatures)
                    {
                        Override.EnemyRemap remap = Override.GetEnemyRemap(creature.id);

                        MSBE.Part.Enemy enemy = MakePart.Creature(remap.character);
                        enemy.Position = creature.relative + Const.MSB_OFFSET;
                        enemy.Rotation = creature.rotation;

                        if (group.IsInterior)
                        {
                            enemy.Unk1.DisplayGroups[0] = 0;
                            enemy.CollisionPartName = rootCollision.Name;
                        }

                        (int npc, int think, int init) paramRows = character.GetParams(item, script, creature, remap); // creates/gets and returns NpcParam, NpcThinkParam, and CharInitParam
                        enemy.NPCParamID = paramRows.npc;
                        enemy.ThinkParamID = paramRows.think;
                        enemy.CharaInitID = paramRows.init;

                        /* Objects with script references added to PleaseCompile list */
                        if (creature.entity > 0)
                        {
                            enemy.EntityID = creature.entity;
                            contentToCompile.Add(new PleaseCompileGroup(group, msb, script, creature, enemy));
                        }

                        /* Add to msb */
                        msb.Parts.Enemies.Add(enemy);
                    }

                    /* Add items */
                    foreach (ItemContent content in chunk.items)
                    {
                        if (Override.CheckDoNotPlace(content.mesh)) { continue; } // skip any meshes listed in the do_not_place override json

                        /* Grab ModelInfo + iteminfo */
                        ItemManager.ItemInfo itemInfo = item.GetItem(content.id);
                        ModelInfo modelInfo;
                        if (itemInfo != null) { modelInfo = cache.GetModel(content.mesh, Const.DYNAMIC_ASSET); } // if we have a treasure for this assset it MUST be dynamic
                        else { modelInfo = cache.GetModel(content.mesh, content.scale); }  // otherwise it doesn't matter. treasure events can only work on dynamic assets for whatever reason

                        /* Make part */
                        MSBE.Part.Asset asset = MakePart.Asset(modelInfo);
                        asset.Position = content.relative + Const.MSB_OFFSET;
                        asset.Rotation = content.rotation;
                        asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);

                        if (group.IsInterior)
                        {
                            asset.Unk1.DisplayGroups[0] = 0;
                            asset.UnkPartNames[1] = rootCollision.Name;
                            asset.UnkPartNames[3] = rootCollision.Name;
                            asset.UnkPartNames[5] = rootCollision.Name;
                        }

                        /* Setup item treasure if it exists */
                        if (itemInfo != null)
                        {
                            MSBE.Event.Treasure treasure = MakePart.Treasure();
                            if (content.entity <= 0) { content.entity = script.CreateEntity(EntityType.Asset, content.id); }  // if this content does not yet have an entity id, give it one
                            treasure.ItemLotID = param.GenerateContentItemLot(script, content, itemInfo);
                            treasure.Name = $"ItemTreasure->{content.id}";
                            treasure.ActionButtonID = param.GenerateActionButtonItemParam(content.ActionText());
                            treasure.TreasurePartName = asset.Name;

                            msb.Events.Treasures.Add(treasure);
                        }

                        /* Objects with script references added to PleaseCompile list */
                        if (content.entity > 0)
                        {
                            asset.EntityID = content.entity;
                            contentToCompile.Add(new PleaseCompileGroup(group, msb, script, content, asset));
                        }

                        /* Add to msb */
                        msb.Parts.Assets.Add(asset);
                    }

                    /* Add pickables */
                    foreach (PickableContent content in chunk.pickables)
                    {
                        if (Override.CheckDoNotPlace(content.mesh)) { continue; } // skip any meshes listed in the do_not_place override json

                        /* Grab ModelInfo */
                        PickableInfo pickableInfo = cache.GetPickableModel(content);

                        /* Make part */
                        MSBE.Part.Asset asset = MakePart.Asset(pickableInfo);
                        asset.Position = content.relative + Const.MSB_OFFSET;
                        asset.Rotation = content.rotation;
                        asset.Scale = new Vector3(content.scale * 0.01f);  // converting from 100-0 int scale to 1f-0f scale

                        if (group.IsInterior)
                        {
                            asset.Unk1.DisplayGroups[0] = 0;
                            asset.UnkPartNames[1] = rootCollision.Name;
                            asset.UnkPartNames[3] = rootCollision.Name;
                            asset.UnkPartNames[5] = rootCollision.Name;
                        }

                        /* Objects with script references added to PleaseCompile list */
                        if (content.entity > 0)
                        {
                            asset.EntityID = content.entity;
                            contentToCompile.Add(new PleaseCompileGroup(group, msb, script, content, asset));
                        }

                        /* Add to msb */
                        msb.Parts.Assets.Add(asset);
                    }

                    /* Add container */
                    foreach (ContainerContent content in chunk.containers)
                    {
                        if (Override.CheckDoNotPlace(content.mesh)) { continue; } // skip any meshes listed in the do_not_place override json

                        /* Grab ModelInfo + iteminfo */
                        List<(ItemManager.ItemInfo item, int quantity)> inventory = item.ResolveInventory(content);
                        ModelInfo modelInfo;
                        if (inventory.Count > 0) { modelInfo = cache.GetModel(content.mesh, Const.DYNAMIC_ASSET); } // if we have a treasure for this assset it MUST be dynamic
                        else { modelInfo = cache.GetModel(content.mesh, content.scale); }  // otherwise it doesn't matter. treasure events can only work on dynamic assets for whatever reason

                        /* Make part */
                        MSBE.Part.Asset asset = MakePart.Asset(modelInfo);
                        asset.Position = content.relative + Const.MSB_OFFSET;
                        asset.Rotation = content.rotation;
                        asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);

                        if (group.IsInterior)
                        {
                            asset.Unk1.DisplayGroups[0] = 0;
                            asset.UnkPartNames[1] = rootCollision.Name;
                            asset.UnkPartNames[3] = rootCollision.Name;
                            asset.UnkPartNames[5] = rootCollision.Name;
                        }

                        /* Setup container treasure if it exists */
                        if (inventory.Count > 0)
                        {
                            MSBE.Event.Treasure treasure = MakePart.Treasure();
                            if (content.entity <= 0) { content.entity = script.CreateEntity(EntityType.Asset, content.id); }  // if this content does not yet have an entity id, give it one
                            treasure.ItemLotID = param.GenerateContainerItemLot(script, content, inventory);
                            treasure.Name = $"ContainerTreasure->{content.id}";
                            treasure.ActionButtonID = param.GenerateActionButtonItemParam(content.ActionText());
                            treasure.TreasurePartName = asset.Name;

                            msb.Events.Treasures.Add(treasure);
                        }

                        /* Objects with script references added to PleaseCompile list */
                        if (content.entity > 0)
                        {
                            asset.EntityID = content.entity;
                            contentToCompile.Add(new PleaseCompileGroup(group, msb, script, content, asset));
                        }

                        /* Add to msb */
                        msb.Parts.Assets.Add(asset);
                    }

                    /* Add scripted positions */
                    foreach (Layout.ScriptedPosition sp in chunk.positions)
                    {
                        MSBE.Part.Player player = MakePart.Player();
                        player.Position = sp.relative + Const.MSB_OFFSET;
                        player.Rotation = sp.rotation;
                        player.EntityID = sp.player;
                        msb.Parts.Players.Add(player);

                        MSBE.Region.Other region = new()
                        {
                            Name = $"ScriptedPosition:{sp.position}",
                            Shape = new MSB.Shape.Point(),
                            Position = sp.relative + Const.MSB_OFFSET,
                            Rotation = sp.rotation,
                            RegionID = nextMPR++,
                            EntityID = sp.region,
                            MapStudioLayer = 4294967295
                        };
                        msb.Regions.Add(region);
                    }

                    /* Add PathGridPoints */
                    foreach (Layout.PathGridPoint point in chunk.paths)
                    {
                        MSBE.Region.PatrolRoute region = MakePart.PatrolRoute();
                        region.Name = point.name;
                        region.Shape = new MSB.Shape.Sphere(Const.PATH_REGION_SIZE);
                        region.Position = point.position + Const.MSB_OFFSET;
                        region.RegionID = nextMPR++;
                        region.EntityID = point.entity;

                        msb.Regions.PatrolRoutes.Add(region);
                    }

                    /* Add TravelPoints */
                    foreach (Layout.TravelPoint travel in chunk.travels)
                    {
                        MSBE.Region.PatrolRoute region = MakePart.PatrolRoute();
                        region.Name = travel.name;
                        region.Shape = new MSB.Shape.Sphere(travel.radius);
                        region.Position = travel.relative + Const.MSB_OFFSET;
                        region.RegionID = nextMPR++;
                        region.EntityID = travel.entity;

                        msb.Regions.PatrolRoutes.Add(region);
                    }

                    /* Handle area names */
                    if (group is Tile || group.IsInterior)
                    {
                        foreach (Layout.MapPoint point in chunk.points)
                        {
                            MSBE.Region.MapPoint mpr = MakePart.MapPoint();
                            int paramId;
                            if (group.IsInterior)
                            {
                                paramId = int.Parse($"60{group.map:D2}{group.coordinate.x:D2}{nextMPR:D2}");
                                mpr.Shape = new MSB.Shape.Box(chunk.bounds.X, chunk.bounds.Z, chunk.bounds.Y);
                                mpr.Position = point.relative + Const.MSB_OFFSET - new Vector3(0f, chunk.bounds.Y / 2f, 0f);
                                mpr.EntityID = scriptManager.areas[chunk.cells[0]]; // entity ids for area covering regions are generated early in build (Layout.cs constructor) but only assigned now
                            }
                            else
                            {
                                paramId = int.Parse($"61{group.coordinate.x:D2}{group.coordinate.y:D2}{nextMPR:D2}");
                                mpr.Shape = new MSB.Shape.Sphere(point.radius);
                                mpr.Position = point.relative + Const.MSB_OFFSET;
                                mpr.EntityID = script.CreateEntity(EntityType.Region, point.name);
                                
                            }
                            mpr.Name = $"{point.name} placename";
                            mpr.RegionID = nextMPR++;
                            mpr.WorldMapPointParamID = param.GenerateWorldMapPoint(group, point, paramId);

                            msb.Regions.MapPoints.Add(mpr);
                            if (point.important) { scriptManager.AddLocation(point.name, mpr.EntityID); }
                        }
                    }
                }

                /* EnvMap & REM for interior */
                // @TODO: make this per chunk so we can setup different rems for different interiors
                if (group.IsInterior)
                {
                    /* Create envmap texture file */
                    int envId = 200;
                    int size = 4096, crossfade = 8;
                    EnvManager.CreateEnvMaps((InteriorGroup)group, envId);

                    /* Create an envbox */
                    MSBE.Region.EnvironmentMapEffectBox envBox = MakePart.EnvBox();
                    envBox.Name = $"Env_Box{envId:D3}";
                    envBox.Shape = new MSB.Shape.Box(size + crossfade, size + crossfade, size + crossfade);
                    envBox.Position = new Vector3(0f, size * -0.5f, 0f) + Const.MSB_OFFSET;
                    envBox.TransitionDist = crossfade / 2f;
                    msb.Regions.EnvironmentMapEffectBoxes.Add(envBox);

                    MSBE.Region.EnvironmentMapPoint envPoint = MakePart.EnvPoint();
                    envPoint.Name = $"Env_Point{envId:D3}";
                    envPoint.Position = new Vector3(0f, size * -0.5f, 0f) + Const.MSB_OFFSET;
                    envPoint.UnkMapID = [(byte)group.map, (byte)group.coordinate.x, (byte)group.coordinate.y, (byte)group.block];
                    msb.Regions.EnvironmentMapPoints.Add(envPoint);
                }

                /* Auto resource */
                AutoResource.Generate(group.map, group.coordinate.x, group.coordinate.y, group.block, msb);

                /* Done */
                msbs.Add(pool);
                Lort.TaskIterate(); // Progress bar update
            }

            /* Generate exterior msbs from layout */
            Lort.Log($"Generating {layout.TileCount} exterior msbs...", Lort.Type.Main);
            Lort.NewTask("Generating MSB", layout.TileCount);

            foreach (IMSBCompilableGroup group in layout.AllTiles)
            {
                GenerateMSB(group);
            }

            /* Generate interior msbs from interiorgroups */
            Lort.Log($"Generating {layout.InteriorCount} interior msbs...", Lort.Type.Main);
            Lort.NewTask("Generating MSB", layout.InteriorCount);

            foreach (IMSBCompilableGroup group in layout.Interiors)
            {
                GenerateMSB(group);
            }

            /* Generate navmeshes and then build nvas and nvbnds */
            /* First start by grabbing all the nav scene representations and converting them OBJ -> HKX -> NAV */
            if (!Const.DEBUG_SKIP_NAVMESH)
            {
                List<string> objs = new();
                foreach (BaseTile bt in layout.Tiles)
                {
                    if (bt is not Tile tile || tile.IsEmpty) { continue; } // skip big/huge tiles and empty tiles
                    tile.FinalizeTerrainNav(); // does some stuff to finish up nav repersentation scene of the tile
                    string objPath = Path.Combine(Const.CACHE_PATH, $@"nav\m{tile.map:D2}_{tile.coordinate.x:D2}_{tile.coordinate.y:D2}_{tile.block:D2}.obj");
                    tile.nav.collapse(Obj.CollisionMaterial.Stock).optimize().write(objPath);
                    objs.Add(objPath);
                }
                foreach (InteriorGroup group in layout.Interiors)
                {
                    if (group.IsEmpty) { continue; } // skip empty groups

                    for (int i = 0; i < group.chunks.Count; i++)
                    {
                        InteriorGroup.Chunk chunk = group.chunks[i];
                        string objPath = Path.Combine(Const.CACHE_PATH, $@"nav\m{group.map:D2}_{group.area:D2}_{group.unk:D2}_{group.block:D2}-{i:D2}.obj");
                        if (Const.DEBUG_REUSE_FILES && File.Exists(objPath)) { objs.Add(objPath); continue; } // if debug_reuse is on, skip if file already created
                        chunk.nav.collapse(Obj.CollisionMaterial.Stock).optimize().write(objPath);
                        objs.Add(objPath);
                    }
                }

                NavWorker.Go(objs);

                /* After all the nav conversions are finshed we can now do nvas and nvbnds */
                Lort.Log($"Binding {layout.TileCount + layout.InteriorCount} NVBNDs...", Lort.Type.Main);
                Lort.NewTask("Binding NVBNDs", layout.TileCount + layout.InteriorCount);
                foreach (BaseTile bt in layout.Tiles)
                {
                    if (bt is not Tile tile || tile.IsEmpty) { continue; } // skip big/huge tiles

                    /* Some vars */
                    int nextNavId = int.Parse($"1{tile.coordinate.x:D2}{tile.coordinate.y:D2}00000");
                    string mid = $"{tile.map:D2}_{tile.coordinate.x:D2}_{tile.coordinate.y:D2}_{tile.block:D2}";
                    string objPath = Path.Combine(Const.CACHE_PATH, $@"nav\m{mid}.obj");
                    string nnavPath = Path.ChangeExtension(objPath, ".n.nav");
                    string onavPath = Path.ChangeExtension(objPath, ".o.nav");

                    /* Create NVA */
                    SoulsFormats.NVA nva = new();
                    nva.Compression = Compression.KRAK();
                    NVA.Entry11 entry11 = new();
                    entry11.Unk00 = Const.NVA_NV_UNK00_MAGIC;
                    entry11.Unk04 = 0;
                    entry11.Unk08 = 0;
                    entry11.Unk0C = 0;
                    nva.Entries11.Add(entry11);
                    nva.Navmeshes.Version = 4;

                    /* Create NVBND */
                    BND4 nvbnd = new();
                    nvbnd.Compression = Compression.KRAK();
                    nvbnd.Version = "07D7R6";

                    /* Add navmesh entry to NVA */
                    NVA.Navmesh navMesh = new();
                    int nextN = int.Parse($"{tile.coordinate.x:D2}{tile.coordinate.y:D2}{0:D2}");
                    navMesh.NameID = nextNavId;
                    navMesh.ModelID = nextN;
                    navMesh.IsConnectedNavmeshesInline = true;
                    navMesh.Position = new(Const.MSB_OFFSET, 1f);
                    navMesh.Rotation = new(0f);
                    navMesh.Scale = new(1f, 1f, 1f, 0f);
                    navMesh.FaceCount = Obj.GetFaceCount(objPath); // might be unnesscary? doesn't really seem to do anything?
                    navMesh.Unk3C = 0;
                    navMesh.Unk4C = 0;
                    navMesh.Unk44 = 1075419545; // magic number used
                    nva.Navmeshes.Add(navMesh);

                    /* Add navmesh file to NVBND */
                    BinderFile nbf = new();
                    nbf.Bytes = File.ReadAllBytes(nnavPath);
                    nbf.ID = 10000;
                    nbf.Name = $"N:\\GR\\data\\INTERROOT_win64\\map\\m{mid}\\navimesh\\bind6\\n{mid}_{nextN:D6}.hkx";
                    nvbnd.Files.Add(nbf);

                    BinderFile obf = new();
                    obf.Bytes = File.ReadAllBytes(onavPath);
                    obf.ID = 20000;
                    obf.Name = $"N:\\GR\\data\\INTERROOT_win64\\map\\m{mid}\\navimesh\\bind6\\o{mid}_{nextN:D6}.hkx";
                    nvbnd.Files.Add(obf);

                    /* Write Files */
                    nva.Write(Path.Combine(Const.OUTPUT_PATH, "map", $"m{tile.map:D2}", $"m{mid}", $"m{mid}.nva.dcx"));
                    nvbnd.Write(Path.Combine(Const.OUTPUT_PATH, "map", $"m{tile.map:D2}", $"m{mid}", $"m{mid}.nvmhktbnd.dcx"));
                    Lort.TaskIterate();
                }
                foreach (InteriorGroup group in layout.Interiors)
                {
                    /* Some vars */
                    int bid = 10000;
                    int nextNavId = int.Parse($"{group.map:D2}{group.area:D2}00000");
                    string mid = $"{group.map:D2}_{group.area:D2}_{group.unk:D2}_{group.block:D2}";

                    /* Create NVA */
                    SoulsFormats.NVA nva = new();
                    nva.Compression = Compression.KRAK();
                    NVA.Entry11 entry11 = new();
                    entry11.Unk00 = Const.NVA_NV_UNK00_MAGIC;
                    entry11.Unk04 = 0;
                    entry11.Unk08 = 0;
                    entry11.Unk0C = 0;
                    nva.Entries11.Add(entry11);
                    nva.Navmeshes.Version = 4;

                    /* Create NVBND */
                    BND4 nvbnd = new();
                    nvbnd.Compression = Compression.KRAK();
                    nvbnd.Version = "07D7R6";

                    for (int i = 0; i < group.chunks.Count; i++)
                    {
                        if (group.IsEmpty) { break; }  // if group is empty dont bother adding entries. just generate a blank nva/nvbnd

                        InteriorGroup.Chunk chunk = group.chunks[i];
                        string objPath = Path.Combine(Const.CACHE_PATH, $@"nav\m{group.map:D2}_{group.area:D2}_{group.unk:D2}_{group.block:D2}-{i:D2}.obj");
                        string nnavPath = Path.ChangeExtension(objPath, ".n.nav");
                        string onavPath = Path.ChangeExtension(objPath, ".o.nav");

                        /* Add navmesh entry to NVA */
                        NVA.Navmesh navMesh = new();
                        int nextN = int.Parse($"{group.map:D2}{group.area:D2}{i:D2}");
                        navMesh.NameID = nextNavId;
                        navMesh.ModelID = nextN;
                        navMesh.IsConnectedNavmeshesInline = true;
                        navMesh.Position = new(Const.MSB_OFFSET, 1f);
                        navMesh.Rotation = new(0f);
                        navMesh.Scale = new(1f, 1f, 1f, 0f);
                        navMesh.FaceCount = Obj.GetFaceCount(objPath); // might be unnesscary? doesn't really seem to do anything?
                        navMesh.Unk3C = 0;
                        navMesh.Unk4C = 0;
                        navMesh.Unk44 = 1075419545; // magic number used
                        nva.Navmeshes.Add(navMesh);

                        /* Add navmesh file to NVBND */
                        BinderFile nbf = new();
                        nbf.Bytes = File.ReadAllBytes(nnavPath);
                        nbf.ID = bid;
                        nbf.Name = $"N:\\GR\\data\\INTERROOT_win64\\map\\m{mid}\\navimesh\\bind6\\n{mid}_{nextN:D6}.hkx";
                        nvbnd.Files.Add(nbf);

                        BinderFile obf = new();
                        obf.Bytes = File.ReadAllBytes(onavPath);
                        obf.ID = 10000 + bid;
                        obf.Name = $"N:\\GR\\data\\INTERROOT_win64\\map\\m{mid}\\navimesh\\bind6\\o{mid}_{nextN:D6}.hkx";
                        nvbnd.Files.Add(obf);

                        bid++;

                        nextNavId += 10;
                    }

                    /* Write Files */
                    nva.Write(Path.Combine(Const.OUTPUT_PATH, "map", $"m{group.map:D2}", $"m{mid}", $"m{mid}.nva.dcx"));
                    nvbnd.Write(Path.Combine(Const.OUTPUT_PATH, "map", $"m{group.map:D2}", $"m{mid}", $"m{mid}.nvmhktbnd.dcx"));
                    Lort.TaskIterate();
                }
            }


            /* Compile Dialog as ESD and Papyrus as EMEVD now that main generation has finished */
            /* Compile Papyrus main global script and it's subscripts */
            Lort.Log($"Processing {contentToCompile.Count + 1} scripts...", Lort.Type.Main);
            Lort.NewTask("Processing Scripts", (contentToCompile.Count + 1) * 3);

            /* Initialize local variables first */
            if (!Const.DEBUG_SKIP_SCRIPTS)
            {
                Papyrus papyrusMain = esm.GetPapyrus("main");   // null check is needed because the vanilla "Main" script won't compile rn. only works with the compatibility patch
                if (papyrusMain != null) { PapyrusEMEVD.InitializeLocalVariables(esm, scriptManager, scriptManager.common, papyrusMain, null); }
                Lort.TaskIterate();
                foreach (PleaseCompile compile in contentToCompile)
                {
                    if (compile.content is BedContent) { continue; } // bed scripts become ESD c1000's

                    Papyrus papyrus = esm.GetPapyrus(compile.content.papyrus);
                    if (papyrus != null) { PapyrusEMEVD.InitializeLocalVariables(esm, scriptManager, compile.script, papyrus, compile.content); }

                    if (compile.content is CharacterContent cc) { character.SetupPackages(compile.msb, compile.script, cc); } // ai packages must be set up before emevd compile starts

                    Lort.TaskIterate();
                }

                /* Then compile papyrus and dialog */
                if (papyrusMain != null) { PapyrusEMEVD.Compile(esm, layout, null, sound.main, scriptManager, param, item, speff, scriptManager.common, papyrusMain, null); }
                Lort.TaskIterate();
                foreach (PleaseCompile compile in contentToCompile)
                {
                    if (compile.content is BedContent) { continue; } // bed scripts become ESD c1000's

                    Papyrus papyrus = esm.GetPapyrus(compile.content.papyrus);
                    if (papyrus != null) { PapyrusEMEVD.Compile(esm, layout, compile.msb, sound.main, scriptManager, param, item, speff, compile.script, papyrus, compile.content); }

                    if (compile.content is CharacterContent characterContent)
                    {
                        MSBE.Part.Enemy enemyPart = compile.part as MSBE.Part.Enemy;
                        if (compile is PleaseCompileGroup pcg) { enemyPart.TalkID = character.GetESD(pcg.group.IdList(), pcg.msb, characterContent); }
                    }
                    else if (compile.content is DoorContent dc && dc.warp != null)
                    {
                        if (!(papyrus != null && (papyrus.HasCall(Papyrus.Call.Type.OnActivate) || papyrus.HasCall(Papyrus.Call.Type.Activate))))
                        {
                            compile.script.RegisterLoadDoor(param, dc); // only register a basic load door script if door doesn't seem to have a custom script attached
                        }
                    }

                    Lort.TaskIterate();
                }

                foreach (PleaseCompile compile in contentToCompile)
                {
                    PapyrusEMEVD.Finalize(scriptManager, compile.script, compile.content);
                    Lort.TaskIterate();
                }
            }

            /* Geneate debug area in Stranded Graveyard */
            Lort.Log($"Generating debug warp zone...", Lort.Type.Main);
            WarpZone.Generate(layout, scriptManager, param);

            /* Write sound BNKs */
            sound.Write();

            /* Write ESD bnds */
            character.Write();

            /* Generate some needed scripts after msb gen */
            scriptManager.GenerateAreaEvents();
            scriptManager.GenerateGlobalCrimeAbsolvedEvent();
            scriptManager.GenerateGlobalResetHostilityEvent();

            /* Generate some params and write to file */
            Lort.Log($"Creating PARAMs...", Lort.Type.Main);
            param.GeneratePartDrawParams();
            param.GenerateAssetRows(cache.assets);
            param.GenerateAssetRows(cache.emitters);
            param.GeneratePickableAssetRows(item, cache.GetPickables());
            param.GenerateAssetRows(cache.liquids);
            param.GenerateMapInfoParam(layout);
            param.GenerateTexInfoParam(esm.regions);
            param.GenerateCustomCharacterCreation();
            param.GenerateLoadingMenuRows(text);
            param.Write();

            /* Write FMGs */
            Lort.Log($"Binding FMGs...", Lort.Type.Main);
            text.Write();

            /* Write FXR files */
            Lort.Log($"Binding FXRs...", Lort.Type.Main);
            FxrManager.Write(layout);

            /* Bind and write all materials and textures */
            Bind.BindMaterials(Path.Combine(Const.OUTPUT_PATH, "material", "allmaterial.matbinbnd.dcx"));
            Bind.BindTPF(cache, layout.ListCommon());
            texManager.Write();

            /* Bind all assets */    // Multithreaded because slow
            Lort.Log($"Binding {cache.assets.Count} assets...", Lort.Type.Main);
            Lort.NewTask("Binding Assets", cache.assets.Count);
            Bind.BindAssets(cache);
            Bind.BindEmitters(cache);
            Bind.BindPickables(cache);
            foreach (LiquidInfo water in cache.liquids)  // bind up them waters toooooo
            {
                Bind.BindAsset(water, Path.Combine(Const.OUTPUT_PATH, $@"asset\aeg\{water.AssetPath()}.geombnd.dcx"));
            }

            /* Generate overworld */
            ResourcePool overworld = OverworldManager.Generate(cache, esm, layout, param);
            msbs.Insert(0, overworld); // this one takes the longest so we put it first so that the thread working on it has plenty of time to finish

            /* Write emevd scripts */
            scriptManager.Write();

            /* Write msbs */
            esm = null;  // free some memory here
            param = null;
            GC.Collect();
            MsbWorker.Go(msbs);

            /* Copy DLLs */
            string[] dlls = Directory.GetFiles(Utility.ResourcePath("dlls"));
            Lort.Log($"Copying {dlls.Length} DLLs", Lort.Type.Main);
            Lort.NewTask("Copying DLLs", dlls.Length);
            foreach (string file in dlls)
            {
                string fileName = Path.Combine(Const.OUTPUT_PATH, Path.GetFileName(file));
                if (!File.Exists(fileName))
                {
                    File.Copy(file, fileName);
                }
                Lort.TaskIterate();
            }

            /* Donezo */
            Lort.Log("Done!", Lort.Type.Main);
            Lort.NewTask("Done!", 1);
            Lort.TaskIterate();
        }

        public abstract record PleaseCompile(MSBE msb, BaseScript script, Content content, MSBE.Part part) { }
        public record PleaseCompileGroup(IMSBCompilableGroup group, MSBE msb, BaseScript script, Content content, MSBE.Part part) : PleaseCompile(msb, script, content, part) { }
    }
}
