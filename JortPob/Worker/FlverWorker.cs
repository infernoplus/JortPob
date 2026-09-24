using JortPob.Common;
using JortPob.Logging;
using JortPob.Model;
using SharpAssimp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace JortPob.Worker
{
    public class FlverWorker : Worker
    {
        private MaterialContext materialContext;
        private List<PreModel> meshes; // in

        private int start;
        private int end;

        public List<ModelInfo> models; // out

        public FlverWorker(MaterialContext materialContext, List<PreModel> meshes, int start, int end)
        {
            this.materialContext= materialContext;
            this.meshes = meshes;

            this.start = start;
            this.end = end;

            models = new();

            _thread = new Thread(Run);
            _thread.Start();
        }

        private void Run()
        {
            ExitCode = 1;

            AssimpContext assimpContext = new();
            for (int i = start; i < Math.Min(meshes.Count, end); i++)
            {
                PreModel premodel = meshes[i];

                if (string.IsNullOrEmpty(premodel.mesh))
                {
                    Lort.Log(" ## ERROR ## Premodel mesh name is invalid!", Lort.Type.Debug);
                    Lort.TaskIterate();
                    continue;
                }

                string newModelpath = Path.ChangeExtension(premodel.mesh.ToLower(), "flver").Replace(@"\", "_").Replace(" ", "");
                /* Generate the 100 scale version of the model. This is the baseline. After this we generate dynamics and baked scale versions from this */
                string meshIn = Path.Combine(Const.MORROWIND_PATH, $@"Data Files\meshes\{premodel.mesh.ToLower()}");
                string meshOut = Path.Combine(Const.CACHE_PATH, "meshes", newModelpath);
                ModelInfo modelInfo = new(premodel.mesh, Path.Combine("meshes", newModelpath), 100);
                //modelInfo = ModelConverter.FBXtoFLVER(assimpContext, materialContext, modelInfo, premodel.forceCollision, meshIn, meshOut);

                modelInfo = ModelConverter.NIFToFLVER(materialContext, modelInfo, premodel.forceCollision, meshIn, meshOut);
                models.Add(modelInfo);

                /* if a model has no collision we don't need baked scale or dynamic versions. nocollide static meshes can just be scaled freely */
                /* if the model does have collision though we need to generate dynamic and baked scale versions */
                if (modelInfo.HasCollision())
                {
                    bool makeDynamic = false;
                    foreach (KeyValuePair<int, int> kvp in premodel.scales)
                    {
                        int scale = kvp.Key;
                        int count = kvp.Value;

                        if (scale == 100) { continue; }  // Already done above;

                        if (count <= Const.ASSET_BAKE_SCALE_CUTOFF)
                        {
                            makeDynamic = true;
                        }
                        else
                        {
                            ModelInfo baked = new(modelInfo.name, modelInfo.path.Replace(".flver", $"_s{scale}.flver"), scale);
                            FLVERUtil.Scale(Path.Combine(Const.CACHE_PATH, modelInfo.path), Path.Combine(Const.CACHE_PATH, baked.path), scale * 0.01f);
                            if (modelInfo.collision != null)
                            {
                                baked.collision = new(modelInfo.collision.name, modelInfo.collision.obj.Replace(".obj", $"_s{scale}.obj"));
                                Obj obj = new(Path.Combine(Const.CACHE_PATH, modelInfo.collision.obj));
                                obj.scale(scale * 0.01f);
                                obj.write(Path.Combine(Const.CACHE_PATH, baked.collision.obj));
                            }
                            baked.size = modelInfo.size * (scale * 0.01f);
                            models.Add(baked);
                        }
                    }
                    if (makeDynamic || premodel.forceDynamic) // force dynamic does not force all instances to be dynamic, it just forces us to make a dynamic version. used by itemcontent specifically
                    {
                        ModelInfo dynamic = new(modelInfo.name, modelInfo.path, Const.DYNAMIC_ASSET);
                        dynamic.collision = modelInfo.collision;
                        dynamic.size = modelInfo.size; // in the future this would be a good time to find and save the largest dynamic scale used for lod gen
                        models.Add(dynamic);
                    }
                }

                Lort.TaskIterate(); // Progress bar update
            }
            assimpContext.Dispose();

            //var barkbark = models.AsParallel().Where(m => m.textures.Any(t => t.name.ToLower().Contains("wood")) || m.textures.Any(t => t.name.ToLower().Contains("wood") || m.textures.Any(t => t.name.ToLower().Contains("wood")))).Count();

            //Lort.Log($"{barkbark} models contain wood materials", Lort.Type.Debug);

            IsDone = true;
            ExitCode = 0;
        }

        public static List<ModelInfo> Go(MaterialContext materialContext, List<PreModel> meshes)
        {
            Lort.Log($"Converting {meshes.Count} models...", Lort.Type.Main); // Not that slow but multithreading good
            Lort.NewTask("Converting NIF", meshes.Count);

            int partition = (int)Math.Ceiling(meshes.Count / (float)Const.THREAD_COUNT);
            List<FlverWorker> workers = new();
            for (int i = 0; i < Const.THREAD_COUNT; i++)
            {
                int start = i * partition;
                int end = start + partition;
                FlverWorker worker = new(materialContext, meshes, start, end);
                workers.Add(worker);
            }

            /* Wait for threads to finish */
            while (true)
            {
                bool done = true;
                foreach (FlverWorker worker in workers)
                {
                    done &= worker.IsDone;
                }

                if (done)
                    break;
            }

            /* Merge output */
            List<ModelInfo> models = new();
            foreach (FlverWorker worker in workers)
            {
                models.AddRange(worker.models);
            }

            return models;
        }
    }
}
