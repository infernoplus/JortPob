using JortPob.Common;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using TES3;

#nullable enable

namespace JortPob.Model
{
    public partial class ModelConverter
    {
        public static ModelInfo? NIFToFLVER(MaterialContext materialContext,
                    ModelInfo modelInfo,
                    bool forceCollision,
                    string modelPath,
                    string outputPath)
        {
            var loadResult = Interop.LoadScene(Utf8String.From(modelPath));

            if (loadResult.IsErr)
            {
                Lort.Log($"Failed to load Model {modelPath}", Lort.Type.Debug);
                return null;
            }

            TES3.Scene nif = loadResult.AsOk();

            FLVER2 flver = new();
            flver.Header.Version = 131098; // Elden Ring FLVER Version Number
            flver.Header.Unk5D = 0;        // Unk
            flver.Header.Unk68 = 4;        // Unk

            /* Add bones and nodes for FLVER */
            FLVER.Node rootNode = new();
            FLVER2.SkeletonSet skeletonSet = new();
            FLVER2.SkeletonSet.Bone rootBone = new(0);

            rootNode.Name = Path.GetFileNameWithoutExtension(modelPath);
            skeletonSet.AllSkeletons.Add(rootBone);
            skeletonSet.BaseSkeleton.Add(rootBone);
            flver.Nodes.Add(rootNode);
            flver.Skeletons = skeletonSet;

            Matrix4x4 desiredRotation = Matrix4x4.CreateRotationY((float)Math.PI) * Matrix4x4.CreateRotationX((float)Math.PI / 2);

            for (int meshIndex = 0; meshIndex < nif.VisualMeshes.Count; meshIndex++)
            {
                TES3.Mesh mesh = nif.VisualMeshes[meshIndex];

                /* Setup Material */
                MaterialContext.MaterialInfo materialInfo = materialContext.GenerateMaterial(mesh.Texture.String, meshIndex);
                flver.Materials.Add(materialInfo.material);
                flver.GXLists.Add(materialInfo.gx);
                flver.BufferLayouts.Add(materialInfo.layout);
                foreach (TextureInfo texInfo in materialInfo.info)
                {
                    modelInfo.textures.Add(texInfo);
                }

                /* Setup FLVER Mesh */
                FLVER2.Mesh flverMesh = new();
                FLVER2.FaceSet flverFaces = new();
                flverMesh.FaceSets.Add(flverFaces);
                flverFaces.CullBackfaces = true;
                flverFaces.Unk06 = 1;
                flverMesh.NodeIndex = 0;
                flverMesh.MaterialIndex = meshIndex;

                /* Setup Vertex Buffer */
                FLVER2.VertexBuffer flverBuffer = new(0);
                flverBuffer.LayoutIndex = meshIndex;
                flverMesh.VertexBuffers.Add(flverBuffer);

                Matrix4x4 mt = Matrix4x4.CreateTranslation(mesh.Transform.Translation.ToVector3());
                Matrix4x4 mr = Matrix4x4.CreateFromQuaternion(mesh.Transform.Rotation.ToQuaternion());
                Matrix4x4 ms = Matrix4x4.CreateScale(mesh.Transform.Scale);

                for (int triangleIndex = 0; triangleIndex < mesh.Triangles.Count; triangleIndex++)
                {
                    Triangle triangle = mesh.Triangles[triangleIndex];
                    int[] indices = new int[] { triangle.v0, triangle.v1, triangle.v2 };
                    foreach (int vertexIndex in indices)
                    {
                        FLVER.Vertex flverVertex = new();

                        /* Grab vertice position + normals/tangents */
                        Vector3 pos = mesh.Vertices[vertexIndex].ToNumeric();
                        Vector3 norm = mesh.Normals[vertexIndex].ToNumeric();
                        Vector3 tang = new Vector3(1, 0, 0);
                        Vector3 bitang = new Vector3(0, 0, 1);

                        // collapse the mesh transform onto the vert data
                        pos = Vector3.Transform(pos, ms * mr * mt);
                        norm = Vector3.TransformNormal(norm, mr);
                        tang = Vector3.TransformNormal(tang, mr);
                        bitang = Vector3.TransformNormal(bitang, mr);

                        // Fromsoftware lives in the mirror dimension. I do not know why.
                        pos *= Const.GLOBAL_SCALE;
                        pos.X *= -1f;
                        norm.X *= -1f;
                        tang.X *= -1f;
                        bitang.X *= -1f;

                        /* Rotate Y 180 degrees because... */
                        pos = Vector3.Transform(pos, desiredRotation);

                        /* Rotate normals/tangents to match */
                        norm = Vector3.Normalize(Vector3.TransformNormal(norm, desiredRotation));
                        tang = Vector3.Normalize(Vector3.TransformNormal(tang, desiredRotation));
                        bitang = Vector3.Normalize(Vector3.TransformNormal(bitang, desiredRotation));

                        // Set ...
                        flverVertex.Position = pos;
                        flverVertex.Normal = norm;
                        if (mesh.UvSet0.Count != 0)
                        {
                            TES3.Vec2 uv = mesh.UvSet0[vertexIndex];
                            flverVertex.UVs.Add(new Vector3(uv.x, uv.y, 0));
                        }
                        else
                        {
                            flverVertex.UVs ??= [];
                            flverVertex.UVs.Add(new Vector3(0, 0, 0));
                        }

                        flverVertex.Bitangent = new Vector4(bitang.X, bitang.Y, bitang.Z, 0);
                        flverVertex.Tangents.Add(new Vector4(tang.X, tang.Y, tang.Z, 0));

                        flverVertex.Colors.Add(new FLVER.VertexColor(255, 255, 255, 255));

                        if (materialInfo.template == MaterialContext.MaterialTemplate.Foliage)
                        {
                            flverVertex.UVs.Add(new Vector3(0f, .2f, 0f));
                            flverVertex.UVs.Add(new Vector3(1f, 1f, 0f));
                            flverVertex.UVs.Add(new Vector3(1f, 1f, 0f));
                        }

                        flverMesh.Vertices.Add(flverVertex);
                        flverFaces.Indices.Add(flverMesh.Vertices.Count - 1);
                    }

                }

                flver.Meshes.Add(flverMesh);
            }

            /* Calculate bounding boxes */
            BoundingBoxSolver.FLVER(flver);

            /* Add Dummy Polys */
            void AddDmy(Vector3 position, short id)
            {
                FLVER.Dummy dmy = new();
                dmy.Position = position;
                dmy.Forward = new(0, 0, 1);
                dmy.Upward = new(0, 1, 0);
                dmy.Color = System.Drawing.Color.White;
                dmy.ReferenceID = id;
                dmy.ParentBoneIndex = 0;
                dmy.AttachBoneIndex = 0;
                dmy.UseUpwardVector = true;
                flver.Dummies.Add(dmy);
            }

            /* Add some generic dmys based on orientations */
            Vector3 root = Vector3.Zero;
            Vector3 center = Vector3.Lerp(flver.Nodes[0].BoundingBoxMin, flver.Nodes[0].BoundingBoxMax, .5f);
            Vector3 bottom = new Vector3(center.X, flver.Nodes[0].BoundingBoxMin.Y, center.Z);
            Vector3 top = new Vector3(center.X, flver.Nodes[0].BoundingBoxMax.Y, center.Z);

            AddDmy(root, Const.FLVER_DMY_ROOT);
            AddDmy(center, Const.FLVER_DMY_CENTER);
            AddDmy(bottom, Const.FLVER_DMY_BOTTOM);
            AddDmy(top, Const.FLVER_DMY_TOP);

            /* Now add dmys from the emitters and nodes in the model */
            short nextRef = 500; // idk why we start at 500, i'm copying old code from DS3 portjob here
            List<(string name, Vector3 position)> nodes = new();

            Vector3 CollapseTransform(Transform transform)
            {
                /* Correct position of emitter dmy based on the vertex orientation code above */
                Matrix4x4 mt = Matrix4x4.CreateTranslation(transform.Translation.ToVector3());
                Matrix4x4 mr = Matrix4x4.CreateFromQuaternion(transform.Rotation.ToQuaternion());
                Matrix4x4 ms = Matrix4x4.CreateScale(transform.Scale);

                Vector3 position = new();
                position = Vector3.Transform(position, ms * mr * mt);
                position *= Const.GLOBAL_SCALE;
                position.X *= -1f;
                position = Vector3.Transform(position, desiredRotation);
                return position;
            }

            for(int i=0;i<nif.Nodes.Count;i++)
            {
                TES3.Node node = nif.Nodes[i];

                string name = node.Name.String.ToLower();
                if (!(name.Contains("attach") && name.Contains("light"))) { continue; }  // skip any nodes that are not light attachment points
                Vector3 position = CollapseTransform(node.Transform);

                nodes.Add((name, position));
            }

            for (int i = 0; i < nif.Emitters.Count; i++)
            {
                TES3.Emitter emitter = nif.Emitters[i];

                string name = emitter.Name.String.ToLower();
                Vector3 position = CollapseTransform(emitter.Transform);

                nodes.Add((name, position));
            }

            foreach ((string name, Vector3 position) node in nodes)
            {
                string name = node.name;
                Vector3 position = node.position;

                short refid = modelInfo.dummies.ContainsKey(name) ? modelInfo.dummies[name] : nextRef++;

                FLVER.Dummy dmy = new();
                dmy.Position = position;
                dmy.Forward = new(0, 0, 1);
                dmy.Upward = new(0, 1, 0);
                dmy.Color = System.Drawing.Color.White;
                dmy.ReferenceID = refid;
                dmy.ParentBoneIndex = 0;
                dmy.AttachBoneIndex = -1;
                dmy.UseUpwardVector = true;
                flver.Dummies.Add(dmy);
                if (!modelInfo.dummies.ContainsKey(name)) { modelInfo.dummies.Add(name, refid); }
            }

            /* Optimize flver */
            flver = FLVERUtil.Optimize(flver);

            /* Calculate model size */
            float size = Vector3.Distance(rootNode.BoundingBoxMin, rootNode.BoundingBoxMax);
            modelInfo.size = size;

            /* Write flver */
            flver.Write(outputPath);

            /* Generate collision obj if the model contains a collision mesh */
            if ((nif.CollisionMeshes.Count > 0 || forceCollision) && !Override.CheckStaticCollision(modelInfo.name))
            {
                /* Best guess for collision material */
                Obj.CollisionMaterial matguess = Obj.CollisionMaterial.None;
                void Guess(string[] keys, Obj.CollisionMaterial type)
                {
                    if (matguess != Obj.CollisionMaterial.None) { return; }

                    for (int i = 0; i < nif.VisualMeshes.Count; i++)
                    {
                        string tex = nif.VisualMeshes[i].Texture.String.ToLower();
                        foreach (string key in keys)
                        {
                            if (Path.GetFileNameWithoutExtension(modelInfo.name).ToLower().Contains(key)) { matguess = type; return; }
                            if (tex.Contains(key)) { matguess = type; return; }
                            if (Path.GetFileNameWithoutExtension(tex).ToLower().Contains(key)) { matguess = type; return; }
                        }
                    }
                    return;
                }

                /* This is a hierarchy, first found keyword determines collision type, more obvious keywords at the top, niche ones at the bottom */
                Guess(new string[] { "wood", "log", "bark" }, Obj.CollisionMaterial.Wood);
                Guess(new string[] { "sand" }, Obj.CollisionMaterial.Sand);
                Guess(new string[] { "rock", "stone", "boulder" }, Obj.CollisionMaterial.Rock);
                Guess(new string[] { "dirt", "soil", "grass" }, Obj.CollisionMaterial.Dirt);
                Guess(new string[] { "iron", "metal", "steel" }, Obj.CollisionMaterial.IronGrate);
                Guess(new string[] { "mushroom", }, Obj.CollisionMaterial.ScarletMushroom);
                Guess(new string[] { "statue", "adobe" }, Obj.CollisionMaterial.Rock);
                Guess(new string[] { "dwrv", "daed" }, Obj.CollisionMaterial.Rock);

                // Give up!
                if (matguess == Obj.CollisionMaterial.None) { matguess = Obj.CollisionMaterial.Stock; }

                /* If the model doesnt have an explicit collision mesh but forceCollision is on because it's a static, we use the visual mesh as a collision mesh */
                Obj obj = COLLISIONtoOBJ(nif.CollisionMeshes.Count > 0 ? nif.CollisionMeshes : nif.VisualMeshes, matguess);
                if (nif.CollisionMeshes.Count <= 0) { Lort.Log($"{modelInfo.name} had forced collision gen...", Lort.Type.Debug); }

                /* Make obj file for collision. These will be converted to HKX later */
                string objPath = outputPath.Replace(".flver", ".obj");
                CollisionInfo collisionInfo = new(modelInfo.name, $"meshes\\{Path.GetFileNameWithoutExtension(objPath)}.obj");
                modelInfo.collision = collisionInfo;

                obj = obj.optimize();
                obj.write(objPath);
            }
            return modelInfo;
        }
    }
        public static class Tes3Extensions
    {
        public static Vec3 Multiply(this Vec3 vec, float value)
        {
            Vec3 result = vec;
            result.x = result.x * value;
            result.y = result.y * value;
            result.z = result.z * value;
            return result;
        }

        public static Vector3 ToNumeric(this Vec3 vec)
        {
            return new System.Numerics.Vector3(vec.x, vec.y, vec.z);
        }

        public static Vector3 ToVector3(this float[] floats)
        {
            if (floats.Length < 3) return Vector3.Zero;
            return new Vector3(floats[0], floats[1], floats[2]);
        }

        public static Vector4 ToVector4(this float[] floats)
        {
            if (floats.Length < 4) return Vector4.Zero;
            return new Vector4(floats[0], floats[1], floats[2], floats[3]);
        }

        public static Quaternion ToQuaternion(this float[] floats)
        {
            if (floats.Length < 4) return Quaternion.Identity;
            return new Quaternion(floats[0], floats[1], floats[2], floats[3]);
        }

        public static Vector3 ToNumeric(this Vec2 vec)
        {
            return new System.Numerics.Vector3(vec.x, vec.y, 0);
        }

        public static int T(this Triangle tri, int i)
        {
            return (new int[] { tri.v0, tri.v1, tri.v2 })[i];
        }

        public static void GetBounds(this Obj obj, out Vector3 min, out Vector3 max)
        {
            if (obj.vs == null || obj.vs.Count == 0)
            {
                min = max = Vector3.Zero;
                return;
            }

            min = obj.vs[0];
            max = obj.vs[0];

            foreach (Vector3 v in obj.vs)
            {
                if (v.X < min.X) min.X = v.X;
                if (v.Y < min.Y) min.Y = v.Y;
                if (v.Z < min.Z) min.Z = v.Z;

                if (v.X > max.X) max.X = v.X;
                if (v.Y > max.Y) max.Y = v.Y;
                if (v.Z > max.Z) max.Z = v.Z;
            }
        }
    }
}
