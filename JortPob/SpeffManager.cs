using JortPob.Common;
using JortPob.Scripts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using WitchyFormats;
using static JortPob.SpeffManager.Speff.Effect;
using static JortPob.SpeffManager.SpeffEnchant;

namespace JortPob
{
    public class SpeffManager
    {
        // Special SPEFFs that are used for script functionality
        public enum Functional
        {
            Alarming = 1000000,      // 5 minute speff that is applied when you are caught committing a crime. triggers "Alarmed" filter in dialog
            NpcFollow = 1000001,     // speff used to switch npcs from normal logic to follower logic. lasts 1 second and is re-applied constantly during follower package
            VoidMurder = 1000002,    // speff applied to npcs when they 'StartCombat' so that if they die you are not given a bounty for "murdering" them. Does not persist between loadscreens though
            GoToJail = 1000003,      // when applied to player, triggers a intervention style warp to nearest jail. see GenerateIntervention() in Script.cs
            DivineIntervention = 1000004,   // same as above
            AlmsiviIntervention = 1000005,  // same as above
        }

        public enum Type
        {
            Custom,        // Freeform gen from a json file
            Spell,         // This is a castable spell in morrowind. Some of these are used in scripts to cast an effect on the player (e.g. shrine restoration/cure disease) * most of these will go unused in ER though
            Enchanting,    // This is an enchantment effect on an item. 
            Alchemy        // Alchemy effet from drinking a potion
        }

        public readonly List<Speff> speffs;

        private Paramanager paramanager;
        private MenuTextureManager textureManager;
        private TextManager textManager;

        private int nextSpeffId = 1000100;

        public SpeffManager(ESM esm, Paramanager paramanager, ScriptManager scriptManager, MenuTextureManager textureManager, TextManager textManager)
        {
            this.paramanager = paramanager;
            this.textureManager = textureManager;
            this.textManager = textManager;

            speffs = new();

            /* Generate SPEFF params from overrides. */
            foreach (Override.SpeffDefinition def in Override.GetSpeffDefinitions())
            {
                Speff speff = new SpeffCustom(def.id, nextSpeffId += 10);
                speffs.Add(speff);
            }

            bool SpeffExists(string id)
            {
                foreach(Speff speff in speffs)
                {
                    if (speff.id == id) { return true; }
                }
                return false;
            }

            /* Parse all magical effects from the esm, skip any that were already generated from override stuff above */
            foreach (JsonNode json in esm.GetAllRecordsByType(ESM.Type.Alchemy))
            {
                if(SpeffExists(json["id"].GetValue<string>().Trim().ToLower()) ) { continue; }

                SpeffAlchemy speff = new(nextSpeffId += 10, json);
                speffs.Add(speff);
            }

            foreach (JsonNode json in esm.GetAllRecordsByType(ESM.Type.Enchanting))
            {
                if (SpeffExists(json["id"].GetValue<string>().Trim().ToLower())) { continue; }

                bool isWeapon = false;
                foreach (JsonNode jsonW in esm.GetAllRecordsByType(ESM.Type.Weapon))
                {
                    if (jsonW["enchanting"] != null && jsonW["enchanting"].GetValue<string>().ToLower() == json["id"].GetValue<string>().ToLower())
                    {
                        isWeapon = true; break;
                    }
                }

                SpeffEnchant speff = new(nextSpeffId += 10, json, isWeapon?SpeffEnchant.EquipmentType.Weapon:SpeffEnchant.EquipmentType.Apparel);
                speffs.Add(speff);
            }

            foreach (JsonNode json in esm.GetAllRecordsByType(ESM.Type.Spell))
            {
                if (SpeffExists(json["id"].GetValue<string>().Trim().ToLower())) { continue; }

                SpeffSpell speff = new(nextSpeffId += 10, json);
                speffs.Add(speff);
            }

            /* Generate SPEFF params from the parsed data */
            foreach (Speff speff in speffs)
            {
                // Generate via custom json speff definition
                if (speff.type == SpeffManager.Type.Custom)
                {
                    Override.SpeffDefinition def = Override.GetSpeffDefinition(speff.id);
                    SillyJsonUtils.CopyRowAndModify(paramanager, this, Paramanager.ParamType.SpEffectParam, $"Custom :: {speff.id}", def.row, speff.row, def.data);
                    if (def.icon != SpeffManager.Speff.Effect.MagicEffect.None && !Const.DEBUG_SKIP_MENU_TEXTURES)
                    {
                        SillyJsonUtils.SetField(paramanager, Paramanager.ParamType.SpEffectParam, speff.row, "iconId", (int)(textureManager.icon.GetBuffByType(def.icon).id));
                    }
                }
                // Generate via data parsed from esm
                else 
                {
                    GenerateSpeff(speff);
                }
            }

            /* Create script functions for applying and maintaining "permament" spell effects ( SpellTypes -> Ability, Curse, Disease, Blight, Corprus ) */
            /* We use scripts to handle these because it is very easy to lose a SPEFF even if it has infinite duration. Death generally wipes all SPEFFs for example. */
            /* So instead of applying these SPEFFS directly we instaed set a on/off flag for them and we have a script keep them applied or removedb ased on that */
            foreach(Speff speff in speffs)
            {
                if(speff.type == SpeffManager.Type.Spell)
                {
                    SpeffSpell speffSpell = (SpeffSpell)speff;
                    if(speffSpell.spellType == SpeffSpell.SpellType.Ability || speffSpell.spellType == SpeffSpell.SpellType.Curse || speffSpell.spellType == SpeffSpell.SpellType.Disease || speffSpell.spellType == SpeffSpell.SpellType.Blight || speffSpell.spellType == SpeffSpell.SpellType.Corprus)
                    {
                        speffSpell.flag = scriptManager.common.CreatePermanentSpeff(speffSpell);
                    }
                }
            }

            /* Generate functional speffs */
            FsParam speffParam = paramanager.param[Paramanager.ParamType.SpEffectParam];

            FsParam.Row funcSpeffAlarming = CreateTemplateSpeff(TemplateType.TemporarySelf, $"Functional :: Alarming", (int)Functional.Alarming);
            funcSpeffAlarming["effectEndurance"].Value.SetValue((float)300f);
            paramanager.AddOrReplaceRow(speffParam, funcSpeffAlarming);

            FsParam.Row funcSpeffFollow = CreateTemplateSpeff(TemplateType.TemporarySelf, $"Functional :: NpcFollow", (int)Functional.NpcFollow);
            funcSpeffFollow["effectEndurance"].Value.SetValue((float)1f);
            paramanager.AddOrReplaceRow(speffParam, funcSpeffFollow);

            FsParam.Row funcSpeffVoidMurder = CreateTemplateSpeff(TemplateType.TemporarySelf, $"Functional :: VoidMurder", (int)Functional.VoidMurder);
            funcSpeffVoidMurder["effectEndurance"].Value.SetValue((float)-1f);
            paramanager.AddOrReplaceRow(speffParam, funcSpeffVoidMurder);

            FsParam.Row funcSpeffJail = CreateTemplateSpeff(TemplateType.TemporarySelf, $"Functional :: GoToJail", (int)Functional.GoToJail);
            funcSpeffJail["effectEndurance"].Value.SetValue((float)1f);
            paramanager.AddOrReplaceRow(speffParam, funcSpeffJail);

            FsParam.Row funcSpeffDivine = CreateTemplateSpeff(TemplateType.TemporarySelf, $"Functional :: DivineIntervention", (int)Functional.DivineIntervention);
            funcSpeffDivine["effectEndurance"].Value.SetValue((float)1f);
            paramanager.AddOrReplaceRow(speffParam, funcSpeffDivine);

            FsParam.Row funSpeffAlmsivi = CreateTemplateSpeff(TemplateType.TemporarySelf, $"Functional :: AlmsiviIntervention", (int)Functional.AlmsiviIntervention);
            funSpeffAlmsivi["effectEndurance"].Value.SetValue((float)1f);
            paramanager.AddOrReplaceRow(speffParam, funSpeffAlmsivi);
        }

        // Temp buffs use 1642100 as a base which is the basic Heal incantation effect
        // Permanent buffs use 310000 as a base which is effect of the crimson amber medallion
        // @TODO: on hit effects do not work rn and need work
        private enum TemplateType { PermanentSelf = 310000, TemporarySelf = 1642100, TemporaryOnHit = 6600 }
        private FsParam.Row CreateTemplateSpeff(TemplateType templateType, string name, int id)
        {
            /* Clone our row */
            FsParam param = paramanager.param[Paramanager.ParamType.SpEffectParam];
            FsParam.Row row;

            /* Nullify some values from our templates to make our speff as neutral as possible */
            switch (templateType)
            {
                case TemplateType.TemporarySelf: // Heal
                    row = paramanager.CloneRow(param[(int)templateType], name, id);
                    row["motionInterval"].Value.SetValue(1f);              // for effects like regen this sets the interval between stat changes. morrowind uses 1 second intervals so we will as well!
                    row["spCategory"].Value.SetValue((ushort)0);           // behaviour when stacking or whatever (default is NONE)
                    row["stateInfo"].Value.SetValue((ushort)0);            // visual effect of spefff (i think??)
                    row["changeHpPoint"].Value.SetValue(0);
                    row["bAdjustFaithAblity"].Value.SetValue((byte)0);   // SIC
                    row["vfxId"].Value.SetValue(-1);                     // also visual effect idk guh??
                    row["effectEndurance"].Value.SetValue((float)1f);
                    row["iconId"].Value.SetValue(-1);
                    break;
                case TemplateType.PermanentSelf: // Crimson Amber Mediallion
                    row = paramanager.CloneRow(param[(int)templateType], name, id);
                    row["motionInterval"].Value.SetValue(1f);            // for effects like regen this sets the interval between stat changes. morrowind uses 1 second intervals so we will as well!
                    row["iconId"].Value.SetValue(-1);                    // buff icon
                    row["maxHpRate"].Value.SetValue(1f);
                    row["iconId"].Value.SetValue(-1);
                    break;
                case TemplateType.TemporaryOnHit:
                    row = paramanager.CloneRow(param[(int)templateType], name, id);
                    break;
                default: throw new Exception("Bad!");
            }

            return row;
        }

        private void GenerateSpeff(Speff speff)
        {
            /* Decide on a source row to copy as a template based on the type of magical effect */
            FsParam param = paramanager.param[Paramanager.ParamType.SpEffectParam];
            TemplateType templateType;
            if (speff.type == SpeffManager.Type.Alchemy) { templateType = TemplateType.TemporarySelf; }
            else if (speff.type == SpeffManager.Type.Spell && ((SpeffSpell)speff).spellType == SpeffSpell.SpellType.Spell) { templateType = TemplateType.TemporarySelf; }
            else if (speff.type == SpeffManager.Type.Spell) { templateType = TemplateType.PermanentSelf; }
            else if (speff.type == SpeffManager.Type.Enchanting && ((SpeffEnchant)speff).enchantType != EnchantType.CastOnStrike) { templateType = TemplateType.PermanentSelf; }
            else if (speff.type == SpeffManager.Type.Enchanting) { templateType = TemplateType.TemporaryOnHit; }
            else { throw new Exception("Something weird happened!"); }

            FsParam.Row row = CreateTemplateSpeff(templateType, $"{speff.type} :: {speff.id}", speff.row);

            /* Find and apply icon for buff */
            if (speff.effects.Count() > 0)
            {
                IconManager.BuffInfo buffIcon = textureManager.icon.GetBuffByType(speff.effects[0].effect);
                if (buffIcon != null) { row["iconId"].Value.SetValue((int)(buffIcon.id)); }
            }

            /* Set duration */
            if(templateType == TemplateType.TemporarySelf || templateType == TemplateType.TemporaryOnHit)
            {
                row["effectEndurance"].Value.SetValue((float)speff.Duration());
            }

            /* Apply some values to our speffs based on what stuff its got in its magic effects */
            foreach (Speff.Effect effect in speff.effects)
            {
                switch (effect.effect)
                {
                    case MagicEffect.RestoreHealth:
                        row["changeHpPoint"].Value.SetValue(-effect.Magnitude());
                        break;
                    case MagicEffect.RestoreMagicka:
                        row["changeMpPoint"].Value.SetValue(-effect.Magnitude());
                        break;
                    case MagicEffect.RestoreFatigue:
                        row["changeStaminaPoint"].Value.SetValue(-effect.Magnitude());
                        break;
                    case MagicEffect.FortifyHealth:
                        row["maxHpRate"].Value.SetValue(effect.PositiveScalarMagnitude(0.2f));
                        break;
                    case MagicEffect.FortifyMagicka:
                        row["maxMpRate"].Value.SetValue(effect.PositiveScalarMagnitude(0.2f));
                        break;
                    case MagicEffect.FortifyFatigue:
                        row["maxStaminaRate"].Value.SetValue(effect.PositiveScalarMagnitude(0.2f));
                        break;
                    case MagicEffect.FortifyAttribute:
                        switch (effect.attribute)
                        {
                            case Speff.Effect.Attribute.Endurance:
                                row["addLifeForceStatus"].Value.SetValue((sbyte)effect.Magnitude());
                                break;
                            case Speff.Effect.Attribute.Willpower:
                                row["addWillpowerStatus"].Value.SetValue((sbyte)effect.Magnitude());
                                break;
                            case Speff.Effect.Attribute.Strength:
                                row["addStrengthStatus"].Value.SetValue((sbyte)effect.Magnitude());
                                break;
                            case Speff.Effect.Attribute.Agility:
                                row["addDexterityStatus"].Value.SetValue((sbyte)effect.Magnitude());
                                break;
                            case Speff.Effect.Attribute.Intelligence:
                                row["addMagicStatus"].Value.SetValue((sbyte)effect.Magnitude());
                                break;
                            case Speff.Effect.Attribute.Speed:
                                row["addEndureStatus"].Value.SetValue((sbyte)effect.Magnitude());
                                break;
                            case Speff.Effect.Attribute.Personality:
                                row["addLuckStatus"].Value.SetValue((sbyte)effect.Magnitude());
                                break;
                            case Speff.Effect.Attribute.Luck:
                                row["addLuckStatus"].Value.SetValue((sbyte)effect.Magnitude());
                                break;
                        }
                        break;
                }
            }

            /* Plonk our new row into the param */
            paramanager.AddOrReplaceRow(param, row);
        }

        public Speff GetSpeff(string id, SpeffManager.Type type)
        {
            foreach(Speff speff in speffs)
            {
                if(speff.id == id.Trim().ToLower() && speff.type == type)
                {
                    return speff;
                }
            }
            return null;
        }

        public Speff GetSpeff(string id)
        {
            foreach (Speff speff in speffs)
            {
                if (speff.id == id.Trim().ToLower())
                {
                    return speff;
                }
            }
            return null;
        }

        public SpeffAlchemy GetAlchemySpeff(string id) { return GetSpeff(id, SpeffManager.Type.Alchemy) as SpeffAlchemy; }
        public SpeffEnchant GetEnchantingSpeff(string id) { return GetSpeff(id, SpeffManager.Type.Enchanting) as SpeffEnchant; }
        public SpeffSpell GetSpellSpeff(string id) { return GetSpeff(id, SpeffManager.Type.Spell) as SpeffSpell; }

        public List<SpeffSpell> GetSpeffBySpellType(SpeffSpell.SpellType spellType)
        {
            List<SpeffSpell> matches = new();
            foreach (Speff speff in speffs)
            {
                if (speff.type == SpeffManager.Type.Spell)
                {
                    SpeffSpell speffSpell = (SpeffSpell)speff;
                    if (speffSpell.spellType == spellType)
                    {
                        matches.Add(speffSpell);
                    }
                }
            }
            return matches;
        }

        public List<SpeffSpell> GetDiseases() { return GetSpeffBySpellType(SpeffSpell.SpellType.Disease); }
        public List<SpeffSpell> GetBlights() { return GetSpeffBySpellType(SpeffSpell.SpellType.Blight); }
        public SpeffSpell GetCorprus() { return GetSpeffBySpellType(SpeffSpell.SpellType.Corprus)[0]; }
        public SpeffSpell GetVampirism() { return GetSpeff("vampire attributes", SpeffManager.Type.Spell) as SpeffSpell; }

        /* Called by Papyrus compiling to create one off effects for certain Papyrus calls */
        /* For the most part it is simple things like ModStrength where we give an NPC +5 strength or something like that */
        /* In most cases this is used on NPCs as we can't directly modify NPC stats */
        /* But in some cases like ModFatigue or ModMagicka we will use it on player to change their current stamina/mana values */
        /* Input is what we are changing and how much */
        /* Returns row id of created speff */
        public enum StatMod
        {
            Runes,
            MaxHP, MaxMP, MaxSP,
            CurrentHP, CurrentMP, CurrentSP,
            Vigor, Mind, Endurance, Strength, Dexterity, Intelligence, Faith, Arcane
        }
        public int CreateScriptedEffect(StatMod stat, int amount, string name)
        {
            FsParam speffParam = paramanager.param[Paramanager.ParamType.SpEffectParam];
            FsParam.Row row;

            float ToScalar(int magnitude)
            {
                return 1f + (magnitude / 100f);
            }

            switch (stat)
            {
                case StatMod.Runes:
                    {
                        row = CreateTemplateSpeff(TemplateType.TemporarySelf, name, nextSpeffId += 10);
                        row["soul"].Value.SetValue(amount);
                        break;
                    }
                case StatMod.CurrentHP:
                    {
                        row = CreateTemplateSpeff(TemplateType.TemporarySelf, name, nextSpeffId += 10);
                        row["changeHpPoint"].Value.SetValue(-amount);  // this field is weirdly backwards so ye
                        break;
                    }
                case StatMod.CurrentMP:
                    {
                        row = CreateTemplateSpeff(TemplateType.TemporarySelf, name, nextSpeffId += 10);
                        row["changeMpPoint"].Value.SetValue(-amount);  // this field is weirdly backwards so ye
                        break;
                    }
                case StatMod.CurrentSP:
                    {
                        row = CreateTemplateSpeff(TemplateType.TemporarySelf, name, nextSpeffId += 10);
                        row["changeStaminaPoint"].Value.SetValue(-amount);  // this field is weirdly backwards so ye
                        break;
                    }
                case StatMod.MaxHP:
                    {
                        row = CreateTemplateSpeff(TemplateType.PermanentSelf, name, nextSpeffId += 10);
                        row["maxHpRate"].Value.SetValue(ToScalar(amount));
                        row["bCurrHPIndependeMaxHP"].Value.SetValue((byte)0); // sic
                        break;
                    }
                case StatMod.MaxMP:
                    {
                        row = CreateTemplateSpeff(TemplateType.PermanentSelf, name, nextSpeffId += 10);
                        row["maxMpRate"].Value.SetValue(ToScalar(amount));
                        break;
                    }
                case StatMod.MaxSP:
                    {
                        row = CreateTemplateSpeff(TemplateType.PermanentSelf, name, nextSpeffId += 10);
                        row["maxStaminaRate"].Value.SetValue(ToScalar(amount));
                        break;
                    }
                case StatMod.Vigor:
                    {
                        row = CreateTemplateSpeff(TemplateType.PermanentSelf, name, nextSpeffId += 10);
                        row["addLifeForceStatus"].Value.SetValue((sbyte)amount);
                        break;
                    }
                case StatMod.Mind:
                    {
                        row = CreateTemplateSpeff(TemplateType.PermanentSelf, name, nextSpeffId += 10);
                        row["addWillpowerStatus"].Value.SetValue((sbyte)amount);
                        break;
                    }
                case StatMod.Endurance:
                    {
                        row = CreateTemplateSpeff(TemplateType.PermanentSelf, name, nextSpeffId += 10);
                        row["addEndureStatus"].Value.SetValue((sbyte)amount);
                        break;
                    }
                case StatMod.Strength:
                    {
                        row = CreateTemplateSpeff(TemplateType.PermanentSelf, name, nextSpeffId += 10);
                        row["addStrengthStatus"].Value.SetValue((sbyte)amount);
                        break;
                    }
                case StatMod.Dexterity:
                    {
                        row = CreateTemplateSpeff(TemplateType.PermanentSelf, name, nextSpeffId += 10);
                        row["addDexterityStatus"].Value.SetValue((sbyte)amount);
                        break;
                    }
                case StatMod.Intelligence:
                    {
                        row = CreateTemplateSpeff(TemplateType.PermanentSelf, name, nextSpeffId += 10);
                        row["addMagicStatus"].Value.SetValue((sbyte)amount);
                        break;
                    }
                case StatMod.Faith:
                    {
                        row = CreateTemplateSpeff(TemplateType.PermanentSelf, name, nextSpeffId += 10);
                        row["addFaithStatus"].Value.SetValue((sbyte)amount);
                        break;
                    }
                case StatMod.Arcane:
                    {
                        row = CreateTemplateSpeff(TemplateType.PermanentSelf, name, nextSpeffId += 10);
                        row["addLuckStatus"].Value.SetValue((sbyte)amount);
                        break;
                    }
                default: throw new Exception("Invalid one shot scripted effect type."); // unreachable
            }

            speffParam.AddRow(row);
            return row.ID;
        }

        /* Stores info on an item */
        [DebuggerDisplay("Speff :: {type} :: {id} :: {row}")]
        public abstract class Speff
        {
            public readonly string id;  // morrowind record id
            public readonly Type type;  // type of record from morrowind
            public readonly int row;    // param row

            public readonly List<Effect> effects;

            public Speff(int row, JsonNode json)
            {
                id = json["id"].GetValue<string>().Trim().ToLower();
                type = (SpeffManager.Type)System.Enum.Parse(typeof(SpeffManager.Type), json["type"].GetValue<string>());
                this.row = row;

                effects = new();
                JsonArray jsonEffects = json["effects"].AsArray();
                foreach(JsonNode jsonEffect in jsonEffects)
                {
                    effects.Add(new Effect(jsonEffect));
                }
            }

            public Speff(string id, int row)
            {
                this.id = id;
                this.row = row;
                type = SpeffManager.Type.Custom;
                effects = new();
            }

            public int Duration()
            {
                int longest = 0;
                foreach(Effect effect in effects)
                {
                    if (effect.duration > longest) { longest = effect.duration; }
                }
                return longest;
            }

            [DebuggerDisplay("MagicEffect :: {effect} :: {skill} :: {range}")]
            public class Effect
            {
                public enum MagicEffect
                {
                    None, // not a real value from MW. This is a special case for json speffs to use when they don't want any buff icon
                    WaterBreathing, SwiftSwim, WaterWalking, Shield, FireShield, LightningShield, FrostShield, Burden, Feather, Jump, Levitate, SlowFall, Lock, Open, FireDamage, ShockDamage,
                    FrostDamage, DrainAttribute, DrainHealth, DrainMagicka, DrainFatigue, DrainSkill, DamageAttribute, DamageHealth, DamageMagicka, DamageFatigue, DamageSkill, Poison,
                    WeaknessToFire, WeaknessToFrost, WeaknessToShock, WeaknessToMagicka, WeaknessToCommonDisease, WeaknessToBlightDisease, WeaknessToCorprus, WeaknessToPoison, WeaknessToNormalWeapons,
                    DisintegrateWeapon, DisintegrateArmor, Invisibility, Chameleon, Light, Sanctuary, NightEye, Charm, Paralyze, Silence, Blind, Sound, CalmHumanoid, CalmCreature, FrenzyHumanoid,
                    FrenzyCreature, DemoralizeHumanoid, DemoralizeCreature, RallyHumanoid, RallyCreature, Dispel, SoulTrap, Telekinesis, Mark, Recall, DivineIntervention, AlmsiviIntervention,
                    DetectAnimal, DetectEnchantment, DetectKey, SpellAbsorption, Reflect, CureCommonDisease, CureBlightDisease, CureCorprus, CurePoison, CureParalyzation, RestoreAttribute, RestoreHealth,
                    RestoreMagicka, RestoreFatigue, RestoreSkill, FortifyAttribute, FortifyHealth, FortifyMagicka, FortifyFatigue, FortifySkill, FortifyMagickaMultiplier, AbsorbAttribute, AbsorbHealth,
                    AbsorbMagicka, AbsorbFatigue, AbsorbSkill, ResistFire, ResistFrost, ResistShock, ResistMagicka, ResistCommonDisease, ResistBlightDisease, ResistCorprus, ResistPoison,
                    ResistNormalWeapons, ResistParalysis, RemoveCurse, TurnUndead, SummonScamp, SummonClannfear, SummonDaedroth, SummonDremora, SummonGhost, SummonSkeleton, SummonLeastBonewalker,
                    SummonGreaterBonewalker, SummonBonelord, SummonTwilight, SummonHunger, SummonGoldenSaint, SummonFlameAtronach, SummonFrostAtronach, SummonStormAtronach, FortifyAttackBonus,
                    CommandCreature, CommandHumanoid, BoundDagger, BoundLongsword, BoundMace, BoundBattleAxe, BoundSpear, BoundLongbow, ExtraSpell, BoundCuirass, BoundHelm, BoundBoots, BoundShield,
                    BoundGloves, Corprus, Vampirism, SummonCenturionSphere, SunDamage, StuntedMagicka
                }

                public enum Skill
                {
                    None,
                    Armorer, Athletics, Axe, Block, BluntWeapon, HeavyArmor, LongBlade, MediumArmor, Spear,
                    Alchemy, Alteration, Conjuration, Destruction, Enchant, Illusion, Mysticism, Restoration, Unarmored,
                    Acrobatics, HandToHand, LightArmor, Marksman, Mercantile, Security, ShortBlade, Sneak, Speechcraft
                }

                public enum Attribute
                {
                    None, Strength, Intelligence, Willpower, Agility, Speed, Endurance, Personality, Luck
                }

                public enum Range
                {
                    OnTouch, OnTarget, OnSelf
                }

                public readonly MagicEffect effect;
                public readonly Skill skill;
                public readonly Attribute attribute;
                public readonly Range range;

                public readonly int area, duration;
                private readonly int min, max;

                public Effect(JsonNode json)
                {
                    effect = (MagicEffect)System.Enum.Parse(typeof(MagicEffect), json["magic_effect"].GetValue<string>());
                    skill = (Skill)System.Enum.Parse(typeof(Skill), json["skill"].GetValue<string>());
                    attribute = (Attribute)System.Enum.Parse(typeof(Attribute), json["attribute"].GetValue<string>());
                    range = (Range)System.Enum.Parse(typeof(Range), json["range"].GetValue<string>());

                    area = json["area"].GetValue<int>();
                    duration = json["duration"].GetValue<int>();
                    min = json["min_magnitude"].GetValue<int>();
                    max = json["max_magnitude"].GetValue<int>();
                }

                /* Random magnitude doesn't exist in ER really so instead we are just going to average the min/max magnitude and use that */
                public int Magnitude()
                {
                    return (min + max) / 2;
                }

                /* Magnitude adjusted from flat value to a % buff. Example: fortify health is flat in morrowind, in ER it is a % increase to max health. This math makes that conversion okayish */
                /* Setting adjustment will let you make the effect weaker or stronger. EX: 0.2f adjustment means a 100 magnitude fortify health will give a 20% buff instead of 100% */
                public float PositiveScalarMagnitude(float adjustment = 1f)
                {
                    int magnitude = Magnitude();
                    magnitude = Math.Max(1, Math.Min(magnitude, 500)); // clamps
                    return 1f + ((magnitude / 100f) * 1f);
                }
            }
        }

        public class SpeffCustom : Speff
        {
            public SpeffCustom(string id, int row) : base(id, row)
            {
                // no extra data from custom effects rn
            }
        }

        public class SpeffAlchemy : Speff
        {
            public SpeffAlchemy(int row, JsonNode json) : base(row, json)
            {
                // no extra data from alchemy effects rn
            }
        }

        public class SpeffEnchant : Speff
        {
            public enum EquipmentType
            {
                Weapon, Apparel
            }

            public enum EnchantType
            {
                CastOnce, CastWhenUsed, CastOnStrike, ConstantEffect
            }

            public readonly EquipmentType equipmentType;
            public readonly EnchantType enchantType; 

            public SpeffEnchant(int row, JsonNode json, EquipmentType equipmentType) : base(row, json)
            {
                this.equipmentType = equipmentType;
                enchantType = Enum.Parse<EnchantType>(json["data"]["enchant_type"].GetValue<string>());
            }
        }

        public class SpeffSpell : Speff
        {
            public enum SpellType
            {
                Spell, Ability, Power, Blight, Disease, Corprus, Curse
            }

            public readonly SpellType spellType;
            public readonly int cost;

            public Script.Flag flag; // null for SpellType.Spell and SpellType.Power. Set this flag on/off for the other spell types and they will be added/removed via a script

            public SpeffSpell(int row, JsonNode json) : base(row, json)
            {
                if (id == "corprus") { spellType = SpellType.Corprus; }
                else { spellType = Enum.Parse<SpellType>(json["data"]["spell_type"].GetValue<string>()); }

                cost = json["data"]["cost"].GetValue<int>();
            }
        }
    }
}
