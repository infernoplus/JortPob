using JortPob.Common;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace JortPob
{
    /* BigTile is a 2x2 grid of Tiles. Sort of like an LOD type thing. */
    [DebuggerDisplay("Big m{map}_{coordinate.x}_{coordinate.y}_{block} :: [{cells.Count}] Cells")]
    public class BigTile(int m, int x, int y, int b) : BaseTile(m, x, y, b)
    {
        public HugeTile huge;
        public List<Tile> tiles = [];

        /* Checks ABSOLUTE POSITION! This is the position of an object from the ESM accounting for the layout offset! */
        public bool PositionInside(Vector3 position)
        {
            Vector3 pos = position + Const.LAYOUT_COORDINATE_OFFSET;

            float x1 = (coordinate.x * 2f * Const.TILE_SIZE) - (Const.TILE_SIZE * 0.5f);
            float y1 = (coordinate.y * 2f * Const.TILE_SIZE) - (Const.TILE_SIZE * 0.5f);
            float x2 = x1 + (Const.TILE_SIZE * 2f);
            float y2 = y1 + (Const.TILE_SIZE * 2f);

            if (pos.X >= x1 && pos.X < x2 && pos.Z >= y1 && pos.Z < y2)
            {
                return true;
            }

            return false;
        }

        public override void AddCell(ScriptManager scriptManager, Cell cell)
        {
            cells.Add(cell);
            Tile tile = GetTile(cell.center);
            if (tile == null) { Lort.Log($" ## WARNING ## Cell fell outside of reality [{cell.coordinate.x}, {cell.coordinate.y}] -- {cell.name} :: B00", Lort.Type.Debug); return; }
            tile.AddCell(scriptManager, cell);
        }

        /* Incoming content is in aboslute worldspace from the ESM, when adding content to a tile we convert it's coordinates to relative space */
        public override void AddContent(Cache cache, Cell cell, Content content, bool forceFallThrough = false)
        {
            switch (content)
            {
                case AssetContent a:
                    ModelInfo modelInfo = cache.GetModel(a.mesh);
                    if (modelInfo.size * (content.scale*0.01f) > Const.CONTENT_SIZE_BIG) {
                        float x = (coordinate.x * 2f * Const.TILE_SIZE) + (Const.TILE_SIZE * 0.5f);
                        float y = (coordinate.y * 2f * Const.TILE_SIZE) + (Const.TILE_SIZE * 0.5f);
                        content.relative = (content.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
                        Tile t = GetTile(cell.center);
                        if (t == null) { break; } // Content fell outside of the bounds of any valid msbs. BAD!
                        content.load = t.coordinate;
                        base.AddContent(cache, cell, content);
                        t.AddNav(cache, cell, content);
                        break;
                    }
                    goto default;
                default:
                    Tile tile = GetTile(cell.center);
                    if (tile == null) { break; } // Content fell outside of the bounds of any valid msbs. BAD!
                    tile.AddContent(cache, cell, content);
                    break;
            }
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
        
        public void AddMatchingTiles(IEnumerable<Tile> tilesToAdd)
        {
            foreach (var tile in FilterTilesToAdd(tilesToAdd, 2, 2))
            {
                tiles.Add(tile);
                tile.big = this;
            }
        }
    }
}
