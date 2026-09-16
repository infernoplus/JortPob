using System.Runtime.InteropServices;
using NavGen = ERNavmeshGenCS.ERNavmeshGen;

namespace NavGenWorker;

/// <summary>
/// Headless navmesh chunk worker. Generates a batch of navmeshes in-process (init paid once)
/// then exits so the OS reclaims Havok's per-generation memory leak. One process per chunk,
/// spawned in parallel by JortPob's NavWorker.ProcessNAV.
///
///   NavGenWorker.exe --game "<...\eldenring.exe>" --list "<chunk.txt>"
///
/// Chunk list: one job per line, tab-separated: "hkxPath\toutNavPath\tsettingsJsonPath".
/// The settings path may be empty for default settings. Exit code = number of jobs that
/// failed (0 = all succeeded), so the caller can detect partial chunks.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string? game = null, listFile = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--game": game = args[++i]; break;
                case "--list": listFile = args[++i]; break;
            }
        }

        if (game == null || listFile == null)
        {
            Console.Error.WriteLine("usage: NavGenWorker --game <eldenring.exe> --list <chunk.txt>");
            return -1;
        }
        if (!File.Exists(listFile))
        {
            Console.Error.WriteLine($"list file not found: {listFile}");
            return -1;
        }

        // Resolve the native ERNavmeshGen.dll next to this worker (works from bin or the
        // deployed Resources\tools\Nav folder).
        NativeLibrary.SetDllImportResolver(typeof(NavGen).Assembly, (name, _, _) =>
            name.StartsWith("ERNavmeshGen", StringComparison.OrdinalIgnoreCase)
                ? NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "ERNavmeshGen.dll"))
                : IntPtr.Zero);

        var jobs = new List<(string hkx, string outPath, string settings)>();
        foreach (string line in File.ReadLines(listFile))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] parts = line.Split('\t');
            if (parts.Length < 2) continue;
            jobs.Add((parts[0], parts[1], parts.Length > 2 ? parts[2] : ""));
        }

        int failed = 0;
        try
        {
            using NavGen gen = new NavGen(game);

            string loadedSettings = "\0"; // sentinel so the first job always loads
            foreach (var (hkx, outPath, settings) in jobs)
            {
                try
                {
                    // Only re-load settings when they change (n vs o alternate in a chunk).
                    if (settings != loadedSettings)
                    {
                        if (!string.IsNullOrEmpty(settings) && File.Exists(settings))
                            gen.LoadSnapshotFromJson(File.ReadAllText(settings));
                        loadedSettings = settings;
                    }

                    string? dir = Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    bool ok = gen.GenerateNavmesh(hkx, outPath, null) && File.Exists(outPath);
                    if (!ok) { failed++; Console.Error.WriteLine($"FAIL\t{hkx}\t{outPath}"); }
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine($"FAIL\t{hkx}\t{outPath}\t{ex.GetType().Name}: {ex.Message}");
                }
                Console.Out.WriteLine("PROG"); // live per-navmesh progress for the parent
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"worker fatal: {ex.GetType().Name}: {ex.Message}");
            return jobs.Count - 0; // whole chunk unusable
        }

        Console.Error.WriteLine($"worker done: {jobs.Count - failed}/{jobs.Count}");
        return failed;
    }
}
