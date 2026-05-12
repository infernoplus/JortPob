using JortPob.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using ERNavmeshGenCS;

namespace JortPob.Worker
{
    public class NavWorker(List<string> objs) : IWorker<Unit>
    {
        public Unit Go()
        {
            /* OBJ -> HKX conversion of navmeshes */
            Lort.Log($"Preprocessing {objs.Count} navmeshes...", Lort.Type.Main);     // Egregiously slow, multithreaded to make less terrible
            Lort.NewTask("Preprocessing NAVs", objs.Count());
            return Run();
        }

        private Unit Run()
        {
            /* Write navmesh settings */
            hkaiNavMeshGenerationSnapshot nNavmeshSettings = HkxUtility.GetDefaultNavmeshGenerationSnapshot();
            hkaiNavMeshGenerationSnapshot oNavmeshSettings = HkxUtility.GetLodNavmeshGenerationSnapshot();
            string nNvmSettingsPath = Path.Combine(Const.CACHE_PATH, "n_nav_settings.json");
            string oNvmSettingsPath = Path.Combine(Const.CACHE_PATH, "o_nav_settings.json");
            HkxUtility.SaveNavmeshGenerationSettings(nNavmeshSettings, nNvmSettingsPath);
            HkxUtility.SaveNavmeshGenerationSettings(oNavmeshSettings, oNvmSettingsPath);
            
            Parallel.ForEach(objs, obj =>
            {
                string hkxPath = Path.ChangeExtension(obj, ".hkx");
                if (Const.DEBUG_REUSE_FILES && File.Exists(hkxPath))
                {
                    Lort.TaskIterate();
                    return;
                }
                Model.ModelConverter.OBJtoHKX(obj, hkxPath);
                Lort.TaskIterate();
            });

            Parallel.ForEach(objs, obj =>
            {
                string hkxPath = Path.ChangeExtension(obj, ".hkx");
                string nnavPath = Path.ChangeExtension(hkxPath, ".n.nav");
                string onavPath = Path.ChangeExtension(hkxPath, ".o.nav");

                if (Const.DEBUG_REUSE_FILES && File.Exists(nnavPath) && File.Exists(onavPath))
                {
                    Lort.TaskIterate(); return; // if debug_reuse is on, skip if file already created
                } 
                Model.ModelConverter.HKXtoNAV(hkxPath, nnavPath, nNvmSettingsPath);
                Model.ModelConverter.HKXtoNAV(hkxPath, onavPath, oNvmSettingsPath);
                Lort.TaskIterate(); // Progress bar update
            });
            
            return Unit.Default;
        }
    }
}
