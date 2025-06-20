﻿using JortPob.Common;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace JortPob
{
    /* HugeTile is a 4x4 grid of Tiles. Sort of like an LOD type thing. (????) */
    public class HugeTile : BaseTile
    {
        public List<BigTile> bigs;
        public List<Tile> tiles;

        public HugeTile(int m, int x, int y, int b) : base(m, x, y, b)
        {
            bigs = new();
            tiles = new();
        }

        public override Tile GetContentTrueTile(Content content)
        {
            foreach(Tile tile in tiles)
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

        public void AddTerrain(Vector3 position, TerrainInfo terrainInfo)
        {
            float x = (coordinate.x * 4f * Const.TILE_SIZE) + (Const.TILE_SIZE * 1.5f);
            float y = (coordinate.y * 4f * Const.TILE_SIZE) + (Const.TILE_SIZE * 1.5f);
            Vector3 relative = (position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
            terrain.Add(new Tuple<Vector3, TerrainInfo>(relative, terrainInfo));

            Tile tile = GetTile(position);
            tile.AddTerrain(position, terrainInfo);
        }

        /* Incoming content is in aboslute worldspace from the ESM, when adding content to a tile we convert it's coordinates to relative space */
        public new void AddContent(Cache cache, Content content)
        {
            switch (content)   // How the fuck is there not a better way to do this. for fucks sake C#
            {
                case AssetContent a:
                    ModelInfo modelInfo = cache.GetModel(a.mesh);
                    if (modelInfo.size * (content.scale * 0.01f) > Const.CONTENT_SIZE_HUGE) {
                        float x = (coordinate.x * 4f * Const.TILE_SIZE) + (Const.TILE_SIZE * 1.5f);
                        float y = (coordinate.y * 4f * Const.TILE_SIZE) + (Const.TILE_SIZE * 1.5f);
                        content.relative = (content.position + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
                        base.AddContent(cache,content);
                        break;
                    }
                    goto default;
                default:
                    BigTile big = GetBigTile(content.position);
                    big.AddContent(cache, content);
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

        public void AddTile(Tile tile)
        {
            tiles.Add(tile);
            tile.huge = this;
        }
    }
}
