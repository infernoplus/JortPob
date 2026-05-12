using JortPob.Common;
using JortPob.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace JortPob.Worker
{
    public class HkxWorker(List<CollisionInfo> collisions) : IWorker<Unit>
    {
        public Unit Go()
        {
            return Run();
        }

        private Unit Run()
        {
            List<(string, string)> uniqueCollisions = collisions
                .Select(c => (c.obj, c.hkx))
                .ToHashSet()
                .ToList();

            Lort.Log($"Converting {collisions.Count} ({uniqueCollisions.Count} unique) collisions...", Lort.Type.Main); // Less slow now :)
            Lort.NewTask("Converting HKX", uniqueCollisions.Count);
            
            Parallel.ForEach(uniqueCollisions, uc =>
            {
                ModelConverter.OBJtoHKX(Path.Combine(Const.CACHE_PATH, uc.Item1), Path.Combine(Const.CACHE_PATH, uc.Item2));
                Lort.TaskIterate();
            });

            return Unit.Default;
        }
    }
}
