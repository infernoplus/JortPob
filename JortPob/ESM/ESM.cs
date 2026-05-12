using JortPob.Common;
using JortPob.Scripts;
using JortPob.Worker;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using static JortPob.Dialog;

namespace JortPob
{
    public class ESM
    {
        /* Types of records in the ESM */
        public enum Type
        {
            Header, GameSetting, GlobalVariable, Class, Faction, Race, Sound, Skill, MagicEffect, Script, Region, Birthsign, LandscapeTexture, Spell, Static, Door,
            MiscItem, Weapon, Container, Creature, Bodypart, Light, Enchanting, Npc, Armor, Clothing, RepairItem, Activator, Apparatus, Lockpick, Probe, Ingredient,
            Book, Alchemy, LeveledItem, LeveledCreature, Cell, Landscape, PathGrid, SoundGen, Dialogue, DialogueInfo
        }

        private readonly Dictionary<Type, List<JsonNode>> unidentifiedRecordsByType;
        private readonly Dictionary<Type, Dictionary<string, JsonNode>> recordsByType;
        private readonly ConcurrentDictionary<Int2, Landscape> landscapesByCoordinate;
        public List<DialogRecord> dialog;
        public List<RegionInfo> regions;
        public List<RaceInfo> races;
        public List<JobInfo> jobs;  // classes, but we cant really use that word so 'job'
        public List<FactionInfo> factions;
        public List<LeveledCreature> leveled; // leveled creature lists
        public List<SoundInfo> sounds;
        public List<Cell> exterior, interior;
        public List<Papyrus> scripts;

        public ESM(ScriptManager scriptManager)
        {
            // Ensure the cache path exists
            Directory.CreateDirectory(Const.CACHE_PATH);
            /* Check if a json has been generated from the esm, if not make one */
            string jsonPath = Path.Combine(Const.CACHE_PATH, "morrowind.json");
            if (!File.Exists(jsonPath))
            {
                /* Merge load order to a single file using merge_to_master */
                string esmPath;
                if (Const.LOAD_ORDER.Length == 1)
                {
                    esmPath = Path.Combine(Const.MORROWIND_PATH, "Data Files", Const.LOAD_ORDER[0]);
                }
                else
                {
                    // Copy our master esm to the cache folder
                    esmPath = Path.Combine(Const.CACHE_PATH, "morrowind.esm");
                    if(File.Exists(esmPath)) { File.Delete(esmPath); }
                    File.Copy(Path.Combine(Const.MORROWIND_PATH, "Data Files", Const.LOAD_ORDER[0]), esmPath);

                    // Merge the rest of the load order into that esm
                    for (int i=1;i<Const.LOAD_ORDER.Length;i++)
                    {
                        Lort.Log($"Merging '{Const.LOAD_ORDER[i]}' ...", Lort.Type.Main);
                        string childPath = Path.Combine(Const.MORROWIND_PATH, "Data Files", Const.LOAD_ORDER[i]);

                        ProcessStartInfo mergeStartInfo = new(Utility.ResourcePath(@"tools\MergeToMaster\merge_to_master.exe"), $"-o \"{childPath}\" \"{esmPath}\"")
                        {
                            WorkingDirectory = Utility.ResourcePath(@"tools\Tes3Conv"),
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        Utility.ExecuteProcess(mergeStartInfo);
                    }
                }

                /* Convert esm to a json file using tes3conv */
                Lort.Log($"Creating 'cache\\morrowind.json' ...", Lort.Type.Main);
                ProcessStartInfo convStartInfo = new(Utility.ResourcePath(@"tools\Tes3Conv\tes3conv.exe"), $"-c \"{esmPath}\" \"{jsonPath}\"")
                {
                    WorkingDirectory = Utility.ResourcePath(@"tools\Tes3Conv"),
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Utility.ExecuteProcess(convStartInfo);
            }
            /* Process json */
            Lort.Log($"Loading 'cache\\morrowind.json' ...", Lort.Type.Main);
            Lort.Log($"Delete this file if you change the load order.", Lort.Type.Main);

            string tempRawJson = File.ReadAllText(jsonPath);
            JsonArray json = JsonNode.Parse(tempRawJson).AsArray();

            recordsByType = new Dictionary<Type, Dictionary<string, JsonNode>>();
            unidentifiedRecordsByType = new Dictionary<Type, List<JsonNode>>();
            var enumNames = Enum.GetNames(typeof(Type)).ToHashSet();

            foreach (string name in Enum.GetNames(typeof(Type)))
            {
                Enum.TryParse(name, out Type type);
                if (type == Type.Dialogue || type == Type.DialogueInfo) { continue; } // special records, need to be handled specially
                recordsByType.Add(type, new Dictionary<string, JsonNode>());
                unidentifiedRecordsByType.Add(type, []);
            }

            foreach (var record in json)
            {
                if (record?["type"] == null)
                {
                    continue;
                }

                var rawRecordType = record["type"].ToString();
                if (!enumNames.Contains(rawRecordType))
                {
                    continue;
                }
                if (!Enum.TryParse(rawRecordType, out Type type))
                {
                    continue;
                }

                // special records, need to be handled specially
                if (type is Type.Dialogue or Type.DialogueInfo)
                {
                    continue;
                }

                if (record["id"] == null)
                {
                    unidentifiedRecordsByType[type].Add(record);
                }
                else
                {
                    recordsByType[type].Add(record["id"].GetValue<string>().ToLower(), record);
                }
            }

            /* Load and set defaults for all global variables listed in the ESM */
            List<string> globalVarFloats = new(); //make a list of variable names that are very bad no good
            foreach (JsonNode jsonNode in GetAllRecordsByType(ESM.Type.GlobalVariable))
            {
                string id = jsonNode["id"].GetValue<string>();
                string type = jsonNode["value"]["type"].GetValue<string>().ToLower();
                if (type != "short") { Lort.Log($" ## ERROR ## DISCARDING UNSUPPORTED GLOBALVAR {id} OF TYPE {type}", Lort.Type.Debug); globalVarFloats.Add(id.ToLower()); continue; }
                int value = jsonNode["value"]["data"].GetValue<int>();
                scriptManager.common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Short, Script.Flag.Designation.Global, id, (uint)value);
            }

            /* Handle dialog stuff now */
            dialog = new();
            DialogRecord current = null;
            for (int i = 0; i < json.Count; i++)
            {
                JsonNode record = json[i];
                Enum.TryParse(record["type"].ToString(), out Type type);

                if (type == Type.Dialogue)
                {
                    string idstr = record["id"].ToString().Trim();
                    string typestr = idstr.Replace(" ", "");
                    string diatype = record["dialogue_type"].ToString();
                    typestr = new String(typestr.Where(c => c != '-' && (c < '0' || c > '9')).ToArray());
                    if (!Enum.TryParse(typestr, out DialogRecord.Type dtype)) { dtype = DialogRecord.Type.Topic; }
                    if (diatype.ToLower() == "journal") { dtype = DialogRecord.Type.Journal; }

                    if (current != null && current.type == DialogRecord.Type.Greeting && dtype == DialogRecord.Type.Greeting) { continue; } // skip so we can merge all 9 greeting levels into a single thingy

                    current = new(dtype, idstr, scriptManager.common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.TopicEnabled, idstr));
                    dialog.Add(current);
                }
                else if (type == Type.DialogueInfo)
                {
                    // check for a "choice" filter and mark this as a Choice type dialoginforecord if that's the case
                    // choice type dialoginfo are only accessed through a choice papyrus call and have to be handled differently than other dialoginfos
                    bool isChoice = false;
                    foreach (JsonNode filterNode in record["filters"].AsArray())
                    {
                        if (filterNode["filter_type"].ToString() == "Function" && filterNode["function"].ToString() == "Choice") { isChoice = true; break; }
                    }

                    DialogInfoRecord dialogInfoRecord = new(isChoice ? DialogRecord.Type.Choice : current.type, record);
                    current.infos.Add(dialogInfoRecord);
                }
            }

            /* Post process, looking for topic unlocks */
            foreach (DialogRecord topic in dialog)
            {
                foreach (DialogInfoRecord info in topic.infos)
                {
                    foreach (DialogRecord otherTopic in dialog)
                    {
                        if (info.text.ToLower().Contains(otherTopic.id.ToLower()))
                        {
                            if (topic == otherTopic) { continue; } // prevent self succ
                            info.unlocks.Add(otherTopic);
                        }
                    }
                }
            }

            /* Load regioninfo from esm */
            regions = new();
            foreach(JsonNode jsonNode in GetAllRecordsByType(ESM.Type.Region))
            {
                regions.Add(new(jsonNode));
            }

            /* Load raceinfo and jobinfo */
            races = new();
            foreach (JsonNode jsonNode in GetAllRecordsByType(ESM.Type.Race))
            {
                races.Add(new(jsonNode));
            }

            jobs = new();
            foreach (JsonNode jsonNode in GetAllRecordsByType(ESM.Type.Class))
            {
                jobs.Add(new(jsonNode));
            }

            /* Load leveled creature lists so we can resolve them to creatures while processing cells */
            leveled = new();
            foreach(JsonNode jsonNode in GetAllRecordsByType(ESM.Type.LeveledCreature))
            {
                leveled.Add(new(jsonNode));
            }
            
            landscapesByCoordinate = new();

            /* Process papyrus scripts */
            scripts = new();
            foreach(JsonNode jsonNode in GetAllRecordsByType(ESM.Type.Script))
            {
                try
                {
                    Papyrus papyrus = new(jsonNode);
                    if (papyrus.HasCall(Papyrus.Call.Type.Float)) { Lort.Log($" ## DISCARDED SCRIPT ->  {jsonNode["id"].GetValue<string>()} :: HAS FLOAT", Lort.Type.Debug); continue; }  // discard scripts with float vars in it for sanity
                    if (papyrus.HasSignedInt()) { Lort.Log($" ## DISCARDED SCRIPT ->  {jsonNode["id"].GetValue<string>()} :: HAS SIGNED INT", Lort.Type.Debug); continue; } // discard scripts with negative numbers
                    if (papyrus.HasVariable(globalVarFloats)) { Lort.Log($" ## DISCARDED SCRIPT ->  {jsonNode["id"].GetValue<string>()} :: HAS GLOBALVAR FLOAT", Lort.Type.Debug); continue; } // discard scripts that reference a float globalvariable
                    scripts.Add(papyrus);
                }
                catch { Lort.Log($" ## FAILED TO PARSE SCRIPT :: {jsonNode["id"].GetValue<string>()}", Lort.Type.Debug); }
            }

            /* Load faction info from esm */
            factions = new();
            foreach (JsonNode jsonNode in GetAllRecordsByType(ESM.Type.Faction))
            {
                FactionInfo faction = new(jsonNode);
                factions.Add(faction);
            }

            /* Load sound records from esm */
            sounds = new();
            foreach (JsonNode jsonNode in GetAllRecordsByType(ESM.Type.Sound))
            {
                SoundInfo sound = new(jsonNode);
                sounds.Add(sound);
            }
        }
        
        public void BuildCells()
        {
            Lort.Log("BUILDING CELLS", Lort.Type.Debug);
            CellWorker worker = new CellWorker(this);
            (exterior, interior) = worker.Go();
        }

        /* List of types that we should search for references */
        public readonly Type[] VALID_CONTENT_TYPES = {
            Type.Static, Type.Container, Type.Light, Type.Sound, Type.Skill, Type.Region, Type.Door, Type.MiscItem, Type.Weapon,  Type.Creature, Type.Bodypart, Type.Npc,
            Type.Armor, Type.Clothing, Type.RepairItem, Type.Activator, Type.Apparatus, Type.Lockpick, Type.Probe, Type.Ingredient, Type.Book, Type.Alchemy, Type.LeveledItem,
            Type.LeveledCreature, Type.PathGrid, Type.SoundGen
        };

        /* References don't contain any explicit 'type' data so... we just gotta go find it lol */
        public Record FindRecordById(string id)
        {
            foreach (var type in VALID_CONTENT_TYPES)
            {
                var recordsById = recordsByType[type];
                if (recordsById.TryGetValue(id.ToLower(), out var value))
                {
                    return new Record(type, value);
                }
            }
            return null; // Not found!
        }

        /* Gets a pathgrid record for the given cell name/grid. cell name is used by interiors and grid is used by exteriors */
        public JsonNode FindPathRecord(string cell)
        {
            foreach (JsonNode json in GetAllRecordsByType(Type.PathGrid))
            {
                string name = json["cell"].GetValue<string>().ToLower();
                if (cell.ToLower() == name) { return json; }
            }
            return null;
        }

        public JsonNode FindPathRecord(Int2 coordinate)
        {
            foreach (JsonNode json in GetAllRecordsByType(Type.PathGrid))
            {
                int x = json["data"]["grid"].AsArray()[0].GetValue<int>();
                int y = json["data"]["grid"].AsArray()[1].GetValue<int>();
                Int2 grid = new(x, y);
                if (grid == coordinate) { return json; }
            }
            return null;
        }

        public IEnumerable<JsonNode> GetAllRecordsByType(Type type)
        {
            return recordsByType[type].Values.Concat(unidentifiedRecordsByType[type]);
        }

        public Cell GetCellByGrid(Int2 position)
        {
            foreach (Cell cell in exterior)
            {
                if (cell.coordinate == position && !cell.HasFlag(Cell.Flag.IsInterior)) { return cell; }
            }
            return null;
        }

        public Cell GetCellByName(string name)
        {
            foreach (Cell cell in exterior)
            {
                if (cell.name == name) { return cell; }
            }
            foreach (Cell cell in interior)
            {
                if (cell.name == name) { return cell; }
            }
            return null;
        }

        /* By Morrowind coordinates, not elden ring relative coordinates. Only exterior cells (obviously) */
        public Cell GetCellByPosition(System.Numerics.Vector3 position)
        {
            foreach(Cell cell in exterior)
            {
                if (cell.IsPointInside(position)) { return cell; }
            }
            return null;
        }

        public Landscape GetLandscape(Int2 coordinate)
        {
            if (GetCellByGrid(coordinate) == null) { return null; } // Performance hack.

            if (landscapesByCoordinate.TryGetValue(coordinate, out var existingLandscape))
            {
                return existingLandscape;
            }

            var matchingRecord = GetAllRecordsByType(Type.Landscape)
                .FirstOrDefault(
                    json => int.Parse(json["grid"][0].ToString()) == coordinate.x &&
                            int.Parse(json["grid"][1].ToString()) == coordinate.y
                );

            if (matchingRecord == null)
            {
                return null;
            }

            Landscape landscape = new(this, coordinate, matchingRecord);
            landscapesByCoordinate[coordinate] = landscape;
            return landscape;
        }

        /* Same as above but only returns a landscape if its already fully loaded. Returns null if its not loaded */
        public Landscape GetLoadedLandscape(Int2 coordinate)
        {
            return landscapesByCoordinate.GetValueOrDefault(coordinate);
        }

        /* Load all landscapes, single threaded */
        public void LoadLandscapes()
        {
            Lort.Log($"Processing {exterior.Count} landscapes...", Lort.Type.Main);
            Lort.NewTask("Processing Landscape", exterior.Count);
            foreach (Cell cell in exterior)
            {
                GetLandscape(cell.coordinate);
                Lort.TaskIterate();
            }
        }

        public JobInfo? GetJob(string id) => jobs.FirstOrDefault(job => job.id == id.ToLower());

        public RaceInfo? GetRace(string id) => races.FirstOrDefault(race => race.id == id.ToLower());

        public FactionInfo? GetFaction(string id) => factions.FirstOrDefault(faction => faction.id == id.ToLower());

        public SoundInfo? GetSound(string id) => sounds.FirstOrDefault(sound => sound.id == id.ToLower());

        public Papyrus? GetPapyrus(string? id) => id is null ? null : scripts.FirstOrDefault(script => script.id == id.ToLower());

        public LeveledCreature? GetLeveledCreature(string id) => leveled.FirstOrDefault(lc => lc.id == id.ToLower());

        /* Get dialog and character data for building esd */
        public List<Tuple<DialogRecord, List<DialogInfoRecord>>> GetDialog(ScriptManager scriptManager, CharacterContent npc)
        {
            List<Tuple<DialogRecord, List<DialogInfoRecord>>> ds = new();  // i am really sorry about this type
            foreach(DialogRecord dialogRecord in dialog)
            {
                if (dialogRecord.type == DialogRecord.Type.Journal) { continue; } // obviously skip these lmao

                // Check if the npc meets requirements for any lines in this topic
                List<DialogInfoRecord> infos = new();
                foreach(DialogInfoRecord info in dialogRecord.infos)
                {
                    if (info.type == DialogRecord.Type.Flee) { continue; } // discarding this for now
                    if (info.type == DialogRecord.Type.Intruder) { continue; } // discarding this for now

                    if (npc.race == CharacterContent.Race.Creature && info.speaker != npc.id) { continue; } // creatures only have lines with the speaker condition set for them explicitly

                    // Check if the npc meets all static requirements for this dialog line. this includes resolving some filter to see if they can ever pass
                    if (info.IsUnreachableFor(scriptManager, npc)) { continue; }

                    infos.Add(info);

                    // If this line has no filters it means that anything below it is unreachable, so we just break in that case
                    if (info.filters.Count() <= 0 && info.playerFaction == null && info.playerRank <= 0 && info.disposition <= 0) { break; }
                }

                if (infos.Count() > 0) { ds.Add(new(dialogRecord, infos)); } // discard if no valid lines
            }

            return ds;
        }

        public Record ResolveLeveledCreature(string id)
        {
            LeveledCreature leveledCreatureList = GetLeveledCreature(id) ?? throw new Exception($"Failed to resolve leveled creature list: {id}");
            string resolvedId = leveledCreatureList.Get();
            Record resolvedRecord = FindRecordById(resolvedId);
            if (resolvedRecord.type == ESM.Type.LeveledCreature) { resolvedRecord = ResolveLeveledCreature(resolvedId); }  // leveld lists can be recursive. jfk todd, why?
            return resolvedRecord;
        }

        /* Checks if a creature has any dialog associated to it and returns true/false. */
        /* This is an expensive and commonly used check so we cache the result for reuse */
        private Dictionary<string, bool> hasDialogCache = new();
        public bool HasDialog(CreatureContent content)
        {
            if (hasDialogCache.ContainsKey(content.id)) { return hasDialogCache[content.id]; }

            foreach (DialogRecord record in dialog)
            {
                foreach (DialogInfoRecord info in record.infos)
                {
                    if (info.speaker == content.id) { hasDialogCache.Add(content.id, true); return true; }
                }
            }

            hasDialogCache.Add(content.id, false);
            return false;
        }
        
        public ScriptReferenceMetadata GetScriptReferences()
        {
            /* Find all objects targeted by script calls so we can make sure they are placd in regular tiles. Objects in Big/Huge tiles can't have script data */
            var allCalls = scripts
                .SelectMany(p => p.GetCalls())
                .Concat(dialog.SelectMany(d => d.GetCalls()))
                .ToList();

            HashSet<string> allReferences = [], toggleableRefs = [];  // able refs is objects targeted by Enable, Disable, and GetDisabled

            foreach (Papyrus.Call call in allCalls)
            {
                // Grab record reference from target if exists
                if (call.target != null && call.target != "player")
                {
                    allReferences.Add(call.target);

                    switch (call.type)
                    {
                        case Papyrus.Call.Type.Enable:
                        case Papyrus.Call.Type.Disable:
                        case Papyrus.Call.Type.GetDisabled:
                            toggleableRefs.Add(call.target);
                            break;
                        default: break;
                    }
                }

                // Grab record reference from arguments if they exist
                switch (call.type)
                {
                    case Papyrus.Call.Type.Cast:
                        {
                            string reference = call.parameters[1].ToLower().Trim();
                            if (reference == "player") { break; }
                            allReferences.Add(reference);
                            break;
                        }
                    case Papyrus.Call.Type.GetDistance:
                        {
                            string reference = call.parameters[0].ToLower().Trim();
                            if (reference == "player") { break; }
                            allReferences.Add(reference);
                            break;
                        }
                    default: break;
                }
            }

            toggleableRefs.UnionWith(
                exterior.Concat(interior).SelectMany(cell => cell.contents)
                    .Where(content => {
                        var papyrus = GetPapyrus(content.papyrus);
                        return papyrus != null && (papyrus.HasCall(Papyrus.Call.Type.Disable) ||
                                                   papyrus.HasCall(Papyrus.Call.Type.Enable) ||
                                                   papyrus.HasCall(Papyrus.Call.Type.GetDisabled));
                    })
                    .Select(content => content.id.ToLower().Trim())
            );

            return new ScriptReferenceMetadata(allCalls, allReferences, toggleableRefs);
        }
    }

    public record ScriptReferenceMetadata(List<Papyrus.Call> AllCalls, HashSet<string> AllReferences, HashSet<string> ToggleableReferences);

    public class RegionInfo
    {
        public readonly string id, name;
        public readonly List<(ScriptCommon.WeatherEMEVD weather, float chance)> weathers;

        private static readonly Dictionary<ScriptCommon.WeatherPapyrus, ScriptCommon.WeatherEMEVD[]> WeatherMap = new()
        {
            { ScriptCommon.WeatherPapyrus.Clear, [ScriptCommon.WeatherEMEVD.Default]  },
            { ScriptCommon.WeatherPapyrus.Cloudy, [ScriptCommon.WeatherEMEVD.PuffyClouds, ScriptCommon.WeatherEMEVD.WindyPuffyClouds] },
            { ScriptCommon.WeatherPapyrus.Foggy, [ScriptCommon.WeatherEMEVD.Fog, ScriptCommon.WeatherEMEVD.HeavyFog, ScriptCommon.WeatherEMEVD.WindyFog] },
            { ScriptCommon.WeatherPapyrus.Overcast, [ScriptCommon.WeatherEMEVD.FlatClouds] },
            { ScriptCommon.WeatherPapyrus.Rain, [ScriptCommon.WeatherEMEVD.Rain, ScriptCommon.WeatherEMEVD.RainyClouds, ScriptCommon.WeatherEMEVD.ScatteredRain] },
            { ScriptCommon.WeatherPapyrus.Ash, [ScriptCommon.WeatherEMEVD.Unknown18] },
            { ScriptCommon.WeatherPapyrus.Blight, [ScriptCommon.WeatherEMEVD.Unknown19] },
            { ScriptCommon.WeatherPapyrus.Snow, [ScriptCommon.WeatherEMEVD.Snow] },
            { ScriptCommon.WeatherPapyrus.Blizzard, [ScriptCommon.WeatherEMEVD.HeavySnow, ScriptCommon.WeatherEMEVD.SnowyHeavyFog] }
        };

        public RegionInfo(JsonNode json)
        {
            id = json["id"].GetValue<string>().ToLower().Trim();
            name = json["name"].GetValue<string>();

            weathers = new();
            foreach(var property in json["weather_chances"].AsObject())
            {
                ScriptCommon.WeatherPapyrus w = Enum.Parse<ScriptCommon.WeatherPapyrus>(property.Key, ignoreCase: true);
                float chance = property.Value.GetValue<float>();
                if (chance == 0) { continue; } // gtfo

                if (WeatherMap.TryGetValue(w, out var mappedWeathers))
                {
                    foreach (var weather in mappedWeathers)
                        weathers.Add((weather, chance / mappedWeathers.Length));
                }
                else
                    Lort.Log($"### DISCARDED VALUE ### Weather type {w.ToString()} does not have a mapped value", Lort.Type.Debug);
            }
        }

        public float ChanceTotal() { return weathers.Sum(w => w.chance); }
    }

    public class RaceInfo
    {
        public readonly string id, name, description;
        public readonly Dictionary<CharacterContent.Stats.Attribute, Dictionary<CharacterContent.Sex, int>> attributes;
        public readonly Dictionary<CharacterContent.Stats.Skill, int> skills;

        public RaceInfo(JsonNode json)
        {
            id = json["id"].GetValue<string>().ToLower();
            name = json["name"].GetValue<string>();
            description = json["description"].GetValue<string>();

            attributes = new();
            skills = new();

            foreach (CharacterContent.Stats.Attribute attribute in Enum.GetValues(typeof(CharacterContent.Stats.Attribute)))
            {
                Dictionary<CharacterContent.Sex, int> values = new();

                JsonArray jary = json["data"][attribute.ToString().ToLower()].AsArray();
                values.Add(CharacterContent.Sex.Male, jary[0].GetValue<int>());
                values.Add(CharacterContent.Sex.Female, jary[1].GetValue<int>());

                attributes.Add(attribute, values);

            }

            for (int i=0;i<=6;i++)  // 7 is the number of skills a race can have as thier 'bonus' skills. hardcoded to esm. indexed as skill_0 to skill_6
            {
                string s = json["data"]["skill_bonuses"][$"skill_{i}"].GetValue<string>();
                if (s.ToLower() == "none") { continue; }
                CharacterContent.Stats.Skill skill = (CharacterContent.Stats.Skill)System.Enum.Parse(typeof(CharacterContent.Stats.Skill), s);
                int value = json["data"]["skill_bonuses"][$"bonus_{i}"].GetValue<int>();
                skills.Add(skill, value);
            }
        }

        public int GetAttribute(CharacterContent.Sex sex, CharacterContent.Stats.Attribute attribute) { return attributes[attribute][sex]; }
        public int GetSkill(CharacterContent.Stats.Skill skill) { if (skills.ContainsKey(skill)) { return skills[skill]; } else { return 0; } }
    }

    public class JobInfo
    {
        public enum Specialization
        {
            Combat, Stealth, Magic
        }

        public readonly string id, name, description;
        private readonly Specialization specialization;
        private readonly List<CharacterContent.Stats.Attribute> attributes;
        private readonly List<CharacterContent.Stats.Skill> major, minor;
        private readonly List<CharacterContent.Service> services;

        public JobInfo(JsonNode json)
        {
            id = json["id"].GetValue<string>().ToLower();
            name = json["name"].GetValue<string>();
            description = json["description"].GetValue<string>();
            specialization = Enum.Parse<Specialization>(json["data"]["specialization"].GetValue<string>());

            attributes = new();
            major = new();
            minor = new();
            services = new();

            attributes.Add(Enum.Parse<CharacterContent.Stats.Attribute>(json["data"]["attribute1"].GetValue<string>()));
            attributes.Add(Enum.Parse<CharacterContent.Stats.Attribute>(json["data"]["attribute2"].GetValue<string>()));
            for(int i=1;i<=5;i++)
            {
                major.Add(Enum.Parse<CharacterContent.Stats.Skill>(json["data"][$"major{i}"].GetValue<string>()));
                minor.Add(Enum.Parse<CharacterContent.Stats.Skill>(json["data"][$"minor{i}"].GetValue<string>()));
            }
        }

        public bool HasAttribute(CharacterContent.Stats.Attribute attribute) { return attributes.Contains(attribute); }
        public bool HasMajor(CharacterContent.Stats.Skill skill) { return major.Contains(skill); }
        public bool HasMinor(CharacterContent.Stats.Skill skill) { return minor.Contains(skill); }
        public bool HasSpecialization(CharacterContent.Stats.Skill skill)
        {
            switch(skill)
            {
                case CharacterContent.Stats.Skill.Armorer:
                case CharacterContent.Stats.Skill.Athletics:
                case CharacterContent.Stats.Skill.Axe:
                case CharacterContent.Stats.Skill.Block:
                case CharacterContent.Stats.Skill.BluntWeapon:
                case CharacterContent.Stats.Skill.HeavyArmor:
                case CharacterContent.Stats.Skill.LongBlade:
                case CharacterContent.Stats.Skill.MediumArmor:
                case CharacterContent.Stats.Skill.Spear:
                    return specialization == Specialization.Combat;
                case CharacterContent.Stats.Skill.Acrobatics:
                case CharacterContent.Stats.Skill.HandToHand:
                case CharacterContent.Stats.Skill.LightArmor:
                case CharacterContent.Stats.Skill.Marksman:
                case CharacterContent.Stats.Skill.Mercantile:
                case CharacterContent.Stats.Skill.Security:
                case CharacterContent.Stats.Skill.ShortBlade:
                case CharacterContent.Stats.Skill.Sneak:
                case CharacterContent.Stats.Skill.Speechcraft:
                    return specialization == Specialization.Stealth;
                case CharacterContent.Stats.Skill.Alchemy:
                case CharacterContent.Stats.Skill.Alteration:
                case CharacterContent.Stats.Skill.Conjuration:
                case CharacterContent.Stats.Skill.Destruction:
                case CharacterContent.Stats.Skill.Enchant:
                case CharacterContent.Stats.Skill.Illusion:
                case CharacterContent.Stats.Skill.Mysticism:
                case CharacterContent.Stats.Skill.Restoration:
                case CharacterContent.Stats.Skill.Unarmored:
                    return specialization == Specialization.Magic;
                default:
                    throw new Exception("What the fuck");
            }
        }
    }

    public class FactionInfo
    {
        public readonly string id, name;
        public readonly List<Rank> ranks;
        private readonly List<(string id, int value)> reactions;

        public FactionInfo(JsonNode json)
        {
            id = json["id"].GetValue<string>().ToLower();
            name = json["name"].GetValue<string>();

            ranks = new();
            JsonArray rankNames = json["rank_names"].AsArray();
            JsonArray rankRequirements = json["data"]["requirements"].AsArray();
            for (int i=0;i< rankNames.Count();i++)
            {
                string rankName = rankNames[i].GetValue<string>();
                JsonNode rankRequiremnt = rankRequirements[i];
                int reputation = rankRequiremnt["reputation"].GetValue<int>();
                Rank rank = new(rankName, i+1, reputation);
                ranks.Add(rank);
            }

            reactions = new();
            JsonArray reacts = json["reactions"].AsArray();
            for(int i=0;i< reacts.Count();i++)
            {
                JsonNode entry = reacts[i];
                reactions.Add((entry["faction"].GetValue<string>().ToLower(), entry["reaction"].GetValue<int>()));
            }
        }

        public List<(string id, int value)> GetHighReactions()
        {
            // Copy and sort array then return.
            List<(string id, int reaction)> highs = new();
            highs.AddRange(reactions);
            highs.Sort((x, y) => y.reaction.CompareTo(x.reaction));
            return highs;
        }

        public List<(string id, int value)> GetLowReactions()
        {
            // Copy and sort array then return.
            List<(string id, int reaction)> lows = new();
            lows.AddRange(reactions);
            lows.Sort((x, y) => x.reaction.CompareTo(y.reaction));
            return lows;
        }

        public bool HasReactions()
        {
            return reactions.Count > 0;
        }

        public class Rank
        {
            public readonly string name;
            public readonly int level, reputation; // required reputation to reach this rank
            public Rank(string name, int level, int reputation)
            {
                this.name = name;
                this.level = level;
                this.reputation = reputation;
            }
        }
    }

    public class LeveledCreature
    {
        public readonly string id;
        public readonly int chance;

        public readonly List<(string id, int level)> creatures;

        public LeveledCreature(JsonNode json)
        {
            id = json["id"].GetValue<string>().ToLower();
            chance = json["chance_none"].GetValue<int>();

            creatures = new();
            foreach (JsonNode entry in json["creatures"].AsArray())
            {
                JsonArray data = entry.AsArray();

                string creature = data[0].GetValue<string>().ToLower();
                int level = data[1].GetValue<int>();

                creatures.Add((creature, level));
            }
        }

        /* @TODO: better selection code. currently just using a starndard random draw */
        public string Get()
        {
            int rand = Utility.RandomRange(0, creatures.Count());
            return creatures[rand].id;
        }
    }

    public class SoundInfo
    {
        public readonly string id, path;
        public readonly int volume, min, max;
        
        public SoundInfo(JsonNode json)
        {
            id = json["id"].GetValue<string>().ToLower();
            path = json["sound_path"].GetValue<string>();
            volume = json["data"]["volume"].GetValue<int>();
            min = json["data"]["range"].AsArray()[0].GetValue<int>();
            max = json["data"]["range"].AsArray()[1].GetValue<int>();
        }
    }

    public class Record
    {
        public readonly ESM.Type type;
        public readonly JsonNode json;
        public Record(ESM.Type type, JsonNode json)
        {
            this.type = type;
            this.json = json;
        }
    }
}
