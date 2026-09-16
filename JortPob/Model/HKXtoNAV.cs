using JortPob.Common;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace JortPob.Model
{
    public partial class ModelConverter
    {
        public static void HKXtoNAV(string listFile, int timeoutMillis, Action onProgress)
        {
            string gamePath = Path.Combine(Const.ELDEN_PATH, @"game\eldenring.exe");
            string navDir = Utility.ResourcePath(@"tools\Nav");
            ProcessStartInfo startInfo = new(Path.Combine(navDir, "NavGenWorker.exe"),
                $"--game \"{gamePath}\" --list \"{listFile}\"")
            {
                WorkingDirectory = navDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start process: {startInfo.FileName}");

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                if (onProgress != null && e.Data.StartsWith("PROG", StringComparison.Ordinal))
                    onProgress();
            };
            StringBuilder stderr = new();
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (stderr) stderr.AppendLine(e.Data); };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            int effectiveTimeout =
                timeoutMillis == 0 ? Const.DEFAULT_PROCESS_TIMEOUT :
                timeoutMillis < 0  ? Timeout.Infinite :
                                     timeoutMillis;

            if (!process.WaitForExit(effectiveTimeout))
            {
                try { process.Kill(entireProcessTree: true); process.WaitForExit(5000); }
                catch (InvalidOperationException) { /* already exited */ }
                throw new TimeoutException($"Process timed out and was killed: {startInfo.FileName}");
            }
            process.WaitForExit();

            // log failure
            if (process.ExitCode != 0)
            {
                string detail;
                lock (stderr) detail = stderr.ToString();
                throw new ApplicationException(
                    $"NavGenWorker exited {process.ExitCode} (failed navmeshes). {detail}");
            }
        }
    }
}
