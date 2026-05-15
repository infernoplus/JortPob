using JortPob.Common;
using JortPob.Scripts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using static JortPob.Layout;

namespace JortPob
{
    public class InteriorGroup
    {
        public readonly int map;
        public readonly int area;
        public readonly int unk;
        public readonly int block;

        public readonly List<Chunk> chunks;

        public InteriorGroup(int m, int a, int u, int b)
        {
            /* Interior Data */
            map = m;
            area = a;
            unk = u;
            block = b;

            chunks = new();
        }

        public int[] IdList()
        {
            return new int[] { map, area, unk, block };
        }

        public bool IsEmpty()
        {
            foreach(Chunk chunk in chunks)
            {
                if (chunk.assets.Count() > 0) { return false; }
            }
            return true;
        }

        public Paramanager.WeatherData GetWeather()
        {
            return Paramanager.INTERIOR_WEATHER_DATA_LIST[1];  // @TODO: actually figure out what kind of cell this is and grab correct weather
        }

        // Fugly code <3
        /* Process an interior cell into a chunk and add it to this group */
        /* This function is awful looking but it does an important bit of math to bound and align the chunk into a grid with other chunks in this group */
        public void AddCell(ScriptManager scriptManager, Cache cache, Cell cell)
        {
            Vector3 root;
            Vector3 bounds = cell.boundsMax - cell.boundsMin;
            if (chunks.Count > 0)
            {
                float x_calc, z_calc;

                if (chunks.Count % Const.CHUNK_PARTITION_SIZE == 0) {
                    x_calc = 0;

                    z_calc = float.MinValue;
                    for (int i = Math.Max(0, chunks.Count - 1 - Const.CHUNK_PARTITION_SIZE); i < chunks.Count; i++)
                    {
                        Chunk c = chunks[i];
                        z_calc = Math.Max(z_calc, c.root.Z + c.bounds.Z);
                    }
                    z_calc = z_calc + bounds.Z;
                }
                else
                {
                    Chunk last = chunks[chunks.Count - 1];
                    x_calc = last.root.X + last.bounds.X + bounds.X;
                    z_calc = last.root.Z;
                }
                root = new Vector3(x_calc, 0, z_calc);
            }
            else
            {
                root = new(0, 0, 0);
            }
            Chunk chunk = new(scriptManager, cache, this, cell, root);
            chunks.Add(chunk);
        }

        public void ProcessTravelPositions(ScriptManager scriptManager)
        {
            foreach(InteriorGroup.Chunk chunk in chunks)
            {
                chunk.ProcessTravelPoints(scriptManager);
            }
        }

        public class Chunk
        {
            public readonly InteriorGroup group;
            public readonly Cell cell;

            public readonly Vector3 root;
            public readonly Vector3 bounds, offset; // size from center

            public Obj nav;  // navmesh repersentation of this msb chunk
            public List<PathGridPoint> paths; // mw uses these for nav. we are only using them for wander positions

            public readonly List<AssetContent> assets;
            public readonly List<DoorContent> doors;
            public readonly List<LightContent> lights;
            public readonly List<EmitterContent> emitters;
            public readonly List<CreatureContent> creatures;
            public readonly List<NpcContent> npcs;
            public readonly List<ContainerContent> containers;
            public readonly List<PickableContent> pickables;
            public readonly List<ItemContent> items;

            public readonly List<Layout.WarpDestination> warps; // end points for load doors in other cells. also used by travel npcs
            public readonly List<Layout.ScriptedPosition> positions; // used by scripts to target locations EX: 'PositionCell'
            public readonly List<TravelPoint> travels; // positions directly referenced in AiPackages

            public Chunk(ScriptManager scriptManager, Cache cache, InteriorGroup group, Cell cell, Vector3 root)
            {
                this.group = group;
                this.cell = cell;
                this.root = root;

                bounds = cell.boundsMax - cell.boundsMin;
                offset = Vector3.Lerp(cell.boundsMin, cell.boundsMax, .5f);

                nav = new();
                paths = new();
                travels = new();

                assets = new();
                doors = new();
                emitters = new();
                lights = new();
                creatures = new();
                npcs = new();
                containers = new();
                pickables = new();
                items = new();

                warps = new();
                positions = new();

                /* Add content */
                foreach (Content content in cell.contents)
                {
                    content.relative = content.position + root - offset;
                    AddContent(cache,content);
                }

                /* Add cells pathgrid to the tile */
                BaseScript script = scriptManager.GetScript(group);
                for (int i=0;i<cell.paths.Count();i++)
                {
                    Vector3 path = cell.paths[i];
                    string name = $"PathGrid_{group.map:D2}{group.area:D2}{group.unk:D2}_{group.chunks.Count():D2}_{i:D4}";
                    Vector3 relative = path + root - offset;
                    Layout.PathGridPoint point = new(name, relative, script.CreateEntity(Script.EntityType.Region, $"PathGridPoint"));
                    paths.Add(point);
                }
            }

            public IEnumerable<Content> GetAllContent()
            {
                IEnumerable<IEnumerable<Content>> all = [
                    assets,
                    doors,
                    emitters,
                    lights,
                    creatures,
                    npcs,
                    containers,
                    pickables,
                    items,
                ];

                foreach (IEnumerable<Content> enumerable in all)
                {
                    foreach (Content content in enumerable)
                    {
                        yield return content;
                    }
                }
            }

            public void AddWarp(DoorContent.Warp warp)
            {
                Layout.WarpDestination dest = new(warp.position + root - offset, warp.rotation, warp.entity);
                warps.Add(dest);
            }

            public void AddScriptedPosition(BaseScript script, Vector3 position, float rot)
            {
                Vector3 relative = position + root - offset;
                Vector3 rotation = new Vector3(0, rot, 0); // @TODO: THIS IS WRONG!
                uint region = script.CreateEntity(Script.EntityType.Region, $"ScriptedPosition:Region:{position.ToString()}");
                uint player = script.CreateEntity(Script.EntityType.Region, $"ScriptedPosition:Player:{position.ToString()}");
                positions.Add(new(position, relative, rotation, region, player, group.map, group.area, group.unk, group.block));
            }

            public void AddNav(Cache cache, Content content)
            {
                if (Const.DEBUG_SKIP_NAVMESH) { return; }
                switch (content)
                {
                    case AssetContent a:
                        ModelInfo modelInfo = cache.GetModel(content.mesh, content.scale);
                        if (!modelInfo.HasCollision()) { break; } // no collision means no nav info
                        nav.add(new Obj(Path.Combine(Const.CACHE_PATH, modelInfo.collision.obj)), content.relative, content.rotation, modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);
                        break;
                    default: break;
                }
            }

            public void AddContent(Cache cache, Content content)
            {
                switch (content)
                {
                    case AssetContent a:
                        assets.Add(a); break;
                    case DoorContent d:
                        doors.Add(d); break;
                    case EmitterContent e:
                        emitters.Add(e); break;
                    case LightContent l:
                        lights.Add(l); break;
                    case ContainerContent o:
                        containers.Add(o); break;
                    case ItemContent i:
                        items.Add(i); break;
                    case PickableContent p:
                        pickables.Add(p); break;
                    case NpcContent n:
                        npcs.Add(n); break;
                    case CreatureContent c:
                        creatures.Add(c); break;
                    default:
                        Lort.Log($" ## WARNING ## Unhandled Content class '{content.type}::{content.id}' fell through AddContent()", Lort.Type.Debug); break;
                }

                AddNav(cache, content);
            }

            /* Add travelpoint */
            public void AddTravelPoint(BaseScript script, Vector3 point, float radius = -1f)
            {
                Vector3 relative = point + root - offset;
                uint region = script.CreateEntity(Script.EntityType.Region, $"Travel:Region:{point.ToString()}");
                TravelPoint travel = new($"Travel_{group.map:D2}{group.area:D2}{group.unk:D2}_{SafeName()}_{travels.Count():D4}", point, relative, radius == -1f ? Const.PATH_REGION_SIZE : radius, region);
                travels.Add(travel);
            }

            /* Converts all "Travel" positions to scriptedpositions */
            public void ProcessTravelPoints(ScriptManager scriptManager)
            {
                BaseScript script = scriptManager.GetScript(group);
                void HandleCharacterContent(CharacterContent content)
                {
                    foreach (CharacterContent.AiPackage package in content.packages)
                    {
                        if (package.type == CharacterContent.AiPackage.Type.Travel)
                        {
                            Vector3 relative = package.position + root - offset;
                            uint region = script.CreateEntity(Script.EntityType.Region, $"Travel:Region:{package.position.ToString()}");
                            TravelPoint travel = new($"Travel_{group.map:D2}{group.area:D2}{group.unk:D2}_{SafeName()}_{travels.Count():D4}", package.position, relative, Const.PATH_REGION_SIZE, region);
                            travels.Add(travel);
                        }
                    }
                }

                foreach (NpcContent c in npcs) { HandleCharacterContent(c); }
                foreach (CreatureContent c in creatures) { HandleCharacterContent(c); }
            }

            private string SafeName()
            {
                return cell.name.Replace(" ", "").Replace(",", "").Replace("-", "").ToLower().Trim();
            }
        }
    }
}
