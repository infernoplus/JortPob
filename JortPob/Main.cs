using HKLib.hk2018.hkaiCollisionAvoidance;
using IronPython.Hosting;
using JortPob.Common;
using JortPob.Worker;
using Microsoft.Scripting.Hosting;
using PortJob;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Text.Json.Nodes;
using static IronPython.Modules._ast;
using static JortPob.InteriorGroup;
using static JortPob.Script;
using static SoulsFormats.MSBAC4.Event;

namespace JortPob
{
    public class Main
    {
        public static void Convert()
        {
            /* Init */
            Lort.Initialize(); // startup logging
            Override.Initialize(); // load all override jsons

            /* Loading stuff */
            ScriptManager scriptManager = new();                                                // Manages EMEVD scripts
            ESM esm = new ESM(scriptManager);                                               // Morrowind ESM parse and partial serialization
            Cache cache = Cache.Load(esm);                                                  // Load existing cache (FAST!) or generate a new one (SLOW!)
            TextManager text = new();                                                           // Manages FMG text files
            IconManager icon = new(esm);                                                       // Manages the creation and assignment of item icons
            Paramanager param = new(text);                                                        // Class for managing PARAM files
            SpeffManager speff = new(esm, param, icon, text);                                                   // Manages speff params, primarily for magic effects like potions and enchanted gear. NOT SPELLS!
            ItemManager item = new(esm, param, speff, icon, text);                                                   // Handles generation and reampping of items
            Layout layout = new(cache, esm, param, text, scriptManager);                          // Subdivides all content data from ESM into a more elden ring friendly format
            SoundManager sound = new();                                                         // Manages vcbanks
            NpcManager character = new(esm, sound, param, text, item, scriptManager);                 // Manages dialog esd


            // Helpers/shared values
            List<Tuple<Vector3, TerrainInfo>> emptyTerrainList = [];

            /* Some quick setup stuff */
            scriptManager.SetupSpecialFlags(esm);

            /* Create some needed text data that is ref'd later */
            for (int i = 0; i <= 100; i++) { text.AddTopic($"Disposition: {i}"); }

            /* Generate exterior msbs from layout */
            List<ResourcePool> msbs = new();

            Lort.Log($"Generating {layout.tiles.Count} exterior msbs...", Lort.Type.Main);
            Lort.NewTask("Generating MSB", layout.tiles.Count);

            foreach (BaseTile tile in layout.all)
            {
                // Just write empty tiles as empty msbs and scripts to prevent base game stuff from loading in the distance. Debug flag if you want to skip this behaviour
                if (tile.IsEmpty() && Const.DEBUG_DONT_WRITE_BLANK_MSBS) { continue; }

                /* Generate msb from tile */
                MSBE msb = new MSBE();
                msb.Compression = SoulsFormats.DCX.Type.DCX_KRAK;

                Script script = scriptManager.GetScript(tile);
                bool isTileType = tile.GetType() == typeof(Tile);
                List<Tuple<Vector3, TerrainInfo>> terrains = isTileType ? tile.terrain : emptyTerrainList;
                LightManager lightManager = new(tile.map, tile.coordinate, tile.block);
                ResourcePool pool = new(tile, msb, lightManager, script);

                /* Various Indices */
                int nextC = 0, nextMPR = 0;

                /* Add terrain */
                foreach ((Vector3 position, TerrainInfo terrainInfo) in terrains)
                {
                    /* Terrain and terrain collision */
                    // Render goes in superoverworld for long view distance. Collision goes in tile for optimization
                    // superoverworld msb is  handled by its own class -> OverworldManager
                    foreach (CollisionInfo collisionInfo in terrainInfo.collision)
                    {
                        string collisionIndex = $"{tile.coordinate.x.ToString("D2")}{tile.coordinate.y.ToString("D2")}{nextC++.ToString("D2")}";

                        MSBE.Part.Collision collision = MakePart.Collision();
                        collision.Name = $"h{collisionIndex}_0000";
                        collision.ModelName = $"h{collisionIndex}";
                        collision.Position = position + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;

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
                        string collisionIndex = $"{tile.coordinate.x.ToString("D2")}{tile.coordinate.y.ToString("D2")}{nextC++.ToString("D2")}";
                        MSBE.Part.Collision collision = MakePart.WaterCollision();
                        collision.Name = $"h{collisionIndex}_0000";
                        collision.ModelName = $"h{collisionIndex}";
                        collision.Position = position + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;

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
                            string collisionIndex = $"{tile.coordinate.x.ToString("D2")}{tile.coordinate.y.ToString("D2")}{nextC++.ToString("D2")}";
                            MSBE.Part.Collision collision = MakePart.WaterCollision(); // also works for lava and poison
                            collision.Name = $"h{collisionIndex}_0000";
                            collision.ModelName = $"h{collisionIndex}";
                            collision.Position = position + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                            msb.Parts.Collisions.Add(collision);
                            pool.collisionIndices.Add(new Tuple<string, CollisionInfo>(collisionIndex, cutoutInfo.collision));

                            /* Make collision for swamp or lava floor, caps the depth of pools so they arent super deep */
                            collision = MakePart.Collision(); // also works for lava and poison
                            collision.Name = $"h{collisionIndex}_0001";
                            collision.ModelName = $"h{collisionIndex}";
                            collision.Position = position + Const.TEST_OFFSET1 + Const.TEST_OFFSET2 + new Vector3(0f, terrainInfo.hasLava ? Const.LAVA_FLOOR_DEPTH : Const.SWAMP_FLOOR_DEPTH, 0f);
                            msb.Parts.Collisions.Add(collision);
                        }
                    }
                }

                /* Add assets */
                foreach (AssetContent content in tile.assets)
                {
                    if (Override.CheckDoNotPlace(content.mesh)) { continue; } // skip any meshes listed in the do_not_place override json

                    /* Grab ModelInfo */
                    ModelInfo modelInfo = cache.GetModel(content.mesh, content.scale);

                    /* Make part */
                    MSBE.Part.Asset asset = MakePart.Asset(modelInfo);
                    asset.Position = content.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    asset.Rotation = content.rotation;
                    asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);

                    if (content.papyrus != null)
                    {
                        if (content.entity <= 0) { content.entity = script.CreateEntity(EntityType.Asset, content.id); }  // if this content does not yet have an entity id, give it one
                        Papyrus papyrusScript = esm.GetPapyrus(content.papyrus);
                        if (papyrusScript != null) { PapyrusEMEVD.Compile(scriptManager, param, item, script, papyrusScript, content); } // this != null check only exists because bugs. @TODO: remove when we get 100% papyrus support
                    }

                    /* Asset tileload config */
                    if (tile.GetType() == typeof(HugeTile) || tile.GetType() == typeof(BigTile))
                    {
                        asset.TileLoad.MapID = new byte[] { (byte)0, (byte)content.load.y, (byte)content.load.x, (byte)tile.map };
                        asset.TileLoad.Unk04 = 13;
                        asset.TileLoad.CullingHeightBehavior = -1;
                    }

                    asset.EntityID = content.entity;

                    msb.Parts.Assets.Add(asset);
                }

                /* Add doors */
                foreach (DoorContent content in tile.doors)
                {
                    /* Grab ModelInfo */
                    ModelInfo modelInfo = cache.GetModel(content.mesh, content.scale);

                    /* Make part */
                    MSBE.Part.Asset asset = MakePart.Asset(modelInfo);
                    asset.Position = content.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    asset.Rotation = content.rotation;
                    asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);
                    asset.EntityID = content.entity;

                    if (content.warp != null) { script.RegisterLoadDoor(param, content, modelInfo); } // if the door is a load door we need to generate scripts for it
                    else if (Const.DEBUG_DISCARD_ANIMATED_DOORS) { continue; } // if the debug flag is set, skip any doors that are NOT load doors. useful for debugging until we get animated doors working

                    msb.Parts.Assets.Add(asset);
                }

                /* Add warp destinations for load doors */
                foreach (Layout.WarpDestination warp in tile.warps)
                {
                    MSBE.Part.Player player = MakePart.Player();
                    player.Position = warp.position + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    player.Rotation = warp.rotation;
                    player.EntityID = warp.id;
                    msb.Parts.Players.Add(player);
                }

                /* Add emitters */
                foreach (EmitterContent content in tile.emitters)
                {
                    /* Grab ModelInfo */
                    EmitterInfo emitterInfo = cache.GetEmitter(content.id);

                    /* Make part */
                    MSBE.Part.Asset asset = MakePart.Asset(emitterInfo);
                    asset.Position = content.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    asset.Rotation = content.rotation;
                    asset.Scale = new Vector3(content.scale * 0.01f);

                    msb.Parts.Assets.Add(asset);
                }

                /* Add lights */
                foreach (LightContent light in tile.lights)
                {
                    lightManager.CreateLight(light);
                }

                /* Create humanoid NPCs (c0000) */
                foreach (NpcContent npc in tile.npcs)
                {
                    MSBE.Part.Enemy enemy = MakePart.Npc();
                    enemy.Position = npc.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    enemy.Rotation = npc.rotation;

                    // If the npc is a deadbody we create a treasure on their body
                    List<ItemManager.ItemInfo> inv = item.GetInventory(npc);
                    if (npc.dead && inv.Count() > 0)
                    {
                        MSBE.Event.Treasure treasure = MakePart.Treasure();
                        treasure.ItemLotID = param.GenerateDeadBodyItemLot(script, npc, inv);

                        MSBE.Part.Asset treasurePart = MakePart.TreasureAsset();
                        treasurePart.Position = enemy.Position;

                        treasure.Name = $"DeadBodyTreaure->{npc.id}";
                        treasure.ActionButtonID = param.GenerateActionButtonItemParam($"Loot {npc.name}");
                        treasure.TreasurePartName = treasurePart.Name;

                        msb.Parts.Assets.Add(treasurePart);
                        msb.Events.Treasures.Add(treasure);
                    }

                    // Doing this BEFORE talkesd so that all nesscary local vars are created beforehand!
                    if (npc.papyrus != null)
                    {
                        Papyrus papyrusScript = esm.GetPapyrus(npc.papyrus);
                        if (papyrusScript != null) { PapyrusEMEVD.Compile(scriptManager, param, item, script, papyrusScript, npc); } // this != null check only exists because bugs. @TODO: remove when we get 100% papyrus support
                        //PapyrusESD esdScript = new PapyrusESD(esm, scriptManager, param, text, script, npc, papyrusScript, 99999);
                    }

                    enemy.TalkID = character.GetESD(tile.IdList(), npc); // creates and returns a character esd
                    enemy.NPCParamID = character.GetParam(item, script, npc); // creates and returns an npcparam
                    enemy.EntityID = npc.entity;

                    msb.Parts.Enemies.Add(enemy);
                }

                /* Add items */ // must happen after npcs since items can be owned by npcs so we need all npcs registerd before we do items
                foreach (ItemContent content in tile.items)
                {
                    if (Override.CheckDoNotPlace(content.mesh)) { continue; } // skip any meshes listed in the do_not_place override json

                    /* Grab ModelInfo + iteminfo */
                    ItemManager.ItemInfo itemInfo = item.GetItem(content.id);
                    ModelInfo modelInfo;
                    if (itemInfo != null) { modelInfo = cache.GetModel(content.mesh, Const.DYNAMIC_ASSET); } // if we have a treasure for this assset it MUST be dynamic
                    else { modelInfo = cache.GetModel(content.mesh, content.scale); }  // otherwise it doesn't matter. treasure events can only work on dynamic assets for whatever reason

                    /* Make part */
                    MSBE.Part.Asset asset = MakePart.Asset(modelInfo);
                    asset.Position = content.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    asset.Rotation = content.rotation;
                    asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);

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

                    if (content.papyrus != null)
                    {
                        if (content.entity <= 0) { content.entity = script.CreateEntity(EntityType.Asset, content.id); }  // if this content does not yet have an entity id, give it one
                        Papyrus papyrusScript = esm.GetPapyrus(content.papyrus);
                        if (papyrusScript != null) { PapyrusEMEVD.Compile(scriptManager, param, item, script, papyrusScript, content); } // this != null check only exists because bugs. @TODO: remove when we get 100% papyrus support
                    }

                    asset.EntityID = content.entity;

                    msb.Parts.Assets.Add(asset);
                }

                /* Add container */
                foreach (ContainerContent content in tile.containers)
                {
                    if (Override.CheckDoNotPlace(content.mesh)) { continue; } // skip any meshes listed in the do_not_place override json

                    /* Grab ModelInfo + iteminfo */
                    List<ItemManager.ItemInfo> inventory = item.GetInventory(content);
                    ModelInfo modelInfo;
                    if (inventory.Count() > 0) { modelInfo = cache.GetModel(content.mesh, Const.DYNAMIC_ASSET); } // if we have a treasure for this assset it MUST be dynamic
                    else { modelInfo = cache.GetModel(content.mesh, content.scale); }  // otherwise it doesn't matter. treasure events can only work on dynamic assets for whatever reason

                    /* Make part */
                    MSBE.Part.Asset asset = MakePart.Asset(modelInfo);
                    asset.Position = content.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    asset.Rotation = content.rotation;
                    asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);

                    if (inventory.Count() > 0)
                    {
                        MSBE.Event.Treasure treasure = MakePart.Treasure();
                        if (content.entity <= 0) { content.entity = script.CreateEntity(EntityType.Asset, content.id); }  // if this content does not yet have an entity id, give it one
                        treasure.ItemLotID = param.GenerateContainerItemLot(script, content, inventory);
                        treasure.Name = $"ContainerTreasure->{content.id}";
                        treasure.ActionButtonID = param.GenerateActionButtonItemParam(content.ActionText());
                        treasure.TreasurePartName = asset.Name;

                        msb.Events.Treasures.Add(treasure);
                    }

                    if (content.papyrus != null)
                    {
                        if (content.entity <= 0) { content.entity = script.CreateEntity(EntityType.Asset, content.id); }  // if this content does not yet have an entity id, give it one
                        Papyrus papyrusScript = esm.GetPapyrus(content.papyrus);
                        if (papyrusScript != null) { PapyrusEMEVD.Compile(scriptManager, param, item, script, papyrusScript, content); } // this != null check only exists because bugs. @TODO: remove when we get 100% papyrus support
                    }

                    asset.EntityID = content.entity;

                    msb.Parts.Assets.Add(asset);
                }

                /* TEST Creatures */ // make some goats where enemies would spawn just as a test
                foreach (CreatureContent creature in tile.creatures)
                {
                    MSBE.Part.Enemy enemy = MakePart.Creature();
                    enemy.Position = creature.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    enemy.Rotation = creature.rotation;
                    enemy.EntityID = creature.entity;

                    msb.Parts.Enemies.Add(enemy);
                }

                /* Handle area names */
                if (isTileType)
                {
                    foreach (Cell cell in tile.cells)
                    {
                        if (cell.name != null)
                        {
                            float x = (tile.coordinate.x * Const.TILE_SIZE);
                            float y = (tile.coordinate.y * Const.TILE_SIZE);
                            Vector3 relative = (cell.center + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);

                            int paramId = int.Parse($"61{tile.coordinate.x:D2}{tile.coordinate.y:D2}{nextMPR:D2}");


                            MSBE.Region.MapPoint mpr = new();
                            mpr.Name = $"{cell.name} placename";
                            mpr.Shape = new MSB.Shape.Box(Const.CELL_SIZE, Const.CELL_SIZE, Const.CELL_SIZE * 8);
                            mpr.Position = relative;
                            mpr.Rotation = Vector3.Zero;
                            mpr.RegionID = nextMPR++;
                            mpr.MapStudioLayer = 4294967295;
                            mpr.WorldMapPointParamID = param.GenerateWorldMapPoint(tile, cell, relative, paramId);

                            mpr.MapID = -1;
                            mpr.UnkE08 = 255;
                            mpr.UnkS04 = 0;
                            mpr.UnkS0C = -1;
                            mpr.UnkT04 = -1;
                            mpr.UnkT08 = -1;
                            mpr.UnkT0C = -1;
                            mpr.UnkT10 = -1;
                            mpr.UnkT14 = -1;
                            mpr.UnkT18 = -1;

                            msb.Regions.MapPoints.Add(mpr);
                        }
                    }
                }

                /* Auto resource */
                AutoResource.Generate(tile.map, tile.coordinate.x, tile.coordinate.y, tile.block, msb);

                /* Done */
                msbs.Add(pool);
                Lort.TaskIterate(); // Progress bar update
            }

            /* Generate interior msbs from interiorgroups */
            Lort.Log($"Generating {layout.interiors.Count} interior msbs...", Lort.Type.Main);
            Lort.NewTask("Generating MSB", layout.interiors.Count);
            foreach (InteriorGroup group in layout.interiors)
            {
                if (Const.DEBUG_SKIP_INTERIOR) { break; }

                // Skip empty groups.
                if (group.IsEmpty()) { continue; }

                /* Misc Indices */
                int nextC = 0, nextMPR = 0;

                /* Generate msb from group */
                MSBE msb = new();
                LightManager lightManager = new(group.map, group.area, group.unk, group.block);
                Script script = scriptManager.GetScript(group);
                ResourcePool pool = new(group, msb, lightManager, script);
                msb.Compression = SoulsFormats.DCX.Type.DCX_KRAK;

                /* Handle chunks */
                for (int i = 0; i < group.chunks.Count(); i++)
                {
                    InteriorGroup.Chunk chunk = group.chunks[i];

                    /* Interior MSB drawgroup */
                    uint chunkDrawGroup = (uint)0 | ((uint)1 << i);

                    /* Interior MSB chunk collision root */
                    string collisionIndex = $"{group.area.ToString("D2")}{group.unk.ToString("D2")}{nextC++.ToString("D2")}";
                    MSBE.Part.Collision rootCollision = MakePart.Collision();
                    rootCollision.Name = $"h{collisionIndex}_0000";
                    rootCollision.ModelName = $"h{collisionIndex}";
                    rootCollision.Position = chunk.root + Const.TEST_OFFSET1 + Const.TEST_OFFSET2 - new Vector3(0f, chunk.bounds.Z, 0f);
                    rootCollision.Unk1.DisplayGroups[0] = 0;
                    rootCollision.Unk1.DisplayGroups[1] = chunkDrawGroup;
                    rootCollision.Unk1.CollisionMask[0] = 0;
                    rootCollision.Unk1.CollisionMask[1] = chunkDrawGroup;
                    msb.Parts.Collisions.Add(rootCollision);
                    pool.collisionIndices.Add(new Tuple<string, CollisionInfo>(collisionIndex, cache.defaultCollision));

                    /* Interior MSB shadow box */
                    ModelInfo shadowBoxModelInfo = cache.GetModel("interiorshadowbox");
                    MSBE.Part.Asset shadowBoxAsset = MakePart.Asset(shadowBoxModelInfo);
                    shadowBoxAsset.Position = chunk.root + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    shadowBoxAsset.Rotation = Vector3.Zero;
                    shadowBoxAsset.Scale = chunk.bounds;
                    shadowBoxAsset.Unk1.DisplayGroups[0] = 0;
                    shadowBoxAsset.UnkPartNames[1] = rootCollision.Name;
                    shadowBoxAsset.UnkPartNames[3] = rootCollision.Name;
                    shadowBoxAsset.UnkPartNames[5] = rootCollision.Name;
                    msb.Parts.Assets.Add(shadowBoxAsset);

                    /* Add assets */
                    foreach (AssetContent content in chunk.assets)
                    {
                        if (Override.CheckDoNotPlace(content.mesh)) { continue; } // skip any meshes listed in the do_not_place override json

                        /* Grab ModelInfo */
                        ModelInfo modelInfo = cache.GetModel(content.mesh, content.scale);

                        /* Make part */
                        MSBE.Part.Asset asset = MakePart.Asset(modelInfo);
                        asset.Position = content.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                        asset.Rotation = content.rotation;
                        asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);

                        if (content.papyrus != null)
                        {
                            if (content.entity <= 0) { content.entity = script.CreateEntity(EntityType.Asset, content.id); }  // if this content does not yet have an entity id, give it one
                            Papyrus papyrusScript = esm.GetPapyrus(content.papyrus);
                            if (papyrusScript != null) { PapyrusEMEVD.Compile(scriptManager, param, item, script, papyrusScript, content); } // this != null check only exists because bugs. @TODO: remove when we get 100% papyrus support
                        }

                        asset.EntityID = content.entity;

                        asset.Unk1.DisplayGroups[0] = 0;
                        asset.UnkPartNames[1] = rootCollision.Name;
                        asset.UnkPartNames[3] = rootCollision.Name;
                        asset.UnkPartNames[5] = rootCollision.Name;

                        msb.Parts.Assets.Add(asset);
                    }

                    /* Add doors */
                    foreach (DoorContent content in chunk.doors)
                    {
                        /* Grab ModelInfo */
                        ModelInfo modelInfo = cache.GetModel(content.mesh, content.scale);

                        /* Make part */
                        MSBE.Part.Asset asset = MakePart.Asset(modelInfo);
                        asset.Position = content.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                        asset.Rotation = content.rotation;
                        asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);
                        asset.EntityID = content.entity;

                        asset.Unk1.DisplayGroups[0] = 0;
                        asset.UnkPartNames[1] = rootCollision.Name;
                        asset.UnkPartNames[3] = rootCollision.Name;
                        asset.UnkPartNames[5] = rootCollision.Name;

                        if (content.warp != null) { script.RegisterLoadDoor(param, content, modelInfo); } // if the door is a load door we need to register scripts for it
                        else if (Const.DEBUG_DISCARD_ANIMATED_DOORS) { continue; } // if the debug flag is set, skip any doors that are NOT load doors. useful for debugging until we get animated doors working

                        msb.Parts.Assets.Add(asset);
                    }

                    /* Add warp destinations for load doors */
                    foreach (Layout.WarpDestination warp in chunk.warps)
                    {
                        MSBE.Part.Player player = MakePart.Player();
                        player.Position = warp.position + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                        player.Rotation = warp.rotation;
                        player.EntityID = warp.id;
                        msb.Parts.Players.Add(player);
                    }

                    /* Add emitters */
                    foreach (EmitterContent content in chunk.emitters)
                    {
                        /* Grab ModelInfo */
                        EmitterInfo emitterInfo = cache.GetEmitter(content.id);

                        /* Make part */
                        MSBE.Part.Asset asset = MakePart.Asset(emitterInfo);
                        asset.Position = content.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                        asset.Rotation = content.rotation;
                        asset.Scale = new Vector3(content.scale * 0.01f);

                        asset.Unk1.DisplayGroups[0] = 0;
                        asset.UnkPartNames[1] = rootCollision.Name;
                        asset.UnkPartNames[3] = rootCollision.Name;
                        asset.UnkPartNames[5] = rootCollision.Name;

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
                        enemy.Position = npc.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                        enemy.Rotation = npc.rotation;

                        // If the npc is a deadbody we create a treasure on their body
                        List<ItemManager.ItemInfo> inv = item.GetInventory(npc);
                        if (npc.dead && inv.Count() > 0)
                        {
                            MSBE.Event.Treasure treasure = MakePart.Treasure();
                            treasure.ItemLotID = param.GenerateDeadBodyItemLot(script, npc, inv);

                            MSBE.Part.Asset treasurePart = MakePart.TreasureAsset();
                            treasurePart.Position = enemy.Position;

                            treasure.Name = $"DeadBodyTreaure->{npc.id}";
                            treasure.ActionButtonID = param.GenerateActionButtonItemParam($"Loot {npc.name}");
                            treasure.TreasurePartName = treasurePart.Name;

                            msb.Parts.Assets.Add(treasurePart);
                            msb.Events.Treasures.Add(treasure);
                        }

                        // Doing this BEFORE talkesd so that all nesscary local vars are created beforehand!
                        if (npc.papyrus != null)
                        {
                            Papyrus papyrusScript = esm.GetPapyrus(npc.papyrus);
                            if (papyrusScript != null) { PapyrusEMEVD.Compile(scriptManager, param, item, script, papyrusScript, npc); } // this != null check only exists because bugs. @TODO: remove when we get 100% papyrus support
                                                                                                                                         //PapyrusESD esdScript = new PapyrusESD(esm, scriptManager, param, text, script, npc, papyrusScript, 99999);
                        }

                        enemy.TalkID = character.GetESD(group.IdList(), npc); // creates and returns a character esd
                        enemy.NPCParamID = character.GetParam(item, script, npc); // creates and returns an npcparam
                        enemy.EntityID = npc.entity;

                        enemy.Unk1.DisplayGroups[0] = 0;
                        enemy.CollisionPartName = rootCollision.Name;

                        msb.Parts.Enemies.Add(enemy);
                    }

                    /* Add items */ // must happen after npcs since items can be owned by npcs so we need all npcs registerd before we do items
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
                        asset.Position = content.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                        asset.Rotation = content.rotation;
                        asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);

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

                        if (content.papyrus != null)
                        {
                            if (content.entity <= 0) { content.entity = script.CreateEntity(EntityType.Asset, content.id); }  // if this content does not yet have an entity id, give it one
                            Papyrus papyrusScript = esm.GetPapyrus(content.papyrus);
                            if (papyrusScript != null) { PapyrusEMEVD.Compile(scriptManager, param, item, script, papyrusScript, content); } // this != null check only exists because bugs. @TODO: remove when we get 100% papyrus support
                        }

                        asset.EntityID = content.entity;

                        asset.Unk1.DisplayGroups[0] = 0;
                        asset.UnkPartNames[1] = rootCollision.Name;
                        asset.UnkPartNames[3] = rootCollision.Name;
                        asset.UnkPartNames[5] = rootCollision.Name;

                        msb.Parts.Assets.Add(asset);
                    }

                    /* Add container */
                    foreach (ContainerContent content in chunk.containers)
                    {
                        if (Override.CheckDoNotPlace(content.mesh)) { continue; } // skip any meshes listed in the do_not_place override json

                        /* Grab ModelInfo + iteminfo */
                        List<ItemManager.ItemInfo> inventory = item.GetInventory(content);
                        ModelInfo modelInfo;
                        if (inventory.Count() > 0) { modelInfo = cache.GetModel(content.mesh, Const.DYNAMIC_ASSET); } // if we have a treasure for this assset it MUST be dynamic
                        else { modelInfo = cache.GetModel(content.mesh, content.scale); }  // otherwise it doesn't matter. treasure events can only work on dynamic assets for whatever reason

                        /* Make part */
                        MSBE.Part.Asset asset = MakePart.Asset(modelInfo);
                        asset.Position = content.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                        asset.Rotation = content.rotation;
                        asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);

                        if (inventory.Count() > 0)
                        {
                            MSBE.Event.Treasure treasure = MakePart.Treasure();
                            if (content.entity <= 0) { content.entity = script.CreateEntity(EntityType.Asset, content.id); }  // if this content does not yet have an entity id, give it one
                            treasure.ItemLotID = param.GenerateContainerItemLot(script, content, inventory);
                            treasure.Name = $"ContainerTreasure->{content.id}";
                            treasure.ActionButtonID = param.GenerateActionButtonItemParam(content.ActionText());
                            treasure.TreasurePartName = asset.Name;

                            msb.Events.Treasures.Add(treasure);
                        }

                        if (content.papyrus != null)
                        {
                            if (content.entity <= 0) { content.entity = script.CreateEntity(EntityType.Asset, content.id); }  // if this content does not yet have an entity id, give it one
                            Papyrus papyrusScript = esm.GetPapyrus(content.papyrus);
                            if (papyrusScript != null) { PapyrusEMEVD.Compile(scriptManager, param, item, script, papyrusScript, content); } // this != null check only exists because bugs. @TODO: remove when we get 100% papyrus support
                        }

                        asset.EntityID = content.entity;

                        asset.Unk1.DisplayGroups[0] = 0;
                        asset.UnkPartNames[1] = rootCollision.Name;
                        asset.UnkPartNames[3] = rootCollision.Name;
                        asset.UnkPartNames[5] = rootCollision.Name;

                        msb.Parts.Assets.Add(asset);
                    }

                    /* TEST Creatures */ // make some goats where enemies would spawn just as a test
                    foreach (CreatureContent creature in chunk.creatures)
                    {
                        var pool = GenerateInteriorMSB(group, scriptManager, cache, esm, param, character);
                        Lort.TaskIterate();
                        return pool;
                    })
                    .Where(pool => pool != null);

                msbs.AddRange(interiorPools);
            }

            WarpZone.Generate(layout, scriptManager, param);

            if (param.param[Paramanager.ParamType.TalkParam].Rows.Count() >= ushort.MaxValue) { throw new Exception("Ran out of talk param rows! Will fail to compile params!"); }

            /* Write sound BNKs */
            sound.Write($"{Const.OUTPUT_PATH}sd\\enus\\");

            /* Write ESD bnds */
            character.Write();

            /* Generate some needed scripts after msb gen */
            scriptManager.GenerateAreaEvents();
            scriptManager.GenerateGlobalCrimeAbsolvedEvent();

            /* Generate some params and write to file */
            Lort.Log($"Creating PARAMs...", Lort.Type.Main);
            param.GeneratePartDrawParams();
            param.GenerateAssetRows(cache.assets);
            param.GenerateAssetRows(cache.emitters);
            param.GenerateAssetRows(cache.liquids);
            param.GenerateMapInfoParam(layout);
            param.SetAllMapLocation();
            param.GenerateCustomCharacterCreation();
            param.KillMapHeightParams();    // murder kill
            param.Write();

            /* Write FMGs */
            Lort.Log($"Binding FMGs...", Lort.Type.Main);
            text.Write($"{Const.OUTPUT_PATH}msg\\engus\\");

            /* Write FXR files */
            Lort.Log($"Binding FXRs...", Lort.Type.Main);
            FxrManager.Write(layout);

            /* Bind and write all materials and textures */
            Bind.BindMaterials($"{Const.OUTPUT_PATH}material\\allmaterial.matbinbnd.dcx");
            Bind.BindTPF(cache, layout.ListCommon());
            icon.Write();

            /* Bind all assets */    // Multithreaded because slow
            Lort.Log($"Binding {cache.assets.Count} assets...", Lort.Type.Main);
            Lort.NewTask("Binding Assets", cache.assets.Count);
            Bind.BindAssets(cache);
            Bind.BindEmitters(cache);
            foreach(LiquidInfo water in cache.liquids)  // bind up them waters toooooo
            {
                Bind.BindAsset(water, $"{Const.OUTPUT_PATH}asset\\aeg\\{water.AssetPath()}.geombnd.dcx");
            }

            /* Generate overworld */
            ResourcePool overworld = OverworldManager.Generate(cache, esm, layout, param);
            msbs.Insert(0, overworld); // this one takes the longest so we put it first so that the thread working on it has plenty of time to finish

            /* Write emevd scripts */
            scriptManager.Write();

            /* Write msbs */
            esm = null;  // free some memory here
            param = null;
            icon = null;
            GC.Collect();
            MsbWorker.Go(msbs);

            /* Donezo */
            Lort.Log("Done!", Lort.Type.Main);
            Lort.NewTask("Done!", 1);
            Lort.TaskIterate();
        }

        private static ResourcePool GenerateInteriorMSB(InteriorGroup group, ScriptManager scriptManager, Cache cache, ESM esm, Paramanager paramanager, NpcManager npcManager)
        {
            // Skip empty groups.
            if (group.IsEmpty()) { return null; }

            /* Misc Indices */
            int nextC = 0, nextMPR = 0;

            /* Generate msb from group */
            MSBE msb = new();
            LightManager lightManager = new(group.map, group.area, group.unk, group.block);
            Script script = scriptManager.GetScript(group);
            ResourcePool pool = new(group, msb, lightManager, script);
            msb.Compression = SoulsFormats.DCX.Type.DCX_KRAK;

            /* Handle chunks */
            for (int i = 0; i < group.chunks.Count(); i++)
            {
                InteriorGroup.Chunk chunk = group.chunks[i];

                /* Interior MSB drawgroup */
                uint chunkDrawGroup = (uint)0 | ((uint)1 << i);

                /* Interior MSB chunk collision root */
                string collisionIndex = $"{group.area.ToString("D2")}{group.unk.ToString("D2")}{nextC++.ToString("D2")}";
                MSBE.Part.Collision rootCollision = MakePart.Collision();
                rootCollision.Name = $"h{collisionIndex}_0000";
                rootCollision.ModelName = $"h{collisionIndex}";
                rootCollision.Position = chunk.root + Const.TEST_OFFSET1 + Const.TEST_OFFSET2 - new Vector3(0f, chunk.bounds.Z, 0f);
                rootCollision.Unk1.DisplayGroups[0] = 0;
                rootCollision.Unk1.DisplayGroups[1] = chunkDrawGroup;
                rootCollision.Unk1.CollisionMask[0] = 0;
                rootCollision.Unk1.CollisionMask[1] = chunkDrawGroup;
                msb.Parts.Collisions.Add(rootCollision);
                pool.collisionIndices.Add(new Tuple<string, CollisionInfo>(collisionIndex, cache.defaultCollision));

                /* Interior MSB shadow box */
                ModelInfo shadowBoxModelInfo = cache.GetModel("interiorshadowbox");
                MSBE.Part.Asset shadowBoxAsset = MakePart.Asset(shadowBoxModelInfo);
                shadowBoxAsset.Position = chunk.root + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                shadowBoxAsset.Rotation = Vector3.Zero;
                shadowBoxAsset.Scale = chunk.bounds;
                shadowBoxAsset.Unk1.DisplayGroups[0] = 0;
                shadowBoxAsset.UnkPartNames[1] = rootCollision.Name;
                shadowBoxAsset.UnkPartNames[3] = rootCollision.Name;
                shadowBoxAsset.UnkPartNames[5] = rootCollision.Name;
                msb.Parts.Assets.Add(shadowBoxAsset);

                /* Add assets */
                foreach (AssetContent content in chunk.assets)
                {
                    if (Override.CheckDoNotPlace(content.mesh.ToLower())) { continue; } // skip any meshes listed in the do_not_place override json

                    /* Grab ModelInfo */
                    ModelInfo modelInfo = cache.GetModel(content.mesh, content.scale);

                    /* Make part */
                    MSBE.Part.Asset asset = MakePart.Asset(modelInfo);
                    asset.Position = content.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    asset.Rotation = content.rotation;
                    asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);

                    if (content.papyrus != null)
                    {
                        content.entity = script.CreateEntity(EntityType.Asset, content.id);
                        asset.EntityID = content.entity;
                        Papyrus papyrusScript = esm.GetPapyrus(content.papyrus);
                        if (papyrusScript != null) { PapyrusEMEVD.Compile(scriptManager, paramanager, script, papyrusScript, content); } // this != null check only exists because bugs. @TODO: remove when we get 100% papyrus support
                    }

                    asset.Unk1.DisplayGroups[0] = 0;
                    asset.UnkPartNames[1] = rootCollision.Name;
                    asset.UnkPartNames[3] = rootCollision.Name;
                    asset.UnkPartNames[5] = rootCollision.Name;

                    msb.Parts.Assets.Add(asset);
                }

                /* Add doors */
                foreach (DoorContent content in chunk.doors)
                {
                    /* Grab ModelInfo */
                    ModelInfo modelInfo = cache.GetModel(content.mesh, content.scale);

                    /* Make part */
                    MSBE.Part.Asset asset = MakePart.Asset(modelInfo);
                    asset.Position = content.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    asset.Rotation = content.rotation;
                    asset.Scale = new Vector3(modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);
                    asset.EntityID = content.entity;

                    asset.Unk1.DisplayGroups[0] = 0;
                    asset.UnkPartNames[1] = rootCollision.Name;
                    asset.UnkPartNames[3] = rootCollision.Name;
                    asset.UnkPartNames[5] = rootCollision.Name;

                    if (content.warp != null) { script.RegisterLoadDoor(content); } // if the door is a load door we need to register scripts for it

                    msb.Parts.Assets.Add(asset);
                }

                /* Add warp destinations for load doors */
                foreach (Layout.WarpDestination warp in chunk.warps)
                {
                    MSBE.Part.Player player = MakePart.Player();
                    player.Position = warp.position + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    player.Rotation = warp.rotation;
                    player.EntityID = warp.id;
                    msb.Parts.Players.Add(player);
                }

                /* Add emitters */
                foreach (EmitterContent content in chunk.emitters)
                {
                    /* Grab ModelInfo */
                    EmitterInfo emitterInfo = cache.GetEmitter(content.id);

                    /* Make part */
                    MSBE.Part.Asset asset = MakePart.Asset(emitterInfo);
                    asset.Position = content.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    asset.Rotation = content.rotation;
                    asset.Scale = new Vector3(content.scale * 0.01f);

                    asset.Unk1.DisplayGroups[0] = 0;
                    asset.UnkPartNames[1] = rootCollision.Name;
                    asset.UnkPartNames[3] = rootCollision.Name;
                    asset.UnkPartNames[5] = rootCollision.Name;

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
                    enemy.Position = npc.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    enemy.Rotation = npc.rotation;

                    // Doing this BEFORE talkesd so that all nesscary local vars are created beforehand!
                    if (npc.papyrus != null)
                    {
                        Papyrus papyrusScript = esm.GetPapyrus(npc.papyrus);
                        if (papyrusScript != null) { PapyrusEMEVD.Compile(scriptManager, paramanager, script, papyrusScript, npc); } // this != null check only exists because bugs. @TODO: remove when we get 100% papyrus support
                                                                                                                               //PapyrusESD esdScript = new PapyrusESD(esm, scriptManager, param, text, script, npc, papyrusScript, 99999);
                    }

                    enemy.TalkID = npcManager.GetESD(group.IdList(), npc); // creates and returns a character esd
                    enemy.NPCParamID = npcManager.GetParam(npc); // creates and returns an npcparam
                    enemy.EntityID = npc.entity;

                    enemy.Unk1.DisplayGroups[0] = 0;
                    enemy.CollisionPartName = rootCollision.Name;

                    msb.Parts.Enemies.Add(enemy);
                }

                /* TEST Creatures */ // make some goats where enemies would spawn just as a test
                foreach (CreatureContent creature in chunk.creatures)
                {
                    MSBE.Part.Enemy enemy = MakePart.Creature();
                    enemy.Position = creature.relative + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                    enemy.Rotation = creature.rotation;

                    enemy.Unk1.DisplayGroups[0] = 0;
                    enemy.CollisionPartName = rootCollision.Name;
                    enemy.EntityID = creature.entity;

                    msb.Parts.Enemies.Add(enemy);
                }

                /* Handle area name */
                int paramId = int.Parse($"60{group.map:D2}{group.area:D2}{nextMPR:D2}");


                MSBE.Region.MapPoint mpr = new();
                mpr.Name = $"{chunk.cell.name} placename";
                mpr.Shape = new MSB.Shape.Box(chunk.bounds.X, chunk.bounds.Z, chunk.bounds.Y);
                mpr.Position = chunk.root + Const.TEST_OFFSET1 + Const.TEST_OFFSET2 - new Vector3(0f, chunk.bounds.Y / 2f, 0f);
                mpr.Rotation = Vector3.Zero;
                mpr.RegionID = nextMPR++;
                mpr.MapStudioLayer = 4294967295;
                mpr.WorldMapPointParamID = paramanager.GenerateWorldMapPoint(group, chunk.cell, chunk.root, paramId);

                mpr.MapID = -1;
                mpr.UnkE08 = 255;
                mpr.UnkS04 = 0;
                mpr.UnkS0C = -1;
                mpr.UnkT04 = -1;
                mpr.UnkT08 = -1;
                mpr.UnkT0C = -1;
                mpr.UnkT10 = -1;
                mpr.UnkT14 = -1;
                mpr.UnkT18 = -1;

                msb.Regions.MapPoints.Add(mpr);
            }

            /* EnvMap & REM for interior */
            // @TODO: make this per chunk so we can setupd different rems for different interiors
            {
                /* Create envmap texture file */
                int envId = 200;
                int size = 4096, crossfade = 8;
                EnvManager.CreateEnvMaps(group, envId);

                /* Create an envbox */
                MSBE.Region.EnvironmentMapEffectBox envBox = MakePart.EnvBox();
                envBox.Name = $"Env_Box{envId.ToString("D3")}";
                envBox.Shape = new MSB.Shape.Box(size + crossfade, size + crossfade, size + crossfade);
                envBox.Position = new Vector3(0f, size * -0.5f, 0f) + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                envBox.TransitionDist = crossfade / 2f;
                msb.Regions.EnvironmentMapEffectBoxes.Add(envBox);

                MSBE.Region.EnvironmentMapPoint envPoint = MakePart.EnvPoint();
                envPoint.Name = $"Env_Point{envId.ToString("D3")}";
                envPoint.Position = new Vector3(0f, size * -0.5f, 0f) + Const.TEST_OFFSET1 + Const.TEST_OFFSET2;
                envPoint.UnkMapID = new byte[] { (byte)group.map, (byte)group.area, (byte)group.unk, (byte)group.block };
                msb.Regions.EnvironmentMapPoints.Add(envPoint);
            }

            /* Auto resource */
            AutoResource.Generate(group.map, group.area, group.unk, group.block, msb);

            return pool;
        }
    }

    public class ResourcePool
    {
        public int[] id;
        public List<Tuple<int, string>> mapIndices;
        public MSBE msb;
        public LightManager lights;
        public Script script;
        public List<Tuple<string, CollisionInfo>> collisionIndices;

        /* Exterior cells */
        public ResourcePool(BaseTile tile, MSBE msb, LightManager lights, Script script = null)
        {
            id = new int[]
            {
                    tile.map, tile.coordinate.x, tile.coordinate.y, tile.block
            };
            mapIndices = new();
            collisionIndices = new();
            this.msb = msb;
            this.lights = lights;
            this.script = script;
        }

        /* Interior cells */
        public ResourcePool(InteriorGroup group, MSBE msb, LightManager lights, Script script = null)
        {
            id = new int[]
            {
                    group.map, group.area, group.unk, group.block
            };
            mapIndices = new();
            this.msb = msb;
            this.lights = lights;
            this.script = script;
            collisionIndices = new();
        }

        /* Super overworld */
        public ResourcePool(MSBE msb, LightManager lights)
        {
            id = new int[]
            {
                    60, 00, 00, 99
            };
            mapIndices = new();
            this.msb = msb;
            this.lights = lights;
            script = null;
            collisionIndices = new();
        }

        public void Add(TerrainInfo terrain)
        {
            mapIndices.Add(new Tuple<int, string>(terrain.id, terrain.path));
        }

        public void Add(string index, CollisionInfo collision)
        {
            collisionIndices.Add(new Tuple<string, CollisionInfo>(index, collision));
        }
    }
}
