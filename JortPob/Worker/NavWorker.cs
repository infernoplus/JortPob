using JortPob.Common;
using JortPob.Logging;
using JortPob.Model;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
            Lort.Log($"Building {objs.Count} navmeshes...", Lort.Type.Main);
            Lort.NewTask("Building NAVs", objs.Count() * 2); // 2 outputs (n.nav + o.nav) per obj
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
            string chunkDir = Path.Combine(Const.CACHE_PATH, @"nav\_chunks");
            Directory.CreateDirectory(chunkDir);

            // Build the pending work list: one entry per obj still needing a nav, carrying only
            // the outputs actually missing (honoring DEBUG_REUSE_FILES). An obj is "done" when
            // both its .n.nav and .o.nav already exist.
            var pending = new List<(string hkx, List<(string outPath, string settings)> outs)>();
            foreach (string objPath in objs)
            {
                string hkxPath = Path.ChangeExtension(objPath, ".hkx");
                string nnavPath = Path.ChangeExtension(hkxPath, ".n.nav");
                string onavPath = Path.ChangeExtension(hkxPath, ".o.nav");
                var outs = new List<(string, string)>();
                if (!(Const.DEBUG_REUSE_FILES && File.Exists(nnavPath))) outs.Add((nnavPath, nNvmSettings));
                if (!(Const.DEBUG_REUSE_FILES && File.Exists(onavPath))) outs.Add((onavPath, oNvmSettings));
                for (int c = outs.Count; c < 2; c++) { Lort.TaskIterate(); } // tick cached outputs now
                if (outs.Count == 0) { continue; } // fully cached, nothing to build
                pending.Add((hkxPath, outs));
            }
            if (pending.Count == 0) { return; }

            // Each chunk runs in its OWN process that exits when done -- the only reliable way to
            // reclaim the DLL's per-generation Havok leak. One worker per core; several chunks per
            // worker (not one each) lets Parallel.ForEach work-steal, so uneven navmesh times don't
            // leave workers idle. MAX_CHUNK caps peak memory per worker on large builds (the leak is
            // ~0.13MB/gen, reclaimed only on exit).
            //
            // The worker count is ALSO capped by available system commit: every NavGenWorker loads the
            // game image, whose CSMemoryImp::InitAllocators VirtualAllocs all of its named heaps with
            // MEM_COMMIT up front (GFX 2.5GB, MAIN 1.5GB, HAVOK 0.8GB, ... ~6.3GB total per process).
            // Launching more workers than the commit limit can hold makes those VirtualAllocs fail
            // silently inside the game, and the worker then crashes at init with a null heap.
            const int CHUNKS_PER_WORKER = 3, MAX_CHUNK = 500;
            int jobs = WorkersByAvailableCommit(Math.Max(1, Const.THREAD_COUNT));
            int evenSplit = (int)Math.Ceiling(pending.Count / (double)(jobs * CHUNKS_PER_WORKER));
            int chunkSize = Math.Max(1, Math.Min(MAX_CHUNK, evenSplit));

            var chunks = new List<List<(string hkx, List<(string outPath, string settings)> outs)>>();
            for (int i = 0; i < pending.Count; i += chunkSize) 
            {
                chunks.Add(pending.GetRange(i, Math.Min(chunkSize, pending.Count - i)));
            }

            // A worker that dies at init (commit exhaustion, see above) produces nothing for its chunk,
            // so each chunk is re-run for whatever outputs are still missing, up to MAX_ATTEMPTS.
            const int MAX_ATTEMPTS = 3;
            var options = new ParallelOptions { MaxDegreeOfParallelism = jobs };
            Parallel.ForEach(Enumerable.Range(0, chunks.Count), options, ci =>
            {
                var remaining = chunks[ci];
                int lineCount = remaining.Sum(c => c.outs.Count);
                for (int attempt = 1; attempt <= MAX_ATTEMPTS && remaining.Count > 0; attempt++)
                {
                    string listFile = Path.Combine(chunkDir, attempt == 1 ? $"chunk_{ci}.txt" : $"chunk_{ci}_retry{attempt}.txt");
                    using (var w = new StreamWriter(listFile, false))
                        foreach (var (hkx, outs) in remaining)
                            foreach (var (outPath, settings) in outs)
                                w.WriteLine($"{hkx}\t{outPath}\t{settings}");

                    int attemptLines = remaining.Sum(c => c.outs.Count);
                    // Generous per-chunk timeout: ~20s/navmesh (heavy ones run ~1-2s), floored at 60s.
                    int timeout = Math.Max(60000, attemptLines * 20000);
                    try
                    {
                        ModelConverter.HKXtoNAV(listFile, timeout, () => Lort.TaskIterate());
                    }
                    catch (Exception ex) { Lort.Log($"Nav chunk {ci} attempt {attempt} ({attemptLines} navmeshes) failed: {ex.Message}", Lort.Type.Debug); }

                    // Success is judged by the files on disk, not the exit code.
                    remaining = remaining
                        .Select(c => (c.hkx, outs: c.outs.Where(o => !File.Exists(o.outPath)).ToList()))
                        .Where(c => c.outs.Count > 0)
                        .ToList();
                    if (remaining.Count > 0 && attempt < MAX_ATTEMPTS)
                        Lort.Log($"Nav chunk {ci}: {remaining.Sum(c => c.outs.Count)} outputs missing after attempt {attempt}, retrying", Lort.Type.Debug);
                }
                int produced = lineCount - remaining.Sum(c => c.outs.Count);
                for (int r = produced; r < lineCount; r++) { Lort.TaskIterate(); } // keep the progress bar honest for lost outputs
            });

            // Fail here, with the list, rather than deep in the NVBND packer with a bare FileNotFoundException.
            var missing = pending.SelectMany(c => c.outs).Where(o => !File.Exists(o.outPath)).Select(o => o.outPath).ToList();
            if (missing.Count > 0)
            {
                foreach (string m in missing) { Lort.Log($"Navmesh output missing: {m}", Lort.Type.Debug); }
                throw new FileNotFoundException($"{missing.Count} navmesh output(s) were not generated after {MAX_ATTEMPTS} attempts (first: {missing[0]}). See log for the full list.");
            }
        }

        /* Cap the parallel NavGenWorker count by what the system commit limit can absorb. */
        private const long COMMIT_PER_WORKER = 2L << 30; // ~1.5GB measured per worker, plus headroom

        private static int WorkersByAvailableCommit(int wanted)
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref status)) { return wanted; }
            int byCommit = (int)Math.Min(int.MaxValue, (long)status.ullAvailPageFile / COMMIT_PER_WORKER);
            int jobs = Math.Clamp(byCommit, 1, wanted);
            if (jobs < wanted)
                Lort.Log($"Limiting nav workers to {jobs} (wanted {wanted}): only {status.ullAvailPageFile / (1L << 30)}GB of system commit available and each NavGenWorker commits ~{COMMIT_PER_WORKER >> 30}GB at init", Lort.Type.Main);
            return jobs;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    }
}
