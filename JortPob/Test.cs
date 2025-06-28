using JortPob.Common;
using JortPob.Model;
using JortPob.Worker;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static SoulsFormats.PARAM;

namespace JortPob
{
    public class Test
    {
        /* This entire class i just for adding temporary test code for things */
        /* Nothing in here actually matters and will not be used in actual builds. some of these may be debug flags in the const */
        /* Basically just have this to keep the resst of the program clean and free of random test code */


        /* Regenerates all terrain without fully rebuilding cahce */
        public static void RegenerateLandscapes(ESM esm, Layout layout, Cache cache)
        {
            Lort.Log("## DEBUG ## Regenerating all landscapes!", Lort.Type.Main);
            //MATBIN test1 = MATBIN.Read(@"I:\SteamLibrary\steamapps\common\ELDEN RING\Game\material\allmaterial-matbinbnd-dcx\GR\data\INTERROOT_win64\material\matbin\Map_m60_00\matxml\AEG110_243_ID014.matbin");
            //MATBIN test2 = MATBIN.Read(@"I:\SteamLibrary\steamapps\common\ELDEN RING\Game\material\allmaterial_dlc02-matbinbnd-dcx\GR\data\INTERROOT_win64\material\matbin_DLC02\Map_m20_00\matxml\m20_00_801.matbin");

            MaterialContext materialContext = new();
            LandscapeWorker.Go(materialContext, esm);
            materialContext.WriteAll(); // while we dont need to regenerate textures, the matbins are needed so guh
            materialContext = null; // dispose


            List<ResourcePool> pools = new();
            foreach (BaseTile tile in layout.all)
            {
                if (tile.assets.Count <= 0 && tile.terrain.Count <= 0) { continue; }   // Skip empty tiles.

                ResourcePool pool = new(tile, null);

                /* Add terrain */
                foreach (Tuple<Vector3, TerrainInfo> tuple in tile.terrain)
                {
                    Vector3 position = tuple.Item1;
                    TerrainInfo terrainInfo = tuple.Item2;

                    /* Terrain and terrain collision */  // Render goes in hugetile for long view distance. Collision goes in tile for optimization
                    if (tile.GetType() == typeof(HugeTile))
                    {
                        pool.mapIndices.Add(new Tuple<int, string>(terrainInfo.id, terrainInfo.path));
                    }
                }
                pools.Add(pool);
            }

            Lort.NewTask("Binding map pieces...", pools.Count);
            TestWorker.Go(pools);
            Bind.BindMaterials($"{Const.OUTPUT_PATH}material\\allmaterial.matbinbnd.dcx");
            Lort.Log("## DEBUG ## Done!", Lort.Type.Main);
        }

        private class TestWorker : Worker.Worker
        {
            private ResourcePool pool;

            public TestWorker(ResourcePool pool)
            {
                this.pool = pool;
                _thread = new Thread(Run);
                _thread.Start();
            }

            private void Run()
            {
                ExitCode = 1;

                string map = $"{pool.id[0].ToString("D2")}";
                string name = $"{pool.id[0].ToString("D2")}_{pool.id[1].ToString("D2")}_{pool.id[2].ToString("D2")}_{pool.id[3].ToString("D2")}";

                /* Write map pieces like terrain */
                foreach (Tuple<int, string> mp in pool.mapIndices)
                {
                    int mpid = mp.Item1;
                    string mppath = mp.Item2;

                    FLVER2 flver = FLVER2.Read($"{Const.CACHE_PATH}{mppath}");

                    BND4 bnd = new();
                    bnd.Compression = SoulsFormats.DCX.Type.DCX_KRAK;
                    bnd.Version = "07D7R6";

                    BinderFile file = new();
                    file.CompressionType = SoulsFormats.DCX.Type.Zlib;
                    file.Flags = SoulsFormats.Binder.FileFlags.Flag1;
                    file.ID = 200;
                    file.Name = $"N:\\GR\\data\\INTERROOT_win64\\map\\m{name}\\m{name}_{mpid.ToString("D8")}\\Model\\m{name}_{mpid.ToString("D8")}.flver";
                    file.Bytes = flver.Write();
                    bnd.Files.Add(file);

                    bnd.Write($"{Const.OUTPUT_PATH}map\\m60\\m{name}\\m{name}_{mpid.ToString("D8")}.mapbnd.dcx");
                }
                Lort.TaskIterate(); // Progress bar update

                IsDone = true;
                ExitCode = 0;
            }

            public static void Go(List<ResourcePool> msbs)
            {
                List<TestWorker> workers = new();
                foreach (ResourcePool msb in msbs)
                {
                    while (workers.Count >= Const.THREAD_COUNT)
                    {
                        foreach (TestWorker worker in workers)
                        {
                            if (worker.IsDone) { workers.Remove(worker); break; }
                        }

                        // wait...
                        Thread.Yield();
                    }

                    workers.Add(new TestWorker(msb));
                }

                /* Wait for threads to finish */
                while (true)
                {
                    bool done = true;
                    foreach (TestWorker worker in workers)
                    {
                        done &= worker.IsDone;
                    }

                    if (done)
                        break;

                    // wait...
                    Thread.Yield();
                }
            }
        }
    }
}
