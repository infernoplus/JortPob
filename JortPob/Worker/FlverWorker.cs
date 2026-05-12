using System;
using System.Collections.Concurrent;
using JortPob.Common;
using JortPob.Model;
using SharpAssimp;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace JortPob.Worker
{
    public class FlverWorker(MaterialContext materialContext, List<PreModel> meshes) : IWorker<List<ModelInfo>>
    {
        private List<ModelInfo> Run(MaterialContext materialContext, List<PreModel> meshes)
        {
            AssimpContext assimpContext = new();
            ConcurrentBag<ModelInfo> models = new ConcurrentBag<ModelInfo>(); 
            
            Parallel.ForEach(meshes, premodel =>
            {
                if (string.IsNullOrEmpty(premodel.mesh))
                {
                    Lort.Log(" ## ERROR ## Premodel mesh name is invalid!", Lort.Type.Debug);
                    Lort.TaskIterate();
                    return;
                }

                string newModelpath = Path.ChangeExtension(premodel.mesh.ToLower(), "flver").Replace(@"\", "_")
                    .Replace(" ", "");
                /* Generate the 100 scale version of the model. This is the baseline. After this we generate dynamics and baked scale versions from this */
                string meshIn = Path.Combine(Const.MORROWIND_PATH,
                    $@"Data Files\meshes\{premodel.mesh.ToLower()}");
                string meshOut = Path.Combine(Const.CACHE_PATH, "meshes", newModelpath);
                ModelInfo modelInfo = new(premodel.mesh, Path.Combine("meshes", newModelpath), 100);
                //modelInfo = ModelConverter.FBXtoFLVER(assimpContext, materialContext, modelInfo, premodel.forceCollision, meshIn, meshOut);

                modelInfo = ModelConverter.NIFToFLVER(materialContext, modelInfo, premodel.forceCollision, meshIn,
                    meshOut);
                if (modelInfo == null) return;

                models.Add(modelInfo);

                /* if a model has no collision we don't need baked scale or dynamic versions. nocollide static meshes can just be scaled freely */
                /* if the model does have collision though we need to generate dynamic and baked scale versions */
                if (modelInfo.HasCollision())
                {
                    bool makeDynamic = false;
                    Parallel.ForEach(premodel.scales, kvp =>
                    {
                        int scale = kvp.Key;
                        int count = kvp.Value;

                        if (scale == 100)
                        {
                            return;
                        } // Already done above;

                        if (count <= Const.ASSET_BAKE_SCALE_CUTOFF)
                        {
                            makeDynamic = true;
                        }
                        else
                        {
                            ModelInfo baked = new(modelInfo.name,
                                modelInfo.path.Replace(".flver", $"_s{scale}.flver"),
                                scale);
                            FLVERUtil.Scale(Path.Combine(Const.CACHE_PATH, modelInfo.path),
                                Path.Combine(Const.CACHE_PATH, baked.path), scale * 0.01f);
                            if (modelInfo.collision != null)
                            {
                                baked.collision = new(modelInfo.collision.name,
                                    modelInfo.collision.obj.Replace(".obj", $"_s{scale}.obj"));
                                Obj obj = new(Path.Combine(Const.CACHE_PATH, modelInfo.collision.obj));
                                obj.scale(scale * 0.01f);
                                obj.write(Path.Combine(Const.CACHE_PATH, baked.collision.obj));
                            }

                            baked.size = modelInfo.size * (scale * 0.01f);
                            models.Add(baked);
                        }
                    });

                    if (makeDynamic ||
                        premodel
                            .forceDynamic) // force dynamic does not force all instances to be dynamic, it just forces us to make a dynamic version. used by itemcontent specifically
                    {
                        ModelInfo dynamic = new(modelInfo.name, modelInfo.path, Const.DYNAMIC_ASSET);
                        dynamic.collision = modelInfo.collision;
                        dynamic.size =
                            modelInfo
                                .size; // in the future this would be a good time to find and save the largest dynamic scale used for lod gen
                        models.Add(dynamic);
                    }
                }

                Lort.TaskIterate(); // Progress bar update
            });
            
            assimpContext.Dispose();

            return models.ToList();
        }

        public List<ModelInfo> Go()
        {
            Lort.Log($"Converting {meshes.Count} models...", Lort.Type.Main); // Not that slow but multithreading good
            Lort.NewTask("Converting NIF", meshes.Count);
            return Run(materialContext, meshes);
        }
    }
}
