using JortPob.Common;
using JortPob.Logging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JortPob.Worker
{
    public class MapWorker : Worker
    {
        private MapWorker()
        {
            _thread = new Thread(Run);
            _thread.Start();
        }

        private void Run()
        {
            using var perf = PerformanceMonitor.TrackPerformance();
            try
            {
                Lort.Log("Loading UI map resources... ", Lort.Type.Main);
                Bitmap image = new Bitmap(Utility.ResourcePath("menu\\map\\map_v1.png"));
                Bitmap map = Utility.LinearToSRGBAlt(image);

                // direct refrence to the naming convention used in the game
                string[] groundLevels = new[] { "M00" };
                MapGenerator.ZoomLevel[] zoomLevels = new[] 
                { 
                    MapGenerator.ZoomLevel.L0,
                    MapGenerator.ZoomLevel.L1,
                    MapGenerator.ZoomLevel.L2
                };

                string maskPath = Path.Combine(Const.ELDEN_PATH, "Game\\menu\\71_maptile.mtmskbnd.dcx");
                string bhdPath = Path.Combine(Const.ELDEN_PATH, "Game\\menu\\71_maptile.tpfbhd");
                string bdtPath = Path.Combine(Const.ELDEN_PATH, "Game\\menu\\71_maptile.tpfbdt");

                // L0 chunks + L1 chunks + L2 chunks
                var chunkCount = (41 * 41) + (31 * 31) + (11 * 11);

                Lort.Log($"Processing {chunkCount} map chunks... ", Lort.Type.Main);
                Lort.NewTask("Processing chunk", chunkCount);

                var result = MapGenerator.ReplaceMapTiles(
                    map,
                    groundLevels,
                    zoomLevels,
                    maskPath,
                    bhdPath,
                    bdtPath,
                    progressCallback: () =>
                    {
                        Lort.TaskIterate();
                    }
                );

                Lort.Log("Writing map files... ", Lort.Type.Main);
                Directory.CreateDirectory(Path.Combine(Const.OUTPUT_PATH, "menu"));
                File.Copy(maskPath, Path.Combine(Const.OUTPUT_PATH, "menu\\71_maptile.mtmskbnd.dcx"), true);
                File.WriteAllBytes(Path.Combine(Const.OUTPUT_PATH, "menu\\71_maptile.tpfbhd"), result.bhdBytes);
                File.WriteAllBytes(Path.Combine(Const.OUTPUT_PATH, "menu\\71_maptile.tpfbdt"), result.bdtBytes);
            } catch (Exception ex) 
            {
                Lort.Log($"Failed to generate UI map: {ex.Message}", Lort.Type.Debug);
            }
            IsDone = true;
        }

        public static void Go()
        {
            using var perf = PerformanceMonitor.TrackPerformance();
            if (Const.DEBUG_SKIP_CUSTOM_MAP) { return; }
            if (Const.DEBUG_REUSE_FILES &&
                File.Exists(Path.Combine(Const.OUTPUT_PATH, "menu\\71_maptile.tpfbhd")) &&
                File.Exists(Path.Combine(Const.OUTPUT_PATH, "menu\\71_maptile.tpfbdt")) &&
                File.Exists(Path.Combine(Const.OUTPUT_PATH, "menu\\71_maptile.mtmskbnd.dcx")))
            { return; }             // if debug_reuse is on and the files are already there...

            MapWorker worker = new();

            while (!worker.IsDone)
            {
                // wait...
                Thread.Yield();
            }
        }
    }
}
