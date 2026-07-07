using JortPob.Common;
using JortPob.Worker;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static JortPob.Dialog;

namespace JortPob
{
    public class SoundManager
    {
        private volatile int nextBankId;
        private readonly List<SoundBankInfo> banks;
        private readonly SoundBankGlobals globals;
        public readonly MainSoundBank main;
        public readonly MusicSoundBank music;

        private readonly List<SAMData> samQueue;

        public class SAMData
        {
            public readonly Dialog.DialogRecord dialog;
            public readonly Dialog.DialogInfoRecord info;
            public readonly string line, mp3;
            public readonly string hashName;
            public readonly CharacterContent npc;
            public SAMData(Dialog.DialogRecord dialog, Dialog.DialogInfoRecord info, string line, string mp3, string hashName, CharacterContent npc)
            {
                this.dialog = dialog;
                this.info = info;
                this.line = line;
                this.hashName = hashName;
                this.npc = npc;
            }
        }

        public SoundManager()
        {
            SAM.CreateProject(); // generate wwise project if it does not exist

            nextBankId = 100;
            banks = new();
            globals = new();
            samQueue = new();

            main = new(globals);
            music = new(globals);
        }

        // this generates the json file used for the VA manifest, which is used to link lines to npcs and info for the override system.
        // This is only generated in debug mode since it's really only useful for development,
        public void GenerateVAManifest()
        {
            // #if DEBUG
            var vaManifestContents = samQueue.Select(sam => new
            {
                line = new
                {
                    sam.line,
                    sam.info.type,
                    sam.info.race,
                    sam.info.rank,
                    gender = sam.info.sex,
                    sam.info.faction,
                    sam.info.job,
                    sam.info.cell,
                    sam.info.disposition,
                    sam.info.filters
                },
                npc = new
                {
                    sam.npc.name,
                    race = sam.npc.race,
                    sam.npc.rank,
                    gender = sam.npc.sex,
                    sam.npc.faction,
                    sam.npc.job,
                    sam.npc.services,
                    sam.npc.disposition,
                    sam.npc.essential,
                    sam.npc.reputation,
                    entity = (int)sam.npc.entity
                },
                hash = sam.hashName
            });

            var vaManifest = JsonConvert.SerializeObject(vaManifestContents);
            File.WriteAllText($"{Const.CACHE_PATH}/Manifest.json", vaManifest);
            // #endif
        }

        /* Either returns an existing bank meeting the requirements, or makes a new one */
        public SoundBankInfo GetBank(CharacterContent npc)
        {
            bool useCustom = Override.CheckCustomVoice(npc.id);
            bool isCreature = npc.race == CharacterContent.Race.Creature;

            foreach (SoundBankInfo bankInfo in banks)
            {
                if (useCustom && bankInfo.race == CharacterContent.Race.Custom && bankInfo.custom == npc.id && bankInfo.uses <= Const.MAX_ESD_PER_VCBNK)
                {
                    return bankInfo;
                }
                else if (isCreature && bankInfo.race == CharacterContent.Race.Creature && bankInfo.custom == npc.id && bankInfo.uses <= Const.MAX_ESD_PER_VCBNK)
                {
                    return bankInfo;
                }
                else if (bankInfo.race == npc.race && bankInfo.sex == npc.sex && bankInfo.uses <= Const.MAX_ESD_PER_VCBNK)
                {
                    return bankInfo;
                }
            }
            SoundBankInfo bnk;
            if (useCustom) { bnk = new(nextBankId++, CharacterContent.Race.Custom, npc.sex, new SoundBank(globals), npc.id); }
            else if (isCreature) { bnk = new(nextBankId++, CharacterContent.Race.Creature, npc.sex, new SoundBank(globals), npc.id); }
            else { bnk = new(nextBankId++, npc.race, npc.sex, new SoundBank(globals)); }
            banks.Add(bnk);
            return bnk;
        }

        public SoundBank.Sound FindSound(CharacterContent npc, int dialogInfo)
        {
            bool useCustom = Override.CheckCustomVoice(npc.id);
            bool isCreature = npc.race == CharacterContent.Race.Creature;

            if (useCustom)
            {
                return banks.Where(b => b.race == CharacterContent.Race.Custom && b.custom == npc.id)
                            .SelectMany(b => b.bank.sounds)
                            .FirstOrDefault(s => s.dialogInfo == dialogInfo, null);
            }
            else if (isCreature)
            {
                return banks.Where(b => b.race == CharacterContent.Race.Creature && b.custom == npc.id)
                            .SelectMany(b => b.bank.sounds)
                            .FirstOrDefault(s => s.dialogInfo == dialogInfo, null);
            }
            else
            {
                return banks.Where(b => b.race == npc.race && b.sex == npc.sex)
                    .SelectMany(b => b.bank.sounds)
                    .FirstOrDefault(s => s.dialogInfo == dialogInfo, null);
            }
        }

        /* Adds lines to a queue so we can do multithreaded tts gen on them */
        public string GenerateLine(DialogRecord dialog, DialogInfoRecord info, string line, string hashName, CharacterContent npc)
        {
            bool useCustom = Override.CheckCustomVoice(npc.id);
            bool isCreature = npc.race == CharacterContent.Race.Creature;

            SAMData dat = new(dialog, info, line, info.mp3, hashName, npc);
            samQueue.Add(dat);

            if (useCustom) { return Path.Combine(Const.CACHE_PATH, @$"dialog\{CharacterContent.Race.Custom}\{npc.id}\{dialog.id}\{hashName}\{hashName}.wem"); }
            else if (isCreature) { return Path.Combine(Const.CACHE_PATH, @$"dialog\{CharacterContent.Race.Creature}\{npc.id}\{dialog.id}\{hashName}\{hashName}.wem"); }
            else { return Path.Combine(Const.CACHE_PATH, @$"dialog\{npc.race}\{npc.sex}\{dialog.id}\{hashName}\{hashName}.wem"); }
        }

        /* Writes all soundbanks to given dir */
        /* This has been broken up into multiple stages to try and improve performance */
        /* 1 - Generate all JSON for bnks in multiple threads */
        /* 2 - Write all WEM files to their proper locations in a single thread */
        /* 3 - Run BNK2JSON on all JSON files to compile bnks in multiple threads */
        public void Write()
        {
            if (Const.DEBUG_SKIP_SOUND) { return; } // worlds largest time save

            GenerateVAManifest();

            SamWorker.Go(samQueue); // actually generate tts and convert wems

            Lort.Log($"Preprocessing {banks.Count()} BNKs...", Lort.Type.Main);
            Lort.NewTask("Preprocessing BNKs", banks.Count());

            ConcurrentBag<Dictionary<uint, string>> wemsToWrite = new();

            Parallel.ForEach(banks, bankInfo =>
            {
                Dictionary<uint, string> wems = bankInfo.bank.WriteSources(bankInfo.id);
                wemsToWrite.Add(wems);
                Lort.TaskIterate();
            });

            var allWemsToWrite = wemsToWrite
                    .SelectMany(dict => dict) // Flatten the list of dictionaries into a single sequence of KeyValuePair
                    .GroupBy(kvp => kvp.Key)  // Group the KeyValuePairs by their key
                    .ToDictionary(
                        group => group.Key,         // The key for the new dictionary is the group's key
                        group => group.First().Value // The value is the value of the first item in the group (e.g., from the first dictionary it appeared in)
                    );

            Lort.Log($"Writing {allWemsToWrite.Count()} WEMs...", Lort.Type.Main);
            Lort.NewTask("Writing WEMs", allWemsToWrite.Count());

            foreach (var kvp in allWemsToWrite)
            {
                string wemSrcPath = kvp.Value;
                string wemTgtPath = Path.Combine(Const.OUTPUT_PATH, "sd", "enus", "wem", @$"{kvp.Key.ToString("D9").Substring(0, 2)}\{kvp.Key:D9}.wem");
                Directory.CreateDirectory(Path.GetDirectoryName(wemTgtPath));
                if (File.Exists(wemTgtPath)) { File.Delete(wemTgtPath); }
                File.Copy(wemSrcPath, wemTgtPath);
                Lort.TaskIterate();
            }

            Lort.Log($"Writing {banks.Count() + 2} BNKs...", Lort.Type.Main);
            Lort.NewTask("Writing BNKs", banks.Count() + 2);

            Task mainSoundBank = Task.Run(() =>
            {
                main.Write();
                Lort.TaskIterate();
            });

            Task musicSoundBank = Task.Run(() =>
            {
                music.Write();
                Lort.TaskIterate();
            });

            Task otherSoundBanks = Task.Run(() =>
            {
                Parallel.ForEach(banks, bankInfo =>
                {
                    string bnkDir = Path.Combine(Const.OUTPUT_PATH, "sd", "enus", $"vc{bankInfo.id:D3}");
                    string bnkPath = $@"{bnkDir}.bnk";
                    string bnkRebuiltPath = $@"{bnkDir}.created.bnk";

                    if (Const.DEBUG_REUSE_FILES && File.Exists(bnkPath)) { Lort.TaskIterate(); return; } // if debug_reuse is on, skip if file already created

                    ProcessStartInfo startInfo = new(Utility.ResourcePath(@"tools\Bnk2Json\bnk2json.exe"), $"\"{bnkDir}\"")
                    {
                        WorkingDirectory = Utility.ResourcePath(@"tools\Bnk2Json"),
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    Utility.ExecuteProcess(startInfo, false);

                    if (File.Exists(bnkPath)) { File.Delete(bnkPath); }
                    File.Move(bnkRebuiltPath, bnkPath);
                    Lort.TaskIterate();
                });
            });

            Task.WhenAll(mainSoundBank, musicSoundBank, otherSoundBanks).Wait();
        }

        public class SoundBankGlobals
        {
            private readonly object bnkIdLock = new(), headerIdLock = new(), sourceIdLock = new();
            private readonly uint[] usedHeaderIds, usedBnkIds, usedSourceIds;  // list of every single used bnk id (of the multiple id types) in stock elden ring. bnk ids are global so we want to avoid collisions
            private readonly List<uint> bnkCallIds; // list of every generating "play" or "stop" bnk id, these are not sequential like other ids so we track them here
            private volatile uint nextBnkId, nextHeaderId, nextSourceId;  // do not use directly, call NextID()
            private volatile uint nextRowId;  // increments by 10

            public SoundBankGlobals()
            {
                uint[] LoadIdList(string path)
                {
                    string[] lines = File.ReadAllLines(path);
                    return lines.Select(uint.Parse).ToArray();
                }

                usedBnkIds = LoadIdList(Utility.ResourcePath(@"sound\all_used_bnk_ids.txt"));
                usedHeaderIds = LoadIdList(Utility.ResourcePath(@"sound\all_used_bnk_header_ids.txt"));
                usedSourceIds = LoadIdList(Utility.ResourcePath(@"sound\all_used_source_ids.txt"));

                bnkCallIds = new();

                nextHeaderId = 100;
                nextSourceId = 100000000;
                nextBnkId = 1000000;
                nextRowId = 20000000;
            }

            public uint[] GetEventBnkId(string sfxType = "v")
            {
                uint[] TryGetNextCallIds(uint rowId)
                {
                    byte[] playCallBytes = Encoding.ASCII.GetBytes($"Play_{sfxType}{rowId.ToString("D8")}0".ToLower());
                    byte[] stopCallBytes = Encoding.ASCII.GetBytes($"Stop_{sfxType}{rowId.ToString("D8")}0".ToLower());

                    uint playCallId = Utility.FNV1_32(playCallBytes);
                    uint stopCallId = Utility.FNV1_32(stopCallBytes);

                    return [rowId, playCallId, stopCallId];
                }

                uint[] ids = TryGetNextCallIds(NextRowId());
                while (usedBnkIds.Contains(ids[1]) || usedBnkIds.Contains(ids[2]))
                {
                    ids = TryGetNextCallIds(NextRowId());
                }

                bnkCallIds.Add(ids[1]);
                bnkCallIds.Add(ids[2]);

                return ids;
            }

            public uint NextBnkId()
            {
                lock (bnkIdLock)
                {
                    while (bnkCallIds.Contains(nextBnkId) || usedBnkIds.Contains(nextBnkId))
                    {
                        nextBnkId++;
                    }

                    return nextBnkId++;
                }
            }

            public uint NextHeaderId()
            {
                lock (headerIdLock)
                {
                    while (usedHeaderIds.Contains(nextHeaderId))
                    {
                        nextHeaderId++;
                    }

                    return nextHeaderId++;
                }
            }

            public uint NextSourceId()
            {
                lock (sourceIdLock)
                {
                    while (usedSourceIds.Contains(nextSourceId))
                    {
                        nextSourceId++;
                    }

                    return nextSourceId++;
                }
            }

            public uint NextRowId()
            {
                return Interlocked.Add(ref nextRowId, 10);
            }
        }

        public record SoundBankInfo
        (
            int id,               // vc###.bnk id
            NpcContent.Race race, // race of npcs that use this bank
            NpcContent.Sex sex,
            SoundBank bank,
            string custom = null  // (usually null)!  If we use the Race enum 'Creature' or 'Custom' then this string is the id of the custom voice role used here
        )
        {
            public int uses { get; set; }
        }
    }
}
