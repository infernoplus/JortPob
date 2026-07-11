using JortPob.Common;
using PortJob;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace JortPob
{
    public class OverworldManager
    {
        /* Makes a modified version  of m60_00_00_99 */
        /* This msb is the "super overworld" an lod msb that is always visible at any distance in the overworld */
        /* It also contains things like the sky and water so yee */
        public static ResourcePool Generate(Cache cache, ESM esm, Layout layout, Paramanager param)
        {
            Lort.Log($"Building overworld...", Lort.Type.Main);
            Lort.NewTask("Overworld Generation", 2);

            MSBE msb = MSBE.Read(Utility.ResourcePath(@"msb\m60_00_00_99.msb.dcx"));
            LightManager lightManager = new(60, 00, 00, 99);
            ResourcePool pool = new(msb, lightManager);

            /* Delete all vanilla map parts */
            msb.Parts.MapPieces.Clear();

            /* Delete all vanilla envboxes and stuffs */
            msb.Regions.EnvironmentMapEffectBoxes.Clear();
            msb.Regions.EnvironmentMapPoints.Clear();
            msb.Regions.SoundRegions.Clear();
            msb.Regions.Sounds.Clear();

            /* Delete all vanilla assets except the skyboxs */
            List<MSBE.Part.Asset> keep = new();
            foreach (MSBE.Part.Asset asset in msb.Parts.Assets)
            {
                int id1 = int.Parse(asset.Name.Substring(3, 3));
                int id2 = int.Parse(asset.Name.Substring(7, 3));

                // Sky stuff
                if (id1 == 96) { keep.Add(asset); continue; }
            }

            msb.Parts.Assets = keep;

            /* Calculate actual 0,0 of morrowind against the superoverworld root offset (DUMB!) */
            Cell cell = esm.GetCellByGrid(new Int2(0, 0));
            Vector3 center;
            if (cell != null)
            {
                float x = (10 * 4f * Const.TILE_SIZE) + (Const.TILE_SIZE * 1.5f);
                float y = (8 * 4f * Const.TILE_SIZE) + (Const.TILE_SIZE * 1.5f);
                center = (cell.center + Const.LAYOUT_COORDINATE_OFFSET) - new Vector3(x, 0, y);
            }
            else
            {
                // if we have the const debug flag set for building a specific cell or group of cells the esm 0,0 cell may not be loaded
                // in that case here is the correct value under normal circumstances, its better to calc it for safety but its debug so w/e
                center = new Vector3(608.051758f, 0f, 2472.96f);
            }

            /* Add water */
            MSBE.Part.Asset water = MakePart.Asset(cache.GetWater());
            water.Position = center + Const.MSB_OFFSET;
            msb.Parts.Assets.Add(water);

            /* Add swamp */
            MSBE.Part.Asset swamp = MakePart.Asset(cache.GetSwamp());
            swamp.Position = center + Const.MSB_OFFSET;
            msb.Parts.Assets.Add(swamp);

            /* Add lava */
            MSBE.Part.Asset lava = MakePart.Asset(cache.GetLava());
            lava.Position = center + Const.MSB_OFFSET + new Vector3(0f, Const.LAVA_VISUAL_OFFSET, 0f);
            msb.Parts.Assets.Add(lava);

            /* Add terrain */
            foreach (TerrainInfo terrainInfo in cache.terrains)
            {
                Vector3 position = new Vector3(terrainInfo.coordinate.x * Const.CELL_SIZE, 0, terrainInfo.coordinate.y * Const.CELL_SIZE) + center;

                MSBE.Part.MapPiece map = MakePart.MapPiece();
                map.Name = $"m{terrainInfo.id.ToString("D8")}_0000";
                map.ModelName = $"m{terrainInfo.id.ToString("D8")}";
                map.Position = position + Const.MSB_OFFSET;
                map.PartsDrawParamID = param.terrainDrawParamID;

                msb.Parts.MapPieces.Add(map);
                pool.mapIndices.Add(new Tuple<int, string>(terrainInfo.id, terrainInfo.path));
            }

            Lort.TaskIterate();

            //MSBE SOURCE = MSBE.Read(Utility.ResourcePath(@"msb\m60_00_00_99.msb.dcx"));
            {
                Int2 min = new(-2, 1);
                Int2 max = new(3, 6);
                int[] start = new int[] { 60, 10, 8, 2 }; // first msb at the min coord
                float size = 1024f; float crossfade = 32f;
                int id = 960;

                for (int y = min.y; y < max.y; y++)
                {
                    for (int x = min.x; x < max.x; x++)
                    {

                        /* Grab tile */
                        int[] msbid = new int[] { start[0], start[1] + x, start[2] + y, start[3] };
                        HugeTile tile = layout.GetHugeTile(new Int2(msbid[1], msbid[2]));
                        if (tile == null || tile.IsEmpty) { continue; } // skip empty or unreal

                        /* Create envmap texture file */
                        EnvManager.CreateEnvMaps(tile, id);

                        /* Create an envbox */
                        MSBE.Region.EnvironmentMapEffectBox envBox = MakePart.EnvBox();
                        envBox.Name = $"Env_Box{id.ToString("D3")}";
                        envBox.Shape = new MSB.Shape.Box(size + crossfade, size + crossfade, size + crossfade);
                        envBox.Position = new Vector3(x * size, -256f, y * size);
                        envBox.TransitionDist = crossfade / 2f;
                        msb.Regions.EnvironmentMapEffectBoxes.Add(envBox);

                        MSBE.Region.EnvironmentMapPoint envPoint = MakePart.EnvPoint();
                        envPoint.Name = $"Env_Point{id.ToString("D3")}";
                        envPoint.Position = new Vector3(x * size, 128f, y * size);
                        envPoint.UnkMapID = new byte[] { (byte)msbid[0], (byte)msbid[1], (byte)msbid[2], (byte)msbid[3] };
                        msb.Regions.EnvironmentMapPoints.Add(envPoint);

                        id++;
                    }
                }
            }

            Lort.TaskIterate();

            /* Regenerate resources */
            msb.Models.Assets.Clear();
            msb.Models.MapPieces.Clear();
            AutoResource.Generate(60, 0, 0, 99, msb);

            return pool;
        }
    }
}
