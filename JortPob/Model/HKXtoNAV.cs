using JortPob.Common;
using System;
using System.Diagnostics;
using System.IO;

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
            };

            Utility.ExecuteProcess(startInfo, timeoutMillis, line =>
            {
                if (onProgress != null && line.StartsWith("PROG", StringComparison.Ordinal)) { onProgress(); }
            });
        }

    }
}
