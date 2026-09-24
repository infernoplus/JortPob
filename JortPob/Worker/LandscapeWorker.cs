using JortPob.Common;
using JortPob.Logging;
using JortPob.Model;
using System;
using System.Collections.Generic;
using System.Threading;
using System.IO;

namespace JortPob.Worker
{
    public class LandscapeWorker : Worker
    {
        private MaterialContext materialContext;
        private ESM esm;

        private int start;
        private int end;

        public List<TerrainInfo> terrains;

        public LandscapeWorker(MaterialContext materialContext, ESM esm, int start, int end)
        {
            this.materialContext = materialContext;
            this.esm = esm;

            this.start = start;
            this.end = end;

            terrains = new();

            _thread = new Thread(Run);
            _thread.Start();
        }

        private void Run()
        {
            ExitCode = 1;

            for (int i = start; i < Math.Min(esm.exterior.Count, end); i++)
            {
                Cell cell = esm.exterior[i];
                Landscape landscape = esm.GetLandscape(cell.coordinate);
                if (landscape == null) { continue; }

                TerrainInfo terrainInfo = new(landscape.coordinate, $"terrain\\ext{landscape.coordinate.x},{landscape.coordinate.y}.flver");
                terrainInfo = ModelConverter.LANDSCAPEtoFLVER(materialContext, terrainInfo, landscape, Path.Combine(Const.CACHE_PATH, "terrain", $"ext{landscape.coordinate.x},{landscape.coordinate.y}.flver"));

                // Set some stuff
                terrainInfo.hasWater = landscape.hasWater;
                terrainInfo.hasSwamp = landscape.hasSwamp;
                terrainInfo.hasLava = landscape.hasLava;

                terrains.Add(terrainInfo);

                Lort.TaskIterate(); // Progress bar update
            }

            IsDone = true;
            ExitCode = 0;
        }

        public static List<TerrainInfo> Go(MaterialContext materialContext, ESM esm)
        {
            /* We can no longer multi-thread landscape loading. This is due to border blending requiring things be loaded 1 at a time. */
            /* We can still multithread the model conversions though so that is still a thing */
            if (!Const.DEBUG_SKIP_TERRAIN_BORDER_BLENDING) { esm.LoadLandscapes(); }

            Lort.Log($"Converting {esm.exterior.Count} landscapes...", Lort.Type.Main); // Not that slow but multithreading good
            Lort.NewTask("Converting Landscape", esm.exterior.Count);

            int partition = (int)Math.Ceiling(esm.exterior.Count / (float)Const.THREAD_COUNT);
            List<LandscapeWorker> workers = new();
            for (int i = 0; i < Const.THREAD_COUNT; i++)
            {
                int start = i * partition;
                int end = start + partition;
                LandscapeWorker worker = new(materialContext, esm, start, end);
                workers.Add(worker);
            }

            /* Wait for threads to finish */
            while (true)
            {
                bool done = true;
                foreach (LandscapeWorker worker in workers)
                {
                    done &= worker.IsDone;
                }

                if (done)
                    break;
            }

            /* Merge output */
            List<TerrainInfo> terrains = new();
            foreach (LandscapeWorker worker in workers)
            {
                terrains.AddRange(worker.terrains);
            }

            return terrains;
        }
    }
}
