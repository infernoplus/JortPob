using Microsoft.Scripting.Utils;
using SoulsFormats;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;

/* This exists for me to test if full voice acting will work properly before we get voice actors involved */
namespace JortPob.Common
{
    public class SAM
    {
        public static string Generate(Dialog.DialogRecord dialog, Dialog.DialogInfoRecord info, string line, string hashName, NpcContent npc)
        {
            // Get the exact location this file will be in
            string lineDir = $"{Const.CACHE_PATH}dialog\\{npc.race}\\{npc.sex}\\{dialog.id}\\{hashName}\\";
            string wavPath = $"{lineDir}{hashName}.wav";
            string wemPath = $"{lineDir}{hashName}.wem";

            try
            {
                // Create synth
                using (SpeechSynthesizer synthesizer = new())
                {
                    // Check if this audio file exists in the cache already // @TODO: ideally we generate a voice cache later but guh w/e filesystem check for now
                    if (System.IO.File.Exists(wemPath)) { return wemPath; }

                    if (npc.sex == NpcContent.Sex.Female) { synthesizer.SelectVoice("Microsoft Zira Desktop"); }
                    else { synthesizer.SelectVoice("Microsoft David Desktop"); }

                    // Make folder if doesn't exist (this is so ugly lmao)
                    if (!System.IO.Directory.Exists(lineDir)) { System.IO.Directory.CreateDirectory(lineDir); }

                    // Write 32bit 44100hz wav file (required format for wem)
                    synthesizer.SetOutputToWaveFile(wavPath, new SpeechAudioFormatInfo(44100, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                    synthesizer.Speak(line);
                }

                // Convert wav to wem
                // Setup paths, make folders
                string wwiseConsolePath = $"{Const.WWISE_PATH}WwiseConsole.exe";
                string xmlName = $"{hashName}.wsources";
                string xmlPath = $"{lineDir}{xmlName}";
                string xmlRelative = $"..\\dialog\\{npc.race}\\{npc.sex}\\{dialog.id}\\{hashName}\\{xmlName}";
                string projectDir = $"{Const.CACHE_PATH}wwise\\";
                string projectPath = $"{projectDir}wwise.wproj";

                // Create XML file
                string xmlRaw = $""""
                                <?xml version='1.0' encoding='UTF-8'?>
                                <ExternalSourcesList SchemaVersion="1" Root="{lineDir}"><Source Path="{hashName}.wav" Conversion="Vorbis Quality High" /></ExternalSourcesList>
                                """";
                File.WriteAllText(xmlPath, xmlRaw);

                // Create project if it doesn't exist
                if (!File.Exists(projectPath))
                {
                    if(Directory.Exists(projectDir)) { Directory.Delete(projectDir); } // creating a wwise proj requires the folder to not exist
                    ProcessStartInfo startInfo = new(wwiseConsolePath)
                    {
                        WorkingDirectory = Const.CACHE_PATH,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    startInfo.ArgumentList.AddRange(new string[] {"create-new-project", $"\"{projectPath}\"", "--platform", "Windows" });
                    using var process = Process.Start(startInfo);
                    process.WaitForExit();
                }

                // Call wwise console to convert wav to wem
                {
                    ProcessStartInfo startInfo = new(wwiseConsolePath)
                    {
                        RedirectStandardOutput = true,
                        WorkingDirectory = lineDir,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    startInfo.ArgumentList.AddRange(new string[] { "convert-external-source", $"\"{projectPath}\"", "--source-file", xmlRelative, "--output", "Windows", $"\"{lineDir}\"" });
                    using var process = Process.Start(startInfo);
                    process.WaitForExit();
                }
            }
            catch
            {
                Lort.Log($"## ERROR ## Failed to generate dialog {wavPath}", Lort.Type.Debug);
            }

            // Return wem path
            return wemPath;
        }
    }
}
