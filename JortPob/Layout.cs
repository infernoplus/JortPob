using JortPob.Common;
using JortPob.Scripts;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Microsoft.Scripting.Utils;
using static JortPob.InteriorGroup;

namespace JortPob
{
    /* Takes the Morrowind ESM cell grid and re-subdivides it into the Elden Ring tile grid */
    public class Layout
    {
        private static readonly int MapId = 60;

        private readonly List<HugeTile> huges = [];
        private readonly List<BigTile> bigs = [];
        private readonly List<Tile> tiles;

        private readonly List<InteriorGroup> interiors = [];

        // For now, we just expose IEnumerable in case we need to change the underlying implementation
        public IEnumerable<BaseTile> AllTiles => [..tiles, ..bigs, ..huges];

        public IEnumerable<Tile> Tiles => tiles;
        public int TileCount => tiles.Count;

        public IEnumerable<InteriorGroup> Interiors => interiors;
        public int InteriorCount => interiors.Count;

        public Layout(Cache cache, ESM esm, Paramanager param, TextManager text, ScriptManager scriptManager)
        {
            Lort.Log("Generating layout...", Lort.Type.Main);
            Lort.NewTask("Generating Layout", 13);

            /* Generate tiles based off base game msb info... */
            var msbData = ProcessMsbList(
                File.ReadAllText(Utility.ResourcePath(@"msb\msblist.txt"))
                        .Split(";")
                        .Select(msb => msb.Split(","))
            ).ToList();

            tiles = msbData
                .Where(t => t.b == 0 && t.m == MapId)
                .Select(t => new Tile(t.m, t.x, t.y, t.b))
                .ToList();

            Lort.TaskIterate(); // Progress bar update
            
            /* Generate BigTiles... */
            foreach (var (m, x, y, b) in msbData)
            {
                if (m != MapId || b != 1) continue;
                BigTile big = new(m, x, y, b);
                big.AddMatchingTiles(tiles);
                bigs.Add(big);
            }
            Lort.TaskIterate(); // Progress bar update

            /* Generate HugeTiles... */
            foreach (var (m, x, y, b) in msbData)
            {
                if (m != MapId || b != 2) continue;
                HugeTile huge = new(m, x, y, b);
                huge.AddMatchingTiles(bigs);
                huge.AddMatchingTiles(tiles);
                huges.Add(huge);
            }
            Lort.TaskIterate(); // Progress bar update

            /* Generate Interior Groups */
            HashSet<int> validMaps =
                [12, 13, 14, 15, 16, 19, 20, 21, 22, 25, 28, 30, 31, 32, 34, 35, 39, 40, 41, 42, 43];
            interiors = msbData
                .Where(t => t is { y: 0, b: 0 } && validMaps.Contains(t.m))
                .Select(t => {
                    var (m, a, u, b) = t;
                    return new InteriorGroup(m, a, u, b);
                })
                .ToList();
            Lort.TaskIterate(); // Progress bar update

            /* Part 1 of papyrus pre-process */
            // able refs is objects targeted by Enable, Disable, and GetDisabled
            var (allCalls, allReferences, toggleableReferences) = esm.GetScriptReferences();
            Lort.TaskIterate(); // Progress bar update
            
            SetFollowerFlags(esm, allCalls);

            /* Subdivide all cell content into tiles */
            PrepareCellTilesAndEmitters(cache, esm, scriptManager, allReferences);
            Lort.TaskIterate(); // Progress bar update

            PrepareInteriorCells(cache, esm, scriptManager);
            Lort.TaskIterate(); // Progress bar update

            /* Resolve load doors and travel npcs */
            ResolveDoorsAndWarps(esm, scriptManager);
            Lort.TaskIterate(); // Progress bar update

            /* default location name value for interiors */
            foreach (InteriorGroup group in interiors)
            {
                int textId = int.Parse($"{group.map:D2}{group.area:D2}0");
                text.SetLocation(textId, "Interior");
            }
            Lort.TaskIterate(); // Progress bar update

            PrecomputeNpcWitnesses();
            Lort.TaskIterate(); // Progress bar update

            /* Statically resolve shop inventories for npcs (also creatures too) */
            ResolveShops(esm);

            Lort.TaskIterate(); // Progress bar update

            /* Part 2 of Papyrus preprocess */
            /* This assigns entity ids to objects that have scripts referencing them */
            /* It also in some cases creates flags and events for objects that require them. */
            /* Also notably we setup disable/enable flags here as well */
            void PreprocessContent(BaseScript areaScript, IEnumerable<Content> contents)
            {
                foreach (Content content in contents)
                {
                    string contentId = content.id.ToLower().Trim();
                    if (allReferences.Contains(contentId) || content is CharacterContent || content is ItemContent || content is DoorContent || content.papyrus != null)
                    {
                        // Create an entity ID for this object so that it can be interacted with via scripts
                        Script.EntityType entityType;
                        switch (content)
                        {
                            case ItemContent ic: entityType = Script.EntityType.Asset; break;
                            case CharacterContent cc: entityType = Script.EntityType.Enemy; break;
                            case StaticContent sc: entityType = Script.EntityType.Asset; break;
                            case LightContent lc: entityType = Script.EntityType.Region; break;         // BTL Light. Scripts on these don't actually work rn
                            default: throw new Exception("Invalid content type for script preprocess");
                        }
                        content.entity = areaScript.CreateEntity(entityType, $"{content.type}::{content.id}");

                        // talkable characters always get disable flags for simplicity. statically resolving dialog triggered self-disable calls is slow as hell
                        if (content is NpcContent || (content is CreatureContent creatureContent && esm.HasDialog(creatureContent)) || toggleableReferences.Contains(contentId))
                        {
                            // Object disabled flag
                            areaScript.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.Disabled, content);
                        }
                    }

                    switch (content)
                    {
                        case LightContent lc:
                            {
                                // BTL lights likely can't have scripts (havent checked) so just continue
                                break;
                            }
                        case ItemContent item:
                            {
                                // Item scripts are registered during MSB generation. This is due to the fact that they are tied in closely with params that are generated at that point.
                                break;
                            }
                        case StaticContent statik:
                            {
                                areaScript.RegisterStaticDisable(statik);
                                break;
                            }
                        case CharacterContent character:
                            {
                                // Dead by type list.
                                // So morrowind uses a weird system where it keeps a count of each "type" of npc/creature is killed
                                // For most npcs this count will only ever be 0 or 1 since there is only one of that npc in the world
                                // But for like rats and shit it keeps count so it knows you've killed 10 rats or whatever
                                // So to emulate this system we will have a seperate counter flag for each record type of creature or npc
                                Script.Flag countFlag = scriptManager.common.GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.DeadCount, character.id);

                                // Humanoid NPCs or creatures that can talk
                                if (character is NpcContent || (character is CreatureContent creatureContent && !character.dead && esm.HasDialog(creatureContent)))
                                {
                                    // Pre-Dead npcs get a special script to have their body just lay there. They do not get any flags or ESD stuff built
                                    if (character is NpcContent npcContent && character.dead) { areaScript.RegisterDeadNpc(npcContent); }
                                    // Create a bunch of stuff needed for NPCs to work
                                    else
                                    {
                                        /* Create various flags requried for NPCs */
                                        Script.Flag firstGreet = areaScript.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.TalkedToPc, character);
                                        Script.Flag disposition = areaScript.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.Disposition, character, (uint)character.disposition);
                                        Script.Flag pickpocketedFlag = areaScript.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.Pickpocketed, character);
                                        Script.Flag thiefFlag = areaScript.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.ThiefCrime, character);

                                        /* Register some scripts for NPCs */
                                        areaScript.RegisterCharacter(param, character, countFlag);
                                        areaScript.RegisterNpcHostility(character);  // setup hostility flag/event
                                    }
                                }
                                // Regular creatures
                                else
                                {
                                    // Dead creatures not supported rn
                                    CreatureContent creature = content as CreatureContent;
                                    if (creature.dead) { throw new Exception("Pre-Dead creatures not supported yet!"); }  // @TODO: add dead bodies for creatures with loot, prolly just a bone pile with items
                                    else { areaScript.RegisterCharacter(param, creature, countFlag); }
                                }
                                break;
                            }
                        default: throw new Exception("Invalid content type for script preprocess");
                    }
                }
            }

            foreach (BaseTile tile in AllTiles) { PreprocessContent(scriptManager.GetScript(tile), tile.GetAllContent()); }
            foreach (InteriorGroup group in interiors)
            {
                foreach (InteriorGroup.Chunk chunk in group.chunks) { PreprocessContent(scriptManager.GetScript(group), chunk.GetAllContent()); }
            }
            Lort.TaskIterate(); // Progress bar update

            /* Preprocess papyrus calls like 'Position' that need a region placed at a location in the world */
            /* Also preprocess Position and PositionCell calls for npcs by creating "PhasedNPCs" */
            PrepareScriptedPositions(cache, esm, param, scriptManager);

            /* Process character aipackage positions */
            foreach (Tile tile in tiles) { tile.ProcessTravelPoints(scriptManager); }
            foreach (InteriorGroup group in interiors) { group.ProcessTravelPositions(scriptManager); }

            /* Generate map point placements */
            AddMapPointsOfInterest(esm, scriptManager);
            Lort.TaskIterate(); // Progress bar update
        }

        private void PrecomputeNpcWitnesses()
        {
            /* Handle npc hasWitness flag */ // this check is very similar to and patially entwined with Script.GenerateCrimeEvents() and the ESD state HANDLECRIME
            /* We can't determine witnesses at runtime so we just do some test now and determine if an npc has witneses to report crimes to */
            /* NOTE: There is a bug here where npcs in exterior tiles do not check nearby tiles. This means that a tile border acts as a barrier that crime reporting wont cross. This is likely a non-issue as tile borders almost never cross through towns but it's important to note! */
            void CheckWitnesses(List<NpcContent> npcs)
            {
                foreach (NpcContent npc in npcs)
                {
                    if (npc.IsHostile()) { npc.witness = CharacterContent.Witness.None; continue; }   // if an npc is naturally hostile to the player they don't report crimes lmao
                    if (npc.IsGuard()) { npc.witness = CharacterContent.Witness.Guard; continue; }
                    if (npc.alarm >= 50) { npc.witness = CharacterContent.Witness.Citizen; }  // iirc alarm is a dice roll from 0 to 100 to determine crime reporting. npcs are usually set to 0, 30, 70, or 100. i'm just going to make this a hard threshold instead for consistency
                    foreach (NpcContent other in npcs)
                    {
                        if (npc == other) { continue; } // dont' self succ

                        // guards get bonus range because I said so
                        if (other.IsGuard() && System.Numerics.Vector3.Distance(npc.position, other.position) < Const.CRIME_GUARD_REACT_RADIUS) { npc.witness = CharacterContent.Witness.Guard; break; }
                        if (other.alarm >= 50 && System.Numerics.Vector3.Distance(npc.position, other.position) < Const.CRIME_CITIZEN_REACT_RADIUS) { npc.witness = CharacterContent.Witness.Citizen; }
                    }
                }
            }

            foreach (Tile tile in tiles) { CheckWitnesses(tile.npcs); }
            foreach (InteriorGroup group in interiors) {
                foreach (InteriorGroup.Chunk chunk in group.chunks) { CheckWitnesses(chunk.npcs); }
            }
        }

        private void ResolveDoorsAndWarps(ESM esm, ScriptManager scriptManager)
        {
            foreach (InteriorGroup group in interiors)
            {
                foreach (InteriorGroup.Chunk chunk in group.chunks)
                {
                    foreach (DoorContent door in chunk.doors)
                    {
                        RegisterDoorWarp(door, scriptManager);
                        if (door.warp != null) { door.entity = scriptManager.GetScript(group).CreateEntity(Script.EntityType.Asset, $"DoorEntry::{door.cell.name}->{door.warp.cell}"); }
                    }

                    foreach (NpcContent npc in chunk.npcs)
                    {
                        RegisterNpcWarp(esm, scriptManager, npc);
                    }
                }
            }

            foreach (Tile tile in tiles)
            {
                foreach (DoorContent door in tile.doors)
                {
                    RegisterDoorWarp(door, scriptManager);
                    if (door.warp != null) { door.entity = scriptManager.GetScript(tile).CreateEntity(Script.EntityType.Asset, $"DoorExit::{door.cell.name}->exterior[{door.warp.x},{door.warp.y}]"); }
                }

                foreach (NpcContent npc in tile.npcs)
                {
                    RegisterNpcWarp(esm, scriptManager, npc, tile);
                }
            }
        }

        private void PrepareInteriorCells(Cache cache, ESM esm, ScriptManager scriptManager)
        {
            int partition = (int)Math.Ceiling(esm.interior.Count / (float)interiors.Count);
            List<Cell>[] cellPreSort = new List<Cell>[interiors.Count];

            int CountBeds(List<Cell> cells) { return cells.Sum(cell => cell.BedCount()); }

            for (int i = 0; i < cellPreSort.Length; i++) { cellPreSort[i] = new(); }  // initialize

            /* Pre-sort interior cells by the number of beds they have (to avoid the 19 bed limit per msb) */
            foreach (Cell cell in esm.interior)
            {
                int beds = cell.BedCount();
                bool fail = true;
                foreach (List<Cell> cells in cellPreSort)
                {
                    if (cells.Count() >= partition) { continue; }                         // group must be less than the partition size of cells
                    if (CountBeds(cells) + beds > Const.MAX_BEDS_PER_MSB) { continue; }  // number of beds in a group has to be 19 or less

                    fail = false;
                    cells.Add(cell);
                    break;
                }

                if (fail) { throw new Exception("Too many beds! Oh god so many beds! Help!"); }
            }
            
            for (int i = 0; i < cellPreSort.Length; i++)
            {
                InteriorGroup group = interiors[i];
                List<Cell> cells = cellPreSort[i];
                BaseScript script = scriptManager.GetScript(group);

                foreach (Cell cell in cells)
                {
                    group.AddCell(scriptManager, cache, cell);
                    scriptManager.areas.Add(cell, script.CreateEntity(Script.EntityType.Region, $"cell {cell.name}"));
                }
            }
        }

        private void AddMapPointsOfInterest(ESM esm, ScriptManager scriptManager)
        {
            Dictionary<string, List<Vector3>> mapPoints = new();

            // collect em all
            foreach (Cell cell in esm.exterior)
            {
                if(!string.IsNullOrEmpty(cell.name))
                {
                    Landscape landscape = esm.GetLandscape(cell.coordinate);
                    Vector3 center;
                    if (landscape == null) { center = new(); }
                    else { center = cell.center + new Vector3(0f, landscape.GetHeightAverage(), 0f); }

                    if (mapPoints.ContainsKey(cell.name)) { mapPoints[cell.name].Add(center); }
                    else { mapPoints.Add(cell.name, new() { center }); }
                }
            }

            // merge similar
            HashSet<string> pointNames = new();
            List<MapPoint> importants = new();
            foreach(var kvp in mapPoints)
            {
                string name = kvp.Key;                // name
                Vector3 center = new();               // average of all points with same name
                float radius = Const.CELL_SIZE / 2;   // minimum size for radius of map point is 1 cell

                Vector3 first = kvp.Value.First();
                foreach(Vector3 pos in kvp.Value)
                {
                    radius = Math.Max(radius, Vector3.Distance(first, pos));
                    center += pos;
                }

                center *= (1f / kvp.Value.Count());

                MapPoint.Icon icon = Override.GetMapIcon(name);
                if (icon == MapPoint.Icon.None) { continue; }  // skip these
                Script.Flag discoverFlag = scriptManager.common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.DiscoverLocation, name);
                Layout.MapPoint mapPoint = new(name, center, radius, true, discoverFlag, icon);
                Tile tile = GetTile(center);
                tile.AddMapPoint(mapPoint);
                importants.Add(mapPoint);
                pointNames.Add(name.ToLower().Trim());
            }

            foreach(Tile tile in tiles)
            {
                foreach(DoorContent door in tile.doors)
                {
                    if(door.warp != null)
                    {
                        string name = door.warp.cell;
                        if (name.Contains(",")) { name = name.Split(",")[0].Trim(); } // Split area sub names so we just have the main area name. Changes things like "Shipwreck, Upper Level" to just "Shipwreck"
                        MapPoint.Icon icon = Override.GetMapIcon(name);

                        if (icon == MapPoint.Icon.None) { continue; }  // skip these

                        // see if map point has already been made. some areas have multiple entrances or exits
                        var lowerName = name.ToLower().Trim();
                        if (pointNames.Contains(lowerName)) { continue; }

                        // checks if a position is inside of one of the important map points we created above. skip these too!
                        if (importants.Any(p => Vector3.Distance(door.position, p.position) <= p.radius)) {  continue; }

                        const float UNIMPORTANT_SIZE_MODIFIER = 0.3f;
                        Script.Flag discoverFlag = scriptManager.common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.DiscoverLocation, name); // if 2 doors go to the same interior we share the flag
                        Layout.MapPoint mapPoint = new(name, door.position, Const.CELL_SIZE * UNIMPORTANT_SIZE_MODIFIER, false, discoverFlag, icon);
                        pointNames.Add(lowerName);
                        tile.AddMapPoint(mapPoint);
                    }
                }
            }
        }

        private void PrepareScriptedPositions(Cache cache, ESM esm, Paramanager param, ScriptManager scriptManager)
        {
            Dictionary<NpcContent, int> phases = new();

            foreach (Tile tile in tiles)
            {
                List<Content> allContent = tile.GetAllContent().ToList();
                foreach(Content content in allContent)
                {
                    if (content is PhasedNpcContent) { continue; } // skip phased npcs as we don't want to re-process them

                    Papyrus papyrus = esm.GetPapyrus(content.papyrus);
                    if (papyrus == null) { continue; }  // no script or failed to get script, skip

                    List<Papyrus.Call> calls = papyrus.GetCalls();
                    PreProcessScriptedPositions(content, calls, scriptManager, param, phases, esm, cache);
                }
            }

            foreach (InteriorGroup group in interiors)
            {
                foreach(InteriorGroup.Chunk chunk in group.chunks)
                {
                    List<Content> allContent = chunk.GetAllContent().ToList();
                    foreach (Content content in allContent)
                    {
                        if (content is PhasedNpcContent) { continue; } // skip phased npcs as we don't want to re-process them

                        Papyrus papyrus = esm.GetPapyrus(content.papyrus);
                        if (papyrus == null) { continue; }  // no script or failed to get script, skip

                        List<Papyrus.Call> calls = papyrus.GetCalls();
                        PreProcessScriptedPositions(content, calls, scriptManager, param, phases, esm, cache);
                    }
                }
            }

            foreach(Papyrus papyrus in esm.scripts)
            {
                foreach(Papyrus.Call call in papyrus.GetCalls(Papyrus.Call.Type.StartScript))
                {
                    Papyrus subscript = esm.GetPapyrus(call.parameters[0]);
                    if (subscript == null) { continue; }  // no script or failed to get script, skip

                    List<Papyrus.Call> calls = subscript.GetCalls();
                    PreProcessScriptedPositions(null, calls, scriptManager, param, phases, esm, cache);
                }
            }

            foreach (Dialog.DialogRecord dialog in esm.dialog)
            {
                List<Papyrus.Call> calls = dialog.GetCalls();
                PreProcessScriptedPositions(null, calls, scriptManager, param, phases, esm, cache); // null here means we cannot process self reference calls. will print error if we run into one
            }
        }

        private void PreProcessScriptedPositions(Content content, List<Papyrus.Call> calls, ScriptManager scriptManager, Paramanager param, Dictionary<NpcContent, int> phases, ESM esm, Cache cache)
        {
            void ReplaceNpc(NpcContent original, PhasedNpcContent replacement)
            {
                // Find any registration scripts pointing at the original content and murder them. This is kind of a bandaid fix for some unanticipated side effects. It gets the job done but ew.
                void RemoveOriginalNpcRegistration(BaseScript script)  // @TODO: may be worth it to simply move this stage up a little bit in this constructor stack to avoid this even happening
                {
                    for(int i=0;i<script.init.Instructions.Count();i++)
                    {
                        EMEVD.Instruction instruction = script.init.Instructions[i];

                        uint arg1 = BitConverter.ToUInt32(instruction.ArgData, 4);
                        uint arg2;

                        // all of these ended up being at the 12 byte offset but im leaving this structure incase we ever need to change things.
                        if (arg1 == scriptManager.common.events[ScriptCommon.Event.NpcHostilityHandler])
                        {
                            arg2 = BitConverter.ToUInt32(instruction.ArgData, 12);
                        }
                        else if (arg1 == scriptManager.common.events[ScriptCommon.Event.SpawnHandler])
                        {
                            arg2 = BitConverter.ToUInt32(instruction.ArgData, 12);
                        }
                        else if (arg1 == scriptManager.common.events[ScriptCommon.Event.SpawnHandlerDisableable])
                        {
                            arg2 = BitConverter.ToUInt32(instruction.ArgData, 12);
                        }
                        else { arg2 = 0; }

                        // Delete if matches
                        if (arg2 != 0 && arg2 == original.entity) { script.init.Instructions.RemoveAt(i--); }
                    }
                }

                // Find this content and replace it with a phased copy. Searches for matching reference, not by id or anything
                bool DoReplacement(BaseScript script, NpcContent original, List<NpcContent> npcs)
                {
                    foreach (NpcContent c in npcs)
                    {
                        if (original == c)
                        {
                            RemoveOriginalNpcRegistration(script);
                            npcs.Replace(original, replacement);
                            scriptManager.AddRoute(replacement, original);
                            script.RegisterCharacter(param, replacement, scriptManager.common.GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.DeadCount, replacement.id));
                            script.RegisterNpcHostility(replacement);
                            return true;
                        }
                    }
                    return false;
                }

                foreach(BaseTile t in AllTiles)
                {
                    BaseScript script = scriptManager.GetScript(t);
                    if(DoReplacement(script, original, t.npcs)) { return; }
                }

                foreach (InteriorGroup g in interiors)
                {
                    foreach (InteriorGroup.Chunk c in g.chunks)
                    {
                        BaseScript script = scriptManager.GetScript(g);
                        if (DoReplacement(script, original, c.npcs)) { return; }
                    }
                }

                throw new Exception("Failed to replace NPC during phased npc preprocess stage!"); // shouldn't happen in theory
            }

            void HandleNpcPhase(BaseTile t, InteriorGroup.Chunk chunk, Vector3 position, float rotation, Content content, Papyrus.Call call)
            {
                // Quick checks before we do anything
                if (content == null && call.target == null) { Lort.Log($" ## Cannot handle self reference position call from empty context! '{call.RAW}' Bad!", Lort.Type.Debug); return; }

                // Find our target
                Content target = null;
                if(call.target == null) { target = content; }
                else
                {
                    // See if our target is already phased
                    foreach(var (n, _) in phases)
                    {
                        if(n.id.ToLower() == call.target) { target = n; break; }
                    }

                    // Otherwise look for it
                    if (target == null) { target = FindScriptReference(content, call.target); }
                }
                if (target == null) { return; } // Failed to find ref, likely a partial build where it doesn't exist
                if (target is not NpcContent) { Lort.Log($" ## Position and PositionCell calls do not support '{target.type}' content. call = '{call.RAW}'", Lort.Type.Debug); return; }

                // Determine if npc is msb promoted or not and set our target tile if so
                BaseTile tile;
                if (t != null && target is CharacterContent cc && cc.follower == true)
                {
                    tile = GetHugeTile(position);
                }
                else { tile = t; }

                // See if there is already a phased npc for this content at the given location
                PhasedNpcContent similar;
                string locationName;
                if(call.type == Papyrus.Call.Type.PositionCell) { locationName = call.parameters[4]; }
                else { locationName = null; }
                if (target is PhasedNpcContent) { similar = scriptManager.FindPhase((PhasedNpcContent)target, locationName, position); }
                else { similar = scriptManager.FindPhase(target.entity, locationName, position); }
                if (similar != null) { return; } // found a phasednpc for this content at the given location already so no need to create another one

                // Get phase
                int phase;
                NpcContent npc = (NpcContent)target;
                if (phases.ContainsKey(npc))
                {
                    // This is NOT the first phase for this npc, just interate and go
                    phase = phases[npc] + 1;
                    phases.Remove(npc);
                    phases.Add(npc, phase);
                }
                else
                {
                    // This is the first phase for this npc so replace the original actor with a phasedactor as phase 0 before we start adding additional phases
                    scriptManager.common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Nibble, Script.Flag.Designation.Phase, npc);
                    PhasedNpcContent rnpc = new(npc, npc.cell, npc.position, npc.rotation, npc.entity, 0);
                    rnpc.entity = npc.entity;
                    ReplaceNpc(npc, rnpc);
                    
                    phase = 1;
                    phases.Add(npc, phase);
                }

                // Add a phased npc to the target location
                BaseScript script;
                PhasedNpcContent pnpc;
                if (tile != null)
                {
                    Cell cell = esm.GetCellByPosition(position);
                    pnpc = new(npc, cell, position, new Vector3(0, rotation, 0), npc.entity, phase);
                    script = scriptManager.GetScript(tile);
                    pnpc.entity = script.CreateEntity(Script.EntityType.Enemy, pnpc.id);
                    tile.AddContent(cache, tile.cells[0], pnpc);
                }
                else
                {
                    pnpc = new(npc, chunk.cell, position, new Vector3(0, rotation, 0), npc.entity, phase);
                    script = scriptManager.GetScript(chunk.group);
                    pnpc.relative = position + chunk.root - chunk.offset;
                    pnpc.entity = script.CreateEntity(Script.EntityType.Enemy, pnpc.id);
                    chunk.AddContent(cache, pnpc);
                }

                scriptManager.AddRoute(pnpc, target);
                script.RegisterCharacter(param, pnpc, scriptManager.common.GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.DeadCount, pnpc.id));
                script.RegisterNpcHostility(pnpc);
            }
                
            foreach (Papyrus.Call call in calls)
            {
                switch (call.type)
                {
                    case Papyrus.Call.Type.Position:
                    {
                        // Find tile this position call is pointing too
                        Vector3 position = Utility.Vector3FromParameters(call.parameters) * Const.GLOBAL_SCALE;
                        float rot = float.Parse(call.parameters[3]);
                        Tile target = GetTile(position);
                        if (target == null) { Lort.Log($"Failed to place region for preprocessed Position call -> '{call.RAW}'", Lort.Type.Debug); break; }

                        // Add a point at this position that will become a region we can use in scripts later to warp the player around
                        if (call.target == "player")
                        {
                            BaseScript script = scriptManager.GetScript(target);
                            target.AddScriptedPosition(script, position, rot);
                        }
                        // Create a phased npc
                        else
                        {
                            HandleNpcPhase(target, null, position, rot, content, call);
                        }
                        break;
                    }
                    case Papyrus.Call.Type.PositionCell:
                    {
                        Vector3 position = Utility.Vector3FromParameters(call.parameters) * Const.GLOBAL_SCALE;
                        float rot = float.Parse(call.parameters[3]);
                        string name = call.parameters[4];
                        InteriorGroup.Chunk target = FindChunk(name);
                        if (target == null) { Lort.Log($"Failed to place region for preprocessed PositionCell call -> '{call.RAW}'", Lort.Type.Debug); break; }

                        // Add a point at this position that will become a region we can use in scripts later to warp the player around
                        if (call.target == "player")
                        {
                            BaseScript script = scriptManager.GetScript(target.group);
                            target.AddScriptedPosition(script, position, rot);
                        }
                        // Create a phased npc
                        else
                        {
                            HandleNpcPhase(null, target, position, rot, content, call);
                        }
                        break;
                    }
                    case Papyrus.Call.Type.AiEscort:
                    case Papyrus.Call.Type.AiFollow:
                    {
                        Vector3 position = Utility.Vector3FromParameters(call.parameters, 2) * Const.GLOBAL_SCALE;
                        Tile target = GetTile(position);
                        if (target == null) { Lort.Log($"Failed to place region for preprocessed AiFollow call -> '{call.RAW}'", Lort.Type.Debug); break; }

                        BaseScript script = scriptManager.GetScript(target);
                        target.AddTravelPoint(script, position, 5f);
                        break;
                    }
                    case Papyrus.Call.Type.AiEscortCell:
                    case Papyrus.Call.Type.AiFollowCell:
                    {
                        Vector3 position = Utility.Vector3FromParameters(call.parameters, 3) * Const.GLOBAL_SCALE;
                        string name = call.parameters[1];
                        InteriorGroup.Chunk target = FindChunk(name);
                        if (target == null) { Lort.Log($"Failed to place region for preprocessed AiFollowCell call -> '{call.RAW}'", Lort.Type.Debug); break; }

                        BaseScript script = scriptManager.GetScript(target.group);
                        target.AddTravelPoint(script, position, 5f);
                        break;
                    }
                    case Papyrus.Call.Type.AiTravel:
                    {
                        Content target;
                        if(content == null) { target = FindScriptReference(null, call.target); } // attempt to resolve a dialog based call ref if possible, no guarantees though
                        else { target = content; }

                        Vector3 position = Utility.Vector3FromParameters(call.parameters) * Const.GLOBAL_SCALE;
                        if(target == null || target.cell.IsExterior()) // failed reference resolve defaults to overworld
                        {
                            Tile t = GetTile(position);
                            if (t == null) { Lort.Log($"Failed to place region for preprocessed AiTravel call -> '{call.RAW}'", Lort.Type.Debug); break; }

                            BaseScript script = scriptManager.GetScript(t);
                            t.AddTravelPoint(script, position, Const.PATH_REGION_SIZE);
                        }
                        else
                        {
                            string name = target.cell.name;
                            InteriorGroup.Chunk c = FindChunk(name);
                            if (c == null) { Lort.Log($"Failed to place region for preprocessed AiTravel call -> '{call.RAW}'", Lort.Type.Debug); break; }

                            BaseScript script = scriptManager.GetScript(c.group);
                            c.AddTravelPoint(script, position, Const.PATH_REGION_SIZE);
                        }
                        break;
                    }
                    default: break; // do nothing
                }
            }
        }

        private void ResolveShops(ESM esm)
        {
            void SetupShop(CharacterContent npc)
            {
                if (!npc.HasBarter()) { return; } // nope!

                bool WillBarter(CharacterContent npc, ESM.Type type)
                {
                    switch (type)
                    {
                        case ESM.Type.Armor:
                            return npc.services.Contains(CharacterContent.Service.BartersArmor);
                        case ESM.Type.Book:
                            return npc.services.Contains(CharacterContent.Service.BartersBooks);
                        case ESM.Type.Clothing:
                            return npc.services.Contains(CharacterContent.Service.BartersClothing);
                        case ESM.Type.Ingredient:
                            return npc.services.Contains(CharacterContent.Service.BartersIngredients);
                        case ESM.Type.Light:
                            return npc.services.Contains(CharacterContent.Service.BartersLights);
                        case ESM.Type.MiscItem:
                            return npc.services.Contains(CharacterContent.Service.BartersMiscItems);
                        case ESM.Type.Weapon:
                            return npc.services.Contains(CharacterContent.Service.BartersWeapons);
                        case ESM.Type.Probe:
                            return npc.services.Contains(CharacterContent.Service.BartersProbes);
                        case ESM.Type.Lockpick:
                            return npc.services.Contains(CharacterContent.Service.BartersLockpicks);
                        case ESM.Type.RepairItem:
                            return npc.services.Contains(CharacterContent.Service.BartersRepairItems);
                        case ESM.Type.Alchemy:
                            return npc.services.Contains(CharacterContent.Service.BartersAlchemy);
                        case ESM.Type.Apparatus:
                            return npc.services.Contains(CharacterContent.Service.BartersApparatus);
                        default:
                            return false;
                    }
                }

                Cell cell = npc.cell;
                List<(string id, int quantity)> shopInv = new();

                void AddOrIncrement(List<(string id, int quantity)> list, (string id, int quantity) tuple)
                {
                    for (int i = 0; i < list.Count(); i++)
                    {
                        (string id, int quantity) entry = list[i];
                        if (entry.id.ToLower() == tuple.id.ToLower()) {
                            list.RemoveAt(i);
                            list.Add((entry.id, entry.quantity + tuple.quantity)); // can't increment value in a tuple because fuck
                            return;
                        }
                    }
                    list.Add(tuple);
                }

                foreach (ItemContent item in cell.items) // add loose items this npc owns
                {
                    if (item.ownerNpc == npc.id)
                    {
                        if (WillBarter(npc, item.type))
                        {
                            AddOrIncrement(shopInv, (item.id, 1));
                        }
                    }
                }
                foreach (ContainerContent container in cell.containers) // add containers this npc owns
                {
                    if (container.ownerNpc == npc.id)
                    {
                        foreach ((string id, int quantity) tuple in container.inventory)
                        {
                            Record record = esm.FindRecordById(tuple.id);
                            if (WillBarter(npc, record.type))
                            {
                                AddOrIncrement(shopInv, tuple);
                            }
                        }
                    }
                }

                foreach ((string id, int quantity) tuple in npc.inventory) // add own inventory to potential barter
                {
                    Record record = esm.FindRecordById(tuple.id);
                    if (WillBarter(npc, record.type))
                    {
                        AddOrIncrement(shopInv, tuple);
                    }
                }
                if (shopInv.Count() > 0) { npc.barter = shopInv; }
            }

            foreach (Tile tile in tiles)
            {
                foreach (NpcContent npc in tile.npcs) { SetupShop(npc); }
                foreach (CreatureContent creature in tile.creatures) { SetupShop(creature); }
            }
            foreach (InteriorGroup group in interiors)
            {
                foreach (InteriorGroup.Chunk chunk in group.chunks)
                {
                    foreach (NpcContent npc in chunk.npcs) { SetupShop(npc); }
                    foreach (CreatureContent creature in chunk.creatures) { SetupShop(creature); }
                }
            }
        }

        private void PrepareCellTilesAndEmitters(Cache cache, ESM esm, ScriptManager scriptManager, HashSet<string> allReferences)
        {
            foreach (Cell cell in esm.exterior)
            {
                HugeTile huge = GetHugeTile(cell.center);
                TerrainInfo terrain = cache.GetTerrain(cell.coordinate);
                if (terrain != null)
                {
                    if (huge != null) { huge.AddTerrain(cell.center, terrain); }
                    else { Lort.Log($" ## WARNING ## Terrain fell outside of reality [{cell.coordinate.x}, {cell.coordinate.y}] -- {cell.region} :: B02", Lort.Type.Debug); }
                }

                if (huge != null)
                {
                    huge.AddCell(scriptManager, cell);

                    foreach (Content content in cell.contents)
                    {
                        if (content is AssetContent assetContent)
                        {
                            /* If an assetcontent has emitter nodes, we convert it to an emittercontent */
                            /* We can't really do this earlier than this point sadly because we need both the ESM loaded and cache built to be able to catch this corner case */
                            /* So we do it here */
                            if (cache.GetModel(assetContent.mesh)?.HasEmitter() == true)
                            {
                                cache.AddConvertedEmitter(assetContent.ConvertToEmitter());
                            }
                        }

                        huge.AddContent(cache, cell, content, allReferences.Contains(content.id.ToLower().Trim()));
                    }
                }
                else { Lort.Log($" ## WARNING ## Cell fell outside of reality [{cell.coordinate.x}, {cell.coordinate.y}] -- {cell.name} :: B02", Lort.Type.Debug); }
            }
        }

        private static void SetFollowerFlags(ESM esm, List<Papyrus.Call> allCalls)
        {
            /* MSB promotion pre-process step */
            /* Goal of this step is to identify all characters that have a "Follow" aipackage  or a "AiFollow" call that target them. */
            void PreProcessFollow(Cell cell)
            {
                foreach (Content content in cell.contents)
                {
                    if (content is CharacterContent cc)
                    {
                        foreach (NpcContent.AiPackage package in cc.packages)
                        {
                            if (package.type == CharacterContent.AiPackage.Type.Follow && package.target == "player") { cc.follower = true; return; }
                        }
                    }
                }
            }
            foreach (Cell cell in esm.exterior) { PreProcessFollow(cell); }  // handle ai packages part
            foreach (Cell cell in esm.interior) { PreProcessFollow(cell); }

            void SetFollowerFlagByRecordId(Cell cell, string id)
            {
                foreach (Content content in cell.contents)
                {
                    if (content is CharacterContent cc && content.id.Trim().ToLower() == id.Trim().ToLower())
                    {
                        cc.follower = true;
                    }
                }
            }

            foreach (Papyrus.Call call in allCalls) // Reusing "allCalls" list from above
            {
                // Grab record reference from target if exists
                if (call.target != null && call.parameters.Count() > 0 && call.parameters[0].ToLower().Trim() == "player")
                {
                    switch (call.type)
                    {
                        case Papyrus.Call.Type.AiFollow:
                        case Papyrus.Call.Type.AiFollowCell:
                        case Papyrus.Call.Type.AiEscort:
                        case Papyrus.Call.Type.AiEscortCell:
                            foreach (Cell cell in esm.exterior) { SetFollowerFlagByRecordId(cell, call.target); }  // handle papyrus call part
                            foreach (Cell cell in esm.interior) { SetFollowerFlagByRecordId(cell, call.target); }
                            break;
                        default: break;
                    }
                }
            }
        }

        private void RegisterDoorWarp(DoorContent door, ScriptManager scriptManager)
        {
            if (door.warp == null) { return; }

            // Door goes to interior cell
            if (door.warp.cell != null)
            {
                InteriorGroup.Chunk to = FindChunk(door.warp.cell);
                if (to == null) { door.warp = null; return; }      // caused by debug sometimes
                string areaName = to.cell.name.Contains(",") ? to.cell.name.Split(",")[^1].Trim() : to.cell.name;
                uint entity = scriptManager.GetScript(to.group).CreateEntity(Script.EntityType.Region, $"DoorExit::{door.cell.name}->{door.warp.cell}");
                door.ApplyWarpParams(to.group.map, to.group.area, to.group.unk, to.group.block,  entity, $"Enter {areaName}");
                to.AddWarp(door.warp);
            }
            // Door goes to exterior cell
            else
            {
                Tile to = FindTile(door.warp.position);  // does not respect cell borders in tile msbs. likely a non-issue but kinda sketch ... @TODO:
                if (to == null) { door.warp = null; return; }     // caused by debug sometimes
                uint entity = scriptManager.GetScript(to).CreateEntity(Script.EntityType.Region, $"DoorExit::{door.cell.name}->exterior[{door.warp.x},{door.warp.y}]"); 
                door.ApplyWarpParams(to.map, to.coordinate.x, to.coordinate.y,  to.block, entity,  $"Exit");
                to.AddWarp(door.warp);
            }
        }

        private void RegisterNpcWarp(ESM esm, ScriptManager scriptManager, NpcContent npc, Tile from = null)
        {
            if (npc.travel.Count <= 0) return;

            // Travel goes to interior cell
            for (int i = 0; i < npc.travel.Count; i++)
            {
                var travel = npc.travel[i];
                if (travel.cell != null)
                {
                    var to = FindChunk(travel.cell);
                    if (to == null) { npc.travel.RemoveAt(i--); continue; }      // caused by debug sometimes
                    var entity = scriptManager.GetScript(to.group).CreateEntity(Script.EntityType.Region, $"TravelDestination::{npc.cell.name}->{travel.cell}");
                    travel.ApplyParams(to.group.map, to.group.area, to.group.unk, to.group.block, entity, to.cell.name, Const.TRAVEL_DEFAULT_COST);
                    to.AddWarp(travel);
                }
                // Travel goes to exterior cell
                else
                {
                    var to = FindTile(travel.position);  // does not respect cell borders in tile msbs. likely a non-issue but kinda sketch ... @TODO:
                    var cell = esm.GetCellByPosition(travel.position); // this should be safe, assuming travel.position is a Morrowind coordinate
                    if (to == null || cell == null) { npc.travel.RemoveAt(i--); continue; }     // caused by debug sometimes
                    var entity = scriptManager.GetScript(to).CreateEntity(Script.EntityType.Region, $"TravelDestination::{npc.cell.name}->exterior[{travel.x},{travel.y}]");
                    int cost;
                    // calculate distance for cost of travel
                    if (from != null)
                    {
                        Vector2 a = new(from.coordinate.x, from.coordinate.y);
                        Vector2 b = new(to.coordinate.x, to.coordinate.y);
                        cost = (int)(Const.TRAVEL_DISTANCE_COST * Math.Max(1, Vector2.Distance(a, b)));
                    }
                    else { cost = Const.TRAVEL_DEFAULT_COST; }
                    travel.ApplyParams(to.map, to.coordinate.x, to.coordinate.y, to.block, entity, cell.name, cost);
                    to.AddWarp(travel);
                }
            }
        }

        public HugeTile GetHugeTile(Vector3 position)
        {
            foreach (HugeTile huge in huges)
            {
                if (huge.PositionInside(position))
                {
                    return huge;
                }
            }
            return null;
        }

        public HugeTile GetHugeTile(Int2 coordinate)
        {
            foreach (HugeTile huge in huges)
            {
                if (huge.coordinate == coordinate)
                {
                    return huge;
                }
            }
            return null;
        }

        public BigTile GetBigTile(Vector3 position)
        {
            foreach (BigTile big in bigs)
            {
                if (big.PositionInside(position))
                {
                    return big;
                }
            }
            return null;
        }

        public Tile GetTile(Vector3 position)
        {
            foreach(Tile tile in tiles)
            {
                if (tile.PositionInside(position))
                {
                    return tile;
                }
            }
            return null;
        }

        public Tile GetTile(Int2 coordinate)
        {
            foreach(Tile tile in tiles)
            {
                if(tile.coordinate == coordinate)
                {
                    return tile;
                }
            }
            return null;
        }

        /* Get an overworld tile that contains the cell of the given name */
        public Tile GetTile(string cellName)
        {
            foreach(Tile tile in tiles)
            {
                foreach(Cell cell in tile.cells)
                {
                    if(cell.name == cellName)
                    {
                        return tile;
                    }
                }
            }
            return null;
        }

        /* Generates a list of map ids that are being used */
        /* This is the first number in the msb name. m60 for example is overworld */
        public List<int> ListCommon()
        {
            List<int> list = new();
            list.Add(60);
            foreach(InteriorGroup group in interiors)
            {
                if (!list.Contains(group.map)) { list.Add(group.map); }
            }

            return list;
        }


        /* These 2 funcs search by reference. It's finding where this specific content object is located */
        public BaseTile FindTile(Content source)
        {
            foreach(BaseTile tile in AllTiles)
            {
                foreach (Content content in tile.GetAllContent())
                {
                    if(content == source)
                    {
                        return tile;
                    }
                }
            }
            return null;
        }

        public Chunk FindChunk(Content source)
        {
            foreach(InteriorGroup group in interiors)
            {
                foreach(InteriorGroup.Chunk chunk in group.chunks)
                {
                    foreach (Content content in chunk.GetAllContent())
                    {
                        if(content == source)
                        {
                            return chunk;
                        }
                    }
                }
            }
            return null;
        }

        public Chunk FindChunk(string name) // find a chunk that contains the named cell
        {
            return interiors.SelectMany(g => g.chunks).FirstOrDefault(chunk => string.Equals(chunk.cell.name, name, StringComparison.OrdinalIgnoreCase));
        }

        public Tile FindTile(Vector3 position) // find a tile based on coords
        {
            return tiles.FirstOrDefault(tile => tile.PositionInside(position));
        }

        /* Called by script compilers in DialogESD.cs and PapyrusEMEVD.cs */
        /* When a Papyrus script references an object by it's record name we need to find that object to do operations on it */
        /* Example is like "fargoth"->disable */
        /* In that case we search through all content to find the first Content with the record id of "fargoth" */
        /* In this search we return the first result even if there are multiple references using that same record */
        /* Morrowind prioritizes loaded cells in this search so we will try to emulate this behaviour */
        /* This function takes a content "source" as an input parameter so we can search the area around the origination of this script call first before searching the full world */
        /* Source can also be null and we skip the local search. Reason for this is that global scripts coming from the papyrus 'Main' do not have a local object they are attached to so it's null */
        public Content FindScriptReference(Content source, string reference)
        {
            if (reference == null) { return null; }
            
            var lowerRef = reference.ToLower();

            if (source != null)
            {
                IEnumerable<Content> local;
                BaseTile tile = FindTile(source);
                if (tile != null)
                {
                    local = tile.GetAllContent();
                }
                else
                {
                    Chunk chunk = FindChunk(source);
                    local = chunk.GetAllContent();
                }

                foreach (Content content in local)
                {
                    if (content.id.ToLower() == lowerRef)
                    {
                        return content;
                    }
                }
            }

            // not found in local area, search whole world now
            foreach(BaseTile t in AllTiles)
            {
                foreach(Content c in t.GetAllContent())
                {
                    if (c.id.ToLower() == lowerRef)
                    {
                        return c;
                    }
                }
            }

            foreach(InteriorGroup group in interiors)
            {
                foreach(InteriorGroup.Chunk chunk in group.chunks)
                {
                    foreach(Content c in chunk.GetAllContent())
                    {
                        if (c.id.ToLower() == lowerRef)
                        {
                            return c;
                        }
                    }
                }
            }

            return null; // not found anywhere!
        }

        /* Finds scripted position in exterior */
        public ScriptedPosition FindScriptedPosition(Vector3 position)
        {
            return tiles
                .SelectMany(tile => tile.positions)
                .FirstOrDefault(sp => Vector3.Distance(position, sp.position) < 0.1f);
        }

        /* Finds scripted position in interior */
        public ScriptedPosition FindScriptedPosition(string name, Vector3 position)
        {
            if (name == null) { return FindScriptedPosition(position); }  // if location = null then we assume its an exterior

            InteriorGroup.Chunk chunk = FindChunk(name);
            return chunk?.positions.FirstOrDefault(sp => Vector3.Distance(position, sp.position) < 0.1f);
        }

        /* Finds travel position that is in the same msb as the given content (used for patrol routes, patrol routes require travel point regions to be in the same msb) */
        public TravelPoint FindTravelable(Content content, Vector3 position)
        {
            BaseTile t = FindTile(content);
            if(t != null && t is Tile tile)
            {
                return tile.travels.FirstOrDefault(tp => Vector3.Distance(position, tp.position) < 0.1f);
            }
            else
            {
                InteriorGroup.Chunk chunk = FindChunk(content);
                return chunk?.travels.FirstOrDefault(tp => Vector3.Distance(position, tp.position) < 0.1f);
            }
        }

        /* Finds travel position in an exterior */
        public TravelPoint FindTravelable(Vector3 position)
        {
            return tiles
                .SelectMany(tile => tile.travels)
                .FirstOrDefault(tp => Vector3.Distance(position, tp.position) < 0.1f);
        }

        /* Finds travel position in an interior */
        public TravelPoint FindTravelable(string name, Vector3 position)
        {
            if (name == null) { return FindTravelable(position); }  // if location = null then we assume its an exterior

            InteriorGroup.Chunk chunk = FindChunk(name);
            return chunk?.travels.FirstOrDefault(tp => Vector3.Distance(position, tp.position) < 0.1f);
        }

        /* Find all pathgridpoints within the radius of the given content */
        public List<PathGridPoint> GetWanderable(Content content, float radius)
        {
            List<PathGridPoint> source;
            BaseTile tile = FindTile(content);
            if (tile != null && tile is Tile t) { source = t.paths; }
            else if (tile != null && tile is HugeTile huge) { return new(); }  // @TODO: This is the case where we attempt to lookup wander points for an CharacterContent that is msb promoted so it fails to find anything. In order to resolve this we will need to propogate pathgrid shit up to big/huge tiles. Big annoying cunt to do and minimal effect on gameplay so fix it later!
            else {
                InteriorGroup.Chunk chunk = FindChunk(content);
                source = chunk.paths;
            }

            List<PathGridPoint> result = new();
            foreach (PathGridPoint p in source)
            {
                if (Vector3.Distance(content.relative, p.position) <= radius * Const.GLOBAL_SCALE) { result.Add(p); }
            }
            return result;
        }

        private static IEnumerable<(int m, int x, int y, int b)> ProcessMsbList(IEnumerable<string[]> msblist)
        {
            foreach (var split in msblist)
            {
                var m = int.Parse(split[0]);
                var x = int.Parse(split[1]);
                var y = int.Parse(split[2]);
                var b = int.Parse(split[3]);
                yield return (m, x, y, b);
            }
        }

        public class MapPoint
        {
            public enum Icon
            {
                None = 0,                 // discards map point completely!
                Auto = 1,                 // Take a wild guess
                StoneBuilding = 3,        // Gnisis, Suran, Maar Gan
                LargeStoneCity = 51,      // Balmora & Ald-ruhn
                Tomb = 4,
                RuinPillars = 5,
                WoodShack = 6,
                WoodTownA = 7,            // Caldera
                RuinTower = 8,
                Circle = 9,               // Default if not specified
                LargeStoneGate = 10,
                LargeBridge = 11,
                CaveEntrance = 13,
                MineEntrance = 14,
                TombSpeical = 16,
                MageTower = 17,
                Fort = 18,
                Farm = 19,
                RuinStoneLarge = 20,
                Encampment = 22,
                StoneTown = 35,         // Pelagiad, Ald Velothi
                WoodTownB = 37,         // Dagon Fel
                WoodTownC = 38,         // Seyda Neen, Gnaar Mok
                WoodTownD = 39,         // Khuul
                WoodTownE = 40,         // Hla Oad
                TombOrnate = 47,
                Canton = 15,            // All vivec cantons
                TreeTown = 55,          // Sadrith Mora and other mushroom towns
                VolcanoTown = 58,       // Molag Mar
                SwampFort = 27,
                Tree = 30,
                TowerTemple = 65,          // Vivec palace
                TreeFort = 26,             // Vos
                SeasideCastle = 28,        // Ebonheart
                DwarvenRuin = 25,          // Ye
                DaedricRuin = 59           // Ye
            }

            public readonly string name;
            public readonly Vector3 position;
            public Vector3 relative;
            public readonly float radius;
            public readonly bool important; // if true then displays text on screen when you enter the area, otherwise just marks it on your map. EX: cities are important, cave entrances are not.
            public Script.Flag discovered;  // 1 bit flag that is flipped when you discover an area. marks it on your map. preston garvey would be proud
            public MapPoint.Icon icon;

            public MapPoint(string name, Vector3 position, float radius, bool important, Script.Flag discovered, MapPoint.Icon icon)
            {
                this.name = name;
                this.position = position;
                this.radius = radius;
                this.important = important;
                this.discovered = discovered;
                if(icon == MapPoint.Icon.Auto)
                {
                    string n = name.ToLower().Trim();
                    if (n.Contains("cave") || n.Contains("grotto")) { this.icon = MapPoint.Icon.CaveEntrance; }
                    else if (n.Contains("farm") || n.Contains("plantation")) { this.icon = MapPoint.Icon.Farm; }
                    else if (n.Contains("shack") || n.Contains("house")) { this.icon = MapPoint.Icon.WoodShack; }
                    else { this.icon = MapPoint.Icon.Circle; }  // default result
                }
                else
                {
                    this.icon = icon;
                }
            }
        }

        public class WarpDestination
        {
            public readonly Vector3 position, rotation;
            public readonly uint id;  // entity id

            public WarpDestination(Vector3 position, Vector3 rotation, uint id)
            {
                this.position = position - new Vector3(0f, Const.NPC_ROOT_OFFSET, 0f);  // fix load door offset
                this.rotation = rotation;
                this.id = id;
            }
        }

        public record PathGridPoint(string name, Vector3 position, uint entity) { }

        public class ScriptedPosition
        {
            public readonly Vector3 position, relative, rotation;
            public readonly uint region, player;  // entity ids
            public int map, area, unk, block;     // for WarpPlayer if needed

            public ScriptedPosition(Vector3 position, Vector3 relative, Vector3 rotation, uint region, uint player, int map, int area, int unk, int block)
            {
                this.position = position;
                this.relative = relative - new Vector3(0f, Const.NPC_ROOT_OFFSET, 0f);  // fix offset
                this.rotation = rotation;
                this.region = region;
                this.player = player;
                this.map = map;
                this.area = area;
                this.unk = unk;
                this.block = block;
            }
        }

        public record TravelPoint(string name, Vector3 position, Vector3 relative, float radius, uint entity) { }
    }
}
