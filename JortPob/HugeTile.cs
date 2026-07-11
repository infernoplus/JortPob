using JortPob.Common;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using HKLib.hk2018.hkcdStaticMeshTree;

namespace JortPob
{
    /* HugeTile is a 4x4 grid of Tiles. Sort of like an LOD type thing. HugeTile contains 4 big tiles & 16 regular tiles. */
    [DebuggerDisplay("Huge m{map}_{coordinate.x}_{coordinate.y}_{block} :: [{cells.Count}] Cells")]
    public class HugeTile(int m, int x, int y, int b) : BaseTile(m, x, y, b)
    {
        public List<BigTile> bigs = [];
        public List<Tile> tiles = [];

        /* Checks ABSOLUTE POSITION! This is the position of an object from the ESM accounting for the layout offset! */
        public bool PositionInside(Vector3 position)
        {
            Vector3 pos = position + Const.LAYOUT_COORDINATE_OFFSET;

            float x1 = (coordinate.x * 4f * Const.TILE_SIZE) - (Const.TILE_SIZE * 0.5f);
            float y1 = (coordinate.y * 4f * Const.TILE_SIZE) - (Const.TILE_SIZE * 0.5f);
            float x2 = x1 + (Const.TILE_SIZE * 4f);
            float y2 = y1 + (Const.TILE_SIZE * 4f);

            if (pos.X >= x1 && pos.X < x2 && pos.Z >= y1 && pos.Z < y2)
            {
                return true;
            }

            return false;
        }

        public override void AddCell(ScriptManager scriptManager, Cell cell)
        {
            cells.Add(cell);
            BigTile big = GetBigTile(cell.center);
            if(big == null) { Lort.Log($" ## WARNING ## Cell fell outside of reality [{cell.coordinate.x}, {cell.coordinate.y}] -- {cell.name} :: B01", Lort.Type.Debug); return; }
            big.AddCell(scriptManager, cell);
        }

        public void AddTerrain(Vector3 position, TerrainInfo terrainInfo)
        {
            /*  // deprecated, all terrain has been moved to superoverworld in the OverworldManager class
            float x = (coordinate.x * 4f * Const.TILE_SIZE) + (Const.TILE_SIZE * 1.5f);
            float y = (coordinate.y * 4f * Const.TILE_SIZE) + (Const.TILE_SIZE * 1.5f);
            Vector3 relative = (position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
            terrain.Add(new Tuple<Vector3, TerrainInfo>(relative, terrainInfo));*/

            Tile tile = GetTile(position);
            if (tile != null) { tile.AddTerrain(position, terrainInfo); }
            else { Lort.Log($" ## WARNING ## Terrain fell outside of reality [{terrainInfo.coordinate.x}, {terrainInfo.coordinate.y}] :: B01", Lort.Type.Debug); }
        }

        /* Incoming content is in aboslute worldspace from the ESM, when adding content to a tile we convert it's coordinates to relative space */
        public override void AddContent(Cache cache, Cell cell, Content content, bool forceFallThrough = false)
        {
            switch (content)
            {
                case AssetContent a:
                    // Special case: if content has a papyrus script it needs to be put in the small tile. large tiles dont have attached EMEVD scripts so they cant live there
                    if (content.papyrus != null || forceFallThrough) { GetTile(cell.center).AddContent(cache, cell, content); return; }

                    ModelInfo modelInfo = cache.GetModel(a.mesh);
                    if (modelInfo.size * (content.scale * 0.01f) > Const.CONTENT_SIZE_HUGE)
                    {
                        float x = (coordinate.x * 4f * Const.TILE_SIZE) + (Const.TILE_SIZE * 1.5f);
                        float y = (coordinate.y * 4f * Const.TILE_SIZE) + (Const.TILE_SIZE * 1.5f);
                        content.relative = (content.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
                        Tile tile = GetTile(cell.center);
                        if(tile == null) { break; } // Content fell outside of the bounds of any valid msbs. BAD!
                        content.load = tile.coordinate;
                        base.AddContent(cache, cell, content);
                        tile.AddNav(cache, cell, content);
                        break;
                    }
                    goto default;
                case CharacterContent c:
                    if(c.follower)
                    {
                        float x = (coordinate.x * 4f * Const.TILE_SIZE) + (Const.TILE_SIZE * 1.5f);
                        float y = (coordinate.y * 4f * Const.TILE_SIZE) + (Const.TILE_SIZE * 1.5f);
                        content.relative = (content.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
                        Tile tile = GetTile(c.position);
                        content.load = tile.coordinate;
                        base.AddContent(cache, cell, content);
                        break;
                    }
                    goto default;
                default:
                    BigTile big = GetBigTile(cell.center);
                    if (big == null) { break; } // Content fell outside of the bounds of any valid msbs. BAD!
                    big.AddContent(cache, cell, content);
                    break;
            }
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
            foreach (Tile tile in tiles)
            {
                if (tile.PositionInside(position))
                {
                    return tile;
                }
            }
            return null;
        }

        public void AddBig(BigTile big)
        {
            bigs.Add(big);
            big.huge = this;
        }
        
        public void AddMatchingTiles(IEnumerable<Tile> tilesToAdd)
        {
            foreach (var tile in FilterTilesToAdd(tilesToAdd, 4, 4))
            {
                tiles.Add(tile);
                tile.huge = this;
            }
        }

        public void AddMatchingTiles(IEnumerable<BigTile> tilesToAdd)
        {
            foreach (var tile in FilterTilesToAdd(tilesToAdd, 2, 2))
            {
                bigs.Add(tile);
                tile.huge = this;
            }
        }

        public void AddTile(Tile tile)
        {
            tiles.Add(tile);
            tile.huge = this;
        }

        public string GetRegion()
        {
            Dictionary<string, int> regions = new();
            foreach (Cell cell in cells)
            {
                if (cell.region == null) { continue; }
                string r = cell.region.Trim().ToLower();
                if (regions.ContainsKey(r)) { regions[r]++; }
                else { regions.Add(r, 1); }
            }

            if (regions.Count <= 0) { return "Default Region"; } // no regions set so guh

            string most = regions.Keys.First();
            foreach (KeyValuePair<string, int> kvp in regions)
            {
                if (regions[most] < kvp.Value)
                {
                    most = kvp.Key;
                }
            }

            /* Red Mountain has priority for skybox */
            string redMountain = "Red Mountain Region".Trim().ToLower();
            if (regions.ContainsKey(redMountain))
            {
                if (regions[redMountain] >= 8) { most = redMountain; }
            }

            return most;
        }
    }
}
