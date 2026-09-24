using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using JortPob.Common;
using JortPob.Logging;
using SoulsFormats;

namespace JortPob.Worker
{
    public class MsbWorker : Worker
    {
        private ResourcePool pool;

        public MsbWorker(ResourcePool pool)
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

            pool.msb.Write(Path.Combine(Const.OUTPUT_PATH, $@"map\mapstudio\m{name}.msb.dcx"));
            if (pool.lights.Count() > 0) { pool.lights.Write(); }

            /* Write map pieces like terrain */
            foreach (Tuple<int, string> mp in pool.mapIndices)
            {
                int mpid = mp.Item1;
                string mppath = mp.Item2;

                FLVER2 flver = FLVER2.Read(Path.Combine(Const.CACHE_PATH, mppath));

                BND4 bnd = new();
                bnd.Compression = Compression.KRAK();
                bnd.Version = "07D7R6";

                BinderFile file = new();
                file.ID = 200;
                file.Name = $"N:\\GR\\data\\INTERROOT_win64\\map\\m{name}\\m{name}_{mpid.ToString("D8")}\\Model\\m{name}_{mpid.ToString("D8")}.flver";
                file.Bytes = flver.Write();
                bnd.Files.Add(file);

                bnd.Write(Path.Combine(Const.OUTPUT_PATH, $@"map\m60\m{name}\m{name}_{mpid.ToString("D8")}.mapbnd.dcx"));
            }

            BXF4 bxfH = new();
            bxfH.Version = "07D7R6";
            BinderFile comH = new();
            comH.Name = $"m{name}\\h{name}.compendium.dcx";
            comH.Bytes = DCX.Compress(File.ReadAllBytes(Utility.ResourcePath(@"test\test.compendium")), Compression.KRAK());
            comH.ID = 0;
            bxfH.Files.Add(comH);
            int id = 1;
            foreach (Tuple<string, CollisionInfo> tuple in pool.collisionIndices)
            {
                string index = tuple.Item1;
                CollisionInfo collisionInfo = tuple.Item2;

                BinderFile testH = new();
                testH.Name = $"m{name}\\h{name}_{index}.hkx.dcx";
                testH.Bytes = DCX.Compress(File.ReadAllBytes(Path.Combine(Const.CACHE_PATH, collisionInfo.hkx)), Compression.KRAK());
                testH.ID = id++;
                bxfH.Files.Add(testH);
            }
            bxfH.Write(Path.Combine(Const.OUTPUT_PATH, $@"map\m{map}\m{name}\h{name}.hkxbhd"), Path.Combine(Const.OUTPUT_PATH, $@"map\m{map}\m{name}\h{name}.hkxbdt"));

            BXF4 bxfL = new();
            bxfL.Version = "07D7R6";
            BinderFile comL = new();
            comL.Name = $"m{name}\\l{name}.compendium.dcx";
            comL.Bytes = DCX.Compress(File.ReadAllBytes(Utility.ResourcePath(@"test\test.compendium")), Compression.KRAK());
            comL.ID = 0;
            bxfL.Files.Add(comL);
            id = 1;
            foreach (Tuple<string, CollisionInfo> tuple in pool.collisionIndices)
            {
                string index = tuple.Item1;
                CollisionInfo collisionInfo = tuple.Item2;

                BinderFile testL = new();
                testL.Name = $"m{name}\\l{name}_{index}.hkx.dcx";
                testL.Bytes = DCX.Compress(File.ReadAllBytes(Path.Combine(Const.CACHE_PATH, collisionInfo.hkx)), Compression.KRAK());
                testL.ID = id++;
                bxfL.Files.Add(testL);
            }
            bxfL.Write(Path.Combine(Const.OUTPUT_PATH, $@"map\m{map}\m{name}\l{name}.hkxbhd"), Path.Combine(Const.OUTPUT_PATH, $@"map\m{map}\m{name}\l{name}.hkxbdt"));

            Lort.TaskIterate(); // Progress bar update

            IsDone = true;
            ExitCode = 0;
        }

        public static void Go(List<ResourcePool> msbs)
        {
            using var perf = PerformanceMonitor.TrackPerformance();
            Lort.Log($"Writing {msbs.Count} msbs...", Lort.Type.Main); // Multithreaded because insanely slow // doing 1 thread per msb with rolling starts since guh
            Lort.NewTask("Writing MSB", msbs.Count);

            List<MsbWorker> workers = new();
            foreach(ResourcePool msb in msbs)
            {
                while(workers.Count >= Const.THREAD_COUNT)
                {
                    foreach(MsbWorker worker in workers)
                    {
                        if (worker.IsDone) { workers.Remove(worker); break; }
                    }

                    // wait...
                    Thread.Yield();
                }

                workers.Add(new MsbWorker(msb));
            }

            /* Wait for threads to finish */
            while (true)
            {
                bool done = true;
                foreach (MsbWorker worker in workers)
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
