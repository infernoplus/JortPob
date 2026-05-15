using JortPob.Common;
using JortPob.Model;
using SharpAssimp;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using TriangleNet.Geometry;

namespace JortPob
{
    /* Automagically generates water assets for  the cache */
    /* Also handles some stuff for swamps and lava */
    /* Note: this class is absolutely disgusting and is devoid of any good coding practices. I'm sorry! */
    public class LiquidManager
    {
        /* Creates assetbnd, hkx file, and matbins for water */
        public static List<LiquidInfo> GenerateLiquids(ESM esm, MaterialContext materialContext)
        {
            /* Further research on water meshes leads me to believe the best approach is a single water mesh for the entire world space. */
            /* Stupid as fuck solution but it is what it is */

            /* Generate visual water mesh data */  // single massive mesh used in super overworld
            Lort.Log("Generating liquids...", Lort.Type.Main);
            Lort.NewTask("Liquid Generation", 20 + 4 + esm.exterior.Count());
            WetMesh wetmesh = new(esm, GetCutoutType(Cutout.Type.Both, false, Const.WATER_CUTOUT_SIZE_TWEAK)); // a lot of things happen in this constructor

            /* Generate collision water mesh data */
            List<Tuple<Int2, WetMesh>> wetcollisions = new();
            WetMesh genericwetcollision = new(esm.GetCellByGrid(new Int2(0, 0)), new()); // generic no cutouts water plane. most cells will use this
            wetcollisions.Add(new(new Int2(0, 0), genericwetcollision));
            
            var lookup = esm.GetAllRecordsByType(ESM.Type.LandscapeTexture).ToLookup(j => int.Parse(j["index"].ToString()));
           
            foreach(Cell cell in esm.exterior)
            {
                // Only generate if there is water, and it has a cutout intersecting somewhere
                // If there is no water, we don't need it. if there water but no cutout, we will re-use a simple default water plane
                bool needsMesh = false;
                foreach(Cutout cutout in CUTOUTS)
                {
                    if (cell.IsPointInside(cutout.Points()))
                    {
                        needsMesh = true; break;
                    }
                }
                if (!needsMesh) { continue; } // no cutout, skip

                Landscape landscape = esm.GetLandscape(cell.coordinate, lookup);
                if (!landscape.hasWater) { continue; } // no water, skip

                WetMesh wetcollision = new(cell, CUTOUTS);
                Tuple<Int2, WetMesh> tuple = new(cell.coordinate, wetcollision);
                wetcollisions.Add(tuple);
                Lort.TaskIterate();
            }

            /* Write generated water mesh data into a flver */
            FLVER2 flver = GenerateWaterFlver(wetmesh, materialContext);

            /* Files happen */
            string flverPath = @"water\super_water.flver";
            flver.Write(Path.Combine(Const.CACHE_PATH, flverPath));

            /* make a waterinfo class about this generated water */
            LiquidInfo waterInfo = new(0, flverPath);
            foreach (Tuple<Int2, WetMesh> tuple in wetcollisions)
            {
                Int2 coordinate = tuple.Item1;
                WetMesh wetcollision = tuple.Item2;
                Obj obj = wetcollision.ToObj(Obj.CollisionMaterial.Water).optimize();
                string objPath = $"water\\collision[{coordinate.x},{coordinate.y}].obj";
                obj.write(Path.Combine(Const.CACHE_PATH, objPath));

                CollisionInfo collisionInfo = new($"water collision[{coordinate.x}, {coordinate.y}]", objPath);
                waterInfo.AddCollision(coordinate, collisionInfo);
            }

            /* Generate visual mesh for lava */
            /* While we could use morrowind lava it's very uhhh boring and ugly... so INSTEAD ima make meshes for Elden Ring lava shaders */
            /* Fundamentally I have to generate new meshes for these because Elden Ring lava is just... way differnt than morrowind lava */
            /* So here we go! */
            WetMesh lavaMesh = new WetMesh(GetCutoutType(Cutout.Type.Lava, true), Cutout.Type.Lava);
            FLVER2 lavaFlver = GenerateLavaFlver(lavaMesh, materialContext);
            string lavaFlverPath = @"water\super_lava.flver";
            lavaFlver.Write(Path.Combine(Const.CACHE_PATH, lavaFlverPath));
            LiquidInfo lavaInfo = new(2, lavaFlverPath);


            /* Generate visual mesh for swamps */
            /* See above for explanation, same as lava */
            WetMesh swampMesh = new WetMesh(GetCutoutType(Cutout.Type.Swamp, true), Cutout.Type.Swamp);
            FLVER2 swampFlver = GenerateSwampFlver(swampMesh, materialContext);
            string swampFlverPath = @"water\super_swamp.flver";
            swampFlver.Write(Path.Combine(Const.CACHE_PATH, swampFlverPath));
            LiquidInfo swampInfo = new(1, swampFlverPath);

            return new List<LiquidInfo>() { waterInfo, swampInfo, lavaInfo };
        }

        public static List<CutoutInfo> GenerateCutouts(ESM esm)
        {
            /* Generate swamp/lava collision planes */
            /* Loop through each ext cell and check if a cutout is inside it. If so make a collision plane for it */
            List<Tuple<Int2, Obj>> cutoutCollisions = new();
            foreach (Cell cell in esm.exterior)
            {
                List<Cutout> cutouts = new();
                List<ShapedCutout> shaped = new();
                foreach (Cutout c in CUTOUTS)
                {
                    if (cell.IsPointInside(c.position))
                    {
                        cutouts.Add(c);
                    }
                }

                foreach (ShapedCutout c in SHAPED_CUTOUTS)
                {
                    if (cell.IsPointInside(c.position))
                    {
                        shaped.Add(c);
                    }
                }
                if (cutouts.Count <= 0 && shaped.Count <= 0) { continue; } // no cuttys, no mesh, no prob

                /* Generate mesh with cutouts we found... making a raw obj because guh */
                Obj obj = new();

                ObjG swampG = new();
                swampG.name = Obj.CollisionMaterial.PoisonSwamp.ToString();
                swampG.mtl = $"hkm_{swampG.name}_Safe1";

                ObjG lavaG = new();
                lavaG.name = Obj.CollisionMaterial.Lava.ToString();
                lavaG.mtl = $"hkm_{lavaG.name}_Safe1";

                obj.vns.Add(new Vector3(0, 1, 0));
                obj.vts.Add(new Vector3(0, 0, 0));

                Vector3 cellOffset = new Vector3(cell.coordinate.x, 0f, cell.coordinate.y) * Const.CELL_SIZE;
                foreach (Cutout cutout in cutouts)
                {
                    foreach (Vector3 point in cutout.Points())
                    {
                        obj.vs.Add(point - cellOffset + new Vector3(0f, cutout.height, 0f));   // offset moves cutout into the coordinate space of the cell. where 0,0 is the center of the cell iirc
                    }

                    ObjV A1 = new(obj.vs.Count() - 1, 0, 0);
                    ObjV B1 = new(obj.vs.Count() - 2, 0, 0);
                    ObjV C1 = new(obj.vs.Count() - 3, 0, 0);

                    ObjV A2 = new(obj.vs.Count() - 3, 0, 0);
                    ObjV B2 = new(obj.vs.Count() - 4, 0, 0);
                    ObjV C2 = new(obj.vs.Count() - 1, 0, 0);

                    ObjF F1 = new(A1, B1, C1);
                    ObjF F2 = new(A2, B2, C2);

                    ObjG gType = null;
                    if(cutout.type == Cutout.Type.Swamp) { gType = swampG; }
                    else if(cutout.type == Cutout.Type.Lava) { gType = lavaG; }

                    gType.fs.Add(F1);
                    gType.fs.Add(F2);
                }

                foreach(ShapedCutout cutout in shaped)
                {
                    foreach (WetFace face in cutout.mesh)
                    {
                        foreach (Vector3 point in face.Points())
                        {
                            obj.vs.Add(point - cellOffset + new Vector3(0f, cutout.height, 0f));   // offset moves cutout into the coordinate space of the cell. where 0,0 is the center of the cell iirc
                        }

                        ObjV A = new(obj.vs.Count() - 1, 0, 0);
                        ObjV B = new(obj.vs.Count() - 2, 0, 0);
                        ObjV C = new(obj.vs.Count() - 3, 0, 0);

                        ObjF F = new(A, B, C);

                        ObjG gType = null;
                        if (cutout.type == Cutout.Type.Swamp) { gType = swampG; }
                        else if (cutout.type == Cutout.Type.Lava) { gType = lavaG; }

                        gType.fs.Add(F);
                    }
                }

                if (lavaG.fs.Count() > 0) { obj.gs.Add(lavaG); }    // realistically, there should never be both lava and swamp in the same cell. it just doesnt happen but like guh.
                if (swampG.fs.Count() > 0) { obj.gs.Add(swampG); }

                cutoutCollisions.Add(new(cell.coordinate, obj.optimize()));
            }

            /* make a cutoutinfo class about each generated cutoutcollision */
            List<CutoutInfo> cutoutInfos = new();
            foreach (Tuple<Int2, Obj> tuple in cutoutCollisions)
            {
                Int2 coordinate = tuple.Item1;
                Obj obj = tuple.Item2;
                string objPath = $"cutout\\collision[{coordinate.x},{coordinate.y}].obj";
                obj.write(Path.Combine(Const.CACHE_PATH, objPath));

                CollisionInfo collisionInfo = new($"cutout collision[{coordinate.x}, {coordinate.y}]", objPath);
                CutoutInfo cutoutInfo = new(coordinate, collisionInfo);
                cutoutInfos.Add(cutoutInfo);
            }

            return cutoutInfos;
        }

        private static FLVER2 GenerateWaterFlver(WetMesh wet, MaterialContext materialContext)
        {
            FLVER2 flver = new();
            flver.Header.Version = 131098; // Elden Ring FLVER Version Number
            flver.Header.Unk5D = 0;        // Unk
            flver.Header.Unk68 = 4;        // Unk

            /* Add bones and nodes for FLVER */
            FLVER.Node rootNode = new();
            FLVER2.SkeletonSet skeletonSet = new();
            FLVER2.SkeletonSet.Bone rootBone = new(0);

            rootNode.Name = Path.GetFileNameWithoutExtension("WaterMesh");
            skeletonSet.AllSkeletons.Add(rootBone);
            skeletonSet.BaseSkeleton.Add(rootBone);
            flver.Nodes.Add(rootNode);
            flver.Skeletons = skeletonSet;

            /* Material */
            MaterialContext.MaterialInfo matinfo = materialContext.GenerateMaterialWater(0);
            flver.Materials.Add(matinfo.material);
            flver.BufferLayouts.Add(matinfo.layout);
            flver.GXLists.Add(matinfo.gx);

            /* make a mesh */
            FLVER2.Mesh mesh = new();
            FLVER2.FaceSet faces = new();
            mesh.FaceSets.Add(faces);
            faces.CullBackfaces = false;
            faces.Unk06 = 1;
            mesh.NodeIndex = 0; // attach to rootnode
            mesh.MaterialIndex = 0;
            FLVER2.VertexBuffer vb = new(0);
            mesh.VertexBuffers.Add(vb);

            /* generic data */
            Vector3 normal = new Vector3(0, 1, 0);
            Vector4 tangent = new Vector4(1, 0, 0, -1);
            Vector4 bitangent = new Vector4(0, 0, 0, 0);
            FLVER.VertexColor color = new(255, 255, 255, 255);

            // returns indice if exists, -1 if doesnt // normally i dont caare about optimizing verts/indices but this material really cares about connected verts so we doing it
            int GetVertex(FLVER.Vertex v)
            {
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    FLVER.Vertex vert = mesh.Vertices[i];
                    if (
                        Vector3.Distance(vert.Position, v.Position) < 0.001 &&
                        Vector3.Distance(vert.UVs[0], v.UVs[0]) < 0.001
                    ) { return i; }
                }
                return -1;
            }

            /* Write wet mesh data to flver and generate UVs */
            foreach (WetFace face in wet.faces)
            {
                FLVER.Vertex[] verts = new FLVER.Vertex[3];

                for(int i=0;i<face.Points().Count();i++)
                {
                    Vector3 v = face.Points()[i];

                    FLVER.Vertex vert = new();
                    vert.Position = v;
                    vert.Normal = normal;
                    vert.Tangents.Add(tangent);
                    vert.Bitangent = bitangent;
                    vert.Colors.Add(color);

                    Vector3 worldSpaceUV = new(vert.Position.X, vert.Position.Z, 0f);

                    float distToZero = Vector3.Distance((worldSpaceUV / Const.CELL_SIZE) - new Vector3(Const.WATER_CENTER.X, Const.WATER_CENTER.Y, 0f), Vector3.Zero);
                    float normDistToZero = distToZero / (Const.WATER_RADIUS + 2.5f);  // +2.5f is to account for outer radius
                    Vector3 normalized = ((worldSpaceUV / Const.CELL_SIZE) - new Vector3(Const.WATER_CENTER.X, Const.WATER_CENTER.Y, 0f)) / (Const.WATER_RADIUS + 3); // not vector normalized, just like, normlaized so entire uv of mesh is within the -1 -> 1 space of uvs
                    if (normalized.X > 1f || normalized.Y > 1f) { Lort.Log($"### WATER UV OUTSIDE VALID RANGE [{normalized.X}, {normalized.Y}]", Lort.Type.Debug); } // guh!

                    Vector3 dir = Vector3.Normalize(normalized); // actually normalized direction of point from 0,0
                    Vector3 forward = Vector3.UnitY;
                    Vector3 left = Vector3.UnitX;
                    double ringAngle = (Math.Acos(Vector3.Dot(dir, left)) > Math.PI / 2 ? -1 : 1) * Math.Acos(Vector3.Dot(dir, forward));
                    float ringX = (float)(ringAngle / Math.PI);
                    Vector3 ringUV = new Vector3(ringX, -1f * ((normDistToZero * 2f) - 1f), 0f);


                    /* Water UV UNK values. I'll add comments to what I think they are as we go. These values determine various ranges for water UVs */
                    float[] UNK_0_0 = new float[] { 16f, 7f }; // scales uvs to the extents of the uv precision. largest uv value here is 16f so we make the flat map as big as possible to use precision fully
                    float UNK_0_1 = 15.95f - UNK_0_0[1] - 1f; // Y value of UV0 helps scale wave intensity. im using this value as an offset to adjust wave intensity a bit
                    float UNK_1_0 = 2f; // for whatever reason this second uv channel is scaled to just like 2f. lol
                    Vector3 UNK_1_1 = new Vector3(.5f, .5f, 0f); // this uv is centered on the 0->1 area. no idea why. this offset moves us there
                    float[,] UNK_2_0 = new float[,] { { 0f, 0f }, { 15.9f, .2f } };  // something to do with depth, x is some kind of distance from shore, y is some kind of var, possibly depth or wave height
                    float[,] UNK_3_0 = new float[,] { { -15.9f, .05f }, { 0f, .125f } }; // same as above but with different offsets and values. oddly the example mesh has a weird thing with some of the x being on the wrong side. maybe X wrap issue
                    float UNK_4_0 = .65f; // dunno, it's almost entirely set to .5f in the elden ring ocean. only exception is in one specific spot near the shore 

                    /* Notes on UV channel 0 */
                    // Most important UV. Seems to have many affects on things
                    // Giant ring layout. Stretches all the way from +16+16 to -16-16
                    // The vertices near Y+16 are nearest to the shore, the vertices near Y-16 are the furthest from shore
                    // The Y value seems to control wave intensity in some way
                    // X Value is unknown
                    // I'm using X*16 and Y*7 to reduce distortion at the shorelines. I could modify it to be exponential the further out into the ocean we get but guh fuck it
                    // I should try and fix the gross stretching at the edges via splitting uvs later.
                    // Also seems to control the actual texture tiling of water textures which means we can't let it be deformed a lot. Needs to be a valid texture unwrap as well as control things

                    /* Notes on what UNKs actually do */
                    // UNK_2_0 :: seems to do almost nothing
                    // UNK_3_0 :: seems to do almost nothing
                    // UNK_4_0 :: 0f = crazy waves, 1f = perfectly flat water plane moving up and down. i think this a scalar for the size of wave noise map?

                    vert.UVs.Add((ringUV * new Vector3(UNK_0_0[0], UNK_0_0[1], 0f)) + new Vector3(0f, UNK_0_1, 0f));  // some kind of loop uv layout, between -15,15
                    vert.UVs.Add((normalized + UNK_1_1) * UNK_1_0);   // normal-ish top down flat uv layout, sized so world is within like -2, 2
                    vert.UVs.Add(new Vector3(float.Lerp(UNK_2_0[0, 0], UNK_2_0[1, 0], normDistToZero), float.Lerp(UNK_2_0[0,1], UNK_2_0[1,1], normDistToZero), 0));   // some kind of value based on distance from center of land
                    vert.UVs.Add(new Vector3(float.Lerp(UNK_3_0[0, 0], UNK_3_0[1, 0], normDistToZero), float.Lerp(UNK_3_0[0, 1], UNK_3_0[1, 1], normDistToZero), 0)); // very similar to above but offset differntly
                    vert.UVs.Add(new Vector3(normalized.X, UNK_4_0, 0)); // weird but X is normal and normalized between 0,1 and y is just flat aside from a few random verts 
                    vert.UVs.Add(new Vector3(normalized.X, UNK_4_0, 0)); // same as last one ????
                    vert.UVs.Add(new Vector3(normalized.X, UNK_4_0, 0)); // also same ???
                    vert.UVs.Add(new Vector3(normalized.X, UNK_4_0, 0)); // still same ??????????

                    verts[i] = vert;
                }

                /* Stuff to do with loop uvs in slot 0. explain later @TODO: */
                bool xEdge = false;
                foreach(FLVER.Vertex vert in verts)
                {
                    if (vert.UVs[0].X < -10) { xEdge = true; break; }
                }

                foreach (FLVER.Vertex vert in verts)
                {
                    if (vert.UVs[0].X >= 16)
                    {
                        vert.UVs[0] = new Vector3(15.9999f, vert.UVs[0].Y, 0);
                    }

                    if(xEdge && vert.UVs[0].X > 10)
                    {
                        vert.UVs[0] = new Vector3(-16f, vert.UVs[0].Y, 0);
                    }

                    int indice = GetVertex(vert);
                    if (indice != -1)
                    {
                        faces.Indices.Add(indice); continue;
                    }

                    mesh.Vertices.Add(vert);
                    faces.Indices.Add(mesh.Vertices.Count - 1);
                }
            }

            /* Add mesh */
            flver.Meshes.Add(mesh);

            /* Bounding box solve */
            BoundingBoxSolver.FLVER(flver);

            return flver;
        }

        public static FLVER2 GenerateLavaFlver(WetMesh hot, MaterialContext materialContext)
        {
            FLVER2 flver = new();
            flver.Header.Version = 131098; // Elden Ring FLVER Version Number
            flver.Header.Unk5D = 0;        // Unk
            flver.Header.Unk68 = 4;        // Unk

            /* Add bones and nodes for FLVER */
            FLVER.Node rootNode = new();
            FLVER2.SkeletonSet skeletonSet = new();
            FLVER2.SkeletonSet.Bone rootBone = new(0);

            rootNode.Name = Path.GetFileNameWithoutExtension("LavaMesh");
            skeletonSet.AllSkeletons.Add(rootBone);
            skeletonSet.BaseSkeleton.Add(rootBone);
            flver.Nodes.Add(rootNode);
            flver.Skeletons = skeletonSet;

            /* Material */
            MaterialContext.MaterialInfo matinfo = materialContext.GenerateMaterialLava(0);
            flver.Materials.Add(matinfo.material);
            flver.BufferLayouts.Add(matinfo.layout);
            flver.GXLists.Add(matinfo.gx);

            /* make a mesh */
            FLVER2.Mesh mesh = new();
            FLVER2.FaceSet faces = new();
            mesh.FaceSets.Add(faces);
            faces.CullBackfaces = false;
            faces.Unk06 = 1;
            mesh.NodeIndex = 0; // attach to rootnode
            mesh.MaterialIndex = 0;
            FLVER2.VertexBuffer vb = new(0);
            mesh.VertexBuffers.Add(vb);

            /* generic data */
            Vector3 normal = new Vector3(0, 1, 0);
            Vector4 tangent = new Vector4(1, 0, 0, -1);
            Vector4 bitangent = new Vector4(0, 0, 0, 0);
            FLVER.VertexColor color = new(255, 255, 255, 255);

            // returns indice if exists, -1 if doesnt // normally i dont caare about optimizing verts/indices but this material really cares about connected verts so we doing it
            int GetVertex(FLVER.Vertex v)
            {
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    FLVER.Vertex vert = mesh.Vertices[i];
                    if (
                        Vector3.Distance(vert.Position, v.Position) < 0.001 &&
                        Vector3.Distance(vert.UVs[0], v.UVs[0]) < 0.001
                    ) { return i; }
                }
                return -1;
            }

            /* Get bounds so we can uvw this easily */
            float BOUND = Const.CELL_EXTERIOR_BOUNDS * Const.CELL_SIZE;
            Vector2 min = new Vector2(BOUND); Vector2 max = new Vector2(-BOUND);
            foreach (WetFace face in hot.faces)
            {
                foreach (Vector3 point in face.Points())
                {
                    min.X = Math.Min(point.X, min.X);
                    min.Y = Math.Min(point.Z, min.Y);
                    max.X = Math.Max(point.X, max.X);
                    max.Y = Math.Max(point.Z, max.Y);
                }
            }

            /* This bounds is a rectangle (probably) so we need to square it off to prevent stretchy UVs */
            if(max.X-min.X < max.Y-min.Y)
            {
                max.X = min.X + (max.Y - min.Y);
            }
            else
            {
                max.Y = min.Y + (max.X - min.X);
            }

            /* Write wet mesh data to flver and generate UVs */
            foreach (WetFace face in hot.faces)
            {
                FLVER.Vertex[] verts = new FLVER.Vertex[3];

                for (int i = 0; i < face.Points().Count(); i++)
                {
                    Vector3 v = face.Points()[i];

                    FLVER.Vertex vert = new();
                    vert.Position = v;
                    vert.Normal = normal;
                    vert.Tangents.Add(tangent);
                    vert.Bitangent = bitangent;
                    vert.Colors.Add(color);

                    float nX = (vert.Position.X - min.X) / (max.X - min.X);
                    float nY = (vert.Position.Z - min.Y) / (max.Y - min.Y);
                    Vector3 normalized = new Vector3(nX, nY, 0f);

                    float UV0_SCALE = 10f;           // tiling of lava texture controlled by this
                    float WAVE_INTENSITY = 0.0875f;   // 0 is perfectly flat, 1 is giga waves
                    float HEAT_INTENSITY = .5f;     // setting this to 0f makes the lava a black texture of like hardened magma
                    float UNK_0 = 0f;  // genuinely think these do nothing. classic fromsoft
                    float UNK_1 = 0f; // genuinely think these do nothing. classic fromsoft
                    float UNK_2 = 0.3f; // not sure but i think something to do with tiling and speed?

                    vert.UVs.Add(normalized * UV0_SCALE);
                    vert.UVs.Add(new Vector3(UNK_2, UNK_0, 0f));
                    vert.UVs.Add(new Vector3(-15.9f, UNK_1, 0f));
                    vert.UVs.Add(new Vector3(WAVE_INTENSITY, HEAT_INTENSITY, 0f));
                    vert.UVs.Add(vert.UVs[3]);

                    verts[i] = vert;
                }

                foreach (FLVER.Vertex vert in verts)
                {
                    int indice = GetVertex(vert);
                    if (indice != -1)
                    {
                        faces.Indices.Add(indice); continue;
                    }

                    mesh.Vertices.Add(vert);
                    faces.Indices.Add(mesh.Vertices.Count - 1);
                }
            }

            /* Add mesh */
            flver.Meshes.Add(mesh);

            /* Bounding box solve */
            BoundingBoxSolver.FLVER(flver);

            return flver;
        }

        public static FLVER2 GenerateSwampFlver(WetMesh swamp, MaterialContext materialContext)
        {
            FLVER2 flver = new();
            flver.Header.Version = 131098; // Elden Ring FLVER Version Number
            flver.Header.Unk5D = 0;        // Unk
            flver.Header.Unk68 = 4;        // Unk

            /* Add bones and nodes for FLVER */
            FLVER.Node rootNode = new();
            FLVER2.SkeletonSet skeletonSet = new();
            FLVER2.SkeletonSet.Bone rootBone = new(0);

            rootNode.Name = Path.GetFileNameWithoutExtension("SwampMesh");
            skeletonSet.AllSkeletons.Add(rootBone);
            skeletonSet.BaseSkeleton.Add(rootBone);
            flver.Nodes.Add(rootNode);
            flver.Skeletons = skeletonSet;

            /* Material */
            MaterialContext.MaterialInfo matinfo = materialContext.GenerateMaterialSwamp(0);
            flver.Materials.Add(matinfo.material);
            flver.BufferLayouts.Add(matinfo.layout);
            flver.GXLists.Add(matinfo.gx);

            /* make a mesh */
            FLVER2.Mesh mesh = new();
            FLVER2.FaceSet faces = new();
            mesh.FaceSets.Add(faces);
            faces.CullBackfaces = false;
            faces.Unk06 = 1;
            mesh.NodeIndex = 0; // attach to rootnode
            mesh.MaterialIndex = 0;
            FLVER2.VertexBuffer vb = new(0);
            mesh.VertexBuffers.Add(vb);

            /* generic data */
            Vector3 normal = new Vector3(0, 1, 0);
            Vector4 tangent = new Vector4(1, 0, 0, -1);
            Vector4 bitangent = new Vector4(0, 0, 0, 0);
            FLVER.VertexColor color = new(255, 255, 255, 255);

            // returns indice if exists, -1 if doesnt // normally i dont caare about optimizing verts/indices but this material really cares about connected verts so we doing it
            int GetVertex(FLVER.Vertex v)
            {
                for (int i = 0; i < mesh.Vertices.Count; i++)
                {
                    FLVER.Vertex vert = mesh.Vertices[i];
                    if (
                        Vector3.Distance(vert.Position, v.Position) < 0.001 &&
                        Vector3.Distance(vert.UVs[0], v.UVs[0]) < 0.001
                    ) { return i; }
                }
                return -1;
            }

            /* Get bounds so we can uvw this easily */
            float BOUND = Const.CELL_EXTERIOR_BOUNDS * Const.CELL_SIZE;
            Vector2 min = new Vector2(BOUND); Vector2 max = new Vector2(-BOUND);
            foreach (WetFace face in swamp.faces)
            {
                foreach (Vector3 point in face.Points())
                {
                    min.X = Math.Min(point.X, min.X);
                    min.Y = Math.Min(point.Z, min.Y);
                    max.X = Math.Max(point.X, max.X);
                    max.Y = Math.Max(point.Z, max.Y);
                }
            }

            /* This bounds is a rectangle (probably) so we need to square it off to prevent stretchy UVs */
            if (max.X - min.X < max.Y - min.Y)
            {
                max.X = min.X + (max.Y - min.Y);
            }
            else
            {
                max.Y = min.Y + (max.X - min.X);
            }

            /* Write wet mesh data to flver and generate UVs */
            Random rand = new();
            foreach (WetFace face in swamp.faces)
            {
                FLVER.Vertex[] verts = new FLVER.Vertex[3];

                for (int i = 0; i < face.Points().Count(); i++) 
                {
                    Vector3 v = face.Points()[i];

                    FLVER.Vertex vert = new();
                    vert.Position = v;
                    vert.Normal = normal;
                    vert.Tangents.Add(tangent);
                    vert.Bitangent = bitangent;
                    vert.Colors.Add(color);

                    float nX = (vert.Position.X - min.X) / (max.X - min.X);
                    float nY = (vert.Position.Z - min.Y) / (max.Y - min.Y);
                    Vector3 normalized = new Vector3(nX, nY, 0f);

                    Vector3 UV0_OFFSET = new Vector3(.5f, .5f, 0f); // the normalized uvs for this are 0 -> 1 so we offset to center it on 0,0 so we can stretch it to fill -16 -> 16
                    float UV0_SCALE = 15.9f;                       // tiling of swamp textures controlled here
                    float UNK_10 = (rand.Next() * .25f) + .25f;   // i have no fucking clue for any of these. honestly could all be stubs that do nothing for all i can tell
                    float UNK_11 = (rand.Next() * .25f) + .25f;
                    float UNK_0 = .05f;
                    float UNK_1 = .05f;
                    float UNK_2 = .0f;
                    float UNK_3 = -15f;

                    vert.UVs.Add((normalized - UV0_OFFSET) * UV0_SCALE);
                    vert.UVs.Add(new Vector3(UNK_2, UNK_0, 0f));
                    vert.UVs.Add(new Vector3(UNK_3, UNK_1, 0f));
                    vert.UVs.Add(new Vector3(UNK_10, UNK_11, 0f));
                    vert.UVs.Add(vert.UVs[3]);

                    verts[i] = vert;
                }

                foreach (FLVER.Vertex vert in verts.Reverse()) // this specific swamp material backface culls disregarding mesh settings so i needed to flip this to make it visible. idk fuck
                {
                    int indice = GetVertex(vert);
                    if (indice != -1)
                    {
                        faces.Indices.Add(indice); continue;
                    }

                    mesh.Vertices.Add(vert);
                    faces.Indices.Add(mesh.Vertices.Count - 1);
                }
            }

            /* Add mesh */
            flver.Meshes.Add(mesh);

            /* Bounding box solve */
            BoundingBoxSolver.FLVER(flver);

            return flver;
        }


        /* When iterating through static assets, if we see swamp meshes we pop em in here. We need a list of swamp areas so we can cut them out of water gen */
        /* Morrowind water is flat so the swamp is just slightly above the water, but elden ring water is 3d so we have to actually slice the water plane to prevent clipping */
        /* Returns true if added, false if not added */
        public static List<Cutout> CUTOUTS = new();
        public static List<ShapedCutout> SHAPED_CUTOUTS = new(); // literally like 2 things. we almost got away with just using squares but bethesda really just HAD to fuck it up

        private static readonly Dictionary<Cutout.Type, List<Cutout>> CUTOUTS_BY_TYPE = new();
        private static readonly Dictionary<Cutout.Type, List<Cutout>> SHAPED_CUTOUTS_BY_TYPE = new();

        public static bool AddCutout(Content content)
        {
            float s;
            Cutout.Type type;
            bool shaped;
            if (content.mesh == @"f\terrain_bc_scum_01.nif") { s = 2048f * Const.GLOBAL_SCALE; type = Cutout.Type.Swamp; shaped = false; }  // measured these meshes in blender. could read actual vert data but they are just squares so why bother
            else if (content.mesh == @"f\terrain_bc_scum_02.nif") { s = 1024f * Const.GLOBAL_SCALE; type = Cutout.Type.Swamp; shaped = false; }
            else if (content.mesh == @"f\terrain_bc_scum_03.nif") { s = 512f * Const.GLOBAL_SCALE; type = Cutout.Type.Swamp; shaped = false; }
            else if (content.mesh == @"i\in_lava_1024.nif") { s = 1024f * Const.GLOBAL_SCALE; type = Cutout.Type.Lava; shaped = false; }
            else if (content.mesh == @"i\in_lava_1024_01.nif") { s = 1024f * Const.GLOBAL_SCALE; type = Cutout.Type.Lava; shaped = false; }
            else if (content.mesh == @"i\in_lava_512.nif") { s = 512f * Const.GLOBAL_SCALE; type = Cutout.Type.Lava; shaped = false; }
            else if (content.mesh == @"i\in_lava_256.nif") { s = 256f * Const.GLOBAL_SCALE; type = Cutout.Type.Lava; shaped = false; }
            else if (content.mesh == @"i\in_lava_oval.nif") { s = 0f; type = Cutout.Type.Lava; shaped = true; } // fuck
            else if (content.mesh == @"i\in_lava_256a.nif") { s = 0f; type = Cutout.Type.Lava; shaped = true; } // off
            else return false;

            Vector3 offsetJank = new Vector3(Const.CELL_SIZE * .5f, 0, Const.CELL_SIZE * .5f); // this is just one of those things where this offset is correct but i'd be hard pressed to tell you why
            if (shaped)
            {
                ShapedCutout cutout = new(type, content.position - offsetJank, content.rotation, content.mesh);
                SHAPED_CUTOUTS.Add(cutout);

                if (SHAPED_CUTOUTS_BY_TYPE.TryGetValue(type, out List<Cutout> value))
                    value.Add(cutout);
                else
                    SHAPED_CUTOUTS_BY_TYPE.Add(type, [cutout]);

                return true;
            }
            else
            {
                Cutout cutout = new(type, content.position - offsetJank, content.rotation, s);
                CUTOUTS.Add(cutout);

                if (CUTOUTS_BY_TYPE.TryGetValue(type, out List<Cutout> value))
                    value.Add(cutout);
                else
                    CUTOUTS_BY_TYPE.Add(type, [cutout]);

                return true;
            }
        }

        public static bool PointInCutout(Cutout.Type type, Vector3 position, bool includeShapedCutouts = false)
        {
            float positionY = position.Y;

            // Check regular cutouts of this type only
            if (CUTOUTS_BY_TYPE.TryGetValue(type, out List<Cutout> regularCutouts))
            {
                foreach (Cutout cutout in regularCutouts)
                {
                    if (positionY <= cutout.height && cutout.IsInside(position, false))
                        return true;
                }
            }

            // Check shaped cutouts of this type only
            if (includeShapedCutouts && SHAPED_CUTOUTS_BY_TYPE.TryGetValue(type, out List<Cutout> shapedCutouts))
            {
                foreach (Cutout cutout in shapedCutouts)
                {
                    if (positionY <= cutout.height && cutout.IsInside(position, false))
                        return true;
                }
            }

            return false;
        }

        public static List<Cutout> GetCutoutType(Cutout.Type type, bool includeShapedCutouts = false, float scaleSizeBy = 1f)
        {
            List<Cutout> cutouts = new();
            foreach(Cutout cutout in CUTOUTS)
            {
                if (cutout.type == type || type == Cutout.Type.Both) {
                    if(scaleSizeBy == 1f) { cutouts.Add(cutout); }
                    else
                    {
                        Cutout scaled = cutout.Copy();
                        scaled.size *= scaleSizeBy;
                        cutouts.Add(scaled);
                    }
                }
            }
            if(includeShapedCutouts)
            {
                foreach (ShapedCutout cutout in SHAPED_CUTOUTS)
                {
                    if (cutout.type == type || type == Cutout.Type.Both) { cutouts.Add(cutout); }
                    // rescaling these is unsupported and hopefully never needed
                }
            }
            return cutouts;
        }

        public class WetMesh
        {
            public List<WetFace> faces;
            public List<List<WetFace>> outlines; // debug

            /* This constructor makes a water mesh for the entire world space. This is use for visuals. Takes the ESM as a param to do it */
            public WetMesh(ESM esm, List<Cutout> cutouts)
            {
                outlines = new();
                void AddDebugOutline(List<WetEdge> es)
                {
                    List<WetFace> group = new();
                    foreach(WetEdge e in es)
                    {
                        Vector3 avg = (e.a + e.b) / 2f;
                        WetFace f = new(e.a, avg, e.b);
                        group.Add(f);
                    }
                    outlines.Add(group);
                }

                float half = Const.CELL_SIZE * .5f; // half a cell

                /* Generate world water mesh */
                faces = new();
                Dictionary<Int2, int> grid = new();
                int flip = 0;
                
                var lookup = esm.GetAllRecordsByType(ESM.Type.LandscapeTexture).ToLookup(j => int.Parse(j["index"].ToString()));
                
                /* We do this 2 in passes. first pass is tesselated, second is filling in more distant squares */
                for (int y = -(Const.WATER_RADIUS + (int)Math.Abs(Const.WATER_CENTER.Y)); y < (Const.WATER_RADIUS + (int)Math.Abs(Const.WATER_CENTER.Y)); y++)
                {
                    for (int x = -(Const.WATER_RADIUS + (int)Math.Abs(Const.WATER_CENTER.X)); x < (Const.WATER_RADIUS + (int)Math.Abs(Const.WATER_CENTER.X)); x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x, y), Const.WATER_CENTER);
                        if (dist < Const.WATER_RADIUS)
                        {
                            Landscape landscape = esm.GetLandscape(new Int2(x, y),lookup);

                            if (landscape == null || landscape.hasWater)
                            {
                                if (landscape != null && landscape.hasSwamp) { continue; } // don't tesselate areas with swamp cutout. we want fewer triangles there for water consistency. just let grid fill those

                                /* Offset */
                                Vector3 posOffset = new Vector3(x, 0f, y) * Const.CELL_SIZE;
                                float startX = -half + posOffset.X;
                                float endX = half + posOffset.X;
                                float startY = -half + posOffset.Z;
                                float endY = half + posOffset.Z;

                                if (dist <= Const.WATER_RADIUS * .75f)
                                {
                                    float size = Const.CELL_SIZE / Const.WATER_TESSELATION;
                                    flip = 0;
                                    for (int yy = 0; yy < Const.WATER_TESSELATION; yy++)
                                    {
                                        for (int xx = 0; xx < Const.WATER_TESSELATION; xx++)
                                        {
                                            Vector3[] quad = new Vector3[]
                                            {
                                            new Vector3(startX + (xx*size), 0f, startY + (yy*size)),
                                            new Vector3(startX + ((xx+1)*size), 0f, startY + (yy*size)),
                                            new Vector3(startX + ((xx+1)*size), 0f, startY + ((yy+1)*size)),
                                            new Vector3(startX + (xx*size), 0f, startY + ((yy+1)*size)),
                                            };

                                            WetFace A = new WetFace(quad[(2 + flip) % 4], quad[(1 + flip) % 4], quad[(0 + flip) % 4]);
                                            WetFace B = new WetFace(quad[(0 + flip) % 4], quad[(3 + flip) % 4], quad[(2 + flip) % 4]);
                                            faces.Add(A); faces.Add(B);
                                            flip = flip == 0 ? 1 : 0;
                                        }
                                        flip = flip == 0 ? 1 : 0;
                                    }
                                    grid.Add(new Int2(x, y), Const.WATER_TESSELATION);
                                }
                            }
                            else
                            {
                                grid.Add(new Int2(x, y), -1); // -1 means land are with no water. for openedges to ignore
                            }
                        }
                    }
                }
                Lort.TaskIterate();

                /* Outline triangle fill time */
                flip = 0;
                              
                for (int y = -(Const.WATER_RADIUS + (int)Math.Abs(Const.WATER_CENTER.Y)); y < (Const.WATER_RADIUS + (int)Math.Abs(Const.WATER_CENTER.Y)); y++)
                {
                    for (int x = -(Const.WATER_RADIUS + (int)Math.Abs(Const.WATER_CENTER.X)); x < (Const.WATER_RADIUS + (int)Math.Abs(Const.WATER_CENTER.X)); x++)
                    {
                        float dist = Vector2.Distance(new Vector2(x, y), Const.WATER_CENTER);
                        if (dist < Const.WATER_RADIUS)
                        {
                            Landscape landscape = esm.GetLandscape(new Int2(x, y), lookup);

                            if (landscape == null || landscape.hasWater)
                            {
                                /* Offset */
                                Vector3 posOffset = new Vector3(x, 0f, y) * Const.CELL_SIZE;
                                float startX = -half + posOffset.X;
                                float endX = half + posOffset.X;
                                float startY = -half + posOffset.Z;
                                float endY = half + posOffset.Z;

                                //if (dist > Const.WATER_RADIUS * .75f) // old way of doing it, going off grid now
                                if (!grid.ContainsKey(new Int2(x,y)))
                                {
                                    /* Get edge vert counts (these are actually face counts so just +1 to get vert counts) */
                                    int pX = grid.ContainsKey(new Int2(x + 1, y)) ? grid[new Int2(x + 1, y)] : 1;
                                    int pY = grid.ContainsKey(new Int2(x, y + 1)) ? grid[new Int2(x, y + 1)] : 1;
                                    int nX = grid.ContainsKey(new Int2(x - 1, y)) ? grid[new Int2(x - 1, y)] : 1;
                                    int nY = grid.ContainsKey(new Int2(x, y - 1)) ? grid[new Int2(x, y - 1)] : 1;

                                    /* Easy mode */
                                    if (pX == 1 && pY == 1 && nX == 1 && nY == 1)
                                    {
                                        // make square lol
                                        Vector3[] quad = new Vector3[]
                                        {
                                                    new Vector3(startX, 0, startY),
                                                    new Vector3(endX, 0, startY),
                                                    new Vector3(endX, 0, endY),
                                                    new Vector3(startX, 0, endY)
                                        };

                                        WetFace A = new WetFace(quad[(2+flip)%4], quad[(1+flip)%4], quad[(0+flip)%4]);
                                        WetFace B = new WetFace(quad[(0+flip)%4], quad[(3+flip)%4], quad[(2+flip)%4]);
                                        faces.Add(A); faces.Add(B);
                                        flip = flip == 0 ? 1 : 0;
                                    }
                                    /* Hard mode */
                                    else
                                    {
                                        /* create an outline via the the grid edges and then fill with tris */

                                        /* Make an outline */
                                        List<WetEdge> outline = new();

                                        /* Negative X side */
                                        {
                                            float size = Const.CELL_SIZE / nX;
                                            Vector3 start = new Vector3(startX, 0, startY);
                                            for (int i = 0; i < nX; i++)
                                            {
                                                outline.Add(new WetEdge(start + new Vector3(0, 0, i * size), start + new Vector3(0, 0, (i + 1) * size)));
                                            }
                                        }
                                        /* Negative Y side */
                                        {
                                            float size = Const.CELL_SIZE / nY;
                                            Vector3 start = new Vector3(startX, 0, startY);
                                            for (int i = 0; i < nY; i++)
                                            {
                                                outline.Add(new WetEdge(start + new Vector3(i * size, 0, 0), start + new Vector3((i + 1) * size, 0, 0)));
                                            }
                                        }
                                        /* Positive X side */
                                        {
                                            float size = Const.CELL_SIZE / pX;
                                            Vector3 start = new Vector3(endX, 0, startY);
                                            for (int i = 0; i < pX; i++)
                                            {
                                                outline.Add(new WetEdge(start + new Vector3(0, 0, i * size), start + new Vector3(0, 0, (i + 1) * size)));
                                            }
                                        }
                                        /* Positive Y side */
                                        {
                                            float size = Const.CELL_SIZE / pY;
                                            Vector3 start = new Vector3(startX, 0, endY);
                                            for (int i = 0; i < pY; i++)
                                            {
                                                outline.Add(new WetEdge(start + new Vector3(i * size, 0, 0), start + new Vector3((i + 1) * size, 0, 0)));
                                            }
                                        }
                                        //AddDebugOutline(outline);

                                        /* Fill triangles */
                                        List<WetFace> newFaces = new();
                                        List<WetEdge> innerEdges = new(); // edges added by new faces, used to prevent overlapping triangles
                                        List<Vector3> points = new();
                                        // edge direction is not consistent with my meshes so im going to add both points and hyper weld and align
                                        foreach (WetEdge edge in outline) { points.Add(edge.a); points.Add(edge.b); }
                                        for(int i=0;i<points.Count();i++)
                                        {
                                            Vector3 a = points[i];
                                            for(int j=0;j<points.Count();j++)
                                            {
                                                if (i == j) { continue; } // dont suicide

                                                Vector3 b = points[j];
                                                if (a.TolerantEquals(b))
                                                {
                                                    points.RemoveAt(j--); // kill with laser beam
                                                    continue;
                                                }

                                                // check and enforce alignment, i fucking hate floats
                                                if(Math.Abs(a.X - b.X) < 0.001f)
                                                {
                                                    a.X = b.X; // force alignment
                                                }
                                                if (Math.Abs(a.Y - b.Y) < 0.001f)
                                                {
                                                    a.Y = b.Y; // force alignment
                                                }
                                            }
                                        }

                                        List<Vector3> FindNearest(Vector3 p, List<Vector3> ps)
                                        {
                                            if (ps == null) return new List<Vector3>();
                                            int n = ps.Count;
                                            if (n <= 1) return new List<Vector3>(ps);

                                            Vector3[] arr = ps.ToArray();          // copy to array (fast)
                                            float[] keys = new float[n];          // distance keys (squared)
                                            for (int i = 0; i < n; i++)
                                                keys[i] = (arr[i] - p).SqrMagnitude();

                                            Array.Sort(keys, arr);                // sorts keys and reorders arr accordingly
                                            return arr.ToList();        // return sorted list (closest first)
                                        }

                                        for (int i= 0;i<points.Count();i++)
                                        {
                                            Vector3 a = points[i];

                                            List<Vector3> nearestB = FindNearest(a, points);

                                            for(int j=0;j< nearestB.Count();j++)
                                            {
                                                Vector3 b = nearestB[j];

                                                if (a == b) { continue; } // self succ is bad

                                                /* Sort points by nearest */
                                                Vector3 center = (a + b) * 0.5f;
                                                List<Vector3> nearestC = FindNearest(center, points); // never ask me about this bugfix, i will cut you

                                                /* Attempt to make a face starting with nearest point. check if they are valid then discard or add and continue */
                                                foreach (Vector3 c in nearestC)
                                                {
                                                    if (c == a || c == b) { continue; } // dont self succ

                                                    WetFace nf = new WetFace(a, b, c);

                                                    /* Verify not an already existing face */
                                                    foreach (WetFace newFace in newFaces)
                                                    {
                                                        if (newFace == nf) { nf = null; break; }
                                                    }
                                                    if (nf == null) { continue; }

                                                    /* Check degenerate */
                                                    if (nf.IsDegenerate()) { continue; }

                                                    /* Check if it intersects with any inner edges */
                                                    if (nf.IsIntersect(innerEdges)) { continue; }

                                                    /* Check if the face is skipping over a vertex on the same edge and effectively encapsulating a smaller face */
                                                    foreach (Vector3 z in points)
                                                    {
                                                        if (z == a || z == b || z == c) { continue; } // make sure this point isnt a part of the new tri
                                                        if (nf.IsInside(z, true)) // see if this point is inside or aligned on the edge of the new tri
                                                        {
                                                            nf = null; break; // if it is then discard because this tri will create an open edge
                                                        }

                                                    }
                                                    if (nf == null) { continue; }

                                                    /* Last check if the dumbfuck edge case "shrink check" */
                                                    /* Deals with the rare but annoying as shit case where we get a triangle with an open edge containing 2 more faces */
                                                    /* I assume this happens because of imprecision causing the paralell instersecting edges to return no intersection */
                                                    /* so we shrink the new tri and retest and that will return true if its being dumb as fuck */
                                                    /* Since we sort points by nearest the smaller tris should exist before the bigger one trys to get added, which amkes this work in theory */
                                                    if (nf.Scale(.9f).IsIntersect(innerEdges)) { continue; }

                                                    /* Cool beans, add it and continue */
                                                    newFaces.Add(nf); innerEdges.AddRange(nf.Edges());
                                                }
                                            }
                                        }
                                        faces.AddRange(newFaces);
                                    }
                                    grid.Add(new Int2(x, y), 1);
                                }
                            }
                        }
                    }
                    flip = flip == 0 ? 1 : 0;
                }
                Lort.TaskIterate();

                /* Circleify those squares */
                /* We have a minecraft circle of squares here, add a ring of verts and fill with triangles to resolve that */
                if (!Const.DEBUG_SKIP_NICE_WATER_CIRCLIFICATION) {
                    /* Make a circle outline */
                    List<WetEdge> outline = new();
                    List<Vector3> outpoints = new();
                    for (int i = 0; i < Const.WATER_RADIUS; i++)
                    {
                        float angle = (float)(i * Math.PI * 2f / Const.WATER_RADIUS);
                        float x = (float)Math.Cos(angle);
                        float y = (float)Math.Sin(angle);
                        Vector3 vert = Vector3.Normalize(new Vector3(x, 0f, y)) * (Const.WATER_RADIUS + 2) * Const.CELL_SIZE;  // +2 offset to give some distance between end of squares grid and circle triangulation
                        outpoints.Add(vert + (new Vector3(Const.WATER_CENTER.X, 0f, Const.WATER_CENTER.Y) * Const.CELL_SIZE));
                    }
                    for (int i = 0; i < outpoints.Count(); i++)
                    {
                        WetEdge edge = new WetEdge(outpoints[i], outpoints[(i + 1) % outpoints.Count()]);
                        outline.Add(edge);
                    }
                    AddDebugOutline(outline);

                    /* Make an inner outline of the squarey mesh, basically an open edges selection */
                    /* actually doing that is annoying though so uhhh cheating! */
                    List<WetEdge> openEdges = new();
                    for (int y = -(Const.WATER_RADIUS + (int)Math.Abs(Const.WATER_CENTER.Y)); y < (Const.WATER_RADIUS + (int)Math.Abs(Const.WATER_CENTER.Y)); y++)
                    {
                        for (int x = -(Const.WATER_RADIUS + (int)Math.Abs(Const.WATER_CENTER.X)); x < (Const.WATER_RADIUS + (int)Math.Abs(Const.WATER_CENTER.X)); x++)
                        {
                            Vector3 posOffset = new Vector3(x, 0f, y) * Const.CELL_SIZE;
                            float nX = -half + posOffset.X;
                            float pX = half + posOffset.X;
                            float nY = -half + posOffset.Z;
                            float pY = half + posOffset.Z;

                            // This square exists
                            if (grid.ContainsKey(new Int2(x, y)))
                            {
                                // Positive X open edge
                                if (!grid.ContainsKey(new Int2(x + 1, y)))
                                {
                                    WetEdge openEdge = new WetEdge(new Vector3(pX, 0f, nY), new Vector3(pX, 0f, pY));
                                    openEdges.Add(openEdge);
                                }

                                // Negative X open edge
                                if (!grid.ContainsKey(new Int2(x - 1, y)))
                                {
                                    WetEdge openEdge = new WetEdge(new Vector3(nX, 0f, nY), new Vector3(nX, 0f, pY));
                                    openEdges.Add(openEdge);
                                }

                                // Positive Y open edge
                                if (!grid.ContainsKey(new Int2(x, y + 1)))
                                {
                                    WetEdge openEdge = new WetEdge(new Vector3(nX, 0f, pY), new Vector3(pX, 0f, pY));
                                    openEdges.Add(openEdge);
                                }

                                // Negative Y open edge
                                if (!grid.ContainsKey(new Int2(x, y - 1)))
                                {
                                    WetEdge openEdge = new WetEdge(new Vector3(nX, 0f, nY), new Vector3(pX, 0f, nY));
                                    openEdges.Add(openEdge);
                                }
                            }
                        }
                    }
                    AddDebugOutline(openEdges);

                    /* Got our openEdges and our circle outline, now we just need to triangulate */
                    /* make point list and weld */
                    /* update to this, welding the edges first then adding the points and welding again. i fucking HATE floats */
                    List<Vector3> points = new();

                    void EdgeWeld(List<WetEdge> edges)
                    {
                        foreach(WetEdge A in edges)
                        {
                            foreach(WetEdge B in edges)
                            {
                                if (A.a.TolerantEquals(B.a)) { A.a = B.a; }
                                if (A.a.TolerantEquals(B.b)) { A.a = B.b; }
                                if (A.b.TolerantEquals(B.a)) { A.b = B.a; }
                                if (A.b.TolerantEquals(B.b)) { A.b = B.b; }
                            }
                        }
                    }

                    EdgeWeld(openEdges);
                    EdgeWeld(outline);

                    foreach (WetEdge edge in openEdges)
                    {
                        points.Add(edge.a);
                        points.Add(edge.b);
                    }
                    foreach (WetEdge edge in outline)
                    {
                        points.Add(edge.a);
                        points.Add(edge.b);
                    }
                    for (int i = 0; i < points.Count(); i++)
                    {
                        Vector3 a = points[i];
                        for (int j = 0; j < points.Count(); j++)
                        {
                            if (i == j) { continue; } // dont suicide

                            Vector3 b = points[j];
                            if (a.TolerantEquals(b))
                            {
                                points.RemoveAt(j--); // kill with laser beam
                                continue;
                            }

                            /*// check and enforce alignment, i fucking hate floats
                            if (Math.Abs(a.X - b.X) < 0.01f)
                            {
                                a.X = b.X; // force alignment
                            }
                            if (Math.Abs(a.Y - b.Y) < 0.01f)
                            {
                                a.Y = b.Y; // force alignment
                            }*/
                        }
                    }

                    /* add an edge from every open edge to center point. prevents faces from being created on the inside */
                    List<WetFace> newFaces = new();
                    List<WetEdge> newEdges = new();
                    newEdges.AddRange(openEdges); //
                    foreach (WetEdge edge in openEdges)
                    {
                        WetEdge jankedgea = new WetEdge(edge.a, new Vector3(Const.WATER_CENTER.X, 0f, Const.WATER_CENTER.Y) * Const.CELL_SIZE);
                        WetEdge jankedgeb = new WetEdge(edge.b, new Vector3(Const.WATER_CENTER.X, 0f, Const.WATER_CENTER.Y) * Const.CELL_SIZE);
                        newEdges.Add(jankedgea);
                        newEdges.Add(jankedgeb);
                    }

                    var wetEdgePolygon = new Polygon();
                    
                    foreach(WetEdge edge in openEdges)
                    {
                        wetEdgePolygon.Add(new Vertex(edge.a.X, edge.a.Z));
                        wetEdgePolygon.Add(new Vertex(edge.b.X, edge.b.Z));
                    }
                    
                    var countourList = new List<Vertex>();
                    
                    for(int i = 0; i < outpoints.Count; i++)
                    {
                        countourList.Add(new Vertex(outpoints[i].X, outpoints[i].Z));
                    }
                    
                    wetEdgePolygon.Add(new Contour(countourList));
                    
                    var mesh2d = wetEdgePolygon.Triangulate();
                    
                    foreach(var triangle in mesh2d.Triangles)
                    {
                        Vector3 a = new Vector3((float)triangle.GetVertex(0).X, 0f, (float)triangle.GetVertex(0).Y);
                        Vector3 b = new Vector3((float)triangle.GetVertex(1).X, 0f, (float)triangle.GetVertex(1).Y);
                        Vector3 c = new Vector3((float)triangle.GetVertex(2).X, 0f, (float)triangle.GetVertex(2).Y); 
                        faces.Add(new WetFace(a,b,c));
                    }

                }

                SubtractCutouts(cutouts);  // the big boom boom function
                Lort.TaskIterate();

                Cleanup();                  // does a few minor housekeeping things at the end of mesh gen
                Lort.TaskIterate();

                // Done! woohoo! that took like 4 days to code
            }

            /* This constructor makes a water collision mesh for a single cell. This is use for water splash collision plane. Takes a single cell as a param */
            public WetMesh(Cell cell, List<Cutout> cutouts)
            {
                // When using debug consts to build specific sections of the map for debug, its possible the cell at 0,0 isn't loaded. this is a quick check for that
                Vector3 posOffset = cell != null ? new Vector3(cell.coordinate.x, 0f, cell.coordinate.y) * Const.CELL_SIZE : new(0, 0, 0);

                faces = new();
                outlines = new();

                float half = Const.CELL_SIZE * .5f;
                float startX = -half + posOffset.X;
                float endX = half + posOffset.X;
                float startY = -half + posOffset.Z;
                float endY = half + posOffset.Z;

                // make square lol
                Vector3[] quad = new Vector3[]
                {
                            new Vector3(startX, 0, startY),
                            new Vector3(endX, 0, startY),
                            new Vector3(endX, 0, endY),
                            new Vector3(startX, 0, endY)
                };

                WetFace A = new WetFace(quad[2], quad[1], quad[0]);
                WetFace B = new WetFace(quad[0], quad[3], quad[2]);

                faces.Add(A);
                faces.Add(B);

                SubtractCutouts(cutouts);  // walk the dinosaur

                /* collisions will be in indvidual small tile msbs so undo the offset to the worldspace coords */
                List<WetFace> oldFaces = faces;
                faces = new();
                foreach(WetFace face in oldFaces)
                {
                    WetFace nf = new(face.a - posOffset, face.b - posOffset, face.c - posOffset);
                    faces.Add(nf);
                }

                Cleanup(); // that's a wrap!
            }

            /* This constructor makes a quad mesh of cutouts via brute force. Used to create visual meshes for lava and swamps */
            public WetMesh(List<Cutout> cutouts, Cutout.Type cutoutType)
            {
                /* Calculate a bounding box of lava cutouts */
                Vector3 offsetJank = new Vector3(Const.CELL_SIZE * .5f, 0, Const.CELL_SIZE * .5f); // this is just one of those things where this offset is correct but i'd be hard pressed to tell you why
                float BOUND = Const.CELL_EXTERIOR_BOUNDS * Const.CELL_SIZE;
                Vector2 min = new Vector2(BOUND); Vector2 max = new Vector2(-BOUND);
                foreach (Cutout cutout in cutouts)
                {
                    foreach (Vector3 point in cutout.Points())
                    {
                        min.X = Math.Min(point.X, min.X);
                        min.Y = Math.Min(point.Z, min.Y);
                        max.X = Math.Max(point.X, max.X);
                        max.Y = Math.Max(point.Z, max.Y);
                    }
                }
                min -= new Vector2(offsetJank.X, offsetJank.Z);  // in theory we shouldnt have to do this but guh!
                max += new Vector2(offsetJank.X, offsetJank.Z);

                /* Generate a quad mesh anywhere there is lava cutouts. We can't use the MW data of lava because it's got open edges everywhere. Elden Ring lava needs a clean quad mesh to work with */
                faces = new(); outlines = new();

                for (float y = min.Y; y < max.Y; y += Const.LIQUID_QUAD_GENERATE_SIZE)
                {
                    for (float x = min.X; x < max.X; x += Const.LIQUID_QUAD_GENERATE_SIZE)
                    {
                        Cutout sq = new(cutoutType, new Vector3(x, 0f, y) - offsetJank, Vector3.Zero, Const.LIQUID_QUAD_GENERATE_SIZE);

                        foreach (Cutout cutout in cutouts)
                        {
                            if (cutout.GetType() == typeof(ShapedCutout)) { continue; } // skip shaped cutouts for now
                            Vector3 h = new Vector3(0, cutout.height, 0);

                            if (cutout.IsIntersect(sq))
                            {
                                foreach (WetFace face in sq.Faces())
                                {
                                    Vector3 a = face.a + h;
                                    Vector3 b = face.b + h;
                                    Vector3 c = face.c + h;
                                    faces.Add(new WetFace(a, b, c));
                                }
                                break;
                            }
                        }
                    }
                }

                // do shaped cutouts now
                foreach (Cutout cutout in cutouts)
                {
                    if (cutout.GetType() != typeof(ShapedCutout)) { continue; }
                    ShapedCutout shaped = (ShapedCutout)cutout;
                    Vector3 h = new Vector3(0, cutout.height, 0);

                    foreach (WetFace face in shaped.mesh)
                    {
                        List<Vector3> transformedPoints = new();
                        foreach (Vector3 point in face.Points())
                        {
                            transformedPoints.Add(point + shaped.position + h); // need to also do rotation as well guh!
                        }

                        faces.Add(new WetFace(transformedPoints[0], transformedPoints[1], transformedPoints[2]));
                    }
                }
            }

            private void SubtractCutouts(List<Cutout> cutouts)
            {
                float CutoutMaxDist(Cutout cutout, WetFace face)
                {
                    Vector3 center = (face.a + face.b + face.c) / 3f;
                    return Vector3.Distance(new Vector3(cutout.position.X, 0f, cutout.position.Z), center);
                }
                
                /* Begin slicing cutouts... god help me */

                /* We check every triangle in the mesh for intersection with a cutout */
                /* Three possible cases 1) edge intersection, 2) cutout fully inside triangle, 3) triangle fullyinside cutout */

                /* Case #1, cutout fully inside triangle */
                for (int i = 0; i < faces.Count(); i++)
                {
                    WetFace face = faces[i];

                    foreach (Cutout cutout in cutouts)
                    {
                        float maxReach = (float)(cutout.size * 1.5);
                        
                        if(CutoutMaxDist(cutout, face) > maxReach) continue;
                        
                        /* Check */
                        bool inside = true;
                        foreach (Vector3 point in cutout.Points())
                        {
                            if (!face.IsInside(point, false)) { inside = false; break; }
                        }
                        /* Perform slice */
                        if (inside)
                        {
                            /* New triangles */
                            List<WetFace> newFaces = new();

                            /* Collect all points in edge order, using modulo we can safely assume that i+1 is the next edge */
                            List<Vector3> outer = face.Points();
                            List<Vector3> inner = cutout.Points();

                            /* List of all edges for raycasting tests */
                            List<WetEdge> edges = face.Edges();
                            edges.AddRange(cutout.Edges());

                            /* Do a raycast from each outer point to each inner point, collect values to create tris */
                            for (int ii = 0; ii < outer.Count(); ii++)
                            {
                                /* Attempt to create valid triangles */
                                Vector3 op = outer[ii];                           // outer point
                                Vector3 opn = outer[(ii + 1) % outer.Count()];    // next outer point
                                for (int jj = 0; jj < inner.Count(); jj++)
                                {
                                    Vector3 ip = inner[jj];                           // inner point
                                    Vector3 ipn = inner[(jj + 1) % inner.Count()];    // next inner point

                                    /* Triangle Attempts */
                                    WetFace nf1 = new(op, ip, opn);
                                    WetFace nf2 = new(op, ip, ipn);

                                    /* Check if they are valid, then add them if they are */
                                    if (!nf1.IsIntersect(edges) && !nf1.IsDegenerate()) { newFaces.Add(nf1); edges.AddRange(nf1.Edges()); }
                                    if (!nf2.IsIntersect(edges) && !nf2.IsDegenerate()) { newFaces.Add(nf2); edges.AddRange(nf2.Edges()); }
                                }
                            }

                            /* Delete the original triangle, and add the new ones to the mesh */
                            //cutouts.Remove(cutout); // fully handled, not needed anymore! we are also breaking so no issue with foreach enum
                            faces.RemoveAt(i--);
                            faces.AddRange(newFaces);
                            break; // we cant do multiple cutouts at the same time so break and we we will loop back through 
                        }
                    }
                }

                /* Case #2, one or more points of a triangle inside cutout */
                List<WetFace> defferedFaces = new();
                for (int i = 0; i < faces.Count(); i++)
                {
                    WetFace face = faces[i];

                    foreach (Cutout cutout in cutouts)
                    {
                        float maxReach = (float)(cutout.size * 1.5);
                        
                        if(CutoutMaxDist(cutout, face) > maxReach) continue;
                       
                        /* Check */
                        if (cutout.IsInside(face.a, false) || cutout.IsInside(face.b, false) || cutout.IsInside(face.c, false))
                        {
                            /* Go edge by edge finding intersections */
                            /* If we find an intersection we keep anything still inside the triangle, discard the rest */
                            /* if a point of the triangle is inside the cutout it gets discarded */
                            /* Ordering points and edges here may be impossible to manage so just do your best and let the triangulation be whatever it ends up being */

                            List<WetEdge> outline = face.Edges(); // edges of tri

                            /* First deal with the situation where the cutout removes a vert of the triangle */
                            for (int ii = 0; ii < outline.Count(); ii++)
                            {
                                WetEdge edge = outline[ii];

                                /* Both points inside */
                                if (cutout.IsInside(edge.a, true) && cutout.IsInside(edge.b, true))
                                {
                                    // additional note, if all 3 points end up inside the cutout this will fully delete the outline and resulting face
                                    outline.RemoveAt(ii--); continue;  // remove edge and continue, fully removed edges dont need any actual splicing
                                }

                                /* One point is inside */
                                else if (cutout.IsInside(edge.a, false) || cutout.IsInside(edge.b, false))
                                {
                                    WetEdge spl = cutout.IsInside(edge.a, false) ? edge.Reverse() : edge;  // flip edge if a is inside, makes things simpler

                                    /* Find nearest intersection for this edge */
                                    Vector3 nearest = Vector3.NaN; // nearest intersected edge point
                                    foreach (WetEdge cutedge in cutout.Edges())
                                    {
                                        Vector3 intersection = spl.Intersection(cutedge, true);
                                        if (intersection.IsNaN()) { continue; } // no intersection, skip

                                        if (nearest.IsNaN() || (Vector3.Distance(nearest, spl.a) > Vector3.Distance(intersection, spl.a)))
                                        {
                                            nearest = intersection;
                                        }
                                    }

                                    if (nearest.IsNaN())
                                    {
                                        // something went wrong and we were unable to find an intersection 
                                        // this seems to be a rare case
                                        outline.RemoveAt(ii--);
                                        continue;
                                    }

                                    /* Create new edge from nearest intersection to replace one with engulfed point */
                                    WetEdge replaceEdge = new WetEdge(spl.a, nearest);

                                    if (replaceEdge == edge) { continue; }  // avoid infinite slicing to the same edge

                                    outline.RemoveAt(ii--);
                                    outline.Add(replaceEdge);
                                }
                            }

                            /* Check if triangle is still sealed, (it's probably a polygon now but uhhh yeah just go ahead. if it's not sealed then seal it */
                            Dictionary<Vector3, int> edgePointCount = new();
                            foreach (WetEdge edge in outline)
                            {
                                if (edgePointCount.ContainsKey(edge.a)) { edgePointCount[edge.a]++; }
                                else { edgePointCount.Add(edge.a, 1); }
                                if (edgePointCount.ContainsKey(edge.b)) { edgePointCount[edge.b]++; }
                                else { edgePointCount.Add(edge.b, 1); }
                            }
                            List<Vector3> openPoints = new();
                            foreach (KeyValuePair<Vector3, int> kvp in edgePointCount)
                            {
                                if (kvp.Value == 1) { openPoints.Add(kvp.Key); }
                            }
                            for (int ii = 0; ii < openPoints.Count - 1; ii += 2)   // this method of sealing is probably fine but could have trouble in some edge cases. potential bugs!
                            {
                                WetEdge sealEdge = new WetEdge(openPoints[ii], openPoints[ii + 1]);
                                outline.Add(sealEdge); // arf arf
                            }

                            /* Now that we are finally done creating the edge outline, lets fill it in with triangles */
                            /* Do a raycast from each edge to every other point and fill in with valid triangles */
                            List<WetFace> newFaces = new();
                            List<WetEdge> edges = new();
                            edges.AddRange(outline);
                            for (int ii = 0; ii < outline.Count(); ii++)
                            {
                                /* Attempt to create valid triangles */
                                WetEdge baseEdge = outline[ii];
                                for (int jj = 0; jj < outline.Count(); jj++)
                                {
                                    if (ii == jj) { continue; } // dont self succ
                                    WetEdge connectingEdge = outline[jj];

                                    /* Triangle Attempts */
                                    WetFace nf1 = new(baseEdge.a, connectingEdge.a, baseEdge.b);
                                    WetFace nf2 = new(baseEdge.a, connectingEdge.b, baseEdge.b);

                                    /* See if this triangle already exists */
                                    foreach (WetFace newFace in newFaces)
                                    {
                                        if (nf1 != null && newFace == nf1) { nf1 = null; }
                                        if (nf2 != null && newFace == nf2) { nf2 = null; }
                                    }

                                    /* Check if they are valid, then add them if they are */
                                    if (nf1 != null && !nf1.IsIntersect(edges) && !nf1.IsDegenerate()) { newFaces.Add(nf1); edges.AddRange(nf1.Edges()); }
                                    else if (nf2 != null && !nf2.IsIntersect(edges) && !nf2.IsDegenerate()) { newFaces.Add(nf2); edges.AddRange(nf2.Edges()); }
                                }
                            }

                            /* Due to imprecision/edge inclusion issues it's possible we slice a face and end up with the exact same face lol */
                            /* Due to this lets just compare the generated face to the og face and if its the same lol lmao discard */
                            if (newFaces.Count() == 1)
                            {
                                if (newFaces[0].TolerantEquals(face))
                                {
                                    continue;
                                }
                            }

                            /* Delete the original triangle, and add the new ones to the mesh */
                            faces.RemoveAt(i--);
                            defferedFaces.AddRange(newFaces);
                            break; // we cant do multiple cutouts at the same time so break and we we will loop back through 
                        }
                    }
                }

                faces.AddRange(defferedFaces); // cum

                /* Case #3, cutout edge intersects a triangle edge, no points of the triangle are inside the cutout though */
                /* As an idea to fix infinite recursive slicing lets uhhh make it a single loop through each cutout, adding new faces at the end instead of in loop */
                foreach (Cutout cutout in cutouts)
                {
                    float maxReach = (float)(cutout.size * 1.5);
                    List<WetFace> newFaces = new();
                 
                    for (int i = 0; i < faces.Count(); i++)
                    {
                        WetFace face = faces[i];
                        Vector3 faceCenter = (face.a + face.b + face.c) / 3f;
                        if (Vector3.Distance(new Vector3(cutout.position.X, 0f, cutout.position.Z), faceCenter) > maxReach) { continue; }
                        
                        List<WetEdge> outline = face.Edges(); // edges of tri

                        if (face.IsIntersect(cutout.Edges()))
                        {
                            /* Next we loop back through edges and look for intersections between the tri edge and cutout. cutting the triangle edges to the intersection */
                            for (int ii = 0; ii < outline.Count(); ii++)
                            {
                                WetEdge edge = outline[ii];

                                /* Find nearest intersection for both the a and b point of the edge, farthest from a is nearest to b btw */
                                Vector3 nearest = Vector3.NaN;
                                Vector3 farthest = Vector3.NaN;
                                foreach (WetEdge cut in cutout.Edges())
                                {
                                    Vector3 intersection = edge.Intersection(cut, false);
                                    if (!intersection.IsNaN())
                                    {
                                        if (nearest.IsNaN() || (Vector3.Distance(nearest, edge.a) > Vector3.Distance(intersection, edge.a)))
                                        {
                                            nearest = intersection;
                                        }
                                        if (farthest.IsNaN() || (Vector3.Distance(farthest, edge.a) < Vector3.Distance(intersection, edge.a)))
                                        {
                                            farthest = intersection;
                                        }
                                    }
                                }

                                // so common sense says if either nearest or farthest is nan then both are but due to imprecision and overlapping edges its feasible for that to be untrue
                                // so we will check both individually and add them
                                if (!nearest.IsNaN())
                                {
                                    WetEdge replaceEdge = new WetEdge(edge.a, nearest);
                                    outline.Add(replaceEdge);
                                }
                                if (!farthest.IsNaN())
                                {
                                    WetEdge reverseEdge = new WetEdge(farthest, edge.b);
                                    outline.Add(reverseEdge);
                                }

                                // at least one of them was legit, if both we dont remove the edge
                                if (!(nearest.IsNaN() && farthest.IsNaN()))
                                {
                                    outline.RemoveAt(ii--);
                                }
                            }
                        }
                        /* Next we intersect the cutout edges that are inside the triangle and add them to the outline using the original triangle edges. anything entirely outside the tri is discarded */
                        /* Im going to skip accounting for a specific edge case where the cutout intersects through the entire tri without encompassing any points of it. lazy! */
                        if (
                           face.IsIntersect(cutout.Edges()) ||
                           face.IsInside(cutout.Points()[0], true) ||
                           face.IsInside(cutout.Points()[1], true) ||
                           face.IsInside(cutout.Points()[2], true) ||
                           face.IsInside(cutout.Points()[3], true)
                           )
                        {
                            foreach (WetEdge cut in cutout.Edges())
                            {
                                foreach (WetEdge edge in face.Edges())
                                {
                                    // if edge is entirly inside triangle add it
                                    if (face.IsInside(cut.a, true) && face.IsInside(cut.b, true))
                                    {
                                        outline.Add(cut);
                                        continue;
                                    }

                                    // if edge intersects triangle add the part thats inside the triangle
                                    Vector3 intersection = cut.Intersection(edge, true);
                                    if (!intersection.IsNaN())
                                    {
                                        // point a is inside; segment
                                        if (face.IsInside(cut.a, true))
                                        {
                                            WetEdge newEdge = new(intersection, cut.a);
                                            outline.Add(newEdge);
                                        }
                                        // point b is inside; segment
                                        else if (face.IsInside(cut.b, true))
                                        {
                                            WetEdge newEdge = new(cut.b, intersection);
                                            outline.Add(newEdge);
                                        }
                                        // neither point inside; bisection
                                        else
                                        {
                                            // assuming since neither point inside, we are bisecting the tri entirely. 
                                            Vector3 bisectionPoint = Vector3.NaN;
                                            foreach (WetEdge e in face.Edges())
                                            {
                                                if (edge == e) { continue; }  // looking for the other edge we hit
                                                bisectionPoint = cut.Intersection(e, false);
                                                if (!bisectionPoint.IsNaN()) { break; } // 99% sure i dont need to test beyond the first positive result
                                            }
                                            if (bisectionPoint.IsNaN())
                                            {
                                                continue; // rare case, we just discard it because bad
                                            }
                                            WetEdge newEdge = new(intersection, bisectionPoint);
                                            outline.Add(newEdge);
                                        }
                                    }
                                }
                            }

                            /* Check if triangle is still sealed, (it's probably a polygon now but uhhh yeah just go ahead. if it's not sealed then seal it */
                            /* attempt to weld points and fix issues caused by imprecision of intersection results */
                            foreach (WetEdge A in outline)
                            {
                                // attempt to weld each pair of points based on which point appears to be less mangled by imprecision
                                foreach (WetEdge B in outline)
                                {
                                    // a to a
                                    if (Vector3.Distance(A.a, B.a) < 0.001)
                                    {
                                        if (cutout.IsInside(A.a, false)) { A.a = B.a; }
                                        else { B.a = A.a; }
                                    }
                                    // a to b
                                    if (Vector3.Distance(A.a, B.b) < 0.001)
                                    {
                                        if (cutout.IsInside(A.a, false)) { A.a = B.b; }
                                        else { B.b = A.a; }
                                    }
                                    // b to a
                                    if (Vector3.Distance(A.b, B.a) < 0.001)
                                    {
                                        if (cutout.IsInside(A.b, false)) { A.b = B.a; }
                                        else { B.a = A.b; }
                                    }
                                    // b to b
                                    if (Vector3.Distance(A.b, B.b) < 0.001)
                                    {
                                        if (cutout.IsInside(A.b, false)) { A.b = B.b; }
                                        else { B.b = A.b; }
                                    }
                                }
                            }

                            /* find open points */
                            Dictionary<Vector3, int> edgePointCount = new();
                            foreach (WetEdge edge in outline)
                            {
                                if (edgePointCount.ContainsKey(edge.a)) { edgePointCount[edge.a]++; }
                                else { edgePointCount.Add(edge.a, 1); }
                                if (edgePointCount.ContainsKey(edge.b)) { edgePointCount[edge.b]++; }
                                else { edgePointCount.Add(edge.b, 1); }
                            }
                            List<Vector3> openPoints = new();
                            foreach (KeyValuePair<Vector3, int> kvp in edgePointCount)
                            {
                                if (kvp.Value == 1) { openPoints.Add(kvp.Key); }
                            }
                            /* seal */
                            for (int ii = 0; ii < openPoints.Count; ii++)
                            {
                                Vector3 pA = openPoints[ii];
                                Vector3 nearest = Vector3.NaN;
                                for (int jj = 0; jj < openPoints.Count; jj++)
                                {
                                    if (ii == jj) { continue; } // self succ prevention
                                    Vector3 pB = openPoints[jj];
                                    if (nearest.IsNaN() || Vector3.Distance(pA, nearest) > Vector3.Distance(pA, pB))
                                    {
                                        nearest = pB;
                                    }
                                }

                                if (nearest.IsNaN())
                                {
                                    continue; // fail to seal
                                }

                                WetEdge sealEdge = new(pA, nearest);
                                outline.Add(sealEdge);
                                openPoints.Remove(pA);
                                openPoints.Remove(nearest);
                                ii--;
                            }

                            List<List<WetEdge>> islands = new();
                            islands.Add(outline);

                            //AddDebugOutline(outline);

                            /* Now that we are finally done creating the edge outline, lets fill it in with triangles */
                            /* Do a raycast from each edge to every other point and fill in with valid triangles */
                            foreach (List<WetEdge> island in islands)
                            {
                                List<WetEdge> edges = new();
                                edges.AddRange(island);
                                for (int ii = 0; ii < island.Count(); ii++)
                                {
                                    /* Attempt to create valid triangles */
                                    WetEdge baseEdge = island[ii];
                                    for (int jj = 0; jj < island.Count(); jj++)
                                    {
                                        if (ii == jj) { continue; } // dont self succ
                                        WetEdge connectingEdge = island[jj];

                                        /* Triangle Attempts */
                                        WetFace nf1 = new(baseEdge.a, connectingEdge.a, baseEdge.b);
                                        WetFace nf2 = new(baseEdge.a, connectingEdge.b, baseEdge.b);

                                        /* See if this triangle already exists */
                                        foreach (WetFace newFace in newFaces)
                                        {
                                            if (nf1 != null && newFace == nf1) { nf1 = null; }
                                            if (nf2 != null && newFace == nf2) { nf2 = null; }
                                        }

                                        /* See if this newly generated triangle actually falls inside of a cutout */
                                        /* This can happen during triangulation for various reasons */
                                        bool InsideCutout(WetFace f)
                                        {
                                            foreach (Cutout c in cutouts) // @TODO: should only check the one we are looping through, this is dumb
                                            {
                                                c.size += 0.001f; // @TODO: disgusting hack
                                                if (c.IsInside(f.a, true) && c.IsInside(f.b, true) && c.IsInside(f.c, true))
                                                {
                                                    c.size -= 0.001f;
                                                    return true;
                                                }
                                                c.size -= 0.001f;
                                            }
                                            return false;
                                        }

                                        /* test the new edges of this triangle, skip outline edge */ // not used
                                        bool BaseSkipIntersectTest(WetFace f)
                                        {
                                            foreach (WetEdge cutedge in cutout.Edges())
                                            {
                                                if (!cutedge.Intersection(new WetEdge(f.a, f.b), false).IsNaN()) { return true; }
                                                if (!cutedge.Intersection(new WetEdge(f.c, f.b), false).IsNaN()) { return true; }
                                            }
                                            return false;
                                        }

                                        /* Check if they are valid, then add them if they are */
                                        cutout.size -= 0.1f;
                                        if (nf1 != null && !nf1.IsIntersect(edges) && !nf1.IsDegenerate() && !InsideCutout(nf1) && !nf1.IsIntersect(cutout.Edges())) { newFaces.Add(nf1); edges.AddRange(nf1.Edges()); }
                                        else if (nf2 != null && !nf2.IsIntersect(edges) && !nf2.IsDegenerate() && !InsideCutout(nf2) && !nf2.IsIntersect(cutout.Edges())) { newFaces.Add(nf2); edges.AddRange(nf2.Edges()); }
                                        cutout.size += 0.1f;
                                    }
                                }
                            }

                            /*if (newFaces.Count() == 1 && face.TolerantEquals(newFaces[0])) { continue; } // gore hack
                            for (int ii=0;ii<newFaces.Count();ii++)
                            {
                                WetFace nuf = newFaces[ii];
                                if (nuf.Area() < 1f) { newFaces.RemoveAt(ii--); }
                            }*/

                            /* Delete the original triangle, and add the new ones to the mesh */
                            faces.RemoveAt(i--);
                        }
                    }
                    faces.AddRange(newFaces);
                }
            }

            private void Cleanup()
            {
                /* Unify all faces */
                Vector3 up = Vector3.UnitY;
                for (int i = 0; i < faces.Count(); i++)
                {
                    WetFace face = faces[i];

                    if (Vector3.Dot(face.Normal(), up) < 0)
                    {
                        faces[i] = face.Flip();
                    }
                }

                /* Weld all verts fairly aggressively */
                /* Delete any faces that become degenerate from welding as well */
                List<Vector3> verts = new();
                Vector3 GetVert(Vector3 v)
                {
                    foreach (Vector3 vert in verts)
                    {
                        if (Vector3.Distance(v, vert) < 0.011)
                        {
                            return vert;
                        }
                    }

                    Vector3 r = new Vector3((float)Math.Round(v.X, 2), (float)Math.Round(v.Y, 2), (float)Math.Round(v.Z, 2));
                    verts.Add(r);
                    return r;
                }
                List<WetFace> oldFaces = faces;
                faces = new();
                foreach (WetFace face in oldFaces)
                {
                    Vector3 a = GetVert(face.a);
                    Vector3 b = GetVert(face.b);
                    Vector3 c = GetVert(face.c);
                    WetFace nf = new(a, b, c);

                    if (nf.IsDegenerate()) { continue; } // discard degenerates created by rounding

                    faces.Add(nf);
                }
            }

            /* Makes a collision obj with the given material type */
            public Obj ToObj(Obj.CollisionMaterial material)
            {
                Obj obj = new();
                // water mesh
                {
                    ObjG g = new();
                    g.name = material.ToString();
                    g.mtl = $"hkm_{g.name}_Safe1";

                    obj.vns.Add(new Vector3(0, 1, 0));
                    obj.vts.Add(new Vector3(0, 0, 0));

                    foreach (WetFace face in faces)
                    {
                        obj.vs.Add(face.a);
                        obj.vs.Add(face.b);
                        obj.vs.Add(face.c);

                        ObjV A = new(obj.vs.Count() - 3, 0, 0);
                        ObjV B = new(obj.vs.Count() - 2, 0, 0);
                        ObjV C = new(obj.vs.Count() - 1, 0, 0);

                        ObjF f = new(A, B, C);
                        g.fs.Add(f);
                    }
                    obj.gs.Add(g);
                }
                return obj;
            }

            /* Debug code, not actually used unless I'm working on this class. Leaving it because it's not hurting anything really */
            public Obj ToDebugObj()
            {
                Obj obj = new();
                // water mesh
                {
                    ObjG g = new();
                    g.name = "water";
                    g.mtl = g.name;

                    obj.vns.Add(new Vector3(0, 1, 0));
                    obj.vts.Add(new Vector3(0, 0, 0));

                    foreach (WetFace face in faces)
                    {
                        obj.vs.Add(face.a);
                        obj.vs.Add(face.b);
                        obj.vs.Add(face.c);

                        ObjV A = new(obj.vs.Count() - 3, 0, 0);
                        ObjV B = new(obj.vs.Count() - 2, 0, 0);
                        ObjV C = new(obj.vs.Count() - 1, 0, 0);

                        ObjF f = new(A, B, C);
                        g.fs.Add(f);
                    }
                    obj.gs.Add(g);
                }
                // cutout meshes
                {
                    ObjG g = new();
                    g.name = "cutout";
                    g.mtl = g.name;

                    obj.vns.Add(new Vector3(0, 1, 0));
                    obj.vts.Add(new Vector3(0, 0, 0));

                    Vector3 up = new(0, 5f, 0); // offset for debug

                    foreach (Cutout cutout in CUTOUTS)
                    {
                        List<WetFace> faces = cutout.Faces();
                        foreach (WetFace face in faces)
                        {
                            obj.vs.Add(face.a + up);
                            obj.vs.Add(face.b + up);
                            obj.vs.Add(face.c + up);

                            ObjV A = new(obj.vs.Count() - 3, 0, 0);
                            ObjV B = new(obj.vs.Count() - 2, 0, 0);
                            ObjV C = new(obj.vs.Count() - 1, 0, 0);

                            ObjF f = new(A, B, C);
                            g.fs.Add(f);
                        }
                    }
                    obj.gs.Add(g);
                }
                // debug outline meshes
                int i = 0;
                foreach (List<WetFace> group in outlines)
                {
                    ObjG g = new();
                    g.name = $"outline [{i++}]";
                    g.mtl = g.name;

                    obj.vns.Add(new Vector3(0, 1, 0));
                    obj.vts.Add(new Vector3(0, 0, 0));

                    foreach (WetFace face in group)
                    {
                        obj.vs.Add(face.a);
                        obj.vs.Add(face.b);
                        obj.vs.Add(face.c);

                        ObjV A = new(obj.vs.Count() - 3, 0, 0);
                        ObjV B = new(obj.vs.Count() - 2, 0, 0);
                        ObjV C = new(obj.vs.Count() - 1, 0, 0);

                        ObjF f = new(A, B, C);
                        g.fs.Add(f);
                    }
                    obj.gs.Add(g);
                }

                return obj;
            }
        }

        /* This is a last second add-in because every single cutout mesh was a square except FUCKING TWO STUPID LAVA MESHES */
        /* So basically dirty hack, just need to insert the stupid oval lava mesh in to this system */
        public class ShapedCutout : Cutout
        {
            public List<WetFace> mesh;

            public ShapedCutout(Type type, Vector3 position, Vector3 rotation, string meshPath) : base(type, position, rotation, 0)
            {
                this.mesh = new();

                AssimpContext assimpContext = new();
                Scene fbx = assimpContext.ImportFile(Path.Combine(Const.MORROWIND_PATH, $@"Data Files\meshes\{Path.ChangeExtension(meshPath, "fbx")}"));
                Mesh mesh = null;  // find first mesh that's not collision and use it
                Node node = null;

                void FBXHierarchySearch(Node fbxParentNode)
                {
                    foreach (Node fbxChildNode in fbxParentNode.Children)
                    {
                        string nodename = fbxChildNode.Name.ToLower();

                        if (nodename.Trim().ToLower() == "collision")
                        {
                            continue;
                        }
                        if (fbxChildNode.HasMeshes)
                        {
                            // Skips if there are no indicies in MeshIndices
                            foreach (int fbxMeshIndex in fbxChildNode.MeshIndices)
                            {
                                node = fbxChildNode;
                                mesh = fbx.Meshes[fbxMeshIndex];
                                return;
                            }
                        }
                        if (fbxChildNode.HasChildren)
                        {
                            FBXHierarchySearch(fbxChildNode);
                        }
                    }
                }
                FBXHierarchySearch(fbx.RootNode);

                foreach (Face face in mesh.Faces)
                {
                    List<Vector3> points = new();
                    for (int i = 0; i < 3; i++)
                    {
                        FLVER.Vertex flverVertex = new();

                        /* Grab vertice position + normals/tangents */
                        Vector3 pos = mesh.Vertices[face.Indices[i]];

                        /* Collapse transformations on positions and collapse rotations on normals/tangents */
                        Node parent = node;
                        while (parent != null)
                        {
                            Vector3 trans;
                            Quaternion rot;
                            Vector3 scale;
                            Matrix4x4.Decompose(parent.Transform, out scale, out rot, out trans);
                            trans = new Vector3(parent.Transform.M14, parent.Transform.M24, parent.Transform.M34); // Hack

                            rot = Quaternion.Inverse(rot);

                            Matrix4x4 ms = Matrix4x4.CreateScale(scale);
                            Matrix4x4 mr = Matrix4x4.CreateFromQuaternion(rot);
                            Matrix4x4 mt = Matrix4x4.CreateTranslation(trans);

                            pos = Vector3.Transform(pos, ms * mr * mt);

                            parent = parent.Parent;
                        }

                        // Fromsoftware lives in the mirror dimension. I do not know why.
                        pos = pos * Const.GLOBAL_SCALE;
                        pos.X *= -1f;

                        /* Rotate Y 180 degrees because... */
                        Matrix4x4 rotateY180Matrix = Matrix4x4.CreateRotationY((float)Math.PI);
                        pos = Vector3.Transform(pos, rotateY180Matrix);

                        points.Add(pos);
                    }

                    WetFace f = new(points[0], points[1], points[2]);
                    this.mesh.Add(f);
                }

                assimpContext.Dispose();

                /* Calculate size so inherited methods from cutout still mostly work */
                float max = 0;
                foreach (WetFace face in this.mesh)
                {
                    foreach(Vector3 point in face.Points())
                    {
                        float dist = Vector3.Distance(Vector3.Zero, point); //lol magnitude()
                        if (dist > max) { max = dist; }
                    }
                }
                this.size = max;
            }
        }

        public class Cutout
        {
            public enum Type
            {
                Swamp, Lava, Both // both should not ever be set in the constructor. only used as a flag when sorting stuff in GetCutout()
            };

            public readonly Type type;
            public readonly Vector3 position, rotation;
            public float size;
            public float height;

            public Cutout(Type type, Vector3 position, Vector3 rotation, float size)
            {
                this.type = type;
                height = position.Y; // height values do actually break *something* in the cutout slicer. so uhhh lets just seperate it for my own sanity
                this.position = position;
                this.position.Y = 0; // while it may not make a whole lot of sense... 
                this.rotation = rotation;
                this.size = size;
            }

            /* Makes a copy */
            private Cutout(Cutout B)
            {
                type = B.type;
                position = B.position;
                rotation = B.rotation;
                size = B.size;
                height = B.height;
            }

            public Cutout Copy()
            {
                return new Cutout(this);
            }

            public List<Vector3> Points()
            {
                Vector3 X = new Vector3(size * .5f, 0f, 0f);
                Vector3 Y = new Vector3(0f, 0f, size * .5f);  // i meant z lmao

                X = Vector3.Transform(X, Matrix4x4.CreateRotationY(rotation.Y * (float)(Math.PI / 180)));
                Y = Vector3.Transform(Y, Matrix4x4.CreateRotationY(rotation.Y * (float)(Math.PI / 180)));

                return new List<Vector3>()
                {
                    position+X+Y, position-X+Y, position-X-Y, position+X-Y
                };
            }

            public List<WetEdge> Edges()
            {
                Vector3 X = new Vector3(size * .5f, 0f, 0f);
                Vector3 Y = new Vector3(0f, 0f, size * .5f);  // i meant z lmao

                X = Vector3.Transform(X, Matrix4x4.CreateRotationY(rotation.Y * (float)(Math.PI / 180)));
                Y = Vector3.Transform(Y, Matrix4x4.CreateRotationY(rotation.Y * (float)(Math.PI / 180)));

                return new List<WetEdge>()
                {
                    new WetEdge(position + X + Y, position - X + Y),
                    new WetEdge(position - X + Y, position - X - Y),
                    new WetEdge(position - X - Y, position + X - Y),
                    new WetEdge(position + X - Y, position + X + Y)
                };
            }

            public List<WetFace> Faces()
            {
                Vector3 X = new Vector3(size * .5f, 0f, 0f);
                Vector3 Y = new Vector3(0f, 0f, size * .5f);

                X = Vector3.Transform(X, Matrix4x4.CreateRotationY(rotation.Y * (float)(Math.PI / 180)));
                Y = Vector3.Transform(Y, Matrix4x4.CreateRotationY(rotation.Y * (float)(Math.PI / 180)));

                Vector3[] quad = new[]
                {
                    position+X+Y, position-X+Y, position-X-Y, position+X-Y
                };

                return new List<WetFace>()
                {
                    new WetFace(quad[2], quad[1], quad[0]),
                    new WetFace(quad[0], quad[3], quad[2])
                };
            }

            // Convex shape test, code adapted from a triangle sameside point inside example
            public bool IsInside(Vector3 v, bool edgeInclusive)
            {
                if (Vector3.DistanceSquared(v, position) > size * size)
                    return false;

                if (edgeInclusive)
                {
                    foreach (Vector3 p in Points())
                    {
                        if (p == v) { return true; }
                    }
                }

                // checks if point is on same side of edge as another point
                bool SameSide(Vector3 p1, Vector3 p2, WetEdge edge)
                {
                    Vector3 v1 = edge.b - edge.a;
                    Vector3 cp1 = Vector3.Cross(v1, p1 - edge.a);
                    Vector3 cp2 = Vector3.Cross(v1, p2 - edge.a);
                    return edgeInclusive ? Vector3.Dot(cp1, cp2) >= 0 : Vector3.Dot(cp1, cp2) > 0; // edgeinclusive toggles whether we count a point inside if its literally exactly on the edge
                }

                // Iterate through all our edges and make sure the point always falls on the same side of an edge as the next edge endpoint
                List<WetEdge> edges = Edges();
                for (int i = 0; i < edges.Count; i++)
                {
                    WetEdge edge = edges[i];
                    WetEdge next = edges[(i + 1) % edges.Count];
                    if (!SameSide(v, next.b, edge)) { return false; }
                }

                return true;
            }

            // Checks if 2 cutouts intersect, doing this via point tests on both. this misses an edge case where 2 recatangles intersect but dont have any points within eachother but that's not gonna happen here
            public bool IsIntersect(Cutout B)
            {
                // Optimizatino for speed, if center points are further away than 2x the the combined size of both we return false before doing any further testing
                if (Vector3.Distance(position, B.position) > (size + B.size * 2f)) { return false; }

                // Check if any A point is inside B
                foreach(Vector3 a in Points())
                {
                    if(B.IsInside(a, true)) { return true; }
                }

                // Check if any B point is inside A
                foreach (Vector3 b in B.Points())
                {
                    if (IsInside(b, true)) { return true; }
                }

                return false;
            }
        }

        public class WetFace
        {
            public readonly Vector3 a, b, c;
            public WetFace(Vector3 a, Vector3 b, Vector3 c)
            {
                this.a = a;
                this.b = b; 
                this.c = c;
            }

            public List<Vector3> Points()
            {
                return new List<Vector3>() { a, b, c };
            }

            public List<WetEdge> Edges()
            {
                return new List<WetEdge>()
                {
                    new WetEdge(a, b),
                    new WetEdge(b, c),
                    new WetEdge(c, a)
                };
            }

            /* Calculate and return normal */
            public Vector3 Normal()
            {
                Vector3 ab = b - a;
                Vector3 ac = c - a;

                return Vector3.Normalize(Vector3.Cross(ab, ac));
            }

            /* Return flipped version of this triangle */
            public WetFace Flip()
            {
                return new WetFace(c, b, a);
            }

            /* Return a scaled version of the triangle, used for a stupid edge case for intersection called the "shrink check" */
            public WetFace Scale(float scale)
            {
                Vector3 center = (a + b + c) / 3f; // center of triangle
                Vector3 na = Vector3.Lerp(center, a, scale);
                Vector3 nb = Vector3.Lerp(center, b, scale);
                Vector3 nc = Vector3.Lerp(center, c, scale);
                return new WetFace(na, nb, nc);
            }

            public bool Equals(WetFace B)
            {
                if (B == null) { return false; }
                return
                    (a == B.a && b == B.b && c == B.c) ||
                    (a == B.a && c == B.b && b == B.c) ||
                    (b == B.a && a == B.b && c == B.c) ||
                    (b == B.a && c == B.b && a == B.c) ||
                    (c == B.a && a == B.b && b == B.c) ||
                    (c == B.a && b == B.b && a == B.c);
            }

            public static bool operator ==(WetFace A, WetFace B)
            {
                if (A is null) { return B is null; }
                return A.Equals(B);
            }
            public static bool operator !=(WetFace A, WetFace B) => !(A == B);

            public override bool Equals(object A) => Equals(A as WetFace);

            public bool TolerantEquals(WetFace B)
            {
                return
                    (a.TolerantEquals(B.a) && b.TolerantEquals(B.b) && c.TolerantEquals(B.c)) ||
                    (a.TolerantEquals(B.a) && c.TolerantEquals(B.b) && b.TolerantEquals(B.c)) ||
                    (b.TolerantEquals(B.a) && a.TolerantEquals(B.b) && c.TolerantEquals(B.c)) ||
                    (b.TolerantEquals(B.a) && c.TolerantEquals(B.b) && a.TolerantEquals(B.c)) ||
                    (c.TolerantEquals(B.a) && a.TolerantEquals(B.b) && b.TolerantEquals(B.c)) ||
                    (c.TolerantEquals(B.a) && b.TolerantEquals(B.b) && a.TolerantEquals(B.c));
            }

            public float Area()
            {
                return 0.5f * Math.Abs((a.X * (b.Z - c.Z) + b.X * (c.Z - a.Z) + c.X * (a.Z - b.Z)));
            }

            /* Degenerate triangles have no surface area, just delete it. 2 smaller sides of a tri should never add up to the largest side. */
            public bool IsDegenerate()
            {
                if (a == b || a == c || b == c) { return true; } // obviously degen if a point is used more than once

                float[] sides = new[] { Vector3.Distance(a, b), Vector3.Distance(b, c), Vector3.Distance(c, a) };
                Array.Sort(sides);
                return sides[0] + sides[1] <= sides[2];
            }

            // Convex shape test, code adapted from a triangle sameside point inside example
            public bool IsInside(Vector3 v, bool edgeInclusive)
            {
                if (edgeInclusive) {
                    foreach (Vector3 p in Points())
                    {
                        if (p == v) { return true; }
                    }
                }

                // checks if point is on same side of edge as another point
                bool SameSide(Vector3 p1, Vector3 p2, WetEdge edge)
                {
                    Vector3 v1 = edge.b - edge.a;
                    Vector3 cp1 = Vector3.Cross(v1, p1 - edge.a);
                    Vector3 cp2 = Vector3.Cross(v1, p2 - edge.a);
                    return edgeInclusive ? Vector3.Dot(cp1, cp2) >= 0 : Vector3.Dot(cp1, cp2) > 0; // edgeinclusive toggles whether we count a point inside if its literally exactly on the edge
                }

                // Iterate through all our edges and make sure the point always falls on the same side of an edge as the next edge endpoint
                List<WetEdge> edges = Edges();
                for (int i = 0; i < edges.Count(); i++)
                {
                    WetEdge edge = edges[i];
                    WetEdge next = edges[(i + 1) % edges.Count];
                    if (!SameSide(v, next.b, edge)) { return false; }
                }

                return true;
            }

            /* Returns true if any edge in the list is intersecting this face, or if any edge is fully within the area of the face */
            public bool IsIntersect(List<WetEdge> B)
            {
                List<WetEdge> A = Edges();
                foreach (WetEdge a in A)
                {
                    foreach (WetEdge b in B)
                    {
                        if (!a.Intersection(b, false).IsNaN()) {
                            return true;
                        } // intersecting edge test
                        if (IsInside(b.a, false)) {
                            return true;
                        } // edge inside test. only need to test one point because if 1 point is inside, either both are or its an intersction lol.
                    }
                }

                return false;
            }
        }

        public class WetEdge
        {
            public Vector3 a, b;
            public WetEdge(Vector3 a, Vector3 b)
            {
                this.a = a;
                this.b = b;
            }

            public bool Equals(WetEdge B)
            {
                if (B == null) { return false; }
                return
                    (a == B.a && b == B.b) ||
                    (a == B.b && b == B.a);
            }

            public static bool operator ==(WetEdge A, WetEdge B)
            {
                if (A is null) { return B is null; }
                return A.Equals(B);
            }
            public static bool operator !=(WetEdge A, WetEdge B) => !(A == B);

            public override bool Equals(object A) => Equals(A as WetEdge);

            public WetEdge Reverse()
            {
                return new WetEdge(b, a);
            }

            /* Not super accurate, just using center point to center point distance calc, good enough for what i'm doing */
            public float Distance(WetEdge B)
            {
                Vector3 centerA = (a + b) / 2f;
                Vector3 centerB = (B.a + B.b) / 2f;
                return Vector3.Distance(centerA, centerB);
            }

            // mostly copied from 20xx.io util class because guh
            public Vector3 Intersection(WetEdge B, bool edgeInclusive)
            {
                // check if the end points are the intersection and discard if so! since we are working with triangles i am not considering endpoints part of an intersection as it means faces intersect themselves and neighbour faces
                if (!edgeInclusive && (a == B.a || a == B.b || b == B.a || b == B.b))
                {
                    return Vector3.NaN;
                }

                // check for intersection
                float s1_x, s1_y, s2_x, s2_y;
                float i_x, i_y;
                s1_x = b.X - a.X; s1_y = b.Z - a.Z;
                s2_x = B.b.X - B.a.X; s2_y = B.b.Z - B.a.Z;

                float s, t;
                s = (-s1_y * (a.X - B.a.X) + s1_x * (a.Z - B.a.Z)) / (-s2_x * s1_y + s1_x * s2_y);
                t = (s2_x * (a.Z - B.a.Z) - s2_y * (a.X - B.a.X)) / (-s2_x * s1_y + s1_x * s2_y);

                if (s >= 0 && s <= 1 && t >= 0 && t <= 1)
                {
                    // intersection found
                    i_x = a.X + (t * s1_x);
                    i_y = a.Z + (t * s1_y);
                    Vector3 intersection = new(i_x, 0, i_y);

                    // if the intersection point is exactly on an endpoint, we discard. we dont want that behavriour in this situation
                    if(!edgeInclusive && (intersection == a || intersection == b || intersection == B.a || intersection == B.b))
                    {
                        return Vector3.NaN;
                    }

                    return intersection;
                }

                return Vector3.NaN; // no intersection
            }
        }
    }
}
