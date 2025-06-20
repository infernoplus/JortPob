﻿using JortPob.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

namespace JortPob
{
    /* BigTile is a 2x2 grid of Tiles. Sort of like an LOD type thing. (????) */
    public class BigTile : BaseTile
    {
        public HugeTile huge;
        public List<Tile> tiles;

        public BigTile(int m, int x, int y, int b) : base(m, x, y, b)
        {
            tiles = new();
        }

        public override Tile GetContentTrueTile(Content content)
        {
            foreach (Tile tile in tiles)
            {
                if (tile.PositionInside(content.position))
                {
                    return tile;
                }
            }
            return null;  // shouldnt happen but will crash if it does
        }

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

        /* Incoming content is in aboslute worldspace from the ESM, when adding content to a tile we convert it's coordiantes to relative space */
        public new void AddContent(Cache cache, Content content)
        {
            switch (content)
            {
                case AssetContent a:
                    ModelInfo modelInfo = cache.GetModel(a.mesh);
                    if (modelInfo.size * (content.scale*0.01f) > Const.CONTENT_SIZE_BIG) {
                        float x = (coordinate.x * 2f * Const.TILE_SIZE) + (Const.TILE_SIZE * 0.5f);
                        float y = (coordinate.y * 2f * Const.TILE_SIZE) + (Const.TILE_SIZE * 0.5f);
                        content.relative = (content.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
                        base.AddContent(cache, content);
                        break;
                    }
                    goto default;
                default:
                    Tile tile = GetTile(content.position);
                    tile.AddContent(cache, content);
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

        public void AddTile(Tile tile)
        {
            tiles.Add(tile);
            tile.big = this;
        }
    }
}
