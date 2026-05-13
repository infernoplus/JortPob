using JortPob.Common;
using Microsoft.Scripting.Utils;
using System.Collections.Generic;
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

        /* Generates and writes JSON, and copys wems to relevant directorys */
        public Dictionary<uint, string> WriteSources(int id)
        {
            Dictionary<uint, string> wemsToWrite = new();

            string dir = Path.Combine(Const.OUTPUT_PATH, "sd", "enus");

            string bnkJsonPath = Path.Combine(dir, $"vc{id:D3}", $"soundbank.json");
            string bnkRebuiltPath = Path.Combine(dir, $"vc{id:D3}.created.bnk");
            string bnkPath = Path.Combine(dir, $"vc{id:D3}.bnk");

            if(Const.DEBUG_REUSE_FILES && File.Exists(bnkJsonPath)) { return new(); } // if debug_reuse is on, skip if file already created

            JsonNode json = JsonNode.Parse(System.IO.File.ReadAllText(Utility.ResourcePath(@"sound\bnk_template.json")));
            JsonArray sections = json["sections"].AsArray();
            JsonNode BKHD = sections[0]["body"]["BKHD"];
            JsonNode HIRC = sections[1]["body"]["HIRC"];
            JsonArray objects = HIRC["objects"].AsArray();

            List<JsonNode> sources = new(), mixers = new(), events = new();

            JsonNode templatePlay = objects[0]; objects.RemoveAt(0);
            JsonNode templatePlayCall = objects[0]; objects.RemoveAt(0);
            JsonNode templateStop = objects[0]; objects.RemoveAt(0);
            JsonNode templateStopCall = objects[0]; objects.RemoveAt(0);
            JsonNode templateData = objects[0]; objects.RemoveAt(0);

            JsonNode attenuation = objects[0]; objects.RemoveAt(0);
            JsonNode mixer = objects[0]; objects.RemoveAt(0);
            JsonNode master = objects[0]; objects.RemoveAt(0);

            BKHD["bank_id"] = globals.NextHeaderId();
            master["id"]["Hash"] = globals.NextBnkId();
            mixer["id"]["Hash"] = globals.NextBnkId();
            attenuation["id"]["Hash"] = globals.NextBnkId();


            master["body"]["ActorMixer"]["children"]["items"].AsArray().Add((uint)mixer["id"]["Hash"]);
            mixer["body"]["ActorMixer"]["node_base_params"]["direct_parent_id"] = (uint)master["id"]["Hash"];
            master["body"]["ActorMixer"]["node_base_params"]["node_initial_params"]["prop_initial_values"].AsArray()[3]["AttenuationID"] = (uint)attenuation["id"]["Hash"];

            //master["body"]["ActorMixer"]["node_base_params"]["override_bus_id"] = (uint)0;   // unk ass fields
            //mixer["body"]["ActorMixer"]["node_base_params"]["override_bus_id"] = (uint)0;

            mixers.Add(attenuation);
            mixers.Add(mixer);
            mixers.Add(master);

            foreach (Sound sound in sounds)
            {
                JsonNode soundPlayEvent = templatePlay.DeepClone();
                JsonNode soundPlayCall = templatePlayCall.DeepClone();
                JsonNode soundStopEvent = templateStop.DeepClone();
                JsonNode soundStopCall = templateStopCall.DeepClone();
                JsonNode soundData = templateData.DeepClone();

                uint sourceId = sound.source;
                soundData["id"]["Hash"] = globals.NextBnkId();
                soundData["body"]["Sound"]["bank_source_data"]["media_information"]["source_id"] = sourceId;
                soundData["body"]["Sound"]["node_base_params"]["direct_parent_id"] = (uint)mixer["id"]["Hash"];
                mixer["body"]["ActorMixer"]["children"]["items"].AsArray().Add((uint)soundData["id"]["Hash"]);

                soundPlayCall["id"]["Hash"] = globals.NextBnkId();
                soundPlayCall["body"]["Action"]["external_id"] = (uint)soundData["id"]["Hash"];
                soundPlayCall["body"]["Action"]["params"]["Play"]["bank_id"] = (uint)BKHD["bank_id"];
                soundStopCall["id"]["Hash"] = globals.NextBnkId();
                soundStopCall["body"]["Action"]["external_id"] = (uint)soundData["id"]["Hash"];

                soundPlayEvent["id"]["String"] = $"Play_v{sound.row:D8}0";
                soundPlayEvent["body"]["Event"]["actions"][0] = (uint)soundPlayCall["id"]["Hash"];
                soundStopEvent["id"]["String"] = $"Stop_v{sound.row:D8}0";
                soundStopEvent["body"]["Event"]["actions"][0] = (uint)soundStopCall["id"]["Hash"];

                sources.Add(soundData);
                events.Add(soundPlayCall);
                events.Add(soundStopCall);
                events.Add(soundPlayEvent);
                events.Add(soundStopEvent);

                string wemSrcPath = sound.file;
                string wemTgtPath = Path.Combine(dir, @$"wem\{sourceId.ToString("D9").Substring(0, 2)}\{sourceId:D9}.wem");
                //Directory.CreateDirectory(Path.GetDirectoryName(wemTgtPath));
                //if (File.Exists(wemTgtPath)) { File.Delete(wemTgtPath); }
                //File.Copy(wemSrcPath, wemTgtPath);
                if(!wemsToWrite.ContainsKey(sourceId)) { wemsToWrite.Add(sourceId, wemSrcPath); }
            }

            objects.AddRange(sources);  // ordering of nodes matters a lot for this json
            objects.AddRange(mixers);
            objects.AddRange(events);

            HIRC["object_count"] = objects.Count;                  // unsure if these fields matter or not
            master["body"]["ActorMixer"]["children"]["count"] = master["body"]["ActorMixer"]["children"]["items"].AsArray().Count;
            mixer["body"]["ActorMixer"]["children"]["count"] = mixer["body"]["ActorMixer"]["children"]["items"].AsArray().Count;

            Directory.CreateDirectory(Path.GetDirectoryName(bnkJsonPath));
            File.WriteAllText(bnkJsonPath, json.ToJsonString());

            return wemsToWrite;
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
