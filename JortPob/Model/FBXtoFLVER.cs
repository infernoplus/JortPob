using JortPob.Common;
using SharpAssimp;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace JortPob.Model
{
    public partial class ModelConverter
    {
        public static ModelInfo FBXtoFLVER(AssimpContext assimpContext, MaterialContext materialContext, ModelInfo modelInfo, bool forceCollision, string fbxFilename, string outputFilename)
        {
            /* Load FBX file via Assimp */
            Scene fbx = assimpContext.ImportFile(fbxFilename, PostProcessSteps.CalculateTangentSpace);

            /* Create a blank FLVER configured for Elden Ring */
            FLVER2 flver = new();
            flver.Header.Version = 131098; // Elden Ring FLVER Version Number
            flver.Header.Unk5D = 0;        // Unk
            flver.Header.Unk68 = 4;        // Unk

            /* Add bones and nodes for FLVER */
            FLVER.Node rootNode = new();
            FLVER2.SkeletonSet skeletonSet = new();
            FLVER2.SkeletonSet.Bone rootBone = new(0);

            rootNode.Name = Path.GetFileNameWithoutExtension(fbxFilename);
            skeletonSet.AllSkeletons.Add(rootBone);
            skeletonSet.BaseSkeleton.Add(rootBone);
            flver.Nodes.Add(rootNode);
            flver.Skeletons = skeletonSet;

            /* Generate material data */
            List<MaterialContext.MaterialInfo> materialInfos = materialContext.GenerateMaterials(fbx.Materials);
            foreach (MaterialContext.MaterialInfo mat in materialInfos)
            {
                flver.Materials.Add(mat.material);
                flver.GXLists.Add(mat.gx);
                flver.BufferLayouts.Add(mat.layout);
                foreach (TextureInfo info in mat.info)
                {
                    modelInfo.textures.Add(info);
                }
            }

            /* Iterate scene hierarchy and identify and sort collision and render meshes and also identify and collect useful nodes for use as dummies */
            List<Tuple<string, Vector3>> nodes = new(); // FBX nodes that we will use as dummies
            List<Tuple<Node, Mesh>> fbxMeshes = new();
            List<Tuple<Node, Mesh>> fbxCollisions = new();

            Vector3 CollapseTransform(Node node)
            {
                Vector3 position = Vector3.Zero;

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

                    position = Vector3.Transform(position, ms * mr * mt);

                    parent = parent.Parent;
                }
                return position;
            }

            void FBXHierarchySearch(Node fbxParentNode, bool isCollision)
            {
                foreach (Node fbxChildNode in fbxParentNode.Children) {
                    bool isNodeCollision = isCollision;
                    string nodename = fbxChildNode.Name.ToLower();

                    if (nodename.Trim().ToLower() == "collision")
                    {
                        isNodeCollision = true;
                    }
                    if (nodename.Contains("attachlight") || nodename.Contains("emitter"))
                    {
                        nodes.Add(new(nodename, CollapseTransform(fbxChildNode)));
                    }
                    if (fbxChildNode.HasMeshes)
                    {
                        foreach (int fbxMeshIndex in fbxChildNode.MeshIndices)
                        {
                            if (isNodeCollision) { fbxCollisions.Add(new Tuple<Node, Mesh>(fbxChildNode, fbx.Meshes[fbxMeshIndex])); }
                            else { fbxMeshes.Add(new Tuple<Node, Mesh>(fbxChildNode, fbx.Meshes[fbxMeshIndex])); }
                        }
                    }
                    if (fbxChildNode.HasChildren)
                    {
                        FBXHierarchySearch(fbxChildNode, isNodeCollision);
                    }
                }
            }
            FBXHierarchySearch(fbx.RootNode, false);

            /* Convert meshes */
            foreach (Tuple<Node, Mesh> tuple in fbxMeshes)
            {
                Node node = tuple.Item1;
                Mesh fbxMesh = tuple.Item2;
                int index = fbxMesh.MaterialIndex;
                MaterialContext.MaterialInfo materialInfo = materialInfos[index];

                /* Some Fix-up code here. We need to remove any meshes with a name like "ShadowBox" */
                /* These meshes are used for like shadow stencils or... something? Regardless they are worthless in the ER engine */
                if (fbxMesh.Name.ToLower().Contains("shadowbox")) { index++; continue; }
                if (fbxMesh.Name.ToLower().Contains("tri attachlight")) { index++; continue; }

                /* Generate blank flver mesh and faceset */
                FLVER2.Mesh flverMesh = new();
                FLVER2.FaceSet flverFaces = new();
                flverMesh.FaceSets.Add(flverFaces);
                flverFaces.CullBackfaces = true;
                flverFaces.Unk06 = 1;
                flverMesh.NodeIndex = 0; // attach to rootnode
                flverMesh.MaterialIndex = index;

                /* Setup Vertex Buffer */
                FLVER2.VertexBuffer flverBuffer = new(0);
                flverBuffer.LayoutIndex = index++;
                flverMesh.VertexBuffers.Add(flverBuffer);

                /* Spit out some warnings */
                if (fbxMesh.TextureCoordinateChannelCount <= 0) { Lort.Log($"## WARNING ## {rootNode.Name}->{fbxMesh.Name} has no UV channels!", Lort.Type.Debug); }
                else if (fbxMesh.TextureCoordinateChannelCount > 1) { Lort.Log($"## WARNING ## {rootNode.Name}->{fbxMesh.Name} has multiple UV channels!", Lort.Type.Debug); }

                /* Convert vert/face data */
                if (fbxMesh.Tangents.Count <= 0)
                {
                    Lort.Log($"## WARNING ## {rootNode.Name}->{fbxMesh.Name} has no tangent data!", Lort.Type.Debug);
                }
                foreach (Face fbxFace in fbxMesh.Faces)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        FLVER.Vertex flverVertex = new();

                        /* Grab vertice position + normals/tangents */
                        Vector3 pos = fbxMesh.Vertices[fbxFace.Indices[i]];
                        Vector3 norm = fbxMesh.Normals[fbxFace.Indices[i]];
                        Vector3 tang;
                        Vector3 bitang;
                        if (fbxMesh.Tangents.Count > 0)
                        {
                            tang = fbxMesh.Tangents[fbxFace.Indices[i]];
                            bitang = fbxMesh.BiTangents[fbxFace.Indices[i]];
                        }
                        else
                        {
                            tang = new Vector3(1, 0, 0);
                            bitang = new Vector3(0, 0, 1);
                        }

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
                            tang = Vector3.TransformNormal(tang, mr);
                            bitang = Vector3.TransformNormal(bitang, mr);

                            parent = parent.Parent;
                        }

                        // Fromsoftware lives in the mirror dimension. I do not know why.
                        pos = pos * Const.GLOBAL_SCALE;
                        pos.X *= -1f;
                        norm.X *= -1f;
                        tang.X *= -1f;
                        bitang.X *= -1f;

                        /* Rotate Y 180 degrees because... */
                        Matrix4x4 rotateY180Matrix = Matrix4x4.CreateRotationY((float)Math.PI);
                        pos = Vector3.Transform(pos, rotateY180Matrix);

                        /* Rotate normals/tangents to match */
                        norm = Vector3.Normalize(Vector3.TransformNormal(norm, rotateY180Matrix));
                        tang = Vector3.Normalize(Vector3.TransformNormal(tang, rotateY180Matrix));
                        bitang = Vector3.Normalize(Vector3.TransformNormal(bitang, rotateY180Matrix));

                        // Set ...
                        flverVertex.Position = pos;
                        flverVertex.Normal = norm;
                        if (fbxMesh.TextureCoordinateChannelCount <= 0)
                        {
                            flverVertex.UVs.Add(new Vector3(0, 0, 0));
                        }
                        else
                        {
                            Vector3 uvw = fbxMesh.TextureCoordinateChannels[0][fbxFace.Indices[i]];
                            uvw.Y *= -1f;
                            flverVertex.UVs.Add(uvw);
                        }
                        flverVertex.Bitangent = new Vector4(bitang.X, bitang.Y, bitang.Z, 0);
                        flverVertex.Tangents.Add(new Vector4(tang.X, tang.Y, tang.Z, 0));
                        if (fbxMesh.HasVertexColors(0))
                        {
                            Vector4 color = fbxMesh.VertexColorChannels[0][fbxFace.Indices[i]];
                            flverVertex.Colors.Add(new FLVER.VertexColor(color.W, color.X, color.Y, color.Z));
                        }
                        else
                        {
                            flverVertex.Colors.Add(new FLVER.VertexColor(255, 255, 255, 255));
                        }

                        /* Some special stuff here. For nonstandard materials like foliage we need to add some extra UV information */
                        if(materialInfo.template == MaterialContext.MaterialTemplate.Foliage)
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
            short nextRef = 500; // idk why we start at 500, i'm copying old code from DS3 portjob here
            // first add a center dummy poly with the  ids 90 and 100. These are default used for item treasure points and interaction points so every model should just have them
            // and also a bottom and top dmy poly at 101 and 102
            Vector3 center = Vector3.Lerp(flver.Nodes[0].BoundingBoxMin, flver.Nodes[0].BoundingBoxMax, .5f);
            Vector3 bottom = new Vector3(center.X, flver.Nodes[0].BoundingBoxMin.Y, center.Z);
            Vector3 top = new Vector3(center.X, flver.Nodes[0].BoundingBoxMax.Y, center.Z);
            {
                FLVER.Dummy dmy = new();
                dmy.Position = center;
                dmy.Forward = new(0, 0, 1);
                dmy.Upward = new(0, 1, 0);
                dmy.Color = System.Drawing.Color.White;
                dmy.ReferenceID = 90;
                dmy.ParentBoneIndex = 0;
                dmy.AttachBoneIndex = 0;
                dmy.UseUpwardVector = true;
                flver.Dummies.Add(dmy);
            }
            {
                FLVER.Dummy dmy = new();
                dmy.Position = center;
                dmy.Forward = new(0, 0, 1);
                dmy.Upward = new(0, 1, 0);
                dmy.Color = System.Drawing.Color.White;
                dmy.ReferenceID = 100;
                dmy.ParentBoneIndex = 0;
                dmy.AttachBoneIndex = 0;
                dmy.UseUpwardVector = true;
                flver.Dummies.Add(dmy);
            }
            {
                FLVER.Dummy dmy = new();
                dmy.Position = bottom;
                dmy.Forward = new(0, 0, 1);
                dmy.Upward = new(0, 1, 0);
                dmy.Color = System.Drawing.Color.White;
                dmy.ReferenceID = 101;
                dmy.ParentBoneIndex = 0;
                dmy.AttachBoneIndex = 0;
                dmy.UseUpwardVector = true;
                flver.Dummies.Add(dmy);
            }
            {
                FLVER.Dummy dmy = new();
                dmy.Position = top;
                dmy.Forward = new(0, 0, 1);
                dmy.Upward = new(0, 1, 0);
                dmy.Color = System.Drawing.Color.White;
                dmy.ReferenceID = 102;
                dmy.ParentBoneIndex = 0;
                dmy.AttachBoneIndex = 0;
                dmy.UseUpwardVector = true;
                flver.Dummies.Add(dmy);
            }
            // next add a root node dummy. this is sometimes used by fxr
            {
                FLVER.Dummy dmy = new();
                dmy.Position = Vector3.Zero;
                dmy.Forward = new(0, 0, 1);
                dmy.Upward = new(0, 1, 0);
                dmy.Color = System.Drawing.Color.White;
                dmy.ReferenceID = 90;
                dmy.ParentBoneIndex = 0;
                dmy.AttachBoneIndex = 0;
                dmy.UseUpwardVector = true;
                flver.Dummies.Add(dmy);
                modelInfo.dummies.Add("root", nextRef++);
            }
            // lastly handle dummies from nif
            foreach (Tuple<string, Vector3> tuple in nodes)
            {
                string name = tuple.Item1;
                Vector3 position = tuple.Item2;

                if (name.Contains(".0")) { name = name.Substring(0, name.Length - 4); }   // Duplicate nodes get a '.001' and what not appended to their names. Remove that.
                short refid = modelInfo.dummies.ContainsKey(name) ? modelInfo.dummies[name] : nextRef++;

                // correct position using same math as we use for vertices above
                position = position * Const.GLOBAL_SCALE;
                position.X *= -1f;
                Matrix4x4 rotateY180Matrix = Matrix4x4.CreateRotationY((float)Math.PI);
                position = Vector3.Transform(position, rotateY180Matrix);

                FLVER.Dummy dmy = new();
                dmy.Position = position;
                dmy.Forward = new(0, 0, 1);
                dmy.Upward = new(0, 1, 0);
                dmy.Color = System.Drawing.Color.White;
                dmy.ReferenceID = refid;
                dmy.ParentBoneIndex = 0;
                dmy.AttachBoneIndex = 0;
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
            flver.Write(outputFilename);

            /* Generate collision obj if the model contains a collision mesh */
            if ((fbxCollisions.Count > 0 || forceCollision) && !Override.CheckStaticCollision(modelInfo.name))
            {
                /* Best guess for collision material */
                Obj.CollisionMaterial matguess = Obj.CollisionMaterial.None;
                void Guess(string[] keys, Obj.CollisionMaterial type)
                {
                    if (matguess != Obj.CollisionMaterial.None) { return; }
                    foreach (Material mat in fbx.Materials)
                    {
                        foreach (string key in keys)
                        {
                            if (Path.GetFileNameWithoutExtension(modelInfo.name).ToLower().Contains(key)) { matguess = type; return; }
                            if (mat.Name.ToLower().Contains(key)) { matguess = type; return; }
                            if (mat.TextureDiffuse.FilePath != null && Path.GetFileNameWithoutExtension(mat.TextureDiffuse.FilePath).ToLower().Contains(key)) { matguess = type; return; }
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
                Obj obj = COLLISIONtoOBJ(fbxCollisions.Count > 0 ? fbxCollisions : fbxMeshes, matguess);
                if (fbxCollisions.Count <= 0) { Lort.Log($"{modelInfo.name} had forced collision gen...", Lort.Type.Debug); }

                /* Make obj file for collision. These will be converted to HKX later */
                string objPath = outputFilename.Replace(".flver", ".obj");
                CollisionInfo collisionInfo = new(modelInfo.name, $"meshes\\{Path.GetFileNameWithoutExtension(objPath)}.obj");
                modelInfo.collision = collisionInfo;

                obj = obj.optimize();
                obj.write(objPath);
            }

            return modelInfo;
        }
    }
}
