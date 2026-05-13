using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace JortPob.Common
{
    public class Obj
    {
        public List<Vector3> vs { get; } = new();
        public List<Vector3> vts { get; } = new();
        public List<Vector3> vns { get; } = new();
        public List<ObjG> gs { get; } = new();

        public Obj()
        { }

        public Obj(string path)
        {
            ObjG? last = null;
            string[] data = File.ReadAllLines(path);
            foreach (string line in data)
            {
                string[] values = line.Replace("  ", " ").Split(" ");
                switch (values[0])
                {
                    case "v":
                        {
                            float x = float.Parse(values[1]);
                            float y = float.Parse(values[2]);
                            float z = float.Parse(values[3]);
                            vs.Add(new Vector3(x, y, z));
                            break;
                        }
                    case "vt":
                        {
                            float x = float.Parse(values[1]);
                            float y = float.Parse(values[2]);
                            float z = float.Parse(values[3]);
                            vts.Add(new Vector3(x, y, z));
                            break;
                        }
                    case "vn":
                        {
                            float x = float.Parse(values[1]);
                            float y = float.Parse(values[2]);
                            float z = float.Parse(values[3]);
                            vns.Add(new Vector3(x, y, z));
                            break;
                        }
                    case "g":
                        {
                            ObjG g = new();
                            g.name = values[1];
                            last = g;
                            gs.Add(g);
                            break;
                        }
                    case "usemtl":
                        {
                            last.mtl = values[1];
                            break;
                        }
                    case "f":
                        {
                            string[] a = values[1].Split("/");
                            string[] b = values[2].Split("/");
                            string[] c = values[3].Split("/");

                            ObjV A = new(int.Parse(a[0]) - 1, int.Parse(a[1]) - 1, int.Parse(a[2]) - 1);
                            ObjV B = new(int.Parse(b[0]) - 1, int.Parse(b[1]) - 1, int.Parse(b[2]) - 1);
                            ObjV C = new(int.Parse(c[0]) - 1, int.Parse(c[1]) - 1, int.Parse(c[2]) - 1);

                            last.fs.Add(new(A, B, C));
                            break;
                        }
                    default: break;
                }
            }
        }

        // optimize by re-indexing duplicate vertice data
        public Obj optimize() {
            Obj nu = new();

            Dictionary<Vector3, int> vsLookup = new();
            Dictionary<Vector3, int> vnsLookup = new();
            Dictionary<Vector3, int> vtsLookup = new();

            int[] GetIndex(Vector3 v, Vector3 vn, Vector3 vt)
            {
                int[] indset = new int[3];

                // V
                if (!vsLookup.TryGetValue(v, out indset[0]))
                {
                    indset[0] = nu.vs.Count;
                    nu.vs.Add(v);
                    vsLookup[v] = indset[0];
                }

                // VN  
                if (!vnsLookup.TryGetValue(vn, out indset[1]))
                {
                    indset[1] = nu.vns.Count;
                    nu.vns.Add(vn);
                    vnsLookup[vn] = indset[1];
                }

                // VT
                if (!vtsLookup.TryGetValue(vt, out indset[2]))
                {
                    indset[2] = nu.vts.Count;
                    nu.vts.Add(vt);
                    vtsLookup[vt] = indset[2];
                }

                return indset;
            }

            foreach (ObjG g in gs)
            {
                ObjG nug = new();
                nug.name = g.name;
                nug.mtl = g.mtl;
                foreach(ObjF f in g.fs)
                {
                    int[] a = GetIndex(vs[f.a.v], vns[f.a.vn], vts[f.a.vt]);
                    int[] b = GetIndex(vs[f.b.v], vns[f.b.vn], vts[f.b.vt]);
                    int[] c = GetIndex(vs[f.c.v], vns[f.c.vn], vts[f.c.vt]);

                    ObjV A = new ObjV(a[0], a[2], a[1]);  //lol i swapped the order of vt vn on accident. fixed
                    ObjV B = new ObjV(b[0], b[2], b[1]);
                    ObjV C = new ObjV(c[0], c[2], c[1]);

                    ObjF nuf = new ObjF(A, B, C);
                    nug.fs.Add(nuf);
                }
                nu.gs.Add(nug);
            }
            return nu;
        }

        /* Scale this obj */
        public void scale(float scale)
        {
            for(int i = 0;i<vs.Count;i++)
            {
                vs[i] *= scale;
            }
        }

        /* Split an obj into multiple objs based of its 'g's. */
        /* This is due to an hkx issue where having multiple materials in an obj just results in broken stuff. So we are splitting each material into its own obj and then it's own hkx */
        public List<Obj> split()
        {
            List<Obj> objs = new();
            foreach(ObjG g in gs)
            {
                Obj nuobj = new();
                ObjG nug = new();
                nug.name = g.name;
                nug.mtl = g.mtl;

                int c = 0;
                foreach(ObjF f in g.fs)
                {
                    List<ObjV> V = new();
                    for (int i = 0; i < 3; i++)
                    {
                        nuobj.vs.Add(vs[f.AsArray()[i].v]);
                        nuobj.vns.Add(vs[f.AsArray()[i].vn]);
                        nuobj.vts.Add(vs[f.AsArray()[i].vt]);
                        V.Add(new ObjV(c, c, c));
                        c++;
                    }
                    ObjF nuf = new(V[0], V[1], V[2]);
                    nug.fs.Add(nuf);
                }
                nuobj.gs.Add(nug);
                objs.Add(nuobj);
            }

            return objs;
        }

        /* Merges all faces into a single G of the given material */
        public Obj collapse(Obj.CollisionMaterial material)
        {
            Obj obj = new();
            obj.vs.AddRange(vs);
            obj.vts.AddRange(vts);
            obj.vns.AddRange(vns);

            ObjG objG = new();
            objG.name = $"{material}";
            objG.mtl = $"hkm_{material}_Safe1";
            
            foreach(ObjG g in gs)
            {
                objG.fs.AddRange(g.fs);
            }

            obj.gs.Add(objG);
            return obj;
        }

        /* Merges an obj into this obj at a given transformation. This particular part of the obj api was created for navmesh scene building. */
        public void add(Obj obj, Vector3 position, Vector3 rotation, float scale)
        {
            int offsetV = vs.Count();
            int offsetVN = vns.Count();
            int offsetVT = vts.Count();

            /* Some fucked math to get correct transform */
            Vector3 d2r = rotation * (MathF.PI / 180f);
            Matrix4x4 rx = Matrix4x4.CreateRotationX(d2r.X);
            Matrix4x4 ry = Matrix4x4.CreateRotationY(d2r.Y);
            Matrix4x4 rz = Matrix4x4.CreateRotationZ(d2r.Z);

            Matrix4x4 mt = Matrix4x4.CreateTranslation(position);
            Matrix4x4 mr = rx * rz * ry;
            Matrix4x4 ms = Matrix4x4.CreateScale(scale);
            Matrix4x4 transform = ms * mr * mt;

            /* copy vertices from source obj into this obj with the given transforms */
            foreach (Vector3 v in obj.vs)
            {
                Vector3 transformed = Vector3.Transform(v, transform);
                vs.Add(transformed);
            }
            foreach (Vector3 vn in obj.vns)
            {
                Vector3 rotated = Vector3.TransformNormal(vn, mr);
                vns.Add(rotated);
            }
            foreach(Vector3 vt in obj.vts)
            {
                vts.Add(vt);
            }

            /* copy face indices */
            foreach(ObjG g in obj.gs)
            {
                ObjG G = new();
                G.name = g.name;
                G.mtl = g.mtl;
                foreach(ObjF f in g.fs)
                {
                    ObjV A = new(f.a.v + offsetV, f.a.vt + offsetVT, f.a.vn + offsetVN);
                    ObjV B = new(f.b.v + offsetV, f.b.vt + offsetVT, f.b.vn + offsetVN);
                    ObjV C = new(f.c.v + offsetV, f.c.vt + offsetVT, f.c.vn + offsetVN);
                    ObjF F = new(A, B, C);
                    G.fs.Add(F);
                }
                gs.Add(G);
            }
        }

        /* Very specific and special function for use in terrain navmeshes */
        /* Finds the border vertices and expands them out from the center by the given distance. Used to create overlap betweeen terrain navmeshes so npcs can traverse easier */
        public void borderize(float distance)
        {
            /* Find borders */
            Vector2 min = Vector2.Zero, max = Vector2.Zero;   // only works if model is centered on 0,0,0
            foreach(Vector3 v in vs)
            {
                if (v.X > max.X) { max.X = v.X; }
                if (v.Z > max.Y) { max.Y = v.Z; }
                if (v.X < min.X) { min.X = v.X; }
                if (v.Z < min.Y) { min.Y = v.Z; }
            }

            const float precision = .1f;
            max.X -= precision;
            max.Y -= precision;
            min.X += precision;
            min.Y += precision;

            /* Expand dong */
            for (int i=0;i<vs.Count;i++)
            {
                Vector3 v = vs[i];
                float x = v.X;
                float z = v.Z;

                if (x >= max.X) { x += distance; }
                if (z >= max.Y) { z += distance; }
                if (x <= min.X) { x -= distance; }
                if (z <= min.Y) { z -= distance; }

                vs[i] = new(x, v.Y, z);
            }
        }

        /* Returns number of triangles in this obj */
        public int count()
        {
            return gs.Sum(g => g.fs.Count());
        }

        /* return true if obj has nothing in it */
        public bool empty()
        {
            return count() <= 0;
        }

        /* only works on objs written by this api */
        public static int GetFaceCount(string objPath)
        {
            var objLines = File.ReadLines(objPath);
            string line = objLines.First();
            return int.Parse(line.Split(":")[1].Trim());
        }

        /* Takes data in this class and writes an obj file of it to the path specified */
        public void write(string outPath)
        {
            StringBuilder sb = new();

            /* Header */
            sb.Append($"## Triangle Count: {count()}\r\n");
            sb.Append($"mtllib {Path.GetFileNameWithoutExtension(outPath)}.mtl\r\n");

            /* write vertices */
            sb.Append("## Vertices: "); sb.Append(vs.Count); sb.Append(" ##\r\n");
            foreach (Vector3 v in vs)
            {
                sb.Append("v  "); sb.Append(v.X); sb.Append(' '); sb.Append(v.Y); sb.Append(' '); sb.Append(v.Z); sb.Append("\r\n");
            }

            /* write texture coordinates */
            sb.Append("\r\n## Texture Coordinates: "); sb.Append(vts.Count); sb.Append(" ##\r\n");
            foreach (Vector3 vt in vts)
            {
                sb.Append("vt "); sb.Append(vt.X); sb.Append(' '); sb.Append(vt.Y); sb.Append(' '); sb.Append(vt.Z); sb.Append("\r\n");
            }

            /* write vertex normals */
            sb.Append("\r\n## Vertex Normals: "); sb.Append(vns.Count); sb.Append(" ##\r\n");
            foreach (Vector3 vn in vns)
            {
                sb.Append("vn "); sb.Append(vn.X); sb.Append(' '); sb.Append(vn.Y); sb.Append(' '); sb.Append(vn.Z); sb.Append("\r\n");
            }

            foreach (ObjG g in gs)
            {
                g.write(sb);
            }

            /* write to file */
            if (File.Exists(outPath)) { File.Delete(outPath); }
            if(!Directory.Exists(Path.GetDirectoryName(outPath))) { Directory.CreateDirectory(Path.GetDirectoryName(outPath)); }
            File.WriteAllText(outPath, sb.ToString());
        }

        /* Opens an obj and scrape the material names */
        public static List<CollisionMaterial> GetMaterials(string path)
        {
            List<CollisionMaterial> mats = new();
            string[] file = File.ReadAllLines(path);
            foreach (string l in file)
            {
                string line = l.Trim();
                if (line.ToLower().StartsWith("usemtl "))
                {
                    string mtl = line.Substring(7).Trim(); // get value of usemtl line
                    mtl = mtl.Substring(4); // remove HKM_
                    mtl = mtl.Substring(0, mtl.Length - 6); //remove _SAFE#
                    mats.Add(Enum.Parse<CollisionMaterial>(mtl));
                }
            }
            return mats;
        }

        public enum CollisionMaterial
        {
            None = 0,
            Stock = 1,
            Rock = 2,
            Sand = 3,
            Wood = 4,
            Dirt = 5,
            Ore = 6,
            Lava = 7,
            ScarletTree = 8,
            IronGrate = 11,
            ScarletMushroom = 12,
            Bone = 14,
            Water = 21,
            ShallowPoisonSwamp = 23,
            PoisonSwamp = 24,
            RainDirt = 44,
            ScarletSwamp = 46,
            LakeofRot = 49
        }
    }

    public class ObjG
    {
        public string name, mtl;
        public List<ObjF> fs;
        public ObjG()
        {
            fs = new();
        }

        public void write(StringBuilder sb)
        {
            /* write object name */
            sb.Append("\r\n");
            sb.Append("g "); sb.Append(name);
            sb.Append("\r\n");

            /* write material */
            sb.Append("usemtl "); sb.Append(mtl);

            /* write triangles */
            sb.Append("\r\n## Triangles: "); sb.Append(fs.Count); sb.Append(" ##\r\n");
            foreach (ObjF f in fs)
            {
                f.write(sb);
            }
            sb.Append("\r\n");
        }
    }

    public class ObjF
    {
        public ObjV a, b, c;
        public ObjF(ObjV a, ObjV b, ObjV c)
        {
            this.a = a; this.b = b; this.c = c;
        }

        public ObjV[] AsArray()
        {
            return new ObjV[] { a, b, c };
        }

        public void write(StringBuilder sb)
        {
            sb.Append("f ");
            a.write(sb);
            b.write(sb);
            c.write(sb);
            sb.Append("\r\n");
        }
    }

    public class ObjV
    {
        public int v, vt, vn;
        public ObjV(int v, int vt, int vn)
        {
            this.v = v; this.vt = vt; this.vn = vn;
        }

        public void write(StringBuilder sb)
        {
            sb.Append(v + 1); sb.Append('/'); sb.Append(vt + 1); sb.Append('/'); sb.Append(vn + 1); sb.Append(' ');
        }
    }
}
