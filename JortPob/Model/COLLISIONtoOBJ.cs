using JortPob.Common;
using SharpAssimp;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace JortPob.Model
{
    public partial class ModelConverter
    {
        public static Obj COLLISIONtoOBJ(List<Tuple<Node, Mesh>> collisions, Obj.CollisionMaterial material)
        {
            Obj obj = new();

            foreach (Tuple<Node, Mesh> tuple in collisions)
            {
                Node node = tuple.Item1;
                Mesh mesh = tuple.Item2;

                ObjG g = new();
                g.name = material.ToString();
                g.mtl = $"hkm_{g.name}_Safe1";

                /* Convert vert/face data */
                foreach (Face face in mesh.Faces)
                {
                    ObjV[] V = new ObjV[3];
                    for (int i = 0; i < 3; i++)
                    {
                        /* Grab vertice position + normals/tangents */
                        Vector3 pos = mesh.Vertices[face.Indices[i]];
                        Vector3 norm = mesh.Normals[face.Indices[i]];

                        /* Collapse transformations on positions and collapse rotations on normals/tangents */
                        Node parent = node;
                        while (parent != null)
                        {
                            Vector3 translation;
                            Quaternion rotation;
                            Vector3 scale;
                            Matrix4x4.Decompose(parent.Transform, out scale, out rotation, out translation);
                            translation = new Vector3(parent.Transform.M14, parent.Transform.M24, parent.Transform.M34); // Hack

                            rotation = Quaternion.Inverse(rotation);

                            Matrix4x4 ms = Matrix4x4.CreateScale(scale);
                            Matrix4x4 mr = Matrix4x4.CreateFromQuaternion(rotation);
                            Matrix4x4 mt = Matrix4x4.CreateTranslation(translation);

                            pos = Vector3.Transform(pos, ms * mr * mt);
                            norm = Vector3.TransformNormal(norm, mr);

                            parent = parent.Parent;
                        }

                        // Fromsoftware lives in the mirror dimension. I do not know why.
                        pos = pos * Const.GLOBAL_SCALE;
                        pos.X *= -1f;
                        norm.X *= -1f;

                        /* Rotate Y 180 degrees because... */
                        Matrix4x4 rotateY180Matrix = Matrix4x4.CreateRotationY((float)Math.PI);
                        pos = Vector3.Transform(pos, rotateY180Matrix);

                        /* Rotate normals/tangents to match */
                        norm = Vector3.Normalize(Vector3.TransformNormal(norm, rotateY180Matrix));

                        /* Get tex coords */
                        Vector3 uvw;
                        if (mesh.TextureCoordinateChannelCount <= 0)
                        {
                            uvw = new Vector3(0, 0, 0);
                        }
                        else
                        {
                            uvw = mesh.TextureCoordinateChannels[0][face.Indices[i]];
                            uvw.Y *= -1f;
                        }

                        /* Set */
                        obj.vs.Add(pos);
                        obj.vns.Add(norm);
                        obj.vts.Add(uvw);

                        V[i] = new(obj.vs.Count - 1, obj.vts.Count - 1, obj.vns.Count - 1);
                    }

                    ObjF F = new(V[2], V[1], V[0]);  // reverse indices going into collision. i don't know *why* but it works
                    g.fs.Add(F);
                }
                obj.gs.Add(g);
            }

            return obj;
        }

        public static Obj COLLISIONtoOBJ(TES3.VecMesh collisions, Obj.CollisionMaterial material)
        {
            Obj obj = new();

            // Define the global engine-specific rotation (used for final alignment)
            Matrix4x4 desiredRotation = Matrix4x4.CreateRotationY((float)Math.PI) * Matrix4x4.CreateRotationX((float)Math.PI / 2);

            for (int mIdx = 0; mIdx < collisions.Count; mIdx++)
            {
                TES3.Mesh mesh = collisions[mIdx];
                ObjG g = new();

                g.name = material.ToString();
                g.mtl = $"hkm_{g.name}_Safe1";

                /*
                 * Extract and build the local mesh transformation matrices (Scale, Rotation, Translation).
                 * This ensures the collision mesh is aligned with the visual mesh's node transform.
                 */
                Matrix4x4 mt = Matrix4x4.CreateTranslation(mesh.Transform.Translation.ToVector3());
                Matrix4x4 mr = Matrix4x4.CreateFromQuaternion(mesh.Transform.Rotation.ToQuaternion());
                Matrix4x4 ms = Matrix4x4.CreateScale(mesh.Transform.Scale);


                for (int tIdx = 0; tIdx < mesh.Triangles.Count; tIdx++)
                {
                    TES3.Triangle tri = mesh.Triangles[tIdx];
                    ObjV[] V = new ObjV[3];

                    for (int i = 0; i < 3; i++)
                    {
                        int idx = (i == 0) ? tri.v0 : (i == 1 ? tri.v1 : tri.v2);

                        // 1. Get raw position and normal
                        Vector3 pos = mesh.Vertices[idx].ToNumeric();
                        Vector3 norm = (mesh.Normals != null && idx < mesh.Normals.Count)
                            ? mesh.Normals[idx].ToNumeric()
                            : Vector3.UnitY; // Fallback if no normal exists

                        // 2. Apply local mesh transformation (matching NIFToFLVER logic)
                        pos = Vector3.Transform(pos, ms * mr * mt);
                        norm = Vector3.TransformNormal(norm, mr);

                        // 3. Apply global scale and engine-specific X-axis inversion
                        pos *= Const.GLOBAL_SCALE;
                        pos.X *= -1f;
                        norm.X *= -1f; // Invert normal X-axis to match engine space

                        // UVs (UVs are not typically used for collision but included for consistency)
                        Vector3 uvw;
                        if (mesh.UvSet0 != null && idx < mesh.UvSet0.Count)
                        {
                            TES3.Vec2 uv = mesh.UvSet0[idx];
                            uvw = new Vector3(uv.x, 1 - uv.y, 0);
                        }
                        else
                        {
                            uvw = Vector3.Zero;
                        }

                        /* 4. Apply final engine-specific rotation (Y 180, X 90) */
                        pos = Vector3.Transform(pos, desiredRotation);
                        norm = Vector3.Normalize(Vector3.TransformNormal(norm, desiredRotation));

                        // Add vertices, normals, and UVs to the OBJ container
                        obj.vs.Add(pos);
                        obj.vns.Add(norm);
                        obj.vts.Add(uvw);
                        V[i] = new ObjV(obj.vs.Count - 1, obj.vts.Count - 1, obj.vns.Count - 1);
                    }

                    // Create face, preserving winding order (triangles are stored v0, v1, v2)
                    // Note: OBJ faces are 1-indexed and often wound V[2], V[1], V[0] for correct backface culling in some systems.
                    ObjF F = new ObjF(V[2], V[1], V[0]);
                    g.fs.Add(F);
                }
                obj.gs.Add(g);
            }
            return obj;
        }
    }
}
