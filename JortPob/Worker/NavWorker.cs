using System;
using JortPob.Common;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using ERNavmeshGenCS;
using HKLib.Reflection.hk2018;

namespace JortPob.Worker
{
    public class NavWorker(List<string> objs, HavokTypeRegistry registry) : IWorker<Unit>
    {
        public Unit Go()
        {
            /* OBJ -> HKX conversion of navmeshes */
            Lort.Log($"Preprocessing {objs.Count} navmeshes...", Lort.Type.Main);     // Egregiously slow, multithreaded to make less terrible

            return Run();
        }

        private Unit Run()
        {
            Lort.NewTask("Processing nav meshes converting to hkx", objs.Count);
            /* Write navmesh settings */
            hkaiNavMeshGenerationSnapshot nNavmeshSettings = HkxUtility.GetDefaultNavmeshGenerationSnapshot();
            hkaiNavMeshGenerationSnapshot oNavmeshSettings = HkxUtility.GetLodNavmeshGenerationSnapshot();
            string nNvmSettingsPath = Path.Combine(Const.CACHE_PATH, "n_nav_settings.json");
            string oNvmSettingsPath = Path.Combine(Const.CACHE_PATH, "o_nav_settings.json");
            HkxUtility.SaveNavmeshGenerationSettings(nNavmeshSettings, nNvmSettingsPath);
            HkxUtility.SaveNavmeshGenerationSettings(oNavmeshSettings, oNvmSettingsPath);
            
            Parallel.ForEach(objs, new ParallelOptions{MaxDegreeOfParallelism = Environment.ProcessorCount / 2}, obj =>
            {
                string hkxPath = Path.ChangeExtension(obj, ".hkx");
                if (Const.DEBUG_REUSE_FILES && File.Exists(hkxPath))
                {
                    Lort.TaskIterate();
                    return;
                }
                Model.ModelConverter.OBJtoHKX(obj, hkxPath, registry);
                Lort.TaskIterate();
            });
            
            Lort.NewTask("Processing nav meshes creating nav n files", objs.Count);
            Parallel.ForEach(objs, new ParallelOptions{MaxDegreeOfParallelism = Environment.ProcessorCount / 2}, obj =>
            {
                string hkxPath = Path.ChangeExtension(obj, ".hkx");
                string nnavPath = Path.ChangeExtension(hkxPath, ".n.nav");

                if (Const.DEBUG_REUSE_FILES && File.Exists(nnavPath))
                {
                    Lort.TaskIterate(); return; // if debug_reuse is on, skip if file already created
                } 
                Model.ModelConverter.HKXtoNAV(hkxPath, nnavPath, nNvmSettingsPath);
                Lort.TaskIterate(); // Progress bar update
            });
            
            Lort.NewTask("Processing nav meshes creating nav o files", objs.Count);
            Parallel.ForEach(objs, new ParallelOptions{MaxDegreeOfParallelism = Environment.ProcessorCount / 2}, obj =>
            {
                string hkxPath = Path.ChangeExtension(obj, ".hkx");
                string onavPath = Path.ChangeExtension(hkxPath, ".o.nav");

                if (Const.DEBUG_REUSE_FILES && File.Exists(onavPath))
                {
                    Lort.TaskIterate(); return; // if debug_reuse is on, skip if file already created
                } 
                Model.ModelConverter.HKXtoNAV(hkxPath, onavPath, oNvmSettingsPath);
                Lort.TaskIterate(); // Progress bar update
            });
            
            return Unit.Default;
        }
    }
}
