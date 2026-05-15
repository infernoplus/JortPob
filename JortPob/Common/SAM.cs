using Microsoft.Scripting.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

/* This exists for me to test if full voice acting will work properly before we get voice actors involved */
namespace JortPob.Common
{
    public class SAM
    {
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
        }

        // Helper record for storing dialog creation data
        public record GenerateAltEntry
        {
            public required Dialog.DialogRecord Dialog;

            public required Dialog.DialogInfoRecord Info;

            public required string Line;

            public required string HashName;
            
            public required CharacterContent Npc;

            public bool OverrideSex;

            public bool AltVariant;

            public string tempHashName;
            
            [SetsRequiredMembers]
            public GenerateAltEntry(
                Dialog.DialogRecord Dialog,
                Dialog.DialogInfoRecord Info,
                string Line,
                string HashName,
                CharacterContent Npc,
                bool OverrideSex = false,
                bool AltVariant = false
            )
            {
                this.Dialog = Dialog;
                this.Info = Info;
                this.Line = Line;
                this.HashName = HashName;
                this.Npc = Npc;
                this.OverrideSex = OverrideSex;
                this.AltVariant =  AltVariant;
                
                int lineIter = HashName.IndexOf('+');
                tempHashName = HashName[..lineIter] + Info.sex.ToString() +  HashName[lineIter..]; // Temp file name for batch processing by dialog sex type
                
            }
            
            public string GetLineDir()
            {
                string baseHash = AltVariant ? HashName[..^4] : HashName;
                
                if (Override.CheckCustomVoice(Npc.id))
                    return Path.Combine(Const.CACHE_PATH, "dialog", "Custom", Npc.id, Dialog.id.ToString(), baseHash);

                if (Npc.race == CharacterContent.Race.Creature)
                    return Path.Combine(Const.CACHE_PATH, "dialog", "Creature", Npc.id, Dialog.id.ToString(), baseHash);

                // Normal case
                string sexFolder = OverrideSex ? "Female" : Npc.sex.ToString();

                return Path.Combine(Const.CACHE_PATH, "dialog", Npc.race.ToString(), sexFolder, Dialog.id.ToString(), baseHash);
            }

            public string WemPath()
            {
                string baseHash = AltVariant ? HashName[..^4] : HashName; 

                return Path.Combine(GetLineDir(), $"{baseHash}.wem");
            }
                
            public bool WemExists => File.Exists(WemPath());
            
        } 
        
        /// <summary>
        /// Batch process alt dialog entries
        /// </summary>
        /// <param name="entries"></param>
        /// <returns></returns>
        public static List<string> GenerateAltBatch(List<GenerateAltEntry> entries) 
        { 
            string batchDir = Path.Combine(Const.CACHE_PATH, "batch_temp");
            
            if (Directory.Exists(batchDir))
                Directory.Delete(batchDir, recursive: true);

            Directory.CreateDirectory(batchDir);

            string wwiseConsolePath = Path.Combine(Const.WWISE_PATH, "WwiseConsole.exe");
            string projectPath = Path.Combine(Const.CACHE_PATH, "wwise", "wwise.wproj");
            string batchXmlPath = Path.Combine(batchDir, "batch.wsources");

            try
            {
                // Since NPCs can share common dialog we have to adjust for batch generation
                var allEntries = entries.Where(e => !e.WemExists).ToList();
                
                var uniqueHashes = allEntries.GroupBy(e => e.HashName)
                    .Select(g => 
                        g.FirstOrDefault(e => e.Info.sex == CharacterContent.Sex.Any && e.Npc.sex == CharacterContent.Sex.Male) ?? g.First())
                    .ToList();
                
                var entryLookup = entries.GroupBy(e => e.HashName).ToDictionary(k => k.Key, k=> k.ToList());
                
                Lort.NewTask("Generating TTS", uniqueHashes.Count(e => !e.WemExists));

                FLiteWrapper.FliteInit(); // init flite synth 
                
                ConcurrentBag<GenerateAltEntry> altTTS = new();
                
                Parallel.ForEach(uniqueHashes,
                    new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                    entry =>
                    {
                        bool useCustom = Override.CheckCustomVoice(entry.Npc.id);
                        bool isCreature = entry.Npc.race == CharacterContent.Race.Creature;
                        
                        string safeText;
                        if (useCustom || isCreature)
                        {
                            safeText = MakeSafe($"{entry.Npc.id} says {entry.Line}"); // change later to skip tts all together
                        }
                        else
                        {
                            safeText = MakeSafe(entry.Line); // Generic lines say the wrong race so removed...
                        } 
                        
                        string voice = entry.Npc.sex == CharacterContent.Sex.Female ? "slt" : "rms";
                        
                        FLiteWrapper.Synthesize(safeText, voice,
                            Path.Combine(batchDir, $"{entry.tempHashName}.wav"));
                        
                        if (entry.Info.sex == CharacterContent.Sex.Any && entry.Npc.sex == CharacterContent.Sex.Male)
                        {
                            bool npcExistsForDialog = entryLookup.GetValueOrDefault(entry.HashName, []).Any(e => e.Npc.sex == CharacterContent.Sex.Female);
                            
                            // Synthesize female variant of generic line
                            if (npcExistsForDialog)
                            {
                                // create a female tts file for the dialog
                                FLiteWrapper.Synthesize(safeText, "slt",
                                    Path.Combine(batchDir, $"{entry.tempHashName}_slt.wav"));

                                if (!File.Exists(Path.Combine(batchDir, $"{entry.tempHashName}_slt.wav")))
                                    Lort.Log($"FLite produced no output for generic alt: {entry.tempHashName}",
                                        Lort.Type.Debug);

                                altTTS.Add(new GenerateAltEntry(entry.Dialog, entry.Info, entry.Line,
                                    $"{entry.HashName}_slt", entry.Npc, true, true));
                            }
                        }
                        
                        if (!File.Exists(Path.Combine(batchDir, $"{entry.tempHashName}.wav")))
                            Lort.Log($"FLite produced no output for: {entry.tempHashName}", Lort.Type.Debug);

                        Lort.TaskIterate();
                    });
                
                uniqueHashes.AddRange(altTTS.ToList());
                
                var needsConversion = uniqueHashes
                    .Where(e => !e.WemExists && File.Exists(Path.Combine(batchDir, $"{e.tempHashName}.wav")))
                    .ToList();
                
                int wwisePass = 0;
                const int maxPasses = 10;
                const int maxBatchSize = 1000; // wwise can handle about this many in a batch before it starts to not like me...
                
                Lort.NewTask("WWISE Batch PASS", maxPasses);
                
                while (needsConversion.Any() && wwisePass < maxPasses)
                {
                    wwisePass++;
                    Lort.Log($"PASS: {wwisePass}, Files to convert: {needsConversion.Count}", Lort.Type.Main);
                    
                    foreach(GenerateAltEntry[] batch in needsConversion.Chunk(maxBatchSize))
                    {
                        string sources = string.Join("\n    ", batch.Select(e =>
                            $"<Source Path=\"{Path.Combine(batchDir, e.tempHashName)}.wav\" Conversion=\"Vorbis Quality High\" />"));

                        string xmlRaw = $"""
                                         <?xml version='1.0' encoding='UTF-8'?>
                                         <ExternalSourcesList SchemaVersion="1" Root=".">
                                             {sources}
                                         </ExternalSourcesList>
                                         """;

                        File.WriteAllText(batchXmlPath, xmlRaw);
                        
                        ProcessStartInfo convertInfo = new(wwiseConsolePath)
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            WorkingDirectory = batchDir,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        
                        convertInfo.ArgumentList.AddRange([
                            "convert-external-source",
                            projectPath,
                            "--source-file", batchXmlPath,
                            "--output", "Windows",
                            batchDir
                        ]);
                        
                        Utility.ExecuteProcess(convertInfo, -1); // wait until finished large batches could take a bit and 15 seconds is too short
                        
                        foreach (var entry in batch)
                        {
                            string batchWem = Path.Combine(batchDir, $"{entry.tempHashName}.wem"); // Get the converted temporary wem will be renamed when copied
                            
                            if (!File.Exists(batchWem))
                            {
                                Lort.Log($"WEM not found after conversion: {entry.tempHashName}", Lort.Type.Debug);
                                continue;
                            }
                            
                            //check if alt exists and if we should copy the female alt sound
                            bool altFound = entry.AltVariant;
                            string hashBase = altFound ? entry.HashName[..^4] : entry.HashName; // get base name 
                            
                            // Get all npcs that use this dialog
                            var npcsWithDialog = entryLookup.GetValueOrDefault(hashBase, []);
                            
                            foreach (var item in npcsWithDialog)
                            {
                                bool shouldCopy = false;

                                if (altFound)
                                {
                                    // Female alt found should copy
                                    shouldCopy = (item.Npc.sex == CharacterContent.Sex.Female);
                                }
                                else
                                {
                                    // If the dialog is generic check if the npc can have the file
                                    if (item.Info.sex == CharacterContent.Sex.Any)
                                    {
                                        shouldCopy = item.Npc.sex == CharacterContent.Sex.Male 
                                                      || (entry.Npc.sex == CharacterContent.Sex.Female && item.Npc.sex == CharacterContent.Sex.Female);
                                    }
                                    else
                                    {
                                        //check if the sexed dialog matches the npcs sex 
                                        shouldCopy = (item.Npc.sex == item.Info.sex);
                                    }
                                }  
                                
                                if (shouldCopy)
                                {
                                    Directory.CreateDirectory(item.GetLineDir());
                                    try
                                    {
                                        File.Copy(batchWem, item.WemPath(), true);
                                    }
                                    catch (Exception e)
                                    {
                                        Lort.Log($"Copy failed for {item.GetLineDir()}: {e.Message}", Lort.Type.Debug);
                                    }
                                }
                            }
                            File.Delete(batchWem);
                        }
                    }
                    
                    // update files that still need to be converted. Shouldn't happen but if wwise times out a batch could fail
                    needsConversion = uniqueHashes
                        .Where(e =>
                        {
                            if (e.AltVariant)
                            {
                                string baseHash = e.HashName[..^4];
                                return entryLookup.GetValueOrDefault(baseHash, [])
                                    .Where(x => x.Npc.sex == CharacterContent.Sex.Female)
                                    .Any(x => !x.WemExists);
                            }
                            return !e.WemExists && File.Exists(Path.Combine(batchDir, $"{e.HashName}.wav"));
                        })
                        .ToList();
                    
                    if(needsConversion.Any())
                        Lort.Log($"Remaining:  {needsConversion.Count}", Lort.Type.Main); // Uh-oh stinky
                    
                    Lort.TaskIterate();
                }
                
                if(needsConversion.Any())
                    Lort.Log($"Failed to convert: {needsConversion.Count}",  Lort.Type.Main);
                
                Lort.Log($"Your Audio Files have been Jortted Sucessfully!!", Lort.Type.Main);

                return entries.Select(e => e.WemPath()).ToList();
            }
            finally
            {
                //regardless of what happens clean up flite and delete batch directory
                FLiteWrapper.Cleanup(); 
                if (Directory.Exists(batchDir))
                    Directory.Delete(batchDir, recursive: true);
            }
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
