﻿using JortPob.Common;
using SharpAssimp.Configs;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

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

        // Fugly code <3
        /* Process an interior cell into a chunk and add it to this group */
        /* This function is awful looking but it does an important bit of math to bound and align the chunk into a grid with other chunks in this group */
        public void AddCell(Cell cell)
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
            Chunk chunk = new(cell, root);
            chunks.Add(chunk);
        }

        public class Chunk
        {
            public Vector3 root;
            public Vector3 bounds; // size from center

            public readonly List<AssetContent> assets;
            public readonly List<LightContent> lights;
            public readonly List<EmitterContent> emitters;
            public readonly List<CreatureContent> creatures;
            public readonly List<NpcContent> npcs;

            public Chunk(Cell cell, Vector3 root)
            {
                this.root = root;

                bounds = cell.boundsMax - cell.boundsMin;
                Vector3 offset = Vector3.Lerp(cell.boundsMin, cell.boundsMax, .5f);

                assets = new();
                emitters = new();
                lights = new();
                creatures = new();
                npcs = new();

                /* Process cell data */
                foreach(Content content in cell.contents)
                {
                    content.relative = content.position + root - offset;

                    AddContent(content);
                }
            }

            public void AddContent(Content content)
            {
                switch (content)
                {
                    case AssetContent a:
                        assets.Add(a); break;
                    case EmitterContent e:
                        emitters.Add(e); break;
                    case LightContent l:
                        lights.Add(l); break;
                    case NpcContent n:
                        npcs.Add(n); break;
                    case CreatureContent c:
                        creatures.Add(c); break;
                    default:
                        Lort.Log(" ## WARNING ## Unhandled Content class fell through AddContent()", Lort.Type.Debug); break;
                }
            }
        }
    }
}
