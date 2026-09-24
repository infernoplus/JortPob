using JortPob.Common;
using JortPob.Logging;
using JortPob.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace JortPob.Worker
{
    public class HkxWorker
    {
        public static void Go(List<CollisionInfo> collisions)
        {
            List<(string, string)> uniqueCollisions = collisions
                .Select(c => (c.obj, c.hkx))
                .ToHashSet()
                .ToList();

            Lort.Log($"Converting {collisions.Count} ({uniqueCollisions.Count} unique) collisions...", Lort.Type.Main);                 // Egregiously slow, multithreaded to make less terrible
            Lort.NewTask("Converting HKX", uniqueCollisions.Count);

            var options = new ParallelOptions { MaxDegreeOfParallelism = Const.THREAD_COUNT };

            Parallel.ForEach(Partitioner.Create(0, uniqueCollisions.Count), options, range =>
            {
                ProcessCollisions(uniqueCollisions, range.Item1, range.Item2);
            });
        }

        protected static void ProcessCollisions(List<(string, string)> collisions, int start, int end)
        {
            int limit = Math.Min(collisions.Count, end);
            for (int i = start; i < limit; i++)
            {
                var obj = collisions[i].Item1;
                var hkx = collisions[i].Item2;

                ModelConverter.OBJtoHKX(Path.Combine(Const.CACHE_PATH, obj), Path.Combine(Const.CACHE_PATH, hkx));
                Lort.TaskIterate(); // Progress bar update
            }
        }
    }
}
