using JortPob.Common;
using Microsoft.Scripting.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JortPob
{
    /* Loads override json files so we can referenec them */
    /* Static class since everything here is going to be static and readonly */
    public class Override
    {
        private static HashSet<string> DO_NOT_PLACE;
        private static HashSet<string> STATIC_COLLISION;
        private static HashSet<string> ITEMS_TO_SKIP;
        private static HashSet<string> CUSTOM_VOICES;

        private static List<PlayerClass> CHARACTER_CREATION_CLASS;
        private static List<PlayerRace> CHARACTER_CREATION_RACE;
        private static List<Gift> CHARACTER_CREATION_GIFT;
        private static Dictionary<string, FaceData> FACE_REMAP;
        private static Dictionary<string, Hair> HAIR_REMAP;
        private static Dictionary<string, ItemRemap> ITEM_REMAPS_BY_ID;
        private static Dictionary<string, ItemDefinition> ITEM_DEFINITIONS_BY_ID;
        private static Dictionary<string, SpeffDefinition> SPEFF_DEFINITIONS_BY_ID;
        private static Dictionary<string, SpellRemap> SPELL_REMAPS_BY_ID;
        private static List<SkillInfo> SKILL_INFOS;
        private static List<AlchemyInfo> ALCHEMY_INFOS;
        private static List<EnemyRemap> ENEMY_REMAPS;
        private static Dictionary<string, Layout.MapPoint.Icon> MAP_ICONS;
        private static List<LoadingTip> LOADING_TIPS;
        private static Dictionary<string, byte> REGION;

        public static bool CheckDoNotPlace(string id)
        {
            return DO_NOT_PLACE.Contains(id.ToLower());
        }

        public static bool CheckStaticCollision(string id)
        {
            return STATIC_COLLISION.Contains(id.ToLower());
        }

        public static bool CheckSkipItem(string id)
        {
            return ITEMS_TO_SKIP.Contains(id.ToLower());
        }

        public static bool CheckCustomVoice(string id)
        {
            return CUSTOM_VOICES.Contains(id.ToLower());
        }

        public static List<PlayerClass> GetCharacterCreationClasses()
        {
            return CHARACTER_CREATION_CLASS;
        }

        public static List<PlayerRace> GetCharacterCreationRaces()
        {
            return CHARACTER_CREATION_RACE;
        }

        public static List<Gift> GetCharacterCreationGifts()
        {
            return CHARACTER_CREATION_GIFT;
        }

        public static ItemRemap GetItemRemap(string id)
        {
            return ITEM_REMAPS_BY_ID.TryGetValue(id, out ItemRemap remapped) ? remapped : null;
        }

        public static ItemDefinition GetItemDefinition(string id)
        {
            return ITEM_DEFINITIONS_BY_ID.TryGetValue(id, out ItemDefinition definition) ? definition : null;
        }

        public static SpeffDefinition GetSpeffDefinition(string id)
        {
            return SPEFF_DEFINITIONS_BY_ID.TryGetValue(id, out SpeffDefinition definition) ? definition : null;
        }

        public static List<SpeffDefinition> GetSpeffDefinitions()
        {
            return SPEFF_DEFINITIONS_BY_ID.Values.ToList();
        }

        public static SpellRemap GetSpellRemap(string id)
        {
            return SPELL_REMAPS_BY_ID.TryGetValue(id, out SpellRemap remap) ? remap : null;
        }

        public static List<SkillInfo> GetSkills()
        {
            return SKILL_INFOS;
        }

        public static List<SkillInfo> GetSkills(CharacterContent.Stats.Tier tier)
        {
            return SKILL_INFOS.Where(skill => skill.tier <= tier).ToList();
        }

        public static List<AlchemyInfo> GetAlchemy()
        {
            return ALCHEMY_INFOS;
        }

        public static EnemyRemap GetEnemyRemap(string id)
        {
            foreach (EnemyRemap remap in ENEMY_REMAPS)
            {
                if (remap.id == id.ToLower().Trim()) { return remap; }
            }
            return new();
        }

        public static List<LoadingTip> GetLoadingTips()
        {
            return LOADING_TIPS;
        }

        public static Layout.MapPoint.Icon GetMapIcon(string name)
        {
            string n = name.ToLower().ToString();
            if(MAP_ICONS.ContainsKey(n)) { return MAP_ICONS[n]; }
            else { return Layout.MapPoint.Icon.Auto; }
        }

        public static Hair GetHair(string name)
        {
            if(HAIR_REMAP.ContainsKey(name.ToLower().Trim())) { return HAIR_REMAP[name.ToLower().Trim()]; }
            else { return new Hair(0, Hair.Color.Black); }  // BALD!
        }

        public static FaceData GetFace(string name)
        {
            if(FACE_REMAP.ContainsKey(name.ToLower().Trim())) { return FACE_REMAP[name.ToLower().Trim()]; }
            else { return FACE_REMAP["default"]; }
        }

        public static byte GetRegionByte(string id)
        {
            string ID = id.ToLower().Trim();
            if(REGION.ContainsKey(ID)) { return REGION[ID]; }
            else { return 255; }  // default
        }

        /* load all the override jsons into this class */
        public static void Initialize()
        {
            /* Load do_not_place overrides */
            DO_NOT_PLACE = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(Utility.ResourcePath(@"overrides\do_not_place.json")))
                .Select(dnp => dnp.ToLower()).ToHashSet();

            /* Load static_collision overrides */
            STATIC_COLLISION = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(Utility.ResourcePath(@"overrides\static_collision.json")))
                .Select(sc => sc.ToLower()).ToHashSet();

            /* Load items_to_skip overrides */
            ITEMS_TO_SKIP = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(Utility.ResourcePath(@"overrides\items_to_skip.json")))
                .Select(its => its.ToLower()).ToHashSet();

            /* Load items_to_skip overrides */
            CUSTOM_VOICES = JsonConvert.DeserializeObject<List<string>>(File.ReadAllText(Utility.ResourcePath(@"overrides\custom_voice_list.json")))
                .Select(cv => cv.ToLower()).ToHashSet();

            /* Load character creation class overrides */
            CHARACTER_CREATION_CLASS = JsonConvert.DeserializeObject<List<PlayerClass>>(File.ReadAllText(Utility.ResourcePath(@"overrides\character_creation_class.json"))).ToList();

            /* Load character creation gift overrides */
            CHARACTER_CREATION_GIFT = JsonConvert.DeserializeObject<List<Gift>>(File.ReadAllText(Utility.ResourcePath(@"overrides\character_creation_gift.json")));

            /* Load character creation race overrides */
            CHARACTER_CREATION_RACE = JsonConvert.DeserializeObject<List<PlayerRace>>(File.ReadAllText(Utility.ResourcePath(@"overrides\character_creation_race.json")));

            /* Load alchemy recipe information */
            ALCHEMY_INFOS = JsonConvert.DeserializeObject<List<AlchemyInfo>>(File.ReadAllText(Utility.ResourcePath(@"overrides\alchemy.json")));

            /* Load weapon skill information list */
            SKILL_INFOS = JsonConvert.DeserializeObject<List<SkillInfo>>(File.ReadAllText(Utility.ResourcePath(@"overrides\weapon_skill_info.json")));

            /* Load spell remapping list */
            SPELL_REMAPS_BY_ID = JsonConvert.DeserializeObject<Dictionary<string, SpellRemap>>(File.ReadAllText(Utility.ResourcePath(@"overrides\spell_remap.json")));

            /* Load item remapping list */
            ITEM_REMAPS_BY_ID = Directory.GetFiles(Utility.ResourcePath(@"overrides\items\remap"))
                .Select(file => JsonConvert.DeserializeObject<List<ItemRemap>>(File.ReadAllText(file)))
                .SelectMany(list => list)
                .ToDictionary(remap => remap.id);

            /* Load all item definitinos from resources/override/items */
            ITEM_DEFINITIONS_BY_ID = Directory.GetFiles(Utility.ResourcePath(@"overrides\items"))
                .Select(file =>
                { 
                    var obj = JsonConvert.DeserializeObject<ItemDefinition>(File.ReadAllText(file));
                    obj.id = Path.GetFileNameWithoutExtension(file);
                    return obj;
                })
                .ToDictionary(def => def.id);

            /* Load all speff definitinos from resources/override/speffs */
            SPEFF_DEFINITIONS_BY_ID = Directory.GetFiles(Utility.ResourcePath(@"overrides\speffs"))
                .Select(file =>
                {
                    var obj = JsonConvert.DeserializeObject<SpeffDefinition>(File.ReadAllText(file));
                    obj.id = Path.GetFileNameWithoutExtension(file);
                    return obj;
                })
                .ToDictionary(def => def.id);

            /* Load enemy remap list */
            ENEMY_REMAPS = JsonConvert.DeserializeObject<List<EnemyRemap>>(File.ReadAllText(Utility.ResourcePath(@"overrides\enemy_remap.json")));

            /* Load map icon overrides */
            MAP_ICONS = JsonConvert.DeserializeObject<Dictionary<string, Layout.MapPoint.Icon>>(File.ReadAllText(Utility.ResourcePath(@"overrides\map_icons.json")));

            /* Load hair id remap to elden ring hair part ids */
            HAIR_REMAP = JsonConvert.DeserializeObject<Dictionary<string, Hair>>(File.ReadAllText(Utility.ResourcePath(@"overrides\face\hair.json")));

            /* Load all json files in overrides/face and stick them in a dictionary for us to grab from */
            FACE_REMAP = Directory.GetFiles(Utility.ResourcePath(@"overrides\face"))
                .Where(file => Path.GetFileNameWithoutExtension(file).ToLower().Trim() != "hair")
                .Select(file =>
                {
                    var obj = new FaceData(JsonConvert.DeserializeObject<Dictionary<string, byte>>(File.ReadAllText(file)));
                    obj.id = Path.GetFileNameWithoutExtension(file);
                    return obj;
                })
                .ToDictionary(remap => remap.id);

            /* Loading tips overrides */
            LOADING_TIPS = JsonConvert.DeserializeObject<List<LoadingTip>>(File.ReadAllText(Utility.ResourcePath(@"overrides\loading_tips.json")));

            /* Loading region bytes */
            REGION = JsonConvert.DeserializeObject<Dictionary<string, byte>>(File.ReadAllText(Utility.ResourcePath(@"overrides\region.json")));
        }

        /* Classes for serializing */
        public record PlayerClass(string name, string description, Dictionary<string, int> data);

        public record PlayerRace(string name, string description, byte id);

        public record Gift(string name, string description, Dictionary<string, int> data);

        public record AlchemyInfo(string comment, string id, CharacterContent.Stats.Tier tier, List<string> ingredients);

        public record SkillInfo(string comment, int row, CharacterContent.Stats.Tier tier, int value, ItemText text)
        {
            public bool HasTextChanges()
            {
                return text != null && (text.name != null || text.summary != null || text.description != null || text.effect != null);
            }
        }

        public record SpellRemap(string id, string comment, int row, ItemText text)
        {
            public bool HasTextChanges()
            {
                return text != null && (text.name != null || text.summary != null || text.description != null || text.effect != null);
            }
        }

        public record ItemRemap(string id, string comment, ItemManager.Type type, int row, ItemManager.Infusion infusion, ItemText text, int skill = -1, int upgrade = 0)
        {
            public bool HasTextChanges()
            {
                return text != null && (text.name != null || text.summary != null || text.description != null || text.effect != null);
            }
        }

        public record ItemText(string name, string summary, string description, string effect, string[] enchant);

        public record SpeffDefinition(string comment, int row, Dictionary<string, string> data,
                                        SpeffManager.Speff.Effect.MagicEffect icon = SpeffManager.Speff.Effect.MagicEffect.None)
        {
            public string id { get; set; }
        }

        public record ItemDefinition(string comment, ItemManager.Type type, int row, ItemManager.Infusion infusion,
                                    ItemText text, Dictionary<string, string> data, int skill = -1, int upgrade = 0, bool useIcon = false)
        {
            public string id { get; set; }
        }

        public record EnemyRemapData(int row, Dictionary<string, string> data)
        {
            public EnemyRemapData(int row)
                : this(row, new())
            {}
        }

        public record EnemyRemap(string id, string comment, string character, EnemyRemapData npc, EnemyRemapData think)
        {
            /* Default constructor, points to a Goat */
            public EnemyRemap()
                : this("DEFAULT", "Default constructor, used when no remap found. Creates a goat.", "c6060", new(60600010), new(60600000))
            {}
        }

        public record FaceData(Dictionary<string, byte> data)
        {
            public string id { get; set; }
        }

        public record Hair(byte part, Hair.Color color)
        {
            [JsonConverter(typeof(StringEnumConverterWithSpaces))]
            public enum Color
            {
                Black, DarkBrown, Brown, LightBrown, DirtyBlonde, Blonde, White, Gray, Grey, Red
            }

            public byte[] GetColor()
            {
                switch (color)
                {
                    case Color.Black: return new byte[] { 0, 0, 0 };         // @TODO: ai autocomplete set these values and they are fine for testing but CHANGE THEM LATER
                    case Color.DarkBrown: return new byte[] { 34, 17, 0 };
                    case Color.Brown: return new byte[] { 85, 42, 0 };
                    case Color.LightBrown: return new byte[] { 170, 85, 0 };
                    case Color.DirtyBlonde: return new byte[] { 221, 170, 85 };
                    case Color.Blonde: return new byte[] { 255, 255, 170 };
                    case Color.White: return new byte[] { 255, 255, 255 };
                    case Color.Grey:
                    case Color.Gray: return new byte[] { 170, 170, 170 };
                    case Color.Red: return new byte[] { 170, 0, 0 };
                    default: return new byte[] { 0, 0, 0 };
                }
            }
        }

        public record LoadingTip(string title, string text);

        public class StringEnumConverterWithSpaces : JsonConverter
        {
            public override bool CanConvert(Type objectType)
            {
                return objectType.IsEnum;
            }

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
            {
                if (!CanConvert(objectType))
                    throw new ArgumentException("Type cannot be converted as it is not an enum", nameof(objectType));

                if (reader.Value is not string curVal)
                    throw new Exception("Reader value is not a string");

                curVal = curVal.Replace(" ", "");
                if (!Enum.TryParse(objectType, curVal, true, out var enumVal))
                    throw new Exception($"Cannot convert {reader.Value} to type {objectType.Name}");

                return enumVal;
            }

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                throw new NotImplementedException(); // This should only be used for reading, not writing
            }
        }
    }
}
