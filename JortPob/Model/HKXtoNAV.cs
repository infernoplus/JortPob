using JortPob.Common;
using System.Diagnostics;
using System.IO;

namespace JortPob.Model
{
    public partial class ModelConverter
    {
        public static void HKXtoNAV(string hkxPath, string navPath, string settingsPath)
        {
            string gamePath = Path.Combine(Const.ELDEN_PATH, @"game\eldenring.exe");
            ProcessStartInfo startInfo = new(Utility.ResourcePath(@"tools\Nav\ERNavmeshGenerator.exe"), $"-g \"{gamePath}\" -i \"{hkxPath}\" -o \"{navPath}\" -s \"{settingsPath}\"" )
            {
                WorkingDirectory = Utility.ResourcePath(@"tools\Nav"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            Utility.ExecuteProcess(startInfo, 60000);
        }
    }
}