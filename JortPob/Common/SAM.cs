using Microsoft.Scripting.Utils;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;

/* This exists for me to test if full voice acting will work properly before we get voice actors involved */
namespace JortPob.Common
{
    public class SAM
    {
        public static List<string> VAHashes = new();

        /* Creates the WWISE project. Made this a seperate call so that we don't have multiple threads trying to do this at the same time! */
        public static void CreateProject()
        {
            if (Const.DEBUG_SKIP_SOUND) { return; }

            string wwiseConsolePath = Path.Combine(Const.WWISE_PATH, "WwiseConsole.exe");
            string projectDir = Path.Combine(Const.CACHE_PATH, "wwise");
            string projectPath = Path.Combine(projectDir, "wwise.wproj");

            // Create project if it doesn't exist
            if (!File.Exists(projectPath))
            {
                if (Directory.Exists(projectDir)) { Directory.Delete(projectDir); } // creating a wwise proj requires the folder to not exist
                ProcessStartInfo startInfo = new(wwiseConsolePath)
                {
                    WorkingDirectory = Const.CACHE_PATH,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.AddRange(["create-new-project", $"\"{projectPath}\"", "--platform", "Windows"]);
                Utility.ExecuteProcess(startInfo);
            }

            var modRoot = Directory.GetParent(Const.OUTPUT_PATH)?.Parent.FullName;

            VAHashes.AddRange(Directory.EnumerateFiles(modRoot + "/lines"));
        }

        /* DO NOT USE */
        public static string Generate(Dialog.DialogRecord dialog, Dialog.DialogInfoRecord info, string line, string hashName, NpcContent npc)
        {
            // Get the exact location this file will be in
            bool useCustom = Override.CheckCustomVoice(npc.id);
            bool isCreature = npc.race == CharacterContent.Race.Creature;

            string lineDir;
            if (useCustom) { lineDir = Path.Combine(Const.CACHE_PATH, "dialog", CharacterContent.Race.Custom.ToString(), npc.id, dialog.id.ToString(), hashName); }
            else if (isCreature) { lineDir = Path.Combine(Const.CACHE_PATH, "dialog", CharacterContent.Race.Creature.ToString(), npc.id, dialog.id.ToString(), hashName); }
            else { lineDir = Path.Combine(Const.CACHE_PATH, "dialog", npc.race.ToString(), npc.sex.ToString(), dialog.id.ToString(), hashName); }

            string wavPath = $"{lineDir}{hashName}.wav";
            string wemPath = $"{lineDir}{hashName}.wem";

            for (int retry = 0; retry < Const.SAM_MAX_RETRY; retry++)
            {
                try
                {
                    // Create synth
                    using (SpeechSynthesizer synthesizer = new())
                    {
                        // Check if this audio file exists in the cache already // @TODO: ideally we generate a voice cache later but guh w/e filesystem check for now
                        if (System.IO.File.Exists(wemPath)) { return wemPath; }

                        if (npc.sex == CharacterContent.Sex.Female) { synthesizer.SelectVoice("Microsoft Zira Desktop"); }
                        else { synthesizer.SelectVoice("Microsoft David Desktop"); }

                        // Make folder if doesn't exist (this is so ugly lmao)
                        if (!System.IO.Directory.Exists(lineDir)) { System.IO.Directory.CreateDirectory(lineDir); }

                        // Write 32bit 44100hz wav file (required format for wem)
                        synthesizer.SetOutputToWaveFile(wavPath, new SpeechAudioFormatInfo(44100, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                        synthesizer.Speak(line);
                    }

                    // Convert wav to wem
                    // Setup paths, make folders
                    string wwiseConsolePath = Path.Combine(Const.WWISE_PATH, "WwiseConsole.exe");
                    string xmlName = $"{hashName}.wsources";
                    string xmlPath = $"{lineDir}{xmlName}";
                    string xmlRelative = @$"..\dialog\{npc.race}\{npc.sex}\{dialog.id}\{hashName}\{xmlName}";
                    string projectDir = Path.Combine(Const.CACHE_PATH, @"wwise\");
                    string projectPath = $"{projectDir}wwise.wproj";

                    // Create XML file
                    string xmlRaw = $""""
                                <?xml version='1.0' encoding='UTF-8'?>
                                <ExternalSourcesList SchemaVersion="1" Root="{lineDir}"><Source Path="{hashName}.wav" Conversion="Vorbis Quality High" /></ExternalSourcesList>
                                """";
                    File.WriteAllText(xmlPath, xmlRaw);

                    // Call wwise console to convert wav to wem
                    {
                        ProcessStartInfo startInfo = new(wwiseConsolePath)
                        {
                            RedirectStandardOutput = true,
                            WorkingDirectory = lineDir,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        startInfo.ArgumentList.AddRange(["convert-external-source", $"\"{projectPath}\"", "--source-file", xmlRelative, "--output", "Windows", $"\"{lineDir}\""]);
                        Utility.ExecuteProcess(startInfo);
                    }
                }
                catch
                {
                    Lort.Log($"## ERROR ## Failed to generate dialog {wavPath}", Lort.Type.Debug);
                }

                if (File.Exists(wemPath)) { break; } // if the file is created successfully we don't need to retry.
            }

            if (!File.Exists(wemPath))
            {
                throw new Exception($"Failed to generated line {wemPath} despite {Const.SAM_MAX_RETRY} retry attempts.");
            }

            // Return wem path
            return wemPath;
        }

        /* Generate TTS of dialog via flite and convert to WEM */
        public static string GenerateAlt(Dialog.DialogRecord dialog, Dialog.DialogInfoRecord info, string line, string hashName, CharacterContent npc)
        {
            // Define paths
            bool useCustom = Override.CheckCustomVoice(npc.id);
            bool isCreature = npc.race == CharacterContent.Race.Creature;

            string lineDir;
            if (useCustom) { lineDir = Path.Combine(Const.CACHE_PATH, "dialog", CharacterContent.Race.Custom.ToString(), npc.id, dialog.id.ToString(), hashName); }
            else if (isCreature) { lineDir = Path.Combine(Const.CACHE_PATH, "dialog", CharacterContent.Race.Creature.ToString(), npc.id, dialog.id.ToString(), hashName); }
            else { lineDir = Path.Combine(Const.CACHE_PATH, "dialog", npc.race.ToString(), npc.sex.ToString(), dialog.id.ToString(), hashName); }

            string wavPath = Path.Combine(lineDir, $"{hashName}.wav");
            string wemPath = Path.Combine(lineDir, $"{hashName}.wem");
            string flitePath = Path.Combine(Environment.CurrentDirectory, "Resources", "tts", "flite.exe");
            bool hasVA = VAHashes.Count(f => f.Contains(hashName)) > 0;

            string safeText;
            if (useCustom || isCreature) { safeText = MakeSafe($"{npc.id} says {line}"); }
            else { safeText = MakeSafe($"{npc.race.ToString()} says {line}"); }

            // Use a loop to handle retries
            for (int retry = 0; retry < Const.SAM_MAX_RETRY; retry++)
            {
                if (File.Exists(wemPath))
                {
                    // Audio file already exists in cache, no need to retry
                    return wemPath;
                }

                try
                {
                    // 1. Setup Environment
                    if (!Directory.Exists(lineDir))
                    {
                        Directory.CreateDirectory(lineDir);
                    }

                    if (hasVA)
                    {
                        File.Copy(VAHashes.First(f => f.Contains(hashName)), wavPath, true);
                        Lort.Log($"Line {hashName}, was replaced with a VA line", Lort.Type.Debug);
                    }
                    else
                    {// 2. Generate WAV (Text-to-Speech)
                     // string ssmlLine = $"<speak>{line}<break time='500ms'/></speak>";
                        string voice = npc.sex == CharacterContent.Sex.Female ? "slt" : "rms";
                        string args = $"-t \"{safeText}\" -voice {voice} \"{wavPath}\"";

                        ProcessStartInfo fliteStartInfo = new(flitePath)
                        {
                            Arguments = args,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true, // Added for better error capture
                            WorkingDirectory = lineDir,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        // The helper method handles the execution, timeout, kill, and exit code check
                        Utility.ExecuteProcess(fliteStartInfo);
                    }

                    // --- 3. Convert WAV to WEM (Wwise Console) ---

                    string wwiseConsolePath = Path.Combine(Const.WWISE_PATH, "WwiseConsole.exe");
                    string xmlName = $"{hashName}.wsources";
                    string xmlPath = Path.Combine(lineDir, xmlName);
                    string projectDir = Path.Combine(Const.CACHE_PATH, "wwise");
                    string projectPath = Path.Combine(projectDir, "wwise.wproj");

                    // Create XML file
                    string xmlRaw = $"""
                        <?xml version='1.0' encoding='UTF-8'?>
                        <ExternalSourcesList SchemaVersion="1" Root="{lineDir}"><Source Path="{hashName}.wav" Conversion="Vorbis Quality High" /></ExternalSourcesList>
                        """;
                    File.WriteAllText(xmlPath, xmlRaw);

                    // Convert wav to wem
                    ProcessStartInfo convertInfo = new(wwiseConsolePath)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = lineDir,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    string xmlRelative;
                    if (useCustom) { xmlRelative = Path.Combine("..", "dialog", CharacterContent.Race.Custom.ToString(), npc.id, dialog.id.ToString(), hashName, xmlName); }
                    else if (isCreature) { xmlRelative = Path.Combine("..", "dialog", CharacterContent.Race.Creature.ToString(), npc.id, dialog.id.ToString(), hashName, xmlName); }
                    else { xmlRelative = Path.Combine("..", "dialog", npc.race.ToString(), npc.sex.ToString(), dialog.id.ToString(), hashName, xmlName); }
                    convertInfo.ArgumentList.AddRange(["convert-external-source", $"\"{projectPath}\"", "--source-file", xmlRelative, "--output", "Windows", $"\"{lineDir}\""]);
                    Utility.ExecuteProcess(convertInfo);

                    // If we reach here, both processes completed successfully (ExitCode 0)
                    if (File.Exists(wemPath))
                    {
                        return wemPath;
                    }

                    // If processes succeeded but the file isn't there, something is wrong, we retry
                    throw new FileNotFoundException($"WEM file was not found after successful conversion: {wemPath}");
                }
                catch
                {
                    // Keep retrying. Don't spam log after every failed generation as it's bloat.
                    // If we fail up to MAX_RETRY then we throw an exception and print log.
                }
            }

            // Final check after all retries
            if (!File.Exists(wemPath))
            {
                Lort.Log($"Failed to generate line {wemPath}. With text <{safeText}> -- despite {Const.SAM_MAX_RETRY} retry attempts.", Lort.Type.Debug);
                throw new($"Failed to generate line {wemPath}. With text <{safeText}> -- despite {Const.SAM_MAX_RETRY} retry attempts.");
            }

            // Should be unreachable if the File.Exists check above is correct, but included for completeness.
            return wemPath;
        }

        private static readonly Regex AnsiRegex =
            new Regex("\u001b\\[[\\d;]*[A-HJ-NP-Zf-m]?", RegexOptions.Compiled);

        /// <summary>
        /// Checks a string for safety and returns a sanitized version based on the specified mode.
        /// </summary>
        /// <param name="input">The string to be checked and fixed.</param>
        /// <param name="mode">The level of safety required (ConsolePrintSafe or PathSafe).</param>
        /// <returns>A safe string.</returns>
        public static string MakeSafe(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return "empty string";
            }

            // 1. Strip ANSI/VT100 Escape Sequences (Always applied)
            string sanitized = AnsiRegex.Replace(input, string.Empty);

            // 2. Filter Control Characters (Always applied)
            StringBuilder sb = new(sanitized.Length);

            foreach (char c in sanitized)
            {
                if (char.IsControl(c))
                {
                    // Allow common, non-disruptive formatting characters in the console context
                    if (c == '\t' || c == '\n' || c == '\r')
                    {
                        sb.Append(c);
                    }
                    // All other control characters are skipped/removed
                }
                else
                {
                    sb.Append(c);
                }
            }
            sanitized = sb.ToString();
            sb.Clear();

            // A. Remove Invalid File Name Characters
            // These characters are illegal in file names on Windows and many other systems
            char[] invalidChars = Path.GetInvalidFileNameChars();

            // Note: Path.GetInvalidFileNameChars() includes path separators ('\' and '/') 
            // but we often need to allow them if the input is a full relative/absolute path. 
            // Since the user asked to handle paths, we'll focus on the illegal chars for segments.

            foreach (char c in sanitized)
            {
                if (Array.IndexOf(invalidChars, c) == -1)
                {
                    sb.Append(c);
                }
                else
                {
                    // Optionally replace illegal chars with an underscore for visibility
                    // sb.Append('_');
                }
            }
            sanitized = sb.ToString();

            // B. Remove Directory Traversal Attempts (e.g., "name/../secret.txt")
            // This prevents an attacker from moving the file creation location.
            // This is a simple but important check. More complex validation might be needed.
            sanitized = sanitized
                .Replace("..\\", "") // Windows
                .Replace("../", "")  // Unix/Linux
                .Replace("./", "")   // Current directory (optional cleanup)
                .Replace(".\\", "");


            sanitized = sanitized.Trim();
            while (sanitized.Contains("  ")) { sanitized = sanitized.Replace("  ", " "); }

            return sanitized;
        }
    }
}