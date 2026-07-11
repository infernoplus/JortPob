using JortPob.Common;
using JortPob.Scripts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using static JortPob.Layout;

namespace JortPob
{
    /* A Tile is what we call a single square on the Elden Ring cell grid. It's basically the Elden Ring version of a "cell" */
    [DebuggerDisplay("Tile m{map}_{coordinate.x}_{coordinate.y}_{block} :: [{cells.Count}] Cells")]
    public class Tile(int m, int x, int y, int b) : BaseTile(m, x, y, b)
    {
        public HugeTile huge;
        public BigTile big;

        public Obj nav = new();  // navmesh representation of this msb tile
        public override List<Layout.PathGridPoint> paths { get; } = [];
        public override List<Layout.TravelPoint> travels { get; } = [];

        /* Checks ABSOLUTE POSITION! This is the position of an object from the ESM accounting for the layout offset! */
        public bool PositionInside(Vector3 position)
        {
            Vector3 pos = position + Const.LAYOUT_COORDINATE_OFFSET;

            float x1 = (coordinate.x * Const.TILE_SIZE) - (Const.TILE_SIZE * 0.5f);
            float y1 = (coordinate.y * Const.TILE_SIZE) - (Const.TILE_SIZE * 0.5f);
            float x2 = x1 + Const.TILE_SIZE;
            float y2 = y1 + Const.TILE_SIZE;

            if(pos.X >= x1 && pos.X < x2 && pos.Z >= y1 && pos.Z < y2)
            {
                return true;
            }

            return false;
        }

        /* Returns averaged region of this tile. Each cell has a region set so the best we can do is see what region is most common among cells in this tile and return that */
        public string GetRegion()
        {
            Dictionary<string, int> regions = [];
            foreach(Cell cell in cells)
            {
                if (cell.region == null) { continue; }
                string r = cell.region.Trim().ToLower();
                if (regions.ContainsKey(r)) { regions[r]++; }
                else { regions.Add(r, 1); }
            }

            if (regions.Count <= 0) { return "Default Region"; } // no regions set so guh

            string most = regions.Keys.First();
            foreach(KeyValuePair<string, int> kvp in regions)
            {
                if (regions[most] < kvp.Value)
                {
                    most = kvp.Key;
                }
            }

            /* Red Mountain has priority for skybox */
            string redMountain = "red mountain region"; // Case sensitive
            if (regions.ContainsKey(redMountain))
            {
                if (regions[redMountain] >= 3) { most = redMountain; }
            }

            return most;
        }

        public override void AddCell(ScriptManager scriptManager, Cell cell)
        {
            cells.Add(cell);

            /* Add cells pathgrid to the tile */
            BaseScript script = scriptManager.GetScript(this);
            for (int i = 0; i < cell.paths.Count; i++)
            {
                Vector3 path = cell.paths[i];
                string name = $"PathGrid_{map:D2}{coordinate.x:D2}{coordinate.y:D2}_{cell.coordinate.x:D2}{cell.coordinate.y:D2}_{i:D4}";
                float x = (coordinate.x * Const.TILE_SIZE);
                float y = (coordinate.y * Const.TILE_SIZE);
                Vector3 relative = (path + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
                Layout.PathGridPoint point = new(name,relative, script.CreateEntity(Script.EntityType.Region, $"PathGridPoint"));
                paths.Add(point);
            }
        }

        public void AddTerrain(Vector3 position, TerrainInfo terrainInfo)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);
            Vector3 relative = (position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
            terrain.Add(new Tuple<Vector3, TerrainInfo>(relative, terrainInfo));
        }

        public void FinalizeTerrainNav()
        {
            Obj all = new();
            foreach((Vector3 v, TerrainInfo t) in terrain)
            {
                Obj obj = new(Path.Combine(Const.CACHE_PATH, t.obj));
                all.add(obj, v, Vector3.Zero, 1f);
            }
            all.collapse(Obj.CollisionMaterial.Stock);
            all.optimize();
            all.borderize(1.25f);
            nav.add(all, Vector3.Zero, Vector3.Zero, 1f);
        }

        public override void AddContent(Cache cache, Cell cell, Content content, bool forceFallThrough = false)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);
            content.relative = (content.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);

            AddNav(cache, cell, content);

            base.AddContent(cache, cell, content);
        }

        public void AddNav(Cache cache, Cell cell, Content content)
        {
            if (Const.DEBUG_SKIP_NAVMESH) { return; }

            /* Recalcualte relative for this tile because this content may be coming from a BigTile or HugeTile and the content.relative will not be valid in those cases */
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);
            Vector3 relative = (content.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);

            switch (content)
            {
                case AssetContent a:
                    ModelInfo modelInfo = cache.GetModel(content.mesh, content.scale);
                    if (!modelInfo.HasCollision()) { break; } // no collision means no nav info
                    nav.add(new Obj(Path.Combine(Const.CACHE_PATH, modelInfo.collision.obj)), relative, content.rotation, modelInfo.UseScale() ? (content.scale * 0.01f) : 1f);
                    break;
                default: break;
            }
        }

        public void AddWarp(DoorContent.Warp warp)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);

            Layout.WarpDestination dest = new((warp.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y), warp.rotation, warp.entity);
            warps.Add(dest);
        }

        public void AddMapPoint(Layout.MapPoint point)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);

            point.relative = (point.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
            points.Add(point);
        }

        public void AddScriptedPosition(BaseScript script, Vector3 position, float rot)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);

            Vector3 relative = (position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
            Vector3 rotation = new Vector3(0, rot, 0); // @TODO: THIS IS WRONG!
            uint region = script.CreateEntity(Script.EntityType.Region, $"ScriptedPosition:Region:{position}");
            uint player = script.CreateEntity(Script.EntityType.Region, $"ScriptedPosition:Player:{position}");
            positions.Add(new(position, relative, rotation, region, player, map, coordinate.x, coordinate.y, block));
        }
        
        /* Add travelpoint */
        public void AddTravelPoint(BaseScript script, Vector3 point, float radius = -1f)
        {
            float x = (coordinate.x * Const.TILE_SIZE);
            float y = (coordinate.y * Const.TILE_SIZE);
            Vector3 relative = (point + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
            uint region = script.CreateEntity(Script.EntityType.Region, $"Travel:Region:{point}");
            TravelPoint travel = new($"Travel_{map:D2}{coordinate.x:D2}{coordinate.y:D2}_{travels.Count:D4}", point, relative, radius == -1f ? Const.PATH_REGION_SIZE : radius, region);
            travels.Add(travel);
        }

        /* Converts all "Travel" positions to travelpoints */
        public void ProcessTravelPoints(ScriptManager scriptManager)
        {
            BaseScript script = scriptManager.GetScript(this);
            void HandleCharacterContent(CharacterContent content)
            {
                foreach (CharacterContent.AiPackage package in content.packages)
                {
                    if (package.type == CharacterContent.AiPackage.Type.Travel)
                    {
                        float x = (coordinate.x * Const.TILE_SIZE);
                        float y = (coordinate.y * Const.TILE_SIZE);
                        Vector3 relative = (package.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
                        uint region = script.CreateEntity(Script.EntityType.Region, $"Travel:Region:{package.position}");
                        TravelPoint travel = new($"Travel_{map:D2}{coordinate.x:D2}{coordinate.y:D2}_{travels.Count:D4}", package.position, relative, Const.PATH_REGION_SIZE, region);
                        travels.Add(travel);
                    }
                }
            }

            foreach (NpcContent c in npcs) { HandleCharacterContent(c); }
            foreach (CreatureContent c in creatures) { HandleCharacterContent(c); }
        }
    }



    public abstract class BaseTile(int m, int x, int y, int b) : IMSBCompilableGroup, IMSBCompilableChunk
    {
        public int map { get; init; } = m;
        public Int2 coordinate { get; init; } = new(x, y);
        public int block { get; init; } = b;

        public List<Cell> cells { get; init; } = [];

        public List<Tuple<Vector3, TerrainInfo>> terrain { get; init; } = [];
        public List<AssetContent> assets { get; init; } = [];
        public List<DoorContent> doors { get; init; } = [];
        public List<LightContent> lights { get; init; } = [];
        public List<EmitterContent> emitters { get; init; } = [];
        public List<CreatureContent> creatures { get; init; } = [];
        public List<NpcContent> npcs { get; init; } = [];
        public List<ContainerContent> containers { get; init; } = [];
        public List<PickableContent> pickables { get; init; } = [];
        public List<ItemContent> items { get; init; } = [];
        public List<Layout.WarpDestination> warps { get; init; } = [];
        public List<Layout.MapPoint> points { get; init; } = [];
        public List<Layout.ScriptedPosition> positions { get; init; } = [];

        public bool IsInterior { get; } = false;
        public Vector3 root
        {
            get { return new Vector3(0); }  // not used but needed to satisfy interface
        }
        public Vector3 bounds
        {
            get { return new Vector3(0); }  // not used but needed to satisfy interface
        }
        public List<IMSBCompilableChunk> Chunks
        {
            get { return [this]; }
        }
        public virtual List<Layout.TravelPoint> travels
        {
            get { return []; }
        }
        public virtual List<Layout.PathGridPoint> paths
        {
            get { return []; }
        }

        public int[] IdList()
        {
            return [map, coordinate.x, coordinate.y, block];
        }

        public bool IsEmpty
        {
            get { return cells.Count <= 0 && terrain.Count <= 0 && assets.Count <= 0; }
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

        public abstract void AddCell(ScriptManager scriptManager, Cell cell);

        /* Incoming content is in aboslute worldspace from the ESM, when adding content to a tile we convert it's coordiantes to relative space */
        public virtual void AddContent(Cache cache, Cell cell, Content content, bool forceFallThrough = false)
        {
            switch(content)
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
                case PickableContent p:
                    pickables.Add(p); break;
                case ItemContent i:
                    items.Add(i); break;
                case NpcContent n:
                    npcs.Add(n); break;
                case CreatureContent c:
                    creatures.Add(c); break;
                default:
                    Lort.Log($" ## WARNING ## Unhandled Content class '{content.type}::{content.id}' fell through AddContent()", Lort.Type.Debug); break;
            }
        }
        
        /**
         * We double the coordinates from our center to approximate the tile shape, then check each of
         * the `tilesToAdd` to see if it is entirely contained within the BigTile (with a little padding),
         * and add any that are fully contained.
         *
         * This is mainly useful in `BigTile`/`HugeTile`, but our class hierarchy puts the implementation here.
         */
        protected IEnumerable<T> FilterTilesToAdd<T>(IEnumerable<T> tilesToAdd, int scaleFactor, int padding) where T: BaseTile
        {
            var x1 = coordinate.x * scaleFactor;
            var y1 = coordinate.y * scaleFactor;
            var x2 = x1 + padding;
            var y2 = y1 + padding;

            return tilesToAdd.Where(t =>
                t.coordinate.x >= x1 && t.coordinate.x < x2 && t.coordinate.y >= y1 && t.coordinate.y < y2);
        }
    }
}
