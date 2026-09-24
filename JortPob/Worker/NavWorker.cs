using JortPob.Common;
using JortPob.Logging;
using JortPob.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ERNavmeshGenCS;

namespace JortPob.Worker
{
    public class NavWorker
    {
        public static void Go(List<string> objs)
        {
            using var perf = PerformanceMonitor.TrackPerformance();
            /* Write navmesh settings */
            hkaiNavMeshGenerationSnapshot nNavmeshSettings = HkxUtility.GetDefaultNavmeshGenerationSnapshot();
            hkaiNavMeshGenerationSnapshot oNavmeshSettings = HkxUtility.GetLodNavmeshGenerationSnapshot();
            string nNvmSettingsPath = Path.Combine(Const.CACHE_PATH, "n_nav_settings.json");
            string oNvmSettingsPath = Path.Combine(Const.CACHE_PATH, "o_nav_settings.json");
            HkxUtility.SaveNavmeshGenerationSettings(nNavmeshSettings, nNvmSettingsPath);
            HkxUtility.SaveNavmeshGenerationSettings(oNavmeshSettings, oNvmSettingsPath);

            /* OBJ -> HKX conversion of navmeshes */
            Lort.Log($"Preprocessing {objs.Count} navmeshes...", Lort.Type.Main);     // Egregiously slow, multithreaded to make less terrible
            Lort.NewTask("Preprocessing NAVs", objs.Count());
            var options = new ParallelOptions { MaxDegreeOfParallelism = Const.THREAD_COUNT };
            Parallel.ForEach(Partitioner.Create(0, objs.Count()), options, range =>
            {
                ProcessHKX(objs, range.Item1, range.Item2);
            });

            /* HKX -> NAV conversion of navmeshes */
            Lort.Log($"Building {objs.Count} navmeshes...", Lort.Type.Main); // can't multithread this part for unknown reason
            Lort.NewTask("Building NAVs", objs.Count());
            ProcessNAV(nNvmSettingsPath, oNvmSettingsPath, objs);
        }

        protected static void ProcessHKX(List<string> objs, int start, int end)
        {
            int limit = Math.Min(objs.Count(), end);
            for (int i = start; i < limit; i++)
            {
                string objPath = objs[i];
                string hkxPath = Path.ChangeExtension(objPath, ".hkx");
                if (Const.DEBUG_REUSE_FILES && File.Exists(hkxPath)) { Lort.TaskIterate(); continue; } // if debug_reuse is on, skip if file already created
                Model.ModelConverter.OBJtoHKX(objPath, hkxPath);
                Lort.TaskIterate(); // Progress bar update
            }
        }

        protected static void ProcessNAV(string nNvmSettings, string oNvmSettings, List<string> objs)
        {
            foreach (string objPath in objs)
            {
                string hkxPath = Path.ChangeExtension(objPath, ".hkx");
                string nnavPath = Path.ChangeExtension(hkxPath, ".n.nav");
                string onavPath = Path.ChangeExtension(hkxPath, ".o.nav");
                if (Const.DEBUG_REUSE_FILES && File.Exists(nnavPath) && File.Exists(onavPath)) { Lort.TaskIterate(); continue; } // if debug_reuse is on, skip if file already created
                Model.ModelConverter.HKXtoNAV(hkxPath, nnavPath, nNvmSettings);
                Model.ModelConverter.HKXtoNAV(hkxPath, onavPath, oNvmSettings);
                Lort.TaskIterate(); // Progress bar update
            }
        }
    }
}
