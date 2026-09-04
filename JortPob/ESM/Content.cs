using JortPob.Common;
using JortPob.Scripts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;

namespace JortPob
{
    /* Content is effectively any physical object in the game world. Anything that has a physical position in a cell */
    [DebuggerDisplay("{type} :: {id}")]
    public abstract class Content
    {
        public readonly Cell cell;

        public readonly string id;   // record id
        public readonly string name; // can be null!
        public readonly ESM.Type type;

        public uint entity;  // entity id, usually 0
        public string papyrus { get; private set; } // papyrus script id if it has one (usually null)
        public Vector3 relative;
        public Int2 load; // if a piece of content needs tile load data this is where it's stored

        public readonly Vector3 position;
        public Vector3 rotation;
        public readonly int scale;  // scale in converted to a int where 100 = 1.0f scale. IE:clamp to nearest 1%. this is to group scale for asset generation.

        public string mesh;  // can be null!

        public Content(Cell cell, JsonNode json, Record record)
        {
            this.cell = cell;
            id = record.json["id"].ToString();
            name = record.json["name"]?.GetValue<string>();

            type = record.type;
            entity = 0;

            papyrus = string.IsNullOrEmpty(record.json["script"]?.GetValue<string>()) ? null : record.json["script"]!.GetValue<string>();

            float x = float.Parse(json["translation"][0].ToString());
            float z = float.Parse(json["translation"][1].ToString());
            float y = float.Parse(json["translation"][2].ToString());

            float i = float.Parse(json["rotation"][0].ToString());
            float j = float.Parse(json["rotation"][1].ToString());
            float k = float.Parse(json["rotation"][2].ToString());

            Vector3 r = Utility.ConvertRotation(new(i, j, k));

            relative = new();
            position = new Vector3(x, y, z) * Const.GLOBAL_SCALE;
            rotation = Utility.ToDegrees(r);
            scale = (int)((json["scale"] != null ? float.Parse(json["scale"].ToString()) : 1f) * 100);
        }

        /* Copy constructor for emitters */
        public Content(Cell cell, string id, string name, ESM.Type type, Int2 load, string papyrus, Vector3 position, Vector3 rotation, int scale)
        {
            this.cell = cell;
            this.id = id;
            this.name = name;
            this.type = type;
            this.load = load;
            this.papyrus = papyrus;
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }
        
        /* Copy constructor for Phasing */
        public Content(Content content, Cell cell, Vector3 position, Vector3 rotation)
        {
            this.cell = cell;
            this.position = position;
            this.rotation = rotation;

            this.id = content.id;
            this.name = content.name;
            this.type = content.type;
            this.scale = content.scale;
            this.entity = content.entity;
            this.papyrus = content.papyrus;
            this.relative = content.relative;
            this.load = content.load;
            this.mesh = content.mesh;
        }
    }

    /* abstrat class that both humanoid NPCs and creature derive from */
    public abstract class CharacterContent : Content
    {
        // Never EVER assign "Race.Custom" to anything! It is only used by SoundManager classes to handle unique voice roles
        public enum Race { Custom = -2, Creature = -1, Any = 0, Argonian = 1, Breton = 2, DarkElf = 3, HighElf = 4, Imperial = 5, Khajiit = 6, Nord = 7, Orc = 8, Redguard = 9, WoodElf = 10 }
        public enum Sex { Any, Male, Female };
        public enum Service {
            OffersTraining, BartersIngredients, BartersApparatus, BartersAlchemy, BartersClothing, OffersSpells, BartersWeapons,
            BartersArmor, BartersBooks, BartersMiscItems, BartersEnchantedItems, OffersEnchanting, OffersSpellmaking, BartersRepairItems,
            OffersRepairs, BartersLockpicks, BartersProbes, BartersLights
        };

        // used for determining crime response type
        public enum Witness
        {
            None, Citizen, Guard
        }

        public readonly string job, faction; // class is job, cant used reserved word
        public readonly Race race;
        public readonly Sex sex;

        public readonly int level, disposition, reputation, rank, gold;
        public readonly int hello, fight, flee, alarm;
        public readonly bool dead;

        public readonly bool essential; // player gets called dumb if they kill this dood
        public Witness witness; // this value is set based on local npcs. defaults none. if guard or citizen then crimes comitted against this npc will cause bounty

        public Script.Flag packageDefaultFlag; // can be null. base ai package. if a script switches packages, it returns to this one when it's done.
        public readonly List<Script.Flag> packageEventFlags; // all ai package flags for this content. used by switcher to clear all running events before a switch
        public readonly List<AiPackage> packages; // defines some simple behaviours an npc can have like wandering around

        public readonly Stats stats; // skills and attributes

        public readonly List<Service> services;

        public List<(string id, int quantity)> inventory;
        public List<(string id, int quantity, bool initial)> flex; // flex is for items that can be added or removed by scripts. these items will be awarded via itemlot on death seperate from regular item drop and have script flags for enable/disable. "inital" determines if items are present at game start or not
        public ItemManager.InventoryInfo inventoryInfo; // will be null intitially. this becomes the actually resolved inventory for this content later on in the build

        public List<string> spells; // spells this character knows or sells as a vendor

        public List<(string id, int quantity)> barter; // can be null

        public List<Travel> travel;  // travel destinations for silt strider people, mage guild teles, etc...

        public Script.Flag treasure; // only used if this is a dead body npc and it has treasure. otherwise null. NEVER SET THIS FOR A LIVING NPC!!!

        public bool follower;       // Flagged for msb promoted followers. If true this character will be placed in a HugeTile and its scripts will be compiled into ScriptCommon.

        public class Travel : DoorContent.Warp
        {
            public string name;
            public int cost;
            public Travel(JsonNode json) : base(json)
            {
                cost = 100;
            }

            public void ApplyParams(int map, int x, int y, int block, uint entity, string name, int cost)
            {
                ApplyParams(map, x, y, block, entity, prompt);
                this.name = name;
                this.cost = cost;
            }
        }

        public class Stats
        {
            public enum Tier { Novice = 0, Apprentice = 25, Journeyman = 50, Expert = 75, Master = 100 }
            public enum Skill { Acrobatics, Alchemy, Alteration, Armorer, Athletics, Axe, Block, BluntWeapon, Conjuration, Destruction, Enchant, HandToHand, HeavyArmor, Illusion, LightArmor, LongBlade, Marksman, MediumArmor, Mercantile, Mysticism, Restoration, Security, ShortBlade, Sneak, Spear, Speechcraft, Unarmored };
            public enum Attribute { Strength, Intelligence, Willpower, Agility, Speed, Endurance, Personality, Luck };

            private readonly Dictionary<Skill, int> skills;
            private readonly Dictionary<Attribute, int> attributes;

            /* Defined stats for a creature constructor */
            public Stats(JsonNode json, int level)
            {
                attributes = new();
                skills = new();

                foreach (Attribute attribute in Enum.GetValues(typeof(Attribute)))
                {
                    int val = json[attribute.ToString().ToLower()].GetValue<int>();
                    attributes.Add(attribute, val);
                }

                foreach (Skill skill in Enum.GetValues(typeof(Skill)))
                {
                    skills.Add(skill, Math.Min(100, level * 5));
                }
            }

            /* Defined stats constructor */
            public Stats(JsonNode json)
            {
                attributes = new();
                skills = new();

                JsonArray jsonAttributes = json["attributes"].AsArray();
                JsonArray jsonSkills = json["skills"].AsArray();

                int i = 0;
                foreach (Attribute attribute in Enum.GetValues(typeof(Attribute)))
                {
                    attributes.Add(attribute, jsonAttributes[i++].GetValue<int>());
                }

                i = 0;
                foreach (Skill skill in Enum.GetValues(typeof(Skill)))
                {
                    skills.Add(skill, jsonSkills[i++].GetValue<int>());
                }
            }

            /* Autocalculated stats constructor */
            public Stats(Sex sex, RaceInfo raceInfo, JobInfo jobInfo, int level)
            {
                attributes = new();
                skills = new();

                foreach (Attribute attribute in Enum.GetValues(typeof(Attribute)))
                {
                    float baseVal = raceInfo.GetAttribute(sex, attribute);  // base racial value for attribute
                    float bonus = 0;
                    if(jobInfo.HasAttribute(attribute)) { baseVal += 10f; }
                    foreach(Skill skill in Enum.GetValues(typeof(Skill)))
                    {
                        if(attribute == GetParent(skill))
                        {
                            if(jobInfo.HasMajor(skill)) { bonus += 1f; }
                            else if(jobInfo.HasMinor(skill)) { bonus += .5f; }
                            else { bonus += .2f; }
                        }
                    }

                    int calculatedValue = (int)(baseVal + (bonus * (level - 1)));
                    attributes.Add(attribute, calculatedValue);
                }

                foreach (Skill skill in Enum.GetValues(typeof(Skill)))
                {
                    float baseVal = raceInfo.GetSkill(skill);
                    float bonus;
                    if (jobInfo.HasMajor(skill)) { baseVal += 30f; bonus = 1f; }
                    else if (jobInfo.HasMinor(skill)) { baseVal += 15f; bonus = 1f; }
                    else { baseVal += 5f; bonus = .1f; }

                    if(jobInfo.HasSpecialization(skill)) { baseVal += 5f; bonus += .5f; }

                    int calculatedValue = (int)(baseVal + (bonus * (level - 1)));
                    skills.Add(skill, calculatedValue);
                }
            }

            private Attribute GetParent(Skill skill)
            {
                switch (skill)
                {
                    case CharacterContent.Stats.Skill.HeavyArmor:
                    case CharacterContent.Stats.Skill.MediumArmor:
                    case CharacterContent.Stats.Skill.Spear:
                        return Attribute.Endurance;
                    case CharacterContent.Stats.Skill.Acrobatics:
                    case CharacterContent.Stats.Skill.Armorer:
                    case CharacterContent.Stats.Skill.Axe:
                    case CharacterContent.Stats.Skill.BluntWeapon:
                    case CharacterContent.Stats.Skill.LongBlade:
                        return Attribute.Strength;
                    case CharacterContent.Stats.Skill.Block:
                    case CharacterContent.Stats.Skill.LightArmor:
                    case CharacterContent.Stats.Skill.Marksman:
                    case CharacterContent.Stats.Skill.Sneak:
                        return Attribute.Agility;
                    case CharacterContent.Stats.Skill.Athletics:
                    case CharacterContent.Stats.Skill.HandToHand:
                    case CharacterContent.Stats.Skill.ShortBlade:
                    case CharacterContent.Stats.Skill.Unarmored:
                        return Attribute.Speed;
                    case CharacterContent.Stats.Skill.Mercantile:
                    case CharacterContent.Stats.Skill.Speechcraft:
                    case CharacterContent.Stats.Skill.Illusion:
                        return Attribute.Personality;
                    case CharacterContent.Stats.Skill.Security:
                    case CharacterContent.Stats.Skill.Alchemy:
                    case CharacterContent.Stats.Skill.Conjuration:
                    case CharacterContent.Stats.Skill.Enchant:
                        return Attribute.Intelligence;
                    case CharacterContent.Stats.Skill.Alteration:
                    case CharacterContent.Stats.Skill.Destruction:
                    case CharacterContent.Stats.Skill.Mysticism:
                    case CharacterContent.Stats.Skill.Restoration:
                        return Attribute.Willpower;
                    default:
                        throw new Exception("What the fuck");
                }
            }

            public int Get(Skill skill) { return skills[skill]; }
            public int Get(Attribute attribute) { return attributes[attribute]; }

            public Tier GetTier(Skill skill) {
                int val = skills[skill];
                if (val >= (int)Tier.Master) { return Tier.Master; }
                else if(val >= (int)Tier.Expert) { return Tier.Expert; }
                else if(val >= (int)Tier.Journeyman) { return Tier.Journeyman; }
                else if(val >= (int)Tier.Apprentice) { return Tier.Apprentice; }
                else { return Tier.Novice; }
            }

            /* Return # highest skills. This is how MW determines trainer skills */
            public List<Skill> GetHighest(int num)
            {
                var list = skills.ToList();
                list.Sort((x, y) => y.Value.CompareTo(x.Value));

                List<Skill> highest = new();
                for(int i=0;i<num||i<list.Count();i++)
                {
                    highest.Add(list[i].Key);
                }

                return highest;
            }
        }

        /* Defines some values for ai packages */
        public class AiPackage
        {
            public enum Type { Wander, Travel, Follow, Escort }   // escort seems unused. wander doubles as "nothing"

            /* Not all values are used for every type. I decided against making each package type it's own class to make construction easier */
            public readonly Type type;
            public readonly float distance;
            public readonly int duration;
            public readonly string target;
            public readonly Vector3 position;
            public Vector3 relative;
            public readonly string location;

            public AiPackage(JsonNode json)
            {
                type = Enum.Parse<AiPackage.Type>(json["type"].GetValue<string>(), true);

                distance = json["distance"]?.GetValue<float>() ?? 0f;
                duration = json["duration"]?.GetValue<int>() ?? 0;

                target = json["target"]?.GetValue<string>();
                location = string.IsNullOrEmpty(json["cell"]?.GetValue<string>()) ? null : json["cell"]?.GetValue<string>();

                if (json["location"] != null)
                {
                    JsonArray array = json["location"].AsArray();
                    float x = array[0].GetValue<float>();
                    float y = array[2].GetValue<float>();
                    float z = array[1].GetValue<float>();

                    if(x > 3e38) { return; }  // the "default" value for these is insane so this is a quick check

                    Vector3 p = new(x, y, z);
                    position = p * Const.GLOBAL_SCALE;
                }
            }
        }

        /* Normal CharacterContent contructor */
        public CharacterContent(ESM esm, Cell cell, JsonNode json, Record record) : base(cell, json, record)
        {
            /* NPC Specific data */
            if (type == ESM.Type.Npc)
            {
                race = (Race)System.Enum.Parse(typeof(Race), record.json["race"].ToString().Replace(" ", ""));
                job = record.json["class"].ToString();
                faction = record.json["faction"].ToString().Trim() != "" ? record.json["faction"].ToString() : null;

                sex = record.json["npc_flags"].ToString().ToLower().Contains("female") ? Sex.Female : Sex.Male;

                disposition = int.Parse(record.json["data"]["disposition"].ToString());
                reputation = int.Parse(record.json["data"]["reputation"].ToString());
                rank = int.Parse(record.json["data"]["rank"].ToString());

                if (record.json["data"]["stats"] != null)
                {
                    stats = new(record.json["data"]["stats"]);
                }
                else
                {
                    stats = new(sex, esm.GetRace(record.json["race"].ToString()), esm.GetJob(job), level);
                }
            }

            /* Creature spefic data */
            else
            {
                race = Race.Creature;
                job = "none";
                faction = null;

                sex = Sex.Male;

                disposition = 50;
                reputation = 0;
                rank = 0;

                stats = new(record.json["data"], level);
            }

            /* Generic data used by both NPC and Creature */
            essential = record.json["npc_flags"] != null ? record.json["npc_flags"].GetValue<string>().ToLower().Contains("essential") : false;

            level = int.Parse(record.json["data"]["level"].ToString());
            gold = int.Parse(record.json["data"]["gold"].ToString());

            hello = int.Parse(record.json["ai_data"]["hello"].ToString());
            fight = int.Parse(record.json["ai_data"]["fight"].ToString());
            flee = int.Parse(record.json["ai_data"]["flee"].ToString());
            alarm = int.Parse(record.json["ai_data"]["alarm"].ToString());

            witness = Witness.None;
            dead = record.json["data"]["stats"] != null && record.json["data"]["stats"]["health"] != null ? (int.Parse(record.json["data"]["stats"]["health"].ToString()) <= 0) : false;

            packageEventFlags = new();
            packages = new();
            foreach(JsonNode jsonNode in record.json["ai_packages"].AsArray())
            {
                packages.Add(new AiPackage(jsonNode));
            }

            string[] serviceFlags = record.json["ai_data"]["services"].ToString().Split("|");
            services = new();
            foreach (string s in serviceFlags)
            {
                string trim = s.Trim().ToLower().Replace("_", "");
                if(trim == "") { continue; }
                try
                {
                    Service service = (Service)System.Enum.Parse(typeof(Service), trim, true);
                    services.Add(service);
                }
                catch { }
            }

            rotation += new Vector3(0f, 180f, 8);  // models are rotated during conversion, placements like this are rotated here during serializiation to match

            inventory = new();
            flex = new();
            JsonArray invJson = record.json["inventory"].AsArray();
            foreach(JsonNode node in invJson)
            {
                JsonArray item = node.AsArray();
                inventory.Add(new(item[1].GetValue<string>().ToLower(), Math.Max(1, Math.Abs(item[0].GetValue<int>()))));
            }

            spells = new();
            if (record.json["spells"] != null)
            {
                JsonArray spellJson = record.json["spells"].AsArray();
                for(int i=0;i<spellJson.Count;i++)
                {
                    spells.Add(spellJson[i].GetValue<string>().ToLower());
                }
            }

            travel = new();
            JsonArray travelJson = record.json["travel_destinations"].AsArray();
            foreach (JsonNode t in travelJson)
            {
                travel.Add(new Travel(t));
            }
        }

        /* Copy constructor for phasing */
        public CharacterContent(CharacterContent content, Cell cell, Vector3 position, Vector3 rotation) : base(content, cell, position, rotation)
        {
            job = content.job;
            faction = content.faction;
            race = content.race;
            sex = content.sex;
            level = content.level;
            disposition = content.disposition;
            reputation = content.reputation;
            rank = content.rank;
            gold = content.gold;
            hello = content.hello;
            fight = content.fight;
            flee = content.flee;
            alarm = content.alarm;
            dead = content.dead;
            essential = content.essential;
            witness = content.witness;
            packageEventFlags = new();       // we do not want to share a list reference between objects here. each phased copy of the character needs their own unique package event flags
            packages = content.packages;
            stats = content.stats;
            treasure = content.treasure;
            services = content.services;
            inventory = content.inventory;
            flex = content.flex;
            spells = content.spells;
            travel = content.travel;
            barter = content.barter;
            follower = content.follower;
        }

        /* Checks innate fight value to determine if npc is naturally hostile to the player or not */
        public bool IsHostile() { return fight >= Const.FIGHT_THRESHOLD; } // @TODO: recalc with disposition mods based off UESP calc (massive task tbh, save for later if we have time to goober around)

        /* Return true if this npc is a generic guard that can arrest the player for crimes */
        public bool IsGuard() { return job == "Guard" || job == "Ordinator Guard"; }

        /* Return true if this npc has any barter service */
        public bool HasBarter()
        {
            return
                services.Contains(Service.BartersWeapons) ||
                services.Contains(Service.BartersArmor) ||
                services.Contains(Service.BartersClothing) ||
                services.Contains(Service.BartersIngredients) ||
                services.Contains(Service.BartersApparatus) ||
                services.Contains(Service.BartersAlchemy) ||
                services.Contains(Service.BartersBooks) ||
                services.Contains(Service.BartersMiscItems) ||
                services.Contains(Service.BartersEnchantedItems) ||
                services.Contains(Service.BartersRepairItems) ||
                services.Contains(Service.BartersLockpicks) ||
                services.Contains(Service.BartersProbes) ||
                services.Contains(Service.BartersLights);
        }

        public bool SellsSpells()
        {
            return services.Contains(Service.OffersSpells);
        }

        public bool OffersMemorize()
        {
            return
                services.Contains(Service.OffersSpells) ||
                services.Contains(Service.OffersSpellmaking) ||
                OffersTraining(Stats.Skill.Alteration) ||
                OffersTraining(Stats.Skill.Conjuration) ||
                OffersTraining(Stats.Skill.Destruction) ||
                OffersTraining(Stats.Skill.Illusion) ||
                OffersTraining(Stats.Skill.Mysticism) ||
                OffersTraining(Stats.Skill.Restoration);
        }

        public bool OffersEnchanting()
        {
            return job.ToLower() == "enchanter service" || services.Contains(Service.OffersEnchanting) || OffersTraining(Stats.Skill.Enchant);
        }

        public bool OffersTraining(Stats.Skill skill)
        {
            return services.Contains(Service.OffersTraining) && stats.GetHighest(3).Contains(skill) && stats.Get(skill) >= (int)Stats.Tier.Apprentice;
        }

        public bool OffersAlchemy()
        {
            return job.ToLower() == "alchemist service" || job.ToLower() == "apothecary service" || OffersTraining(Stats.Skill.Alchemy);
        }

        public bool OffersTailoring()
        {
            return job.ToLower() == "clothier";
        }

        public bool OffersSmithing()
        {
            return job.ToLower() == "smith" || OffersTraining(Stats.Skill.Armorer);
        }
    }

    /* npcs, humanoid only */
    public class NpcContent : CharacterContent
    {
        public readonly string head, hair;

        // can be null, these fields are resolved by ItemManager.ResolveInventory
        public ItemManager.ItemInfo equipWeaponLeft, equipWeaponRight, equipRange, equipHead, equipBody, equipHands, equipLegs, equipArrow, equipBolt;
        public ItemManager.ItemInfo[] equipAcc, equipGood;

        public NpcContent(ESM esm, Cell cell, JsonNode json, Record record) : base(esm, cell, json, record)
        {
            equipAcc = [];   // initialized with empty arrays because if a character has an empty inventory (barbarians) we will skip resolving equipment for them
            equipGood = [];

            head = record.json["head"].GetValue<string>();
            hair = record.json["hair"].GetValue<string>();
        }

        public NpcContent(NpcContent content, Cell cell, Vector3 position, Vector3 rotation) : base(content, cell, position, rotation)
        {
            head = content.head;
            hair = content.hair;

            equipWeaponLeft = content.equipWeaponLeft;
            equipWeaponRight = content.equipWeaponRight;
            equipRange = content.equipRange;
            equipHead = content.equipHead;
            equipBody = content.equipBody;
            equipHands = content.equipHands;
            equipLegs = content.equipLegs;
            equipArrow = content.equipArrow;
            equipBolt = content.equipBolt;
            equipAcc = content.equipAcc;
            equipGood = content.equipGood;
        }
    }

    public class PhasedNpcContent : NpcContent
    {
        public readonly uint source;  // source entity id. from original NpcContent that was converted to phased
        public readonly int phase;   // index of which phase this is for the phased npc

        public PhasedNpcContent(NpcContent content, Cell cell, Vector3 position, Vector3 rotation, uint source, int phase) : base(content, cell, position, rotation)
        {
            this.source = source;
            this.phase = phase;
        }
    }

    /* creatures, both leveled and non-leveled */
    public class CreatureContent : CharacterContent
    {
        public CreatureContent(ESM esm, Cell cell, JsonNode json, Record record) : base(esm, cell, json, record)
        {
            /* Parent constructor does all the work */
        }
    }

    /* Abstract base class for any content that just ends up as a static mesh in the overworld, excluding some special cases like loose items */
    public abstract class StaticContent : Content
    {
        public StaticContent(Cell cell, JsonNode json, Record record) : base(cell, json, record) { }
        public StaticContent(Cell cell, string id, string name, ESM.Type type, Int2 load, string papyrus, Vector3 position, Vector3 rotation, int scale) : base(cell, id, name, type, load, papyrus, position, rotation, scale) { }
    }

    /* static meshes to be converted to assets */
    public class AssetContent : StaticContent
    {
        public AssetContent(Cell cell, JsonNode json, Record record) : base(cell, json, record)
        {
            mesh = record.json["mesh"].ToString().ToLower();
        }

        public EmitterContent ConvertToEmitter()
        {
            return new EmitterContent(cell, id, name, type, load, papyrus, position, rotation, scale, mesh);
        }
    }

    /* covers markers like TempleMarker and PrisonMarker. These are statics placed in morrowin to mark locatinos with special purposes. they should not show up ingame as objects */
    public class MarkerContent : Content
    {
        public Layout.InterventionPoint.Type markerType;

        public MarkerContent(Cell cell, JsonNode json, Record record, Layout.InterventionPoint.Type marketType) : base(cell, json, record)
        {
            this.markerType = marketType;
            rotation += new Vector3(0f, 180f, 0f);  // models are rotated during conversion, placements like this are rotated here during serializiation to match
        }
    }

    /* beds, which will have esd objects assocaitd with them */
    public class BedContent : AssetContent
    {
        public readonly string ownerNpc; // npc record id of the owenr of this bed, can be null
        public readonly string ownerFaction; // faction id that owns this bed, player can use it if they are in that faction. can be null
        public readonly string ownerGlobal; // a global var is used to control ownership. used by rentable beds

        public BedContent(Cell cell, JsonNode json, Record record) : base(cell, json, record)
        {
            ownerNpc = json["owner"]?.GetValue<string>();
            ownerFaction = json["owner_faction"]?.GetValue<string>();
            ownerGlobal = json["owner_global"]?.GetValue<string>();
        }
    }

    /* doors, both warp doors and activator doors */
    public class DoorContent : StaticContent
    {
        public class Warp
        {
            // this data comes from the esm, we use it to resolve the actual data we will use
            public readonly string cell;
            public readonly Vector3 position, rotation;

            // this is the actual warp data we generate
            public int map, x, y, block;
            public uint entity;
            public string prompt; // used for the action button prompt. this is either the cell name, region name, or a generic "Morrowind" as a last case

            public Warp(JsonNode json)
            {
                float x = float.Parse(json["translation"][0].ToString());
                float z = float.Parse(json["translation"][1].ToString());
                float y = float.Parse(json["translation"][2].ToString());

                float i = float.Parse(json["rotation"][0].ToString());
                float j = float.Parse(json["rotation"][1].ToString());
                float k = float.Parse(json["rotation"][2].ToString());

                Vector3 r = Utility.ConvertRotation(new(i, j, k));

                position = new Vector3(x, y, z) * Const.GLOBAL_SCALE;
                rotation = Utility.ToDegrees(r) + new Vector3(0f, 180f, 0); // bonus rotation here, actual models get rotated 180 Y in the model itself, placements like this need it here
                cell = json["cell"].ToString().Trim();
                if (cell == "") { cell = null; }
            }

            public void ApplyParams(int map, int x, int y, int block, uint entity, string prompt)
            {
                this.map = map;
                this.x = x;
                this.y = y;
                this.block = block;
                this.entity = entity;
                this.prompt = prompt;
            }
        }

        public Warp warp;
        public DoorContent(Cell cell, JsonNode json, Record record) : base(cell, json, record)
        {
            mesh = record.json["mesh"].ToString().ToLower();

            if (json["destination"]  == null) { warp = null; }
            else
            {
                warp = new(json["destination"]);
            }
        }

        public void ApplyWarpParams(int map, int x, int y, int block, uint warpEntity, string prompt)
        {
            warp.ApplyParams(map, x, y, block, warpEntity, prompt);
        }
    }

    /* static mesh of a container in the world that can **CAN** (but not always) be lootable */
    public class ContainerContent : StaticContent
    {
        public readonly string ownerNpc; // npc record id of the owenr of this container, can be null
        public readonly string ownerFaction; // faction id that owns this container, player can take it if they are in that faction. can be null

        public List<(string id, int quantity)> inventory;
        public List<(string id, int quantity, bool initial)> flex; // see description on flex from charactercontent
        public ItemManager.InventoryInfo inventoryInfo; // will be null intitially. this becomes the actually resolved inventory for this contetn later on in the build

        public Script.Flag treasure; // if this container content has a treasure event and is a lootable container, this flag will be the "has been looted" flag. otherwise null

        public ContainerContent(Cell cell, JsonNode json, Record record) : base(cell, json, record)
        {
            mesh = record.json["mesh"].ToString().ToLower();
            if (json["owner"] != null) { ownerNpc = json["owner"].GetValue<string>(); }
            if (json["owner_faction"] != null) { ownerFaction = json["owner_faction"].GetValue<string>(); }

            inventory = new();
            flex = new();
            JsonArray invJson = record.json["inventory"].AsArray();
            foreach (JsonNode node in invJson)
            {
                JsonArray item = node.AsArray();
                inventory.Add(new(item[1].GetValue<string>().ToLower(), Math.Max(1, Math.Abs(item[0].GetValue<int>()))));  // get item record id and quantity from json
            }
        }

        // Generates button prompt text for looting this container
        public string ActionText()
        {
            if (ownerNpc != null || ownerFaction != null) { return $"Steal from {name}"; }
            return $"Loot {name}";
        }
    }

    /* PickableContent */    // plants you can pick for alchemy ingredients. EX: rowa berry bushes
    public class PickableContent : StaticContent
    {
        public List<(string id, int quantity)> inventory;

        public PickableContent(Cell cell, JsonNode json, Record record) : base(cell, json, record)
        {
            mesh = record.json["mesh"].ToString().ToLower();

            inventory = new();
            JsonArray invJson = record.json["inventory"].AsArray();
            foreach (JsonNode node in invJson)
            {
                JsonArray item = node.AsArray();
                inventory.Add(new(item[1].GetValue<string>().ToLower(), Math.Max(1, Math.Abs(item[0].GetValue<int>()))));  // get item record id and quantity from json
            }
        }

        // Generates button prompt text for looting this container
        public string ActionText()
        {
            return $"Harvest {name}";
        }
    }

    /* static mesh of an item placed in the world that can **CAN** (but not always) be pickupable */
    public class ItemContent : Content
    {
        public readonly string ownerNpc; // npc record id of the owenr of this item, can be null
        public readonly string ownerFaction; // faction id that owns this item, player can take it if they are in that faction. can be null

        public readonly int value; // morrowind gp value for this item

        public Script.Flag treasure; // if this item content has a treasure event and is a lootable item, this flag will be the "is picked up" flag. otherwise null

        public ItemContent(Cell cell, JsonNode json, Record record) : base(cell, json, record)
        {
            mesh = record.json["mesh"].ToString().ToLower();
            if (json["owner"] != null ) { ownerNpc = json["owner"].GetValue<string>(); }
            if (json["owner_faction"] != null) { ownerFaction = json["owner_faction"].GetValue<string>(); }
            value = record.json["data"]["value"].GetValue<int>();
        }

        // Generates button prompt text for looting this container
        public string ActionText()
        {
            if (ownerNpc != null || ownerFaction != null) { return $"Steal {name}"; }
            return $"Pick up {name}";
        }
    }

    /* static meshes that have emitters/lights EX: candles/campfires -- converted to assets but also generates ffx files and params to make them work */
    public class EmitterContent : StaticContent
    {
        public EmitterContent(Cell cell, JsonNode json, Record record) : base(cell, json, record)
        {
            mesh = record.json["mesh"].ToString().ToLower();
        }

        public EmitterContent(Cell cell, string id, string name, ESM.Type type, Int2 load, string papyrus, Vector3 position, Vector3 rotation, int scale, string mesh) : base(cell, id, name, type, load, papyrus, position, rotation, scale)
        {
            this.mesh = mesh;
        }
    }

    /* invisible lights with no static mesh associated */
    public class LightContent : Content 
    {
        public readonly Byte4 color;
        public readonly float radius, weight;
        public readonly int value, time;

        public bool dynamic, fire, negative, defaultOff;
        public Mode mode;

        public enum Mode { Flicker, FlickerSlow, Pulse, PulseSlow, Default }

        public LightContent(Cell cell, JsonNode json, Record record) : base(cell, json, record)
        {
            int r = int.Parse(record.json["data"]["color"][0].ToString());
            int g = int.Parse(record.json["data"]["color"][1].ToString());
            int b = int.Parse(record.json["data"]["color"][2].ToString());
            int a = int.Parse(record.json["data"]["color"][3].ToString());
            color = new(r, g, b, a);  // 0 -> 255 colors

            radius = float.Parse(record.json["data"]["radius"].ToString()) * Const.GLOBAL_SCALE;
            weight = float.Parse(record.json["data"]["weight"].ToString());

            value = int.Parse(record.json["data"]["value"].ToString());
            time = int.Parse(record.json["data"]["time"].ToString());

            string flags = record.json["data"]["flags"].ToString();

            dynamic = flags.Contains("DYNAMIC");
            fire = flags.Contains("FIRE");
            negative = flags.Contains("NEGATIVE");
            defaultOff = flags.Contains("OFF_BY_DEFAULT");

            if (flags.Contains("FLICKER_SLOW")) { mode = Mode.FlickerSlow; }
            else if (flags.Contains("FLICKER")) { mode = Mode.Flicker; }
            else if (flags.Contains("PULSE_SLOW")) { mode = Mode.PulseSlow; }
            else if (flags.Contains("PULSE")) { mode = Mode.Pulse; }
            else { mode = Mode.Default; }
        }
    }
}
