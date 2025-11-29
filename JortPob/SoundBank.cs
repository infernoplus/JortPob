using JortPob.Common;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using static JortPob.SoundManager;

namespace JortPob
{
    public class SoundBank
    {
        private readonly SoundBankGlobals globals;
        public readonly List<Sound> sounds;

        public SoundBank(SoundBankGlobals globals)
        {
            this.globals = globals;
            sounds = new();
        }


        // Returns the row id of the sound you add
        public int AddSound(string file, int dialogInfo, string transcript)
        {
            uint[] ids = globals.GetEventBnkId();

            Sound sound = new(dialogInfo, ids[0], ids[1], ids[2], transcript, file, globals.NextSourceId());
            sounds.Add(sound);

            return (int)ids[0];
        }

        // Same as above but we set a specific rowId to use. For split text talks
        public int AddSound(string file, int dialogInfo, string transcript, uint rowId)
        {
            byte[] playCallBytes = Encoding.ASCII.GetBytes($"Play_v{rowId.ToString("D8")}0".ToLower());
            byte[] stopCallBytes = Encoding.ASCII.GetBytes($"Stop_v{rowId.ToString("D8")}0".ToLower());
            uint playCallId = Utility.FNV1_32(playCallBytes);
            uint stopCallId = Utility.FNV1_32(stopCallBytes);

            Sound sound = new(dialogInfo, rowId, playCallId, stopCallId, transcript, file, globals.NextSourceId());
            sounds.Add(sound);

            return (int)rowId;
        }

        public int AddSound(SoundBank.Sound snd)
        {
            sounds.Add(snd);
            return (int)snd.row;
        }

        /* Generates and writes JSON, then calls bnk2json to build the bnk */
        public void Write(string dir, int id)
        {
            JsonNode json = JsonNode.Parse(System.IO.File.ReadAllText(Utility.ResourcePath(@"sound\bnk_template.json")));
            JsonArray sections = json["sections"].AsArray();
            JsonNode BKHD = sections[0]["BKHD"];
            JsonNode HIRC = sections[1]["HIRC"];
            JsonArray objects = HIRC["objects"].AsArray();

            JsonNode templatePlay = objects[0]; objects.RemoveAt(0);
            JsonNode templatePlayCall = objects[0]; objects.RemoveAt(0);
            JsonNode templateStop = objects[0]; objects.RemoveAt(0);
            JsonNode templateStopCall = objects[0]; objects.RemoveAt(0);
            JsonNode templateData = objects[0]; objects.RemoveAt(0);

            BKHD["bank_id"] = globals.NextHeaderId();

            foreach (Sound sound in sounds)
            {
                JsonNode soundPlayEvent = templatePlay.DeepClone();
                JsonNode soundPlayCall = templatePlayCall.DeepClone();
                JsonNode soundStopEvent = templateStop.DeepClone();
                JsonNode soundStopCall = templateStopCall.DeepClone();
                JsonNode soundData = templateData.DeepClone();

                uint sourceId = sound.source;
                soundData["id"] = globals.NextBnkId();
                soundData["object"]["Sound"]["bank_source_data"]["media_information"]["source_id"] = sourceId;

                soundPlayCall["id"] = globals.NextBnkId();
                soundPlayCall["object"]["Action"]["initial_values"]["external_id"] = (uint)soundData["id"];
                soundStopCall["id"] = globals.NextBnkId();
                soundStopCall["object"]["Action"]["initial_values"]["external_id"] = (uint)soundData["id"];

                soundPlayEvent["id"] = sound.play;
                soundPlayEvent["object"]["Event"]["actions"][0] = (uint)soundPlayCall["id"];
                soundStopEvent["id"] = sound.stop;
                soundStopEvent["object"]["Event"]["actions"][0] = (uint)soundStopCall["id"];

                objects.Add(soundData);
                objects.Add(soundPlayCall);
                objects.Add(soundStopCall);
                objects.Add(soundPlayEvent);
                objects.Add(soundStopEvent);

                string wemSrcPath = sound.file;
                string wemTgtPath = $"{dir}\\wem\\{sourceId.ToString("D9").Substring(0, 2)}\\{sourceId.ToString("D9")}.wem";
                if (!Directory.Exists(Path.GetDirectoryName(wemTgtPath))) { Directory.CreateDirectory(Path.GetDirectoryName(wemTgtPath)); }
                if (File.Exists(wemTgtPath)) { File.Delete(wemTgtPath); }
                System.IO.File.Copy(wemSrcPath, wemTgtPath);
            }

            string jsonData = json.ToJsonString();
            string bnkPath = $"{dir}vc{id.ToString("D3")}.bnk";
            if (!Directory.Exists(Path.GetDirectoryName(bnkPath))) { Directory.CreateDirectory(Path.GetDirectoryName(bnkPath)); }
            System.IO.File.WriteAllText($"{bnkPath}json", jsonData);

            ProcessStartInfo startInfo = new(Utility.ResourcePath(@"tools\Bnk2Json\bnk2json.exe"), $"\"{bnkPath}json\"")
            {
                WorkingDirectory = Utility.ResourcePath(@"tools\Bnk2Json"),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            process.WaitForExit();

            if (File.Exists(bnkPath)) { File.Delete(bnkPath); }
            System.IO.File.Move($"{bnkPath}.rebuilt", bnkPath);
        }

        public class Sound
        {
            public readonly int dialogInfo; // id from dialoginfo we use for quicker searching when adding wems
            public readonly uint row, play, stop, source;   // source is wem id
            public readonly string transcript, file;        // wem file
            public Sound(int dialogInfo, uint row, uint play, uint stop, string transcript, string file, uint source)
            {
                this.dialogInfo = dialogInfo;
                this.row = row;
                this.play = play;
                this.stop = stop;
                this.transcript = transcript;
                this.file = file;
                this.source = source;
            }
        }
    }
}
