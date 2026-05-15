using gfoidl.Base64;
using JortPob.Common;
using JortPob.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.IO;


namespace JortPob
{
    public class Landscape
    {
        private static readonly ushort DEFAULT_TEXTURE_INDEX = 65535;

        public readonly Int2 coordinate;

        public readonly string flags;

        public readonly List<Vertex> vertices;
        public readonly Dictionary<Vertex, int> vertIndMap;
        public readonly List<int>[] indices;   // 0 is full detail, 1 is reduced detail for lod, 2 is minimum possible detail for super overworld
        public readonly Vertex[,] borders;

        private readonly Dictionary<ushort, Texture> texturesByIndex;

        public List<Mesh> meshes;

        public bool hasWater, hasSwamp, hasLava;

        public Landscape(ESM esm, Int2 coordinate, JsonNode json, ILookup<int, JsonNode> landscapeTexturesByIndex)
        {
            this.coordinate = coordinate;
            flags = json["landscape_flags"].ToString();
            hasWater = false;  // cant trust esm flags, default false and check later in this constructor
            hasSwamp = false;
            hasLava = false;

            byte[] b64Height = Base64.Default.Decode(json["vertex_heights"]["data"].ToString());
            byte[] b64Normal = Base64.Default.Decode(json["vertex_normals"]["data"].ToString());
            byte[] b64Color = Base64.Default.Decode(json["vertex_colors"]["data"].ToString());
            byte[] b64Texture = Base64.Default.Decode(json["texture_indices"]["data"].ToString());

            int bA = 0; // Buffer postion reading heights
            int bB = 0; // Buffer position reading normals
            int bC = 0; // Buffer position reading color
            int bD = 0; // Buffer position for texture indices

            /* Checks through all landscape texture data and makes sure there is no duplicate texture index that points to the same texture file. Returns same index if no dupe or a dupe at a higher index, returns dupe index if found and it's a lower value. */
            ushort[,] ltex = new ushort[16, 16];
            for (int yy = 0; yy < 15; yy += 4)
            {
                for (int xx = 0; xx < 15; xx += 4)
                {
                    for (int yyy = 0; yyy < 4; yyy++)
                    {
                        for (int xxx = 0; xxx < 4; xxx++)
                        {
                            ushort texIndex = (ushort)(BitConverter.ToUInt16(new byte[] { b64Texture[bD++], b64Texture[bD++] }, 0) - (ushort)1);
                            //ushort texIndex = Cell.DeDupeTextureIndex(esm, (ushort)(BitConverter.ToUInt16(new byte[] { zstdTexture[bD++], zstdTexture[bD++] }, 0) - (ushort)1));
                            ltex[xx + xxx, yy + yyy] = texIndex;
                        }
                    }
                }
            }

            texturesByIndex = new()
            {
                {
                    DEFAULT_TEXTURE_INDEX, // Default hardcoded terrain texture. Morrowind moment.
                    new Texture("Default Terrain Texture", Path.Combine(Const.MORROWIND_PATH, Const.TERRAIN_DEFAULT_TEXTURE), DEFAULT_TEXTURE_INDEX)
                }
            };

            foreach (ushort index in ltex)
            {
                if (texturesByIndex.ContainsKey(index)) { continue; }

                JsonNode ltjson = landscapeTexturesByIndex[index].FirstOrDefault();

                if (ltjson != null)
                {
                    var ltFile = ltjson["file_name"]!.ToString();
                    texturesByIndex.Add(
                        index,
                        new Texture(ltjson["id"].ToString().ToLower(), Path.Combine(Const.MORROWIND_PATH, $@"Data Files\textures\{Path.ChangeExtension(ltFile, "dds")}"), index)
                    );
                }
                else
                {
                    Lort.Log($" ## WARNING ## INVALID LANDSCAPE TEXTURE INDEX IN LANDSCAPE DATA: {index}", Lort.Type.Debug);
                }
            }

            //float offset = BitConverter.ToSingle(new byte[] { zstdHeight[bA++], zstdHeight[bA++], zstdHeight[bA++], zstdHeight[bA++] }, 0);
            float offset = float.Parse(json["vertex_heights"]["offset"].ToString());

            /* Vertex Data */
            Vector3 centerOffset = new Vector3((Const.CELL_SIZE / 2f), 0f, -(Const.CELL_SIZE / 2f));
            vertices = new();
            vertIndMap = new();
            Vertex[,] vertgrid = new Vertex[Const.CELL_GRID_SIZE+1, Const.CELL_GRID_SIZE+1];
            float last = offset;
            float lastEdge = last;
            for (int yy = Const.CELL_GRID_SIZE; yy >= 0; yy--)
            {
                for (int xx = 0; xx < Const.CELL_GRID_SIZE + 1; xx++)
                {
                    sbyte height = (sbyte)(b64Height[bA++]);
                    last += height;
                    if (xx == 0) { lastEdge = last; }

                    float xxx = -xx * (Const.CELL_SIZE / (float)(Const.CELL_GRID_SIZE));
                    float yyy = (Const.CELL_GRID_SIZE - yy) * (Const.CELL_SIZE / (float)(Const.CELL_GRID_SIZE)); // I do not want to talk about this coordinate swap
                    float zzz = last * 8f * Const.GLOBAL_SCALE;
                    Vector3 position = new Vector3(xxx, zzz, yyy) + centerOffset;
                    Int2 grid = new Int2(xx, yy);

                    float iii = (sbyte)b64Normal[bB++];
                    float jjj = (sbyte)b64Normal[bB++];
                    float kkk = (sbyte)b64Normal[bB++];

                    Byte4 color = new Byte4(Byte.MaxValue); // Default
                    if (b64Color != null)
                    {
                        color = new Byte4(b64Color[bC++], b64Color[bC++], b64Color[bC++], byte.MaxValue);
                    }

                    Vertex vertex = new Vertex(position, grid, Vector3.Normalize(new Vector3(iii, kkk, jjj)), new Vector2(xx * (1f / Const.CELL_GRID_SIZE), yy * (1f / Const.CELL_GRID_SIZE)), color, ltex[Math.Min((xx) / 4, 15), Math.Min((Const.CELL_GRID_SIZE - yy) / 4, 15)]);
                    vertIndMap.Add(vertex, vertices.Count);
                    vertices.Add(vertex);
                    vertgrid[xx, yy] = vertex;
                }
                last = lastEdge;
            }

            /* Generate indices */
            indices = new List<int>[Const.TERRAIN_LOD_VALUES.Length];
            foreach (Const.LOD_VALUE lod in Const.TERRAIN_LOD_VALUES)
            {
                indices[lod.INDEX] = new List<int>();
                bool flip = false;
                for (int yy = 0; yy < Const.CELL_GRID_SIZE; yy += lod.DIVISOR)
                {
                    for (int xx = 0; xx < Const.CELL_GRID_SIZE; xx += lod.DIVISOR)
                    {
                        int[] quad = {
                                (yy * (Const.CELL_GRID_SIZE + 1)) + xx,
                                (yy * (Const.CELL_GRID_SIZE + 1)) + (xx + lod.DIVISOR),
                                ((yy + lod.DIVISOR) * (Const.CELL_GRID_SIZE + 1)) + (xx + lod.DIVISOR),
                                ((yy + lod.DIVISOR) * (Const.CELL_GRID_SIZE + 1)) + xx
                            };


                        int[,] tris = flip ?
                            new int[,] {
                                {
                                    quad[2],
                                    quad[1],
                                    quad[0]
                                },
                                {
                                    quad[0],
                                    quad[3],
                                    quad[2]
                                }
                                } :
                            new int[,] {
                            {
                                quad[3],
                                quad[1],
                                quad[0]
                            },
                            {
                                quad[3],
                                quad[2],
                                quad[1]
                            }
                            };

                        for (int t = 0; t < 2; t++)
                        {
                            for (int i = 2; i >= 0; i--)
                            {
                                indices[lod.INDEX].Add(tris[t, i]);
                            }
                        }

                        flip = !flip;
                    }
                    flip = !flip;
                }
            }

            /* Do border blending */
            /* Morrowind terrain is mildly cursed and the borders between landscape data are usually awful */
            /* What we do is we look at what landscapes around us are already generated and we adopt that landscapes border vertices textures */
            /* Start out by creating border arrays */
            if (!Const.DEBUG_SKIP_TERRAIN_BORDER_BLENDING)
            {
                borders = new Vertex[4, 65];
                for (int ii = 0; ii <= Const.CELL_GRID_SIZE; ii++)
                {
                    borders[3, ii] = vertgrid[ii, 0];
                    borders[1, ii] = vertgrid[Const.CELL_GRID_SIZE, ii];
                    borders[2, ii] = vertgrid[ii, Const.CELL_GRID_SIZE];
                    borders[0, ii] = vertgrid[0, ii];
                    
                }

                /* Now we need to look at neighboring landscapes (that are already loaded) and blend our border vertices to them */
                /* I also think this unfortunately means... we cant multithread landscape generation anymore... fuck....... */
                Landscape[] adjacents = {
                        esm.GetLoadedLandscape(coordinate + new Int2(-1, 0)),
                        esm.GetLoadedLandscape(coordinate + new Int2(1, 0)),
                        esm.GetLoadedLandscape(coordinate + new Int2(0, -1)),
                        esm.GetLoadedLandscape(coordinate + new Int2(0, 1))
                    };
                int[] ri = {  // Reverse index thing. Do not ask me how this works. I don't know.
                        1,
                        0,
                        3,
                        2,
                    };
                void AddTexture(Landscape land, ushort tex)
                {
                    if (land.texturesByIndex.TryGetValue(tex, out Texture texture))
                    {
                        texturesByIndex.TryAdd(tex, texture);
                    }
                }
                for (int ii = 0; ii < adjacents.Length; ii++)
                {
                    if (adjacents[ii] != null)
                    {
                        for (int jj = 0; jj <= Const.CELL_GRID_SIZE; jj++)
                        {
                            AddTexture(adjacents[ii], adjacents[ii].borders[ri[ii], jj].texture); // absorb textures from adjacent cell border vertices
                            borders[ii, jj].texture = adjacents[ii].borders[ri[ii], jj].texture; // summons demons
                            borders[ii, jj].color = adjacents[ii].borders[ri[ii], jj].color; // enchants armor
                        }
                    }
                }
            }

            /* Next up we need to generate new border indices for lod terrain */
            /* This is so you dont see holes to the void along the edge between lower and higher detail terrain lods */
            /* These holes are the result of lower detail edges not aligning perfectly with the higher detail ones */
            /* So we generate some new triangles on the edge that connect between the low detail indices and the full detail ones. */
            /* Extra note, we are doing this generation before mesh splitting so the added skirt verts get split properly as well */
            foreach (Const.LOD_VALUE lod in Const.TERRAIN_LOD_VALUES)
            {
                if (lod.INDEX == 0) { continue; } // skip full detail lod because lol

                // delete all bordering indices, we are going to regenerate them to make the borders seemless between lods
                for (int i = 0; i < indices[lod.INDEX].Count(); i += 3)
                {
                    bool delete = false;
                    for (int j = 0; j < 3; j++)
                    {
                        Vertex v = vertices[indices[lod.INDEX][i + j]];
                        if (v.grid.x == 0 || v.grid.y == 0 || v.grid.x == Const.CELL_GRID_SIZE || v.grid.y == Const.CELL_GRID_SIZE)
                        {
                            delete = true;
                        }
                    }
                    if (delete)
                    {
                        indices[lod.INDEX].RemoveRange(i, 3);
                        i -= 3;
                    }
                }

                // Y0 border
                Vertex vlast = null;
                for (int xx = 0; xx <= Const.CELL_GRID_SIZE; xx += lod.DIVISOR)
                {
                    //little minmax offset here to slice the corners
                    Vertex vroot = vertgrid[Math.Min(Math.Max(xx, lod.DIVISOR), Const.CELL_GRID_SIZE - lod.DIVISOR), lod.DIVISOR];
                    int start = xx - (lod.DIVISOR / 2);
                    int end = Math.Min(start + lod.DIVISOR, Const.CELL_GRID_SIZE);
                    start = Math.Max(start, 0);
                    for (int f = start; f < end; f++)
                    {
                        Vertex v1 = vertgrid[f, 0];
                        Vertex v2 = vertgrid[f + 1, 0];
                        indices[lod.INDEX].Add(vertIndMap[vroot]);
                        indices[lod.INDEX].Add(vertIndMap[v2]);
                        indices[lod.INDEX].Add(vertIndMap[v1]);
                    }
                    if (vlast != null && vlast != vroot)
                    {
                        Vertex v = vertgrid[start, 0];
                        indices[lod.INDEX].Add(vertIndMap[vroot]);
                        indices[lod.INDEX].Add(vertIndMap[v]);
                        indices[lod.INDEX].Add(vertIndMap[vlast]);
                    }
                    vlast = vroot;
                }

                // Y+ border
                vlast = null;
                for (int xx = 0; xx <= Const.CELL_GRID_SIZE; xx += lod.DIVISOR)
                {
                    //little minmax offset here to slice the corners
                    Vertex vroot = vertgrid[Math.Min(Math.Max(xx, lod.DIVISOR), Const.CELL_GRID_SIZE - lod.DIVISOR), Const.CELL_GRID_SIZE - lod.DIVISOR];
                    int start = xx - (lod.DIVISOR / 2);
                    int end = Math.Min(start + lod.DIVISOR, Const.CELL_GRID_SIZE);
                    int Yp = Const.CELL_GRID_SIZE;
                    start = Math.Max(start, 0);
                    for (int f = start; f < end; f++)
                    {
                        Vertex v1 = vertgrid[f, Yp];
                        Vertex v2 = vertgrid[f + 1, Yp];
                        indices[lod.INDEX].Add(vertIndMap[vroot]);
                        indices[lod.INDEX].Add(vertIndMap[v1]);
                        indices[lod.INDEX].Add(vertIndMap[v2]);
                    }
                    if (vlast != null && vlast != vroot)
                    {
                        Vertex v = vertgrid[start, Yp];
                        indices[lod.INDEX].Add(vertIndMap[vroot]);
                        indices[lod.INDEX].Add(vertIndMap[vlast]);
                        indices[lod.INDEX].Add(vertIndMap[v]);
                    }
                    vlast = vroot;
                }

                // X0 border
                vlast = null;
                for (int yy = 0; yy <= Const.CELL_GRID_SIZE; yy += lod.DIVISOR)
                {
                        //little minmax offset here to slice the corners
                        Vertex vroot = vertgrid[lod.DIVISOR, Math.Min(Math.Max(yy, lod.DIVISOR), Const.CELL_GRID_SIZE - lod.DIVISOR)];
                        int start = yy - (lod.DIVISOR / 2);
                        int end = Math.Min(start + lod.DIVISOR, Const.CELL_GRID_SIZE);
                        start = Math.Max(start, 0);
                        for (int f = start; f < end; f++)
                        {
                            Vertex v1 = vertgrid[0, f];
                            Vertex v2 = vertgrid[0, f + 1];
                            indices[lod.INDEX].Add(vertIndMap[vroot]);
                            indices[lod.INDEX].Add(vertIndMap[v1]);
                            indices[lod.INDEX].Add(vertIndMap[v2]);
                        }
                        if (vlast != null && vlast != vroot)
                        {
                            Vertex v = vertgrid[0, start];
                            indices[lod.INDEX].Add(vertIndMap[vroot]);
                            indices[lod.INDEX].Add(vertIndMap[vlast]);
                            indices[lod.INDEX].Add(vertIndMap[v]);
                        }
                        vlast = vroot;
                }

                // X+ border
                vlast = null;
                for (int yy = 0; yy <= Const.CELL_GRID_SIZE; yy += lod.DIVISOR)
                {
                    //little minmax offset here to slice the corners
                    Vertex vroot = vertgrid[Const.CELL_GRID_SIZE - lod.DIVISOR, Math.Min(Math.Max(yy, lod.DIVISOR), Const.CELL_GRID_SIZE - lod.DIVISOR)];
                    int start = yy - (lod.DIVISOR / 2);
                    int end = Math.Min(start + lod.DIVISOR, Const.CELL_GRID_SIZE);
                    int Xp = Const.CELL_GRID_SIZE;
                    start = Math.Max(start, 0);
                    for (int f = start; f < end; f++)
                    {
                        Vertex v1 = vertgrid[Xp, f];
                        Vertex v2 = vertgrid[Xp, f + 1];
                        indices[lod.INDEX].Add(vertIndMap[vroot]);
                        indices[lod.INDEX].Add(vertIndMap[v2]);
                        indices[lod.INDEX].Add(vertIndMap[v1]);
                    }
                    if (vlast != null && vlast != vroot)
                    {
                        Vertex v = vertgrid[Xp, start];
                        indices[lod.INDEX].Add(vertIndMap[vroot]);
                        indices[lod.INDEX].Add(vertIndMap[v]);
                        indices[lod.INDEX].Add(vertIndMap[vlast]);
                    }
                    vlast = vroot;
                }
            }

            /* Now that we've built the terrain mesh, let's subdivide it into multiple meshes based on what textures it uses */
            /* Elden Ring shaders can only render like 2 or 3 textures on a mesh, while morrowind can do dozens. So this subdivision is to allow use to do this */
            /* Doing subdivision of 3 textures per mesh using an [Mb3] material */
            Mesh GetMesh(List<Texture> textures)
            {
                foreach (Mesh mesh in meshes)
                {
                    bool match = true;
                    foreach (Texture tex in textures)
                    {
                        if (!mesh.textures.Contains(tex)) { match = false; break; }
                    }
                    if (match) { return mesh; }
                }

                Mesh nu;
                switch (textures.Count())
                {
                    case 1:
                        nu = new(textures, MaterialContext.MaterialTemplate.Opaque);
                        break;
                    case 2:
                        nu = new(textures, MaterialContext.MaterialTemplate.Multi2);
                        break;
                    case 3:
                        nu = new(textures, MaterialContext.MaterialTemplate.Multi3);
                        break;
                    default:
                        Lort.Log("## WARNING ## INVALID TEXTURE COUNT FOR MESH IN LANDSCAPE! WE WILL NOW CRASH!", Lort.Type.Debug);
                        nu = null;
                        break;

                }
                meshes.Add(nu);
                return nu;
            }

            List<List<Texture>>[] texsets = new List<List<Texture>>[] { new(), new(), new() };  // lol, lmao even
            void AddTexSet(List<Texture> ts)
            {
                foreach (List<Texture> texset in texsets[ts.Count - 1])
                {
                    bool match = true;
                    foreach (Texture t in ts)
                    {
                        if (!texset.Contains(t)) { match = false; break; }
                    }
                    if (match) { return; }
                }

                texsets[ts.Count - 1].Add(ts);
                return;
            }

            /* First let's prepass the indices and optimize the number of meshes we need to do this */
            foreach(Const.LOD_VALUE lod in Const.TERRAIN_LOD_VALUES)
            {
                for (int itr = 0; itr < indices[lod.INDEX].Count; itr += 3)
                {
                    int i = indices[lod.INDEX][itr];
                    int j = indices[lod.INDEX][itr + 1];
                    int k = indices[lod.INDEX][itr + 2];

                    Vertex a = vertices[i];
                    Vertex b = vertices[j];
                    Vertex c = vertices[k];

                    List<Texture> texs = new();
                    texs.Add(GetTexture(a.texture));
                    if (!texs.Contains(GetTexture(b.texture))) { texs.Add(GetTexture(b.texture)); }
                    if (!texs.Contains(GetTexture(c.texture))) { texs.Add(GetTexture(c.texture)); }

                    AddTexSet(texs);
                }
            }

            /* Condense and create meshes */
            meshes = new();
            foreach (List<Texture> texset in texsets[2])
            {
                GetMesh(texset);
            }
            foreach (List<Texture> texset in texsets[1])
            {
                GetMesh(texset);
            }
            foreach (List<Texture> texset in texsets[0])
            {
                GetMesh(texset);
            }

            foreach (Const.LOD_VALUE lod in Const.TERRAIN_LOD_VALUES)
            {
                for (int itr = 0; itr < indices[lod.INDEX].Count; itr += 3)
                {
                    int i = indices[lod.INDEX][itr];
                    int j = indices[lod.INDEX][itr + 1];
                    int k = indices[lod.INDEX][itr + 2];

                    Vertex a = vertices[i];
                    Vertex b = vertices[j];
                    Vertex c = vertices[k];

                    List<Texture> texs = new();
                    texs.Add(GetTexture(a.texture));
                    if (!texs.Contains(GetTexture(b.texture))) { texs.Add(GetTexture(b.texture)); }
                    if (!texs.Contains(GetTexture(c.texture))) { texs.Add(GetTexture(c.texture)); }

                    Mesh mesh = GetMesh(texs);

                    int A = mesh.AddVertex(a);
                    int B = mesh.AddVertex(b);
                    int C = mesh.AddVertex(c);

                    mesh.indices[lod.INDEX].Add(A);
                    mesh.indices[lod.INDEX].Add(B);
                    mesh.indices[lod.INDEX].Add(C);
                }
            }

            /* Check if this landscape ever goes low enough to have water */
            foreach (Vertex v in vertices)
            {
                // -v.position.x is because terrain is mirrored during the model conversion. its flipped in this context
                Vector3 posActual = new Vector3((Const.CELL_SIZE * coordinate.x) + -v.position.X, v.position.Y, (Const.CELL_SIZE * coordinate.y) + v.position.Z);
                if (LiquidManager.PointInCutout(LiquidManager.Cutout.Type.Lava, posActual, true)) { v.lava = true; hasLava = true;  continue; }
                else if (LiquidManager.PointInCutout(LiquidManager.Cutout.Type.Swamp, posActual, true)) { v.swamp = true; hasSwamp = true; continue; }
                else if (v.position.Y < Const.WATER_HEIGHT) { v.underwater = true;  hasWater = true; continue; }
            }
        }

        public Texture GetTexture(ushort id)
        {
            if (texturesByIndex.TryGetValue(id, out Texture value))
            {
                return value;
            }

            Lort.Log($"# ## WARNING ## Missing texture index [{id}] in landscape mesh!", Lort.Type.Debug);
            return texturesByIndex[DEFAULT_TEXTURE_INDEX];
        }

        /* Returns average height value of all vertices. Used for estimating location of regions in this cell */
        public float GetHeightAverage()
        {
            return vertices.Average(v => v.position.Y);
        }

        public class Mesh
        {
            public readonly List<Texture> textures;
            public readonly List<int>[] indices;    
            public readonly List<Vertex> vertices;
            public readonly Dictionary<Vertex, int> vertices_indices;

            public readonly MaterialContext.MaterialTemplate template;

            public Mesh(List<Texture> textures, MaterialContext.MaterialTemplate template)
            {
                this.textures = textures;
                indices = new List<int>[] { new(), new(), new() };  // 0 is full detail, 1 is reduced detail for lod, 2 is minimum possible detail for super overworld
                vertices = new();
                vertices_indices = new();
                this.template = template;
            }

            public int AddVertex(Vertex vertex)
            {
                if (vertices_indices.TryGetValue(vertex, out int existing_index))
                    return existing_index;
                int index = vertices.Count;
                vertices_indices.Add(vertex, vertices.Count);
                vertices.Add(vertex);
                return index;
            }
        }

        public class Vertex
        {
            public Vector3 position;
            public Int2 grid; // position on this cells grid
            public Vector3 normal;
            public Vector2 coordinate;  // UV
            public Byte4 color; // Bytes of a texture that contains the converted vertex color information

            public bool underwater, swamp, lava;

            public ushort texture;

            public Vertex(Vector3 position, Int2 grid, Vector3 normal, Vector2 coordinate, Byte4 color, ushort texture)
            {
                this.position = position;
                this.grid = grid;
                this.normal = normal;
                this.coordinate = coordinate;
                this.color = color;
                this.texture = texture;

                underwater = false;
                swamp = false;
                lava = false;
            }

            public static bool operator ==(Vertex a, Vertex b)
            {
                if (a is null)
                {
                    return b is null;
                }
                return a.Equals(b);
            }
            public static bool operator !=(Vertex a, Vertex b) => !(a == b);

            public bool Equals(Vertex b)
            {
                return b == null ? false : grid == b.grid;
            }
            public override bool Equals(object a) => Equals(a as Vertex);

            public override int GetHashCode()
            {
                unchecked
                {
                    return grid.GetHashCode();
                }
            }
        }

        public class Texture
        {
            public string name;
            public string path;
            public ushort index;

            public Texture(string name, string path, ushort index)
            {
                this.name = name;  this.path = path; this.index = index;
            }

            public static bool operator ==(Texture a, Texture b)
            {
                if (a is null)
                {
                    return b is null;
                }
                return a.Equals(b);
            }
            public static bool operator !=(Texture a, Texture b) => !(a == b);

            public bool Equals(Texture b)
            {
                return b == null ? false : index == b.index;
            }
            public override bool Equals(object a) => Equals(a as Texture);

            public override int GetHashCode()
            {
                unchecked
                {
                    return index.GetHashCode();
                }
            }
        }
    }
}
