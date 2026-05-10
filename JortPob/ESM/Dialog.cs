using JortPob.Common;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;
using static JortPob.Script;

namespace JortPob
{
    public class Dialog
    {
        /* Just a serializiation of the dialog and dialoginfo thingies. We will be iterating through them a lot so may as well do it. */
        public class DialogRecord
        {
            public enum Type
            {
                Greeting, Topic, Journal, Choice,
                Alarm, Attack, Flee, Hello, Hit, Idle, Intruder, Thief,
                AdmireFail, AdmireSuccess, BribeFail, BribeSuccess, InfoRefusal, InfoFail, IntimidateFail, IntimidateSuccess, ServiceRefusal, TauntFail, TauntSuccess
            }

            public readonly Type type;
            public readonly string id;
            public readonly Script.Flag flag; // script flag that determines if this topic is unlocked

            public readonly List<DialogInfoRecord> infos;

            public DialogRecord(Type type, string id, Script.Flag flag)
            {
                this.type = type;
                this.id = id.StartsWith("Greeting") ? "Greeting" : id; // change 'Greeting #' to just 'Greeting' for sanity reasons
                this.flag = flag;

                infos = new();
            }

            /* Return all calls of a given type inside this dialog record */
            public List<Papyrus.Call> GetCalls(Papyrus.Call.Type type)
            {
                return infos
                    .Where(info => info.script != null)
                    .SelectMany(info => info.script.calls)
                    .Where(call => call.type == type)
                    .ToList();
            }

            public List<Papyrus.Call> GetCalls()
            {
                return infos
                    .Where(info => info.script != null)
                    .SelectMany(info => info.script.calls)
                    .ToList();
            }
        }

        public class DialogInfoRecord
        {
            private static int NEXT_ID = 0;

            public readonly int id; // generated id used when lookin up wems, not used by elden ring or morrowind
            public readonly DialogRecord.Type type;

            // static requirements for a dialog to be added
            public readonly string speaker, job, faction, cell;
            public readonly int rank;
            public readonly CharacterContent.Race race;
            public readonly CharacterContent.Sex sex;

            // non-static requirements
            public readonly string playerFaction;
            public readonly int disposition, playerRank;

            public readonly List<DialogFilter> filters;

            public readonly string text; // actual dialog text
            public readonly string mp3;  // path to mp3 file of dialog line (if it exists, only some specific types of lines have them)

            public readonly DialogPapyrus script; // parsed script snippet for this line to execute after playback

            /* Next couple of vars are generated in a second pass, these relate to how dialog lines unlock topics */
            public readonly List<DialogRecord> unlocks; // list of topics this line unlocks, if any

            public DialogInfoRecord(DialogRecord.Type type, JsonNode json)
            {
                id = DialogInfoRecord.NEXT_ID+=10;  // increment by 10 so we can use the 9 values between each id as the split text ids (guh)
                this.type = type;

                string NullEmpty(string s) { return s.Trim() == "" ? null : s; }

                speaker = NullEmpty(json["speaker_id"].ToString());
                string raceStr = NullEmpty(json["speaker_race"].ToString());
                race = raceStr != null ? Enum.Parse<CharacterContent.Race>(raceStr.Replace(" ", "")) : CharacterContent.Race.Any;
                job = NullEmpty(json["speaker_class"].ToString());
                faction = NullEmpty(json["speaker_faction"].ToString());
                cell = NullEmpty(json["speaker_cell"].ToString());
                rank = int.Parse(json["data"]["speaker_rank"].ToString());
                Enum.TryParse(json["data"]["speaker_sex"].ToString(), out sex);

                playerFaction = NullEmpty(json["player_faction"].ToString());
                disposition = int.Parse(json["data"]["disposition"].ToString());
                playerRank = playerFaction!=null?int.Parse(json["data"]["player_rank"].ToString()):-1;  // minor MW bug fix. some dialogs have a mistake where they have a required rank set but no faction

                filters = new();
                foreach (JsonNode filterNode in json["filters"].AsArray())
                {
                    filters.Add(new(filterNode));
                }

                text = json["text"].ToString();
                mp3 = NullEmpty(json["sound_path"].ToString());

                if (json["script_text"].ToString() == null || json["script_text"].ToString() == "") { script = null; }
                else
                {
                    DialogPapyrus parsed = new DialogPapyrus(json["script_text"].ToString());
                    script = parsed.calls.Count() > 0 || parsed.choice != null ? parsed : null;  // if we parse the script and find its empty (for example, just a comment) discard it
                }

                unlocks = new();
            }

            /* Very special function for optimization */
            // So basically, this function is dedicated to determining if any filters in this DialogRecord cause it to be completely unreachable for a given npc
            // Most filters require runtime information to determine if they will resolve true or false, but many can be statically determined based on the npcs data
            // Good examples of statically determined filters are things like NotRace or NotLocal. Or if a character has no faction then any faction related filters.
            // It is important to be careful here though, we don't want to accidentally discard dialog lines that could resolve true at some point
            private static uint DISCARD_COUNT = 0; // some tracking to see how effective the filter discards are
            public bool IsUnreachableFor(ScriptManager scriptManager, CharacterContent npc)
            {
                if (speaker != null && speaker != npc.id) { return true; }
                if (race != CharacterContent.Race.Any && race != npc.race) { return true; }
                if (job != null && job != npc.job) { return true; }
                if (faction != null && faction != npc.faction) { return true; }
                if (rank > npc.rank || (rank >= 0 && npc.faction == null)) { return true; }  // unsure if this is correct, i *think* it is but haven't verified
                if ((cell != null && npc.cell.name == null) || (cell != null && npc.cell.name != null && !npc.cell.name.ToLower().StartsWith(cell.ToLower()))) { return true; }
                if (sex != CharacterContent.Sex.Any && sex != npc.sex) { return true; }

                DISCARD_COUNT++;
                foreach (DialogFilter filter in filters)
                {
                    switch (filter.type)
                    {
                        case DialogFilter.Type.Function:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.FactionRankDifference:
                                    {
                                        if (npc.faction == null) { return true; } break;
                                    }
                                case DialogFilter.Function.RankRequirement:
                                    {
                                        if (npc.faction == null) { return true; }
                                        break;
                                    }
                                case DialogFilter.Function.SameFaction:
                                    {
                                        if (npc.faction == null) { return true; }
                                        break;
                                    }
                                case DialogFilter.Function.PcExpelled:
                                    {
                                        if (npc.faction == null) { return true; }
                                        break;
                                    }
                                case DialogFilter.Function.Reputation:
                                    {
                                        if(!filter.ResolveOperator(npc.reputation)) { return true; }
                                        break;
                                    }
                                case DialogFilter.Function.Level:
                                    {
                                        if (!filter.ResolveOperator(npc.level)) { return true; }
                                        break;
                                    }
                                default: break;
                            }
                            break;

                        case DialogFilter.Type.NotLocal:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.VariableCompare:
                                    {
                                        Flag lvar = scriptManager.GetFlagLocal(npc, filter.id); // look for flag
                                        if (lvar != null) { return true; } // local vars are preprocessed so we can just check if the local var exists or not
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        case DialogFilter.Type.Local:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.VariableCompare:
                                    {
                                        Flag lvar = scriptManager.GetFlagLocal(npc, filter.id); // look for flag
                                        if (lvar == null) { return true; } // local vars are preprocessed so we can just check if the local var exists or not
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        case DialogFilter.Type.NotCell:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.NotCell:
                                    {
                                        // static check, characters in elden ring can't really travel around so it's fine for now. may need to change at some point tho
                                        if (npc.cell.name != null && npc.cell.name.ToLower().StartsWith(filter.id.ToLower())) { return true; }
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        case DialogFilter.Type.NotId:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.NotIdType:
                                    {
                                        if (npc.id == filter.id) { return true; }
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        case DialogFilter.Type.NotClass:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.NotClass:
                                    {
                                        if (npc.job == filter.id) { return true; }
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        case DialogFilter.Type.NotRace:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.NotRace:
                                    {
                                        if (npc.race.ToString().ToLower() == filter.id) { return true; }
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        case DialogFilter.Type.NotFaction:
                            switch (filter.function)
                            {
                                case DialogFilter.Function.NotFaction:
                                    {
                                        // Checking speakers faction, static true/false is fine for this as well
                                        if (npc.faction == filter.id) { return true; }
                                        break;
                                    }

                                default: break;
                            }
                            break;

                        default: break;
                    }
                }
                DISCARD_COUNT--;
                return false;
            }

            /* Generates an ESD condition for this line using the data from its filters */ // used by DialogESD.cs
            private static List<String> debugUnsupportedFiltersLogging = new();
            public string GenerateCondition(ItemManager itemManager, SpeffManager speffManager, ScriptManager scriptManager, CharacterContent npcContent)
            {
                List<string> conditions = new();

                // Handle disposition check
                if (disposition > 0)
                {
                    Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.Disposition, npcContent);
                    conditions.Add($"GetEventFlagValue({flag.id}, {(int)flag.type}) >= {disposition}");
                }

                if(playerFaction != null)
                {
                    Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, playerFaction);
                    conditions.Add($"GetEventFlag({flag.id}) == True");
                }

                if (playerRank > -1)
                {
                    Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.FactionRank, playerFaction);
                    conditions.Add($"GetEventFlagValue({flag.id}, {flag.Bits()}) >= {playerRank + 1}"); // the +1 is because i made the first rank 1 and morrowind assumes 0
                }

                // Handle filters
                for (int i = 0; i < filters.Count(); i++)
                {
                    DialogFilter filter = filters[i];

                    string handleFilter(DialogFilter filter)
                    {
                        switch (filter.type)
                        {
                            case DialogFilter.Type.Function:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.Alarmed:
                                        {
                                            return $"DoesPlayerHaveSpEffect({(int)SpeffManager.Functional.Alarming}) == {filter.ResolveBinaryComparison()}";
                                        }
                                    case DialogFilter.Function.Attacked:
                                        {
                                            Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.HasBeenAttacked, npcContent);
                                            return $"GetEventFlag({flag.id}) == {filter.ResolveBinaryComparison()}";
                                        }
                                    case DialogFilter.Function.FactionRankDifference:
                                        {
                                            if (npcContent.faction == null) { return "False"; } // static false return if npc is not in a faction
                                            Script.Flag rvar = scriptManager.GetFlag(Script.Flag.Designation.FactionRank, npcContent.faction);
                                            return $"(GetEventFlagValue({rvar.id}, {rvar.Bits()}) - {rank+1}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.RankRequirement:
                                        {
                                            if (npcContent.faction == null) { return "False"; } // static false return if npc is not in a faction
                                            Script.Flag retVal = scriptManager.GetFlag(Script.Flag.Designation.ReturnValueRankReq, npcContent);
                                            return $"GetEventFlagValue({retVal.id}, {retVal.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.SameFaction:
                                        {
                                            if (npcContent.faction == null) { return "False"; } // static false return if npc is not in a faction
                                            Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, npcContent.faction);
                                            if (flag == null) { return "False"; }       // another static return. if the npc has no faction it is always false
                                            return $"GetEventFlag({flag.id}) == {filter.value}";
                                        }
                                    case DialogFilter.Function.SameRace:
                                        {
                                            Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.PlayerRace, npcContent.race.ToString());
                                            return $"GetEventFlag({flag.id}) == {filter.ResolveBinaryComparison()}";
                                        }
                                    case DialogFilter.Function.SameSex:
                                        {
                                            int sexVal = npcContent.sex == CharacterContent.Sex.Male ? 1 : 0; // elden ring values are :: male = 1, female = 0 
                                            return $"ComparePlayerStat(PlayerStat.Gender, CompareType.Equal, {sexVal}) == {filter.value}";
                                        }
                                    case DialogFilter.Function.TalkedToPc:
                                        {
                                            Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.TalkedToPc, npcContent);
                                            return $"GetEventFlag({flag.id}) == {filter.ResolveBinaryComparison()}";
                                        }
                                    case DialogFilter.Function.PcLevel:
                                        {
                                            return $"ComparePlayerStat(PlayerStat.RuneLevel, {filter.OperatorString()}, {filter.value})";
                                        }
                                    case DialogFilter.Function.PcSex:
                                        {
                                            return $"ComparePlayerStat(PlayerStat.Gender, CompareType.Equal, 0) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.PcExpelled:
                                        {
                                            if (npcContent.faction == null) { return "False"; } // static false return if npc is not in a faction
                                            Script.Flag flag = scriptManager.GetFlag(Script.Flag.Designation.FactionExpelled, npcContent.faction);
                                            return $"GetEventFlag({flag.id}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.Reputation:
                                        {
                                            return $"{npcContent.reputation} {filter.OperatorSymbol()} {filter.value}"; // could be statically resolved
                                        }
                                    case DialogFilter.Function.PcReputation:
                                        {
                                            Flag rvar = scriptManager.GetFlag(Script.Flag.Designation.Reputation, "Reputation");  // grab player reputation flag
                                            return $"GetEventFlagValue({rvar.id}, {rvar.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.PcCrimeLevel:
                                        {
                                            Flag cvar = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel"); // grab crime gold flag
                                            return $"GetEventFlagValue({cvar.id}, {cvar.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.PcAgility:
                                    case DialogFilter.Function.PcSneak:
                                        {
                                            return $"ComparePlayerStat(PlayerStat.Dexterity, {filter.OperatorString()}, {filter.value})";
                                        }
                                    case DialogFilter.Function.PcStrength:
                                    case DialogFilter.Function.PcBluntWeapon:
                                        {
                                            return $"ComparePlayerStat(PlayerStat.Strength, {filter.OperatorString()}, {filter.value})";
                                        }
                                    case DialogFilter.Function.PcIntelligence:
                                        {
                                            return $"ComparePlayerStat(PlayerStat.Intelligence, {filter.OperatorString()}, {filter.value})";
                                        }
                                    case DialogFilter.Function.PcPersonality:
                                    case DialogFilter.Function.PcMercantile:
                                    case DialogFilter.Function.PcSpeechcraft:
                                        {
                                            return $"ComparePlayerStat(PlayerStat.Arcane, {filter.OperatorString()}, {filter.value})";
                                        }
                                    case DialogFilter.Function.PcHealthPercent:
                                        {
                                            // GetPlayerRemainingHP() returns 0 to 100 as an int. Morrowind also does this so it lines up lmao. Lucky.
                                            return $"GetPlayerRemainingHP() {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.PcHealth:
                                        {
                                            // Calculate actual health from Max HP and current HP percent (0 to 100 int)
                                            Script.Flag hpFlag = scriptManager.GetFlag(Script.Flag.Designation.PlayerStat, "MaxHP");
                                            return $"((GetEventFlagValue({hpFlag.id}, {hpFlag.Bits()}) * GetPlayerRemainingHP()) / 100) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.PcBlightDisease:
                                        {
                                            List<SpeffManager.SpeffSpell> speffs = speffManager.GetBlights();
                                            List<string> conditions = new();
                                            foreach(SpeffManager.SpeffSpell speff in speffs)
                                            {
                                                conditions.Add($"DoesPlayerHaveSpEffect({speff.row}) == True");
                                            }
                                            return $"({String.Join(" or ", conditions)}) == {filter.ResolveBinaryComparison()}";
                                        }
                                    case DialogFilter.Function.PcCommonDisease:
                                        {
                                            List<SpeffManager.SpeffSpell> speffs = speffManager.GetDiseases();
                                            List<string> conditions = new();
                                            foreach (SpeffManager.SpeffSpell speff in speffs)
                                            {
                                                conditions.Add($"DoesPlayerHaveSpEffect({speff.row}) == True");
                                            }
                                            return $"({String.Join(" or ", conditions)}) == {filter.ResolveBinaryComparison()}";
                                        }
                                    case DialogFilter.Function.PcCorprus:
                                        {
                                            SpeffManager.SpeffSpell speff = speffManager.GetCorprus();
                                            return $"DoesPlayerHaveSpEffect({speff.row}) == {filter.ResolveBinaryComparison()}";
                                        }
                                    case DialogFilter.Function.PcVampire:
                                        {
                                            SpeffManager.SpeffSpell speff = speffManager.GetVampirism();
                                            return $"DoesPlayerHaveSpEffect({speff.row}) == {filter.ResolveBinaryComparison()}";
                                        }
                                    case DialogFilter.Function.Level:
                                        {
                                            // npcs level can't change so static comparison is fine
                                            return $"{npcContent.level} {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.HealthPercent:
                                        {
                                            // It just so happens that ESD GetSelfHP() returns a percent as an integer from 0 to 100.
                                            // Morrowind does the same thing with HealthPercent so it lines up.
                                            return $"GetSelfHP() {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.ReactionHigh:
                                        {
                                            // NPC is in a faction so we need to call the substate for calculating reaction values
                                            if (npcContent.faction != null)
                                            {
                                                Script.Flag highFlag = scriptManager.GetFlag(Flag.Designation.ReturnReactionHigh, npcContent);
                                                return $"GetEventFlagValue({highFlag.id}, {highFlag.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                            }
                                            // NPC not in a faction so reaction is 0
                                            else
                                            {
                                                return $"0 {filter.OperatorSymbol()} {filter.value}";
                                            }
                                        }
                                    case DialogFilter.Function.ReactionLow:
                                        {
                                            // NPC is in a faction so we need to call the substate for calculating reaction values
                                            if (npcContent.faction != null)
                                            {
                                                Script.Flag lowFlag = scriptManager.GetFlag(Flag.Designation.ReturnReactionLow, npcContent);
                                                return $"(0 - GetEventFlagValue({lowFlag.id}, {lowFlag.Bits()})) {filter.OperatorSymbol()} {filter.value}";
                                            }
                                            // NPC not in a faction so reaction is 0
                                            else
                                            {
                                                return $"0 {filter.OperatorSymbol()} {filter.value}";
                                            }
                                        }
                                    case DialogFilter.Function.FriendHit:
                                        {
                                            Script.Flag hflag = scriptManager.GetFlag(Flag.Designation.Hostile, npcContent);
                                            Script.Flag fvar = scriptManager.GetFlag(Script.Flag.Designation.FriendHitCounter, npcContent);
                                            Script.Flag dvar = scriptManager.GetFlag(Script.Flag.Designation.Disposition, npcContent);
                                            return $"(not GetEventFlag({hflag.id}) and GetEventFlagValue({dvar.id}, {dvar.Bits()}) > 60 and GetEventFlagValue({fvar.id}, {fvar.Bits()}) {filter.OperatorSymbol()} {filter.value - 1})";
                                        }

                                    case DialogFilter.Function.Weather:
                                        {
                                            Script.Flag weatherFlag = scriptManager.GetFlag(Flag.Designation.CurrentWeather, "CurrentWeather");
                                            return $"GetEventFlagValue({weatherFlag.id}, {weatherFlag.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    case DialogFilter.Function.Choice:
                                        {
                                            return $"True"; // choice commands are handled differently. this "true" is just to prevent breaking choices that have filters
                                        }

                                    default: return null;
                                }

                            case DialogFilter.Type.Journal:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.JournalType:
                                        {
                                            Flag jvar = scriptManager.GetFlag(Script.Flag.Designation.Journal, filter.id); // look for flag, if not found make one
                                            if (jvar == null) { jvar = scriptManager.common.CreateFlag(Flag.Category.Saved, Flag.Type.Byte, Script.Flag.Designation.Journal, filter.id); }
                                            return $"GetEventFlagValue({jvar.id}, {jvar.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }
                                    default: return null;
                                }

                            case DialogFilter.Type.Global:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.Global:
                                    case DialogFilter.Function.VariableCompare:
                                        {
                                            /* Some global variables are sort of like special functions. We handle those here */
                                            Script.Flag crimeLevelFlag = scriptManager.GetFlag(Flag.Designation.CrimeLevel, "CrimeLevel");
                                            switch (filter.id.ToLower())
                                            {
                                                case "random100":
                                                    return $"CompareRNGValue({filter.OperatorString()}, {filter.value}) == True";

                                                case "pchasturnin":
                                                case "pchascrimegold":
                                                    return $"ComparePlayerStat(PlayerStat.RunesCollected, CompareType.GreaterOrEqual, GetEventFlagValue({crimeLevelFlag.id}, {crimeLevelFlag.Bits()})) and GetEventFlagValue({crimeLevelFlag.id}, {crimeLevelFlag.Bits()}) >= 0"; // operator bug. >= is > in esd
                                            }

                                            Flag gvar = scriptManager.GetFlag(Script.Flag.Designation.Global, filter.id); // look for flag. if not found return a static 'False' as it's probably a float variable
                                            if(gvar == null) { return "False"; }
                                            return $"GetEventFlagValue({gvar.id}, {gvar.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }

                                    default: return null;
                                }

                            case DialogFilter.Type.Dead:
                                {
                                    switch(filter.function)
                                    {
                                        case DialogFilter.Function.DeadType:
                                            {
                                                Flag deadCount = scriptManager.GetFlag(Flag.Designation.DeadCount, filter.id);
                                                if(deadCount == null) { return "False"; } // Only happens if doing a partial build of the game world
                                                return $"GetEventFlagValue({deadCount.id}, {deadCount.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                            }
                                    }
                                    return null;
                                }

                            case DialogFilter.Type.NotLocal:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.VariableCompare:
                                        {
                                            Flag lvar = scriptManager.GetFlagLocal(npcContent, filter.id); // look for flag
                                            if(lvar == null) { return "True"; } // if we don't find the flag for a local var it doesn't exist
                                            return $"False";
                                        }

                                    default: return null;
                                }
                            case DialogFilter.Type.NotCell:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.NotCell:
                                        {
                                            // static check, characters in elden ring can't really travel around so it's fine for now. may need to change at some point tho
                                            if(npcContent.cell.name == null) { return "True"; }
                                            if (npcContent.cell.name.ToLower().StartsWith(filter.id.ToLower())) { return "False"; }
                                            return "True";
                                        }

                                    default: return null;
                                }
                            case DialogFilter.Type.NotId:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.NotIdType:
                                        {
                                            // Checking speaker id, static true/false is fine for this one
                                            if(npcContent.id != filter.id) { return "True"; }
                                            else { return "False"; }
                                        }

                                    default: return null;
                                }
                            case DialogFilter.Type.NotClass:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.NotClass:
                                        {
                                            // Checking speakers class, static true/false is fine for this as well
                                            if (npcContent.job != filter.id) { return "True"; }
                                            else { return "False"; }
                                        }

                                    default: return null;
                                }
                            case DialogFilter.Type.NotRace:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.NotRace:
                                        {
                                            // Checking speakers race, static true/false is fine for this as well
                                            if (npcContent.race.ToString().ToLower() != filter.id) { return "True"; }
                                            else { return "False"; }
                                        }

                                    default: return null;
                                }
                            case DialogFilter.Type.NotFaction:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.NotFaction:
                                        {
                                            // Checking speakers faction, static true/false is fine for this as well
                                            if (npcContent.faction != filter.id) { return "True"; }
                                            else { return "False"; }
                                        }

                                    default: return null;
                                }
                            case DialogFilter.Type.Local:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.VariableCompare:
                                    case DialogFilter.Function.Global:  // This appears to be a bug where Locals that are floats get marked as FunctionType 'Global'
                                        {
                                            Flag lvar = scriptManager.GetFlagLocal(npcContent, filter.id); // look for flag, if not found it doesnt exist so return false
                                            if (lvar == null) { return "False"; }
                                            return $"GetEventFlagValue({lvar.id}, {lvar.Bits()}) {filter.OperatorSymbol()} {filter.value}";
                                        }

                                    default: return null;
                                }
                            case DialogFilter.Type.Item:
                                switch (filter.function)
                                {
                                    case DialogFilter.Function.ItemType:
                                        {
                                            // Gold specifically handled as souls so its diffo from other item checks
                                            if (filter.id.ToLower() == "gold_001")
                                            {
                                                return $"ComparePlayerStat(PlayerStat.RunesCollected, {filter.OperatorString()}, {filter.value})";
                                            }
                                            // Any other item
                                            else
                                            {
                                                ItemManager.ItemInfo itemInfo = itemManager.GetItem(filter.id.ToLower());
                                                if (itemInfo == null) { throw new Exception("Script failed to find referenced item! This should not happen!"); }
                                                return $"ComparePlayerInventoryNumber({(int)itemInfo.type}, {itemInfo.row}, {filter.OperatorString()}, {filter.value}, False)";
                                            }
                                        }
                                    default: return null;
                                }

                            default: return null; // @TODO: debug thing while we are implementing these functions. if its not implemented it returns null which we convert to a "false" below
                        }
                    }

                    string filterCond = handleFilter(filter);
                    if(filterCond == null)
                    {
                        string unsupportedFilterType = $"{filter.type}::{filter.function}";
                        if (!debugUnsupportedFiltersLogging.Contains(unsupportedFilterType))
                        {
                            Lort.Log($" ## WARNING ## Unsupported filter type {unsupportedFilterType}", Lort.Type.Debug);
                            debugUnsupportedFiltersLogging.Add(unsupportedFilterType);
                        }

                        filterCond = "False";
                    }

                    conditions.Add(filterCond);
                }

                // Collapse to string
                string condition = "";
                for (int i = 0; i < conditions.Count(); i++)
                {
                    condition += conditions[i];
                    if (i < conditions.Count() - 1) { condition += " and "; }
                }

                return condition;
            }
        }

        public class DialogPapyrus
        {
            public readonly List<Papyrus.Call> calls;
            public readonly PapyrusChoice choice;    // usually null unless the papyrus script had a choice call. choice is always the last call in a script and there can only be 1

            public DialogPapyrus(string script)
            {
                calls = new();
                string[] lines = script.Split("\r\n");
                choice = null;
                foreach (string line in lines)
                {
                    Papyrus.Call call = new(line);
                    if (call.type == Papyrus.Call.Type.None) { continue; } // discard empty calls
                    if(call.type == Papyrus.Call.Type.Choice)  // choice calls are special and are stored differently
                    {
                        choice = new PapyrusChoice(call);
                        continue;
                    }
                    calls.Add(call);
                }
            }

            /* Creates code for a dialog esd to execute when the dialoginfo that this dialogpapyrus is owned by gets played */
            private static List<String> debugUnsupportedPapyrusCallLogging = new();
            public string GenerateEsdSnippet(ESM esm, Layout layout, MSBE msb, MainSoundBank sound, Paramanager paramanager, ItemManager itemManager, SpeffManager speffManager, ScriptManager scriptManager, CharacterContent npcContent, uint esdId, int indent)
            {
                /* Used by AiFollow, AiFollowCell, AiWander, AiEscort, and AiEscortCell to have a time based cancel for their ai package */
                Script.Flag CreateAiPackageDurationEvent(Papyrus.Call call, BaseScript script, CharacterContent content, float duration)
                {
                    Script.Flag switchFlag = script.GetOrCreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.SwitchAiPackage, content.entity.ToString());  // purposefully avoid phased rerouting for this eventid flag
                    uint switchToId = switchFlag != null ? switchFlag.id : 0;
                    Script.Flag timerEvtFlag = script.CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"AiPackageTimer::{call.type}::{content.entity}");
                    EMEVD.Event timerEvt = new();
                    timerEvt.ID = timerEvtFlag.id;
                    timerEvt.Instructions.Add(script.AUTO.ParseAdd($"WaitFixedTimeSeconds({duration});"));                       // wait for duration
                    timerEvt.Instructions.Add(script.AUTO.ParseAdd($"InitializeEvent(0, {switchFlag.id}, {switchToId}, 1);"));  // run switcher event to switch back to default package
                    script.emevd.Events.Add(timerEvt);
                    content.packageEventFlags.Add(timerEvtFlag);
                    return timerEvtFlag;
                }

                // Takes any mixed numeric parameter and converts it to an esd friendly format. for example  "1 + 2 + crimeGold + 7" or "crimeGold - valueValue" or just "5"
                string ParseParameters(string[] parameters, int startIndex)
                {
                    string parsed = parameters.Length - startIndex > 1 ? "(" : "";
                    for (int i = startIndex; i < parameters.Length; i++)
                    {
                        string p = parameters[i];
                        if (Utility.StringIsInteger(p)) { parsed += p; }
                        else if (Utility.StringIsOperator(p)) { parsed += p; }
                        else  // its (probably) a variable
                        {
                            Flag pvar = GetFlagByVariable(p); // get variable flag
                            if (pvar == null) { parsed += "0"; } // @TODO: discarding function calls rn, should support them properly (like in papyrusemevd.cs)
                            else { parsed += $"GetEventFlagValue({pvar.id}, {(int)pvar.type})"; }
                        }
                        if (i < parameters.Length - 1) { parsed += " "; }
                    }
                    if (parsed.StartsWith("(")) { parsed += ")"; }
                    return parsed;
                }

                // Little function to resolve a variable to a flag
                Script.Flag GetFlagByVariable(string varName)
                {
                    Script.Flag retFlag = null;

                    // probably a local var of this object. ex: powerLevel or angryness
                    if (!varName.Contains(".")) 
                    {
                        retFlag = scriptManager.GetFlagLocal(npcContent, varName); // look for flag
                    }
                    // looks like it's actually a local var of a different object EX: fargoth.sexy or "dagoth ur".dreamy
                    else
                    {
                        // Find refernce to object that matches the record id of this local var 
                        string[] spl = varName.Split(".");
                        string recordId = spl[0].Replace("\"", "").Trim();
                        string varId = spl[1].Replace("\"", "").Trim();
                        Content target = layout.FindScriptReference(npcContent, recordId);
                        if (target != null)
                        {
                            retFlag = scriptManager.GetFlagLocal(target, varId);
                        }
                    }
                    // if the above cases failed to turn up anything then lets see if its a global var EX: crimeGold or tutorialDone
                    if (retFlag == null) { retFlag = scriptManager.GetFlag(Script.Flag.Designation.Global, varName); }

                    // return whatever we found, even if null
                    return retFlag;
                }

                List<string> lines = new();

                foreach (Papyrus.Call call in calls)
                {
                    switch (call.type)
                    {
                        case Papyrus.Call.Type.Set:
                            {
                                // This var can be either global or local so check for both
                                Flag var = GetFlagByVariable(call.parameters[0]);
                                if (var == null) { break; } // if we fail to find the variable just discard for now. generally due to partial builds or script parsing issues
                                lines.Add($"SetEventFlagValue({var.id}, {var.Bits()}, {ParseParameters(call.parameters, 2)})");
                                break;
                            }
                        case Papyrus.Call.Type.Journal:
                            {
                                Flag jvar = scriptManager.common.GetOrCreateFlag(Flag.Category.Saved, Flag.Type.Byte, Script.Flag.Designation.Journal, call.parameters[0]); 
                                lines.Add($"SetEventFlagValue({jvar.id}, {jvar.Bits()}, {int.Parse(call.parameters[1])})");
                                break;
                            }
                        case Papyrus.Call.Type.AddTopic:
                            {
                                Flag tvar = scriptManager.GetFlag(Script.Flag.Designation.TopicEnabled, call.parameters[0]);
                                lines.Add($"SetEventFlag({tvar.id}, FlagState.On)");
                                break;
                            }
                        case Papyrus.Call.Type.PcJoinFaction:
                            {
                                string faction;
                                if (call.parameters.Count() > 0) { faction = call.parameters[0].ToLower().Trim(); }
                                else { faction = npcContent.faction; }

                                Script.Flag fvar = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, faction);
                                lines.Add($"SetEventFlag({fvar.id}, FlagState.On)");
                                break;
                            }
                        case Papyrus.Call.Type.ModPcFacRep:
                            {
                                string faction;
                                if (call.parameters.Count() > 1) { faction = call.parameters[1].ToLower().Trim(); }
                                else { faction = npcContent.faction; }

                                int rep = int.Parse(call.parameters[0]);

                                Script.Flag fvar = scriptManager.GetFlag(Script.Flag.Designation.FactionReputation, faction);
                                lines.Add($"assert t{esdId:D9}_x{Const.ESD_STATE_HARDCODE_MODFACREP}(facrepflag={fvar.id}, value={call.parameters[0]})");
                                break;
                            }
                        case Papyrus.Call.Type.PcRaiseRank:
                            {
                                string faction;
                                if (call.parameters.Count() > 0) { faction = call.parameters[0].ToLower().Trim(); }
                                else { faction = npcContent.faction; }

                                Script.Flag jvar = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, faction);
                                Script.Flag rvar = scriptManager.GetFlag(Script.Flag.Designation.FactionRank, faction);
                                lines.Add($"SetEventFlag({jvar.id}, True);");
                                lines.Add($"SetEventFlagValue({rvar.id}, {rvar.Bits()}, ( GetEventFlagValue({rvar.id}, {rvar.Bits()}) + {1} ))");
                                break;
                            }
                        case Papyrus.Call.Type.PcExpell:
                            {
                                string faction;
                                if (call.parameters.Count() > 0) { faction = call.parameters[0].ToLower().Trim(); }
                                else { faction = npcContent.faction; }

                                Script.Flag fvar = scriptManager.GetFlag(Script.Flag.Designation.FactionExpelled, faction);
                                lines.Add($"SetEventFlag({fvar.id}, FlagState.On)");
                                break;
                            }
                        case Papyrus.Call.Type.PcClearExpelled:
                            {
                                string faction;
                                if (call.parameters.Count() > 0) { faction = call.parameters[0].ToLower().Trim(); }
                                else { faction = npcContent.faction; }

                                Script.Flag fvar = scriptManager.GetFlag(Script.Flag.Designation.FactionExpelled, faction);
                                lines.Add($"SetEventFlag({fvar.id}, FlagState.Off)");
                                break;
                            }
                        case Papyrus.Call.Type.MessageBox:
                            {
                                Script.Flag msgFlag = scriptManager.common.GetOrRegisterMessage(paramanager, "Message", call.parameters[0]);
                                lines.Add($"SetEventFlag({msgFlag.id}, FlagState.On)");
                                break;
                            }
                        case Papyrus.Call.Type.RemoveItem:
                            {
                                // only supporting items/gold added to player rn. will eventually support other stuff
                                if (call.target == "player")
                                {
                                    // Gold specifically handled as souls
                                    if (call.parameters[0] == "gold_001")
                                    {
                                        lines.Add($"ChangePlayerStat(PlayerStat.RunesCollected, ChangeType.Subtract, {ParseParameters(call.parameters, 1)})");
                                    }
                                    // Any other item
                                    else
                                    {
                                        ItemManager.ItemInfo itemInfo = itemManager.GetItem(call.parameters[0].ToLower());
                                        if (itemInfo == null) { throw new Exception("Script failed to find referenced item! This should not happen!"); }
                                        Script.Flag removeItemFlag = scriptManager.common.GetOrRegisterRemoveItem(itemInfo, int.Parse(call.parameters[1]));
                                        lines.Add($"SetEventFlag({removeItemFlag.id}, FlagState.On)");
                                    }
                                }
                                break;
                            }
                        case Papyrus.Call.Type.AiEscort:
                        case Papyrus.Call.Type.AiFollow:
                        case Papyrus.Call.Type.AiEscortCell:
                        case Papyrus.Call.Type.AiFollowCell:
                            {
                                // find our target content
                                Content t;
                                if (call.target == null) { t = npcContent; }
                                else { t = layout.FindScriptReference(npcContent, call.target); }
                                if (t == null || t is not CharacterContent target) { break; } // Failed to find script reference. Should only happen when making partial builds.

                                // Make sure this is following player
                                if (call.parameters[0].ToLower().Trim() != "player") { Lort.Log($"AiFollow only works on the player -> {call.RAW}", Lort.Type.Debug); break; }

                                // Parameters
                                int offset = call.type.ToString().ToLower().Contains("cell") ? 1 : 0;
                                int hours = int.Parse(call.parameters[1 + offset]);
                                float duration = 2.5f * 60f * hours; // mw uses hours, er uses seconds. 1 hour in morrowind is 2.5~ minutes
                                Vector3 position = Utility.Vector3FromParameters(call.parameters, 2 + offset) * Const.GLOBAL_SCALE;
                                string location = offset == 1 ? call.parameters[1] : null;

                                // Grab area script
                                BaseScript areaScript = scriptManager.FindScriptFor(layout, target);
                                Script.Flag doneFlag = areaScript.GetOrCreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.AiPackageDone, target, 0, true);
                                Script.Flag switchFlag = areaScript.GetOrCreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.SwitchAiPackage, target.entity.ToString()); // purposefully avoid phased rerouting for this eventid flag

                                // Create an event to act as this scripted AiPackage
                                Script.Flag evtFlag = areaScript.CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"AiPackage::{call.type}::{target.entity}");
                                EMEVD.Event evt = new();
                                evt.ID = evtFlag.id;

                                // If we have a duration parameter then setup a timer to end the event
                                if (hours != 0)
                                {
                                    Script.Flag timerFlag = CreateAiPackageDurationEvent(call, areaScript, target, duration);
                                    evt.Instructions.Add(areaScript.AUTO.ParseAdd($"InitializeEvent(0, {timerFlag.id}, 0);"));  // start timer
                                }

                                // Destination goal stuff
                                if (
                                    (location != null && target.cell.name.ToLower() == location.ToLower()) || // goal is valid as it is in the same interior cell
                                    (location == null && !areaScript.IsInterior()) // goal is valid as it is an ext target and we are outside
                                ) {
                                    if (position != Vector3.Zero)
                                    {
                                        Layout.TravelPoint goal = layout.FindTravelable(location, position);
                                        if (goal != null)
                                        {
                                            uint defaultId = target.packageDefaultFlag != null ? target.packageDefaultFlag.id : 0;
                                            evt.Instructions.Add(areaScript.AUTO.ParseAdd($"SkipIfInoutsideArea(1, 0, {target.entity}, {goal.entity}, 1);"));     // check if inside goal area...
                                            evt.Instructions.Add(areaScript.AUTO.ParseAdd($"InitializeEvent(0, {switchFlag.id}, {defaultId}, 1);"));             // switch back to our default normal package
                                        }
                                        else { Lort.Log($"AiFollow failed to resolve goal location: '{call.RAW}'", Lort.Type.Debug); }
                                    }
                                }
                                else { Lort.Log($"AiFollow goal was determined unreacahable by interior cell traversal: '{call.RAW}'", Lort.Type.Debug); }

                                // Follow stuff
                                evt.Instructions.Add(areaScript.AUTO.ParseAdd($"SetSpEffect({target.entity}, {(int)SpeffManager.Functional.NpcFollow});"));   // apply speff for follower
                                evt.Instructions.Add(areaScript.AUTO.ParseAdd($"WaitFixedTimeFrames(15);"));                                                 // wait half a second~
                                evt.Instructions.Add(areaScript.AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);"));                               // repeat endlessly
                                areaScript.emevd.Events.Add(evt);
                                target.packageEventFlags.Add(evtFlag);

                                // Initialize a trigger event for the ai package switch
                                Script.Flag triggerFlag = areaScript.RegisterTriggerAiPackageSwitch(npcContent, switchFlag, evtFlag);

                                // Trigger the 'SwitchAiPackage' event on our created event
                                lines.Add($"SetEventFlag({triggerFlag.id}, FlagState.On)");
                                break;
                            }
                        case Papyrus.Call.Type.AiTravel:
                            {
                                // find our target content
                                Content t;
                                if (call.target == null) { t = npcContent; }
                                else { t = layout.FindScriptReference(npcContent, call.target); }
                                if (t == null || t is not CharacterContent target) { break; } // Failed to find script reference. Should only happen when making partial builds.

                                // Parameters
                                Vector3 position = Utility.Vector3FromParameters(call.parameters) * Const.GLOBAL_SCALE;

                                // Grab area script
                                BaseScript areaScript = scriptManager.FindScriptFor(layout, target);
                                Script.Flag doneFlag = areaScript.GetOrCreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.AiPackageDone, target, 0, true);
                                Script.Flag switchFlag = areaScript.GetOrCreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.SwitchAiPackage, target.entity.ToString());  // purposefully avoid phased rerouting for this eventid flag

                                // Get patrol route
                                Layout.TravelPoint tp = layout.FindTravelable(target, position);
                                if (tp == null) { break; } // partial build result, or travel point was in a differnt msb so we can't ref it in a patrol

                                MSBE.Event.PatrolInfo patrol = MakePart.PatrolTo(tp);
                                patrol.EntityID = areaScript.CreateEntity(Script.EntityType.Event, $"Goto->{patrol.Name}");
                                msb.Events.Add(patrol);

                                // Create an event to act as this scripted AiPackage
                                Script.Flag evtFlag = areaScript.CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"AiPackage::{call.type}::{target.entity}");
                                EMEVD.Event evt = new();
                                evt.ID = evtFlag.id;
                                uint defaultId = target.packageDefaultFlag != null ? target.packageDefaultFlag.id : 0;
                                evt.Instructions.Add(areaScript.AUTO.ParseAdd($"ChangeCharacterPatrolBehavior({target.entity}, {patrol.EntityID});"));                      // set route for them to travel
                                evt.Instructions.Add(areaScript.AUTO.ParseAdd($"RequestCharacterAIReplan({target.entity});"));                                             // request replan to get their brain actually working
                                evt.Instructions.Add(areaScript.AUTO.ParseAdd($"IfInoutsideArea(MAIN, InsideOutsideState.Inside, {target.entity}, {tp.entity}, 1);"));    // blocking wait till they reach the destination
                                evt.Instructions.Add(areaScript.AUTO.ParseAdd($"InitializeEvent(0, {switchFlag.id}, {defaultId}, 1);"));                                 // switch back to our default normal package
                                areaScript.emevd.Events.Add(evt);
                                target.packageEventFlags.Add(evtFlag);

                                // Initialize a trigger event for the ai package switch
                                Script.Flag triggerFlag = areaScript.RegisterTriggerAiPackageSwitch(npcContent, switchFlag, evtFlag);

                                // Trigger the 'SwitchAiPackage' event on our created event
                                lines.Add($"SetEventFlag({triggerFlag.id}, FlagState.On)");
                                break;
                            }
                        case Papyrus.Call.Type.AiWander:
                            {
                                // find our target content
                                Content t;
                                if (call.target == null) { t = npcContent; }
                                else { t = layout.FindScriptReference(npcContent, call.target); }
                                if (t == null || t is not CharacterContent target) { break; } // Failed to find script reference. Should only happen when making partial builds.

                                // Parameters
                                float distance = float.Parse(call.parameters[0]) * Const.GLOBAL_SCALE;
                                int hours = int.Parse(call.parameters[1]);
                                float duration = 2.5f * 60f * hours; // mw uses hours, er uses seconds. 1 hour in morrowind is 2.5~ minutes

                                // Grab area script
                                BaseScript areaScript = scriptManager.FindScriptFor(layout, target);
                                Script.Flag doneFlag = areaScript.GetOrCreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.AiPackageDone, target, 0, true);

                                // Get patrol route
                                List<Layout.PathGridPoint> paths = layout.GetWanderable(target, distance);
                                paths.Shuffle();                                            // randomize
                                if (paths.Count() > 15) { paths = paths.GetRange(0, 15); } // truncate to max size of nibble (not strictly needed here but eh, we dont need more than this i swear)

                                // Create an event to act as this scripted AiPackage
                                Script.Flag evtFlag = areaScript.CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"AiPackage::{call.type}::{target.entity}");
                                EMEVD.Event evt = new();
                                evt.ID = evtFlag.id;

                                // If we have a duration parameter then setup a timer to end the event
                                if (duration > 0)
                                {
                                    Script.Flag timerFlag = CreateAiPackageDurationEvent(call, areaScript, target, duration);
                                    evt.Instructions.Add(areaScript.AUTO.ParseAdd($"InitializeEvent(0, {timerFlag.id}, 0);"));  // start timer
                                }

                                // 'Do nothing' wander with 0 distance
                                if (distance <= 0)
                                {
                                    evt.Instructions.Add(areaScript.AUTO.ParseAdd($"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, 6000);"));               // Wait forever
                                }
                                // Regular wander but no pathgrid so just improvise with type 6 patrol "Randomly wander around"
                                else if (paths.Count() <= 0)
                                {
                                    MSBE.Event.PatrolInfo patrol = MakePart.PatrolRandom();
                                    patrol.EntityID = areaScript.CreateEntity(Script.EntityType.Event, $"Random->{patrol.Name}");
                                    msb.Events.Add(patrol);

                                    evt.Instructions.Add(areaScript.AUTO.ParseAdd($"ChangeCharacterPatrolBehavior({target.entity}, {patrol.EntityID});"));          // set route to "wander randomly"
                                    evt.Instructions.Add(areaScript.AUTO.ParseAdd($"RequestCharacterAIReplan({target.entity});"));                                 // brain go boom
                                    evt.Instructions.Add(areaScript.AUTO.ParseAdd($"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, 6000);"));               // Wait forever                      
                                }
                                // Regular wander on pathgrid
                                else
                                {
                                    foreach (Layout.PathGridPoint path in paths)
                                    {
                                        MSBE.Event.PatrolInfo patrol = MakePart.PatrolTo(path);
                                        patrol.EntityID = areaScript.CreateEntity(Script.EntityType.Event, $"Goto->{patrol.Name}");
                                        msb.Events.Add(patrol);

                                        evt.Instructions.Add(areaScript.AUTO.ParseAdd($"WaitRandomTimeSeconds(1, 12);"));                                                                // wait around for a bit
                                        evt.Instructions.Add(areaScript.AUTO.ParseAdd($"ChangeCharacterPatrolBehavior({target.entity}, {patrol.EntityID});"));                          // set route to next wander position
                                        evt.Instructions.Add(areaScript.AUTO.ParseAdd($"RequestCharacterAIReplan({target.entity});"));                                                 // request replan to kickstart npcs brain
                                        evt.Instructions.Add(areaScript.AUTO.ParseAdd($"IfInoutsideArea(MAIN, InsideOutsideState.Inside, {target.entity}, {path.entity}, 1);"));      // blocking wait till they reach destination
                                    }
                                    evt.Instructions.Add(areaScript.AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);"));                                // repeat endlessly
                                }
                                areaScript.emevd.Events.Add(evt);
                                target.packageEventFlags.Add(evtFlag);

                                // Initialize a trigger event for the ai package switch
                                Script.Flag switchFlag = areaScript.GetOrCreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.SwitchAiPackage, target.entity.ToString());  // purposefully avoid phased rerouting for this eventid flag
                                Script.Flag triggerFlag = areaScript.RegisterTriggerAiPackageSwitch(npcContent, switchFlag, evtFlag);

                                // Trigger the 'SwitchAiPackage' event on our created event
                                lines.Add($"SetEventFlag({triggerFlag.id}, FlagState.On)");
                                break;
                            }
                        case Papyrus.Call.Type.AddItem:
                            {
                                // only supporting items/gold added to player rn. will eventually support other stuff
                                if (call.target == "player")
                                {
                                    // Gold specifically handled as souls
                                    if (call.parameters[0] == "gold_001")
                                    {
                                        lines.Add($"ChangePlayerStat(PlayerStat.RunesCollected, ChangeType.Add, {ParseParameters(call.parameters, 1)})");
                                    }
                                    // Any other item
                                    else
                                    {
                                        ItemManager.ItemInfo itemInfo = itemManager.GetItem(call.parameters[0].ToLower());
                                        if (itemInfo == null) { throw new Exception("Script failed to find referenced item! This should not happen!"); }
                                        int row = paramanager.GenerateAddItemLot(itemInfo, int.Parse(call.parameters[1]));
                                        lines.Add($"AwardItemLot({row})");
                                    }
                                }
                                break;
                            }
                        case Papyrus.Call.Type.AddSpell:
                            {
                                SpeffManager.SpeffSpell spell = speffManager.GetSpellSpeff(call.parameters[0]);
                                if (spell == null) { Lort.Log($"AddSpell: no SpeffSpell for '{call.parameters[0]}', skipping", Lort.Type.Debug); break; }
                                if (call.target == "player")
                                {
                                    if (spell.spellType == SpeffManager.SpeffSpell.SpellType.Spell || spell.spellType == SpeffManager.SpeffSpell.SpellType.Power)
                                    {
                                        ItemManager.ItemInfo scroll = itemManager.GetSpellScroll(call.parameters[0]);
                                        if (scroll == null) { Lort.Log($"AddSpell: no scroll registered for '{call.parameters[0]}', skipping", Lort.Type.Debug); break; }
                                        int row = paramanager.GenerateAddItemLot(scroll, 1);
                                        lines.Add($"AwardItemLot({row})");
                                    }
                                    else
                                    {
                                        lines.Add($"SetEventFlag({spell.flag.id}, FlagState.On)");
                                    }
                                }
                                break;
                            }
                        case Papyrus.Call.Type.RemoveSpell:
                            {
                                SpeffManager.SpeffSpell spell = speffManager.GetSpellSpeff(call.parameters[0]);
                                if (call.target == "player")
                                {
                                    if (spell.spellType == SpeffManager.SpeffSpell.SpellType.Spell || spell.spellType == SpeffManager.SpeffSpell.SpellType.Power)
                                    {
                                        // @TODO: stub. this should remove a spell item from a players inventory but we dont have those mapped out yet
                                    }
                                    else
                                    {
                                        lines.Add($"SetEventFlag({spell.flag.id}, FlagState.Off)");
                                    }
                                }
                                break;
                            }
                        case Papyrus.Call.Type.Cast:
                            {
                                SpeffManager.SpeffSpell spell = speffManager.GetSpellSpeff(call.parameters[0]);
                                if (call.parameters[1].ToLower().Trim() == "player")
                                {
                                    if (spell.spellType == SpeffManager.SpeffSpell.SpellType.Spell || spell.spellType == SpeffManager.SpeffSpell.SpellType.Power)
                                    {
                                        lines.Add($"GiveSpEffectToPlayer({spell.row})");
                                    }
                                }
                                break;
                            }
                        case Papyrus.Call.Type.Enable:
                        case Papyrus.Call.Type.Disable:
                            {
                                // find our target content
                                Content target;
                                if (call.target == null) { target = npcContent; }
                                else { target = layout.FindScriptReference(npcContent, call.target); }
                                if (target == null) { break; } // Failed to find script reference. Should only happen when making partial builds.

                                /* Get flag and/or register script */
                                Script.Flag disabledFlag = scriptManager.GetFlag(Flag.Designation.Disabled, target);

                                /* Add code */
                                string toggle = call.type == Papyrus.Call.Type.Disable ? "On" : "Off";
                                lines.Add($"SetEventFlag({disabledFlag.id}, FlagState.{toggle})");

                                /* For enable/disable we also need to manually set the enable state but contextually based on the objects currrent status */
                                BaseScript script = scriptManager.FindScriptFor(layout, target); // grab area script of target

                                Script.Flag triggerFlag;
                                if(call.type == Papyrus.Call.Type.Disable) { triggerFlag = script.GetOrRegisterTriggerDisable(target); }
                                else { triggerFlag = script.GetOrRegisterTriggerEnable(target); }

                                switch (target)
                                {
                                    case CharacterContent c:
                                        {
                                            if(c.dead) { goto default; } // dead bodies can be treated basically like static objects
                                            Script.Flag deadFlag = scriptManager.GetFlag(Flag.Designation.Dead, target);
                                            lines.Add($"if not GetEventFlag({deadFlag.id}):");               // if character is not dead then trigger enable/disable
                                            lines.Add($"    SetEventFlag({triggerFlag.id}, FlagState.On)");
                                            break;
                                        }
                                    case ItemContent i:
                                        {
                                            if(i.treasure == null) { goto default; }  // items that dont have treasure are literally just statics so treat them as such
                                            lines.Add($"if not GetEventFlag({i.treasure.id}):");               // if item has not been picked up then trigger enable/disable
                                            lines.Add($"    SetEventFlag({triggerFlag.id}, FlagState.On)");
                                            break;
                                        }
                                    default:
                                        {
                                            lines.Add($"SetEventFlag({triggerFlag.id}, FlagState.On)");  // unconditional, just send it
                                            break;
                                        }
                                }
                                break;
                            }
                        case Papyrus.Call.Type.ModCurrentHealth:
                        case Papyrus.Call.Type.ModCurrentMagicka:
                        case Papyrus.Call.Type.ModCurrentFatigue:
                            {
                                uint entityId;
                                if (call.target == null) { entityId = npcContent.entity; }                      // case 1: no target so current object is target
                                else if (call.target == "player") { entityId = 10000; }                        // case 2: target is player
                                else                                                                          // case 3: target is a direct reference to an object record
                                {
                                    entityId = layout.FindScriptReference(npcContent, call.target).entity;
                                }

                                int amount = int.Parse(call.parameters[0]);
                                SpeffManager.StatMod statToMod;
                                switch (call.type)
                                {
                                    case Papyrus.Call.Type.ModCurrentHealth: statToMod = SpeffManager.StatMod.CurrentHP; break;
                                    case Papyrus.Call.Type.ModCurrentMagicka: statToMod = SpeffManager.StatMod.CurrentMP; break;
                                    case Papyrus.Call.Type.ModCurrentFatigue: statToMod = SpeffManager.StatMod.CurrentSP; break;
                                    default: throw new Exception("Invalid papyrus call type");  // unreachable
                                }

                                int speffId = speffManager.CreateScriptedEffect(statToMod, amount, call.RAW);

                                lines.Add($"GiveSpEffectToEntity({entityId}, {speffId})");

                                break;
                            }
                        case Papyrus.Call.Type.ModHealth:
                        case Papyrus.Call.Type.ModMagicka:
                        case Papyrus.Call.Type.ModFatigue:
                        case Papyrus.Call.Type.ModStrength:
                        case Papyrus.Call.Type.ModIntelligence:
                        case Papyrus.Call.Type.ModWillpower:
                        case Papyrus.Call.Type.ModAgility:
                        case Papyrus.Call.Type.ModSpeed:
                        case Papyrus.Call.Type.ModEndurance:
                        case Papyrus.Call.Type.ModPersonality:
                        case Papyrus.Call.Type.ModLuck:
                        case Papyrus.Call.Type.ModAcrobatics:
                        case Papyrus.Call.Type.ModAlchemy:
                        case Papyrus.Call.Type.ModAlteration:
                        case Papyrus.Call.Type.ModArmorer:
                        case Papyrus.Call.Type.ModAthletics:
                        case Papyrus.Call.Type.ModAxe:
                        case Papyrus.Call.Type.ModBlock:
                        case Papyrus.Call.Type.ModBluntWeapon:
                        case Papyrus.Call.Type.ModConjuration:
                        case Papyrus.Call.Type.ModDestruction:
                        case Papyrus.Call.Type.ModEnchant:
                        case Papyrus.Call.Type.ModHandToHand:
                        case Papyrus.Call.Type.ModHeavyArmor:
                        case Papyrus.Call.Type.ModIllusion:
                        case Papyrus.Call.Type.ModLightArmor:
                        case Papyrus.Call.Type.ModLongBlade:
                        case Papyrus.Call.Type.ModMarksman:
                        case Papyrus.Call.Type.ModMediumArmor:
                        case Papyrus.Call.Type.ModMercantile:
                        case Papyrus.Call.Type.ModMysticism:
                        case Papyrus.Call.Type.ModRestoration:
                        case Papyrus.Call.Type.ModSecurity:
                        case Papyrus.Call.Type.ModShortBlade:
                        case Papyrus.Call.Type.ModSneak:
                        case Papyrus.Call.Type.ModSpear:
                        case Papyrus.Call.Type.ModSpeechcraft:
                        case Papyrus.Call.Type.ModUnarmored:
                            {
                                Content target;
                                uint entityId;
                                if (call.target == null) { target = npcContent; entityId = target.entity; }                        // case 1: no target so current object is target
                                else if (call.target == "player") { target = null; entityId = 10000; }                            // case 2: target is player
                                else { target = layout.FindScriptReference(npcContent, call.target); entityId = target.entity; } // case 3: target is a direct reference to an object record

                                /* Not the player, modify stat via SPEFF */
                                if (entityId != 10000)
                                {
                                    BaseScript areaScript = scriptManager.FindScriptFor(layout, target);

                                    int amount = int.Parse(call.parameters[0]);
                                    SpeffManager.StatMod statToMod;
                                    switch (call.type)
                                    {
                                        /* Stats */
                                        case Papyrus.Call.Type.ModHealth: statToMod = SpeffManager.StatMod.MaxHP; break;
                                        case Papyrus.Call.Type.ModMagicka: statToMod = SpeffManager.StatMod.MaxMP; break;
                                        case Papyrus.Call.Type.ModFatigue: statToMod = SpeffManager.StatMod.MaxSP; break;
                                        /* Attributes */
                                        case Papyrus.Call.Type.ModStrength: statToMod = SpeffManager.StatMod.Strength; break;
                                        case Papyrus.Call.Type.ModIntelligence: statToMod = SpeffManager.StatMod.Intelligence; break;
                                        case Papyrus.Call.Type.ModWillpower: statToMod = SpeffManager.StatMod.Mind; break;
                                        case Papyrus.Call.Type.ModAgility: statToMod = SpeffManager.StatMod.Dexterity; break;
                                        case Papyrus.Call.Type.ModSpeed: statToMod = SpeffManager.StatMod.Dexterity; break;
                                        case Papyrus.Call.Type.ModEndurance: statToMod = SpeffManager.StatMod.Endurance; break;
                                        case Papyrus.Call.Type.ModPersonality: statToMod = SpeffManager.StatMod.Arcane; break;
                                        case Papyrus.Call.Type.ModLuck: statToMod = SpeffManager.StatMod.Arcane; break;
                                        /* Skills */
                                        case Papyrus.Call.Type.ModArmorer:
                                        case Papyrus.Call.Type.ModAcrobatics:
                                        case Papyrus.Call.Type.ModAxe:
                                        case Papyrus.Call.Type.ModBluntWeapon:
                                        case Papyrus.Call.Type.ModLongBlade:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statToMod = SpeffManager.StatMod.Strength;
                                                break;
                                            }
                                        case Papyrus.Call.Type.ModAlchemy:
                                        case Papyrus.Call.Type.ModConjuration:
                                        case Papyrus.Call.Type.ModEnchant:
                                        case Papyrus.Call.Type.ModSecurity:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statToMod = SpeffManager.StatMod.Intelligence;
                                                break;
                                            }
                                        case Papyrus.Call.Type.ModAlteration:
                                        case Papyrus.Call.Type.ModDestruction:
                                        case Papyrus.Call.Type.ModMysticism:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statToMod = SpeffManager.StatMod.Mind;
                                                break;
                                            }
                                        case Papyrus.Call.Type.ModRestoration:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statToMod = SpeffManager.StatMod.Faith;
                                                break;
                                            }
                                        case Papyrus.Call.Type.ModAthletics:
                                        case Papyrus.Call.Type.ModHandToHand:
                                        case Papyrus.Call.Type.ModShortBlade:
                                        case Papyrus.Call.Type.ModUnarmored:
                                        case Papyrus.Call.Type.ModBlock:
                                        case Papyrus.Call.Type.ModLightArmor:
                                        case Papyrus.Call.Type.ModMarksman:
                                        case Papyrus.Call.Type.ModSneak:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statToMod = SpeffManager.StatMod.Dexterity;
                                                break;
                                            }
                                        case Papyrus.Call.Type.ModHeavyArmor:
                                        case Papyrus.Call.Type.ModMediumArmor:
                                        case Papyrus.Call.Type.ModSpear:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statToMod = SpeffManager.StatMod.Endurance;
                                                break;
                                            }
                                        case Papyrus.Call.Type.ModIllusion:
                                        case Papyrus.Call.Type.ModMercantile:
                                        case Papyrus.Call.Type.ModSpeechcraft:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statToMod = SpeffManager.StatMod.Arcane;
                                                break;
                                            }
                                        default: throw new Exception("Invalid papyrus call type");  // unreachable
                                    }

                                    int speffId = speffManager.CreateScriptedEffect(statToMod, amount, call.RAW);
                                    Script.Flag modStatFlag = areaScript.RegisterModStat(entityId, speffId);

                                    lines.Add($"SetEventFlag({modStatFlag.id}, FlagState.On);");                       // turn on flag to make this speff permanent on the npc
                                    lines.Add($"GiveSpEffectToEntity({entityId}, {speffId})");                        // and apply the speff for right now
                                }
                                /* For player, modify stats via HKS nonsense */
                                else
                                {
                                    string statFlagName;
                                    int amount = int.Parse(call.parameters[0]);
                                    switch (call.type)
                                    {
                                        /* Stats */
                                        case Papyrus.Call.Type.ModHealth:
                                            amount = (int)(amount * 0.1f); // direct stat values are heavily reducded
                                            statFlagName = "SetVigor";
                                            break;
                                        case Papyrus.Call.Type.ModMagicka:
                                            amount = (int)(amount * 0.1f); // direct stat values are heavily reducded
                                            statFlagName = "SetMind";
                                            break;
                                        case Papyrus.Call.Type.ModFatigue:
                                            amount = (int)(amount * 0.1f); // direct stat values are heavily reducded
                                            statFlagName = "SetEndurance";
                                            break;
                                        /* Attributes */
                                        case Papyrus.Call.Type.ModStrength: statFlagName = "SetStrength"; break;
                                        case Papyrus.Call.Type.ModIntelligence: statFlagName = "SetIntelligence"; break;
                                        case Papyrus.Call.Type.ModWillpower: statFlagName = "SetMind"; break;
                                        case Papyrus.Call.Type.ModAgility: statFlagName = "SetDexterity"; break;
                                        case Papyrus.Call.Type.ModSpeed: statFlagName = "SetDexterity"; break;
                                        case Papyrus.Call.Type.ModEndurance: statFlagName = "SetEndurance"; break;
                                        case Papyrus.Call.Type.ModPersonality: statFlagName = "SetArcane"; break;
                                        case Papyrus.Call.Type.ModLuck: statFlagName = "SetArcane"; break;
                                        /* Skills */
                                        case Papyrus.Call.Type.ModArmorer:
                                        case Papyrus.Call.Type.ModAcrobatics:
                                        case Papyrus.Call.Type.ModAxe:
                                        case Papyrus.Call.Type.ModBluntWeapon:
                                        case Papyrus.Call.Type.ModLongBlade:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statFlagName = "SetStrength";
                                                break;
                                            }
                                        case Papyrus.Call.Type.ModAlchemy:
                                        case Papyrus.Call.Type.ModConjuration:
                                        case Papyrus.Call.Type.ModEnchant:
                                        case Papyrus.Call.Type.ModSecurity:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statFlagName = "SetIntelligence";
                                                break;
                                            }
                                        case Papyrus.Call.Type.ModAlteration:
                                        case Papyrus.Call.Type.ModDestruction:
                                        case Papyrus.Call.Type.ModMysticism:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statFlagName = "SetMind";
                                                break;
                                            }
                                        case Papyrus.Call.Type.ModRestoration:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statFlagName = "SetFaith";
                                                break;
                                            }
                                        case Papyrus.Call.Type.ModAthletics:
                                        case Papyrus.Call.Type.ModHandToHand:
                                        case Papyrus.Call.Type.ModShortBlade:
                                        case Papyrus.Call.Type.ModUnarmored:
                                        case Papyrus.Call.Type.ModBlock:
                                        case Papyrus.Call.Type.ModLightArmor:
                                        case Papyrus.Call.Type.ModMarksman:
                                        case Papyrus.Call.Type.ModSneak:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statFlagName = "SetDexterity";
                                                break;
                                            }
                                        case Papyrus.Call.Type.ModHeavyArmor:
                                        case Papyrus.Call.Type.ModMediumArmor:
                                        case Papyrus.Call.Type.ModSpear:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statFlagName = "SetEndurance";
                                                break;
                                            }
                                        case Papyrus.Call.Type.ModIllusion:
                                        case Papyrus.Call.Type.ModMercantile:
                                        case Papyrus.Call.Type.ModSpeechcraft:
                                            {
                                                amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                                statFlagName = "SetArcane";
                                                break;
                                            }
                                        default: throw new Exception("Invalid papyrus call type");  // unreachable
                                    }
                                    Script.Flag statFlag = scriptManager.GetFlag(Script.Flag.Designation.Hardcode, statFlagName);
                                    lines.Add($"SetEventFlagValue({statFlag.id}, {statFlag.Bits()}, {100 + amount})"); // the SetStat hks hack offsets value by 100 to allow lowering stats EX: 100 + (-5)
                                }

                                break;
                            }
                        case Papyrus.Call.Type.ModDisposition:
                            {
                                // Find our target content
                                Content target;
                                if (call.target == null) { target = npcContent; }
                                else { target = layout.FindScriptReference(npcContent, call.target); }
                                if (target == null) { break; } // Failed to find script reference. Should only happen when making partial builds.

                                // Add call to mod disposition func
                                Script.Flag dvar = scriptManager.GetFlag(Script.Flag.Designation.Disposition, target);
                                lines.Add($"assert t{esdId:D9}_x{Const.ESD_STATE_HARDCODE_MODDISPOSITION}(dispositionflag={dvar.id}, value={call.parameters[0]})");
                                break;
                            }
                        case Papyrus.Call.Type.SetDisposition:
                            {
                                // Find our target content
                                Content target;
                                if (call.target == null) { target = npcContent; }
                                else { target = layout.FindScriptReference(npcContent, call.target); }
                                if (target == null) { break; } // Failed to find script reference. Should only happen when making partial builds.

                                // Set disp value
                                Script.Flag dvar = scriptManager.GetFlag(Script.Flag.Designation.Disposition, target);
                                lines.Add($"SetEventFlagValue({dvar.id}, {dvar.Bits()}, {call.parameters[0]})");
                                break;
                            }
                        case Papyrus.Call.Type.ModReputation:
                            {
                                if (call.target == "player")  // only for player
                                {
                                    Script.Flag rvar = scriptManager.GetFlag(Script.Flag.Designation.Reputation, "Reputation");
                                    lines.Add($"SetEventFlagValue({rvar.id}, {rvar.Bits()}, ( GetEventFlagValue({rvar.id}, {rvar.Bits()}) + {call.parameters[0]} ))");
                                }
                                // stub reputation is a static value for NPCs so we can't really do anything with that
                                break;
                            }
                        case Papyrus.Call.Type.SetPcCrimeLevel:
                            {
                                Script.Flag cvar = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel");
                                string to = call.parameters[0];
                                /* Setting to a literal, ez pz */
                                if (Utility.StringIsInteger(to))
                                {
                                    lines.Add($"SetEventFlagValue({cvar.id}, {cvar.Bits()}, {to})");
                                }
                                /* Setting to a var, handley wandley */
                                else
                                {
                                    Script.Flag toFlag = GetFlagByVariable(to.ToLower().Trim());
                                    lines.Add($"SetEventFlagValue({cvar.id}, {cvar.Bits()}, GetEventFlagValue({toFlag.id}, {toFlag.Bits()}))");
                                }
                                break;
                            }
                        case Papyrus.Call.Type.PayFine:
                            {
                                Script.Flag aflag = scriptManager.GetFlag(Script.Flag.Designation.CrimeAbsolved, "CrimeAbsolved");
                                Script.Flag crimeLevel = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel");
                                lines.Add($"SetEventFlag({aflag.id}, FlagState.On);"); // setting this flag triggers a common event that clears all crime values
                                lines.Add($"SetEventFlagValue({crimeLevel.id}, {crimeLevel.Bits()}, 0)"); // seting crimelevel to zero here since if this value isnt cleared immidieatly it can cause guards to re-engage you
                                break;
                            }
                        case Papyrus.Call.Type.GoToJail:
                            {
                                Script.Flag aflag = scriptManager.GetFlag(Script.Flag.Designation.CrimeAbsolved, "CrimeAbsolved");
                                Script.Flag crimeLevel = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel");
                                lines.Add($"SetEventFlag({aflag.id}, FlagState.On);"); // setting this flag triggers a common event that clears all crime values
                                lines.Add($"SetEventFlagValue({crimeLevel.id}, {crimeLevel.Bits()}, 0)"); // seting crimelevel to zero here since if this value isnt cleared immidieatly it can cause guards to re-engage you
                                break;
                            }
                        case Papyrus.Call.Type.StartCombat:
                            {
                                // Find our target content A
                                CharacterContent targetA;
                                if (call.target == null) { targetA = npcContent; }
                                else { targetA = layout.FindScriptReference(npcContent, call.target) as CharacterContent; }
                                if (targetA == null) { break; } // Failed to find script reference. Should only happen when making partial builds.

                                if (call.parameters[0].Trim() == "player")
                                {
                                    // if a guard starts combat with a player its a crime, if its anyone else it's just them being angy at you
                                    if (targetA.IsGuard()) {
                                        Flag cvar = scriptManager.GetFlag(Flag.Designation.CrimeEvent, targetA);
                                        Script.Flag lvar = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel"); // crime gold flag
                                        lines.Add($"SetEventFlagValue({lvar.id}, {lvar.Bits()}, {Const.CRIME_GOLD_RESIST})");
                                        lines.Add($"SetEventFlag({cvar.id}, FlagState.On)");
                                    }
                                    else {
                                        Flag hvar = scriptManager.GetFlag(Flag.Designation.Hostile, targetA);
                                        lines.Add($"SetEventFlag({hvar.id}, FlagState.On)");
                                        lines.Add($"GiveSpEffectToSelf({(int)SpeffManager.Functional.VoidMurder})");
                                    }
                                }
                                else
                                {
                                    CharacterContent targetB = layout.FindScriptReference(npcContent, call.parameters[0].ToLower().Trim()) as CharacterContent;
                                    if (targetB == null) { break; } // Failed to find script reference. Should only happen when making partial builds.

                                    BaseScript areaScriptA = scriptManager.FindScriptFor(layout, targetA);
                                    BaseScript areaScriptB = scriptManager.FindScriptFor(layout, targetB);

                                    Flag aFlag = areaScriptA.GetOrRegisterInfight(targetA);
                                    Flag bFlag = areaScriptB.GetOrRegisterInfight(targetB);

                                    lines.Add($"SetEventFlag({aFlag.id}, FlagState.On)");
                                    lines.Add($"SetEventFlag({bFlag.id}, FlagState.On)");
                                }
                                break;
                            }
                        case Papyrus.Call.Type.StopCombat:
                            {
                                // Find our target content A
                                CharacterContent target;
                                if (call.target == null) { target = npcContent; }
                                else { target = layout.FindScriptReference(npcContent, call.target) as CharacterContent; }
                                if (target == null) { break; } // Failed to find script reference. Should only happen when making partial builds.

                                BaseScript areaScript = scriptManager.FindScriptFor(layout, target);
                                Flag hostileFlag = scriptManager.GetFlag(Flag.Designation.Hostile, target);
                                Flag fightFlag = areaScript.GetOrRegisterInfight(target);
                                lines.Add($"SetEventFlag({hostileFlag.id}, FlagState.Off)");
                                lines.Add($"SetEventFlag({fightFlag.id}, FlagState.Off)");
                                break;
                            }
                        case Papyrus.Call.Type.ShowMap:
                            {
                                Script.Flag discoverFlag = scriptManager.GetFlag(Script.Flag.Designation.DiscoverLocation, call.parameters[0]);
                                if (discoverFlag != null)
                                {
                                    lines.Add($"SetEventFlag({discoverFlag.id}, FlagState.On)");
                                }
                                break;
                            }
                        case Papyrus.Call.Type.PlaySound:
                        case Papyrus.Call.Type.PlaySoundVP:
                        case Papyrus.Call.Type.PlaySound3D:
                        case Papyrus.Call.Type.PlaySound3DVP:
                        case Papyrus.Call.Type.PlayLoopSound3D:
                        case Papyrus.Call.Type.PlayLoopSound3DVP:
                            {
                                SoundInfo info = esm.GetSound(call.parameters[0].ToLower().Trim());
                                float volume, pitch;
                                switch(call.type)
                                {
                                    case Papyrus.Call.Type.PlaySound:
                                    case Papyrus.Call.Type.PlaySound3D:
                                    case Papyrus.Call.Type.PlayLoopSound3D:
                                        {
                                            volume = 1f; pitch = 1f; break;
                                        }
                                    case Papyrus.Call.Type.PlaySoundVP:
                                    case Papyrus.Call.Type.PlaySound3DVP:
                                    case Papyrus.Call.Type.PlayLoopSound3DVP:
                                        {
                                            volume = float.Parse(call.parameters[1]);
                                            pitch = float.Parse(call.parameters[2]);
                                            break;
                                        }
                                    default: throw new Exception("Invalid PlaySound call type."); // unreachable or else
                                }

                                bool loop = call.type == Papyrus.Call.Type.PlayLoopSound3D || call.type == Papyrus.Call.Type.PlayLoopSound3DVP;
                                bool spatialize = call.type == Papyrus.Call.Type.PlaySound3D || call.type == Papyrus.Call.Type.PlaySound3DVP || call.type == Papyrus.Call.Type.PlayLoopSound3D || call.type == Papyrus.Call.Type.PlayLoopSound3DVP;
                                string file = Path.Combine(Const.MORROWIND_PATH, @"Data Files\sound", info.path);

                                // find our target content
                                Content target;
                                if (call.target == null) { target = npcContent; }
                                else { target = layout.FindScriptReference(npcContent, call.target); }
                                if (target == null) { break; } // Failed to find script reference. Should only happen when making partial builds.

                                // If this is a non spatialized sound effect play it directly off the player
                                uint targetId;
                                if (spatialize) { targetId = target.entity; }
                                else { targetId = 10000; }

                                // find area script for that target content
                                BaseScript script = scriptManager.FindScriptFor(layout, target);

                                // Add sound to main bank and get playback id
                                int seId = sound.AddSound(info.id, MainSoundBank.Sound.Type.SFX, loop, spatialize, volume, pitch, file);

                                // Get PlaySE event
                                Script.Flag playFlag = script.GetOrRegisterPlaySE(targetId, seId);

                                // Trigger flag for the event
                                lines.Add($"SetEventFlag({playFlag.id}, FlagState.On)");
                                break;
                            }
                        case Papyrus.Call.Type.SetHealth:
                        case Papyrus.Call.Type.SetMagicka:
                        case Papyrus.Call.Type.SetFatigue:
                            {
                                int value = int.Parse(call.parameters[0]);
                                if (value <= 0) { value = -999999; }  // YOU MUST DIE!
                                else { value = 999999; }             // YOU MUST LIVE!

                                uint entityId;
                                if (call.target == null) { entityId = npcContent.entity; }                      // case 1: no target so current object is target
                                else if (call.target == "player") { entityId = 10000; }                        // case 2: target is player
                                else                                                                          // case 3: target is a direct reference to an object record
                                {
                                    entityId = layout.FindScriptReference(npcContent, call.target).entity;
                                }

                                SpeffManager.StatMod statToMod;
                                switch (call.type)
                                {
                                    case Papyrus.Call.Type.SetHealth: statToMod = SpeffManager.StatMod.CurrentHP; break;
                                    case Papyrus.Call.Type.SetMagicka: statToMod = SpeffManager.StatMod.CurrentMP; break;
                                    case Papyrus.Call.Type.SetFatigue: statToMod = SpeffManager.StatMod.CurrentSP; break;
                                    default: throw new Exception("Invalid papyrus call type");  // unreachable
                                }

                                int speffId = speffManager.CreateScriptedEffect(statToMod, value, call.RAW);
                                lines.Add($"GiveSpEffectToEntity({entityId}, {speffId})");
                                break;
                            }
                        case Papyrus.Call.Type.SetStrength:
                        case Papyrus.Call.Type.SetIntelligence:
                        case Papyrus.Call.Type.SetWillpower:
                        case Papyrus.Call.Type.SetAgility:
                        case Papyrus.Call.Type.SetSpeed:
                        case Papyrus.Call.Type.SetEndurance:
                        case Papyrus.Call.Type.SetPersonality:
                        case Papyrus.Call.Type.SetLuck:
                        case Papyrus.Call.Type.SetAcrobatics:
                        case Papyrus.Call.Type.SetAlchemy:
                        case Papyrus.Call.Type.SetAlteration:
                        case Papyrus.Call.Type.SetArmorer:
                        case Papyrus.Call.Type.SetAthletics:
                        case Papyrus.Call.Type.SetAxe:
                        case Papyrus.Call.Type.SetBlock:
                        case Papyrus.Call.Type.SetBluntWeapon:
                        case Papyrus.Call.Type.SetConjuration:
                        case Papyrus.Call.Type.SetDestruction:
                        case Papyrus.Call.Type.SetEnchant:
                        case Papyrus.Call.Type.SetHandToHand:
                        case Papyrus.Call.Type.SetHeavyArmor:
                        case Papyrus.Call.Type.SetIllusion:
                        case Papyrus.Call.Type.SetLightArmor:
                        case Papyrus.Call.Type.SetLongBlade:
                        case Papyrus.Call.Type.SetMarksman:
                        case Papyrus.Call.Type.SetMediumArmor:
                        case Papyrus.Call.Type.SetMercantile:
                        case Papyrus.Call.Type.SetMysticism:
                        case Papyrus.Call.Type.SetRestoration:
                        case Papyrus.Call.Type.SetSecurity:
                        case Papyrus.Call.Type.SetShortBlade:
                        case Papyrus.Call.Type.SetSneak:
                        case Papyrus.Call.Type.SetSpear:
                        case Papyrus.Call.Type.SetSpeechcraft:
                        case Papyrus.Call.Type.SetUnarmored:
                            {
                                CharacterContent target;
                                uint entityId;
                                if (call.target == null) { target = npcContent; entityId = target.entity; }                                            // case 1: no target so current object is target
                                else if (call.target == "player") { throw new Exception("SetStat papyrus call targeting player not supported!"); }    // case 2: target is player. UNSUPPORTED!
                                else { target = layout.FindScriptReference(npcContent, call.target) as CharacterContent; entityId = target.entity; } // case 3: target is a direct reference to an object record

                                BaseScript areaScript = scriptManager.FindScriptFor(layout, target);

                                int amount = int.Parse(call.parameters[0]);
                                SpeffManager.StatMod statToMod;
                                switch (call.type)
                                {
                                    /* Attributes */
                                    case Papyrus.Call.Type.SetStrength: amount = amount - target.stats.Get(CharacterContent.Stats.Attribute.Strength); statToMod = SpeffManager.StatMod.Strength; break;
                                    case Papyrus.Call.Type.SetIntelligence: amount = amount - target.stats.Get(CharacterContent.Stats.Attribute.Intelligence); statToMod = SpeffManager.StatMod.Intelligence; break;
                                    case Papyrus.Call.Type.SetWillpower: amount = amount - target.stats.Get(CharacterContent.Stats.Attribute.Willpower); statToMod = SpeffManager.StatMod.Mind; break;
                                    case Papyrus.Call.Type.SetAgility: amount = amount - target.stats.Get(CharacterContent.Stats.Attribute.Agility); statToMod = SpeffManager.StatMod.Dexterity; break;
                                    case Papyrus.Call.Type.SetSpeed: amount = amount - target.stats.Get(CharacterContent.Stats.Attribute.Speed); statToMod = SpeffManager.StatMod.Dexterity; break;
                                    case Papyrus.Call.Type.SetEndurance: amount = amount - target.stats.Get(CharacterContent.Stats.Attribute.Endurance); statToMod = SpeffManager.StatMod.Endurance; break;
                                    case Papyrus.Call.Type.SetPersonality: amount = amount - target.stats.Get(CharacterContent.Stats.Attribute.Personality); statToMod = SpeffManager.StatMod.Arcane; break;
                                    case Papyrus.Call.Type.SetLuck: amount = amount - target.stats.Get(CharacterContent.Stats.Attribute.Luck); statToMod = SpeffManager.StatMod.Arcane; break;
                                    /* Skills */
                                    case Papyrus.Call.Type.SetArmorer:
                                    case Papyrus.Call.Type.SetAcrobatics:
                                    case Papyrus.Call.Type.SetAxe:
                                    case Papyrus.Call.Type.SetBluntWeapon:
                                    case Papyrus.Call.Type.SetLongBlade:
                                        {
                                            CharacterContent.Stats.Skill skill = Enum.Parse<CharacterContent.Stats.Skill>(call.type.ToString()[^3..]);
                                            amount = amount - target.stats.Get(skill);
                                            amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                            statToMod = SpeffManager.StatMod.Strength;
                                            break;
                                        }
                                    case Papyrus.Call.Type.SetAlchemy:
                                    case Papyrus.Call.Type.SetConjuration:
                                    case Papyrus.Call.Type.SetEnchant:
                                    case Papyrus.Call.Type.SetSecurity:
                                        {
                                            CharacterContent.Stats.Skill skill = Enum.Parse<CharacterContent.Stats.Skill>(call.type.ToString()[^3..]);
                                            amount = amount - target.stats.Get(skill);
                                            amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                            statToMod = SpeffManager.StatMod.Intelligence;
                                            break;
                                        }
                                    case Papyrus.Call.Type.SetAlteration:
                                    case Papyrus.Call.Type.SetDestruction:
                                    case Papyrus.Call.Type.SetMysticism:
                                        {
                                            CharacterContent.Stats.Skill skill = Enum.Parse<CharacterContent.Stats.Skill>(call.type.ToString()[^3..]);
                                            amount = amount - target.stats.Get(skill);
                                            amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                            statToMod = SpeffManager.StatMod.Mind;
                                            break;
                                        }
                                    case Papyrus.Call.Type.SetRestoration:
                                        {
                                            CharacterContent.Stats.Skill skill = Enum.Parse<CharacterContent.Stats.Skill>(call.type.ToString()[^3..]);
                                            amount = amount - target.stats.Get(skill);
                                            amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                            statToMod = SpeffManager.StatMod.Faith;
                                            break;
                                        }
                                    case Papyrus.Call.Type.SetAthletics:
                                    case Papyrus.Call.Type.SetHandToHand:
                                    case Papyrus.Call.Type.SetShortBlade:
                                    case Papyrus.Call.Type.SetUnarmored:
                                    case Papyrus.Call.Type.SetBlock:
                                    case Papyrus.Call.Type.SetLightArmor:
                                    case Papyrus.Call.Type.SetMarksman:
                                    case Papyrus.Call.Type.SetSneak:
                                        {
                                            CharacterContent.Stats.Skill skill = Enum.Parse<CharacterContent.Stats.Skill>(call.type.ToString()[^3..]);
                                            amount = amount - target.stats.Get(skill);
                                            amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                            statToMod = SpeffManager.StatMod.Dexterity;
                                            break;
                                        }
                                    case Papyrus.Call.Type.SetHeavyArmor:
                                    case Papyrus.Call.Type.SetMediumArmor:
                                    case Papyrus.Call.Type.SetSpear:
                                        {
                                            CharacterContent.Stats.Skill skill = Enum.Parse<CharacterContent.Stats.Skill>(call.type.ToString()[^3..]);
                                            amount = amount - target.stats.Get(skill);
                                            amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                            statToMod = SpeffManager.StatMod.Endurance;
                                            break;
                                        }
                                    case Papyrus.Call.Type.SetIllusion:
                                    case Papyrus.Call.Type.SetMercantile:
                                    case Papyrus.Call.Type.SetSpeechcraft:
                                        {
                                            CharacterContent.Stats.Skill skill = Enum.Parse<CharacterContent.Stats.Skill>(call.type.ToString()[^3..]);
                                            amount = amount - target.stats.Get(skill);
                                            amount = (int)(amount * 0.33f); // skill bonuses are reduced
                                            statToMod = SpeffManager.StatMod.Arcane;
                                            break;
                                        }

                                    default: throw new Exception("Invalid papyrus call type");  // unreachable
                                }

                                int speffId = speffManager.CreateScriptedEffect(statToMod, amount, call.RAW);
                                Script.Flag modStatFlag = areaScript.RegisterModStat(entityId, speffId);

                                lines.Add($"SetEventFlag({modStatFlag.id}, FlagState.On);");                       // turn on flag to make this speff permanent on the npc
                                lines.Add($"GiveSpEffectToEntity({entityId}, {speffId})");                        // and apply the speff for right now
                                break;
                            }
                        case Papyrus.Call.Type.StopScript:
                        case Papyrus.Call.Type.StartScript:
                            {
                                // Grab subscript papyrus
                                Papyrus subscript = esm.GetPapyrus(call.parameters[0]);
                                if (subscript == null) { break; } // failed to find script, this only happens because our papyrus parsing is still not 100% finished

                                // find our target content
                                Content target;
                                if (call.target == null) { target = npcContent; }
                                else { target = layout.FindScriptReference(npcContent, call.target); }
                                if (target == null) { break; } // Failed to find script reference. Should only happen when making partial builds.

                                // find area script for this npc
                                BaseScript targetScript = scriptManager.FindScriptFor(layout, target);

                                // See if the subscript is already created. this is needed as multiple scripts can potenitlaly start/stop the same subscript.
                                Script.Flag subscriptRunFlag = scriptManager.GetFlag(Script.Flag.Designation.RunSubscript, $"{npcContent.entity}->{subscript.id}"); 

                                // If the subscript does not exist yet, we create it
                                if (subscriptRunFlag == null)
                                {
                                    subscriptRunFlag = targetScript.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.RunSubscript, $"{npcContent.entity}->{subscript.id}");
                                    PapyrusEMEVD.InitializeLocalVariables(esm, scriptManager, targetScript, subscript, npcContent); // @TODO: intialize subscript vars during the Main.cs local initializer phase? this might be an issue here?
                                    PapyrusEMEVD.Compile(esm, layout, msb, sound, scriptManager, paramanager, itemManager, speffManager, targetScript, subscript, npcContent, subscriptRunFlag);
                                }

                                // Finally we just add some code here to start/stop the subscript
                                string toggle = call.type == Papyrus.Call.Type.StartScript ? "On" : "Off";
                                lines.Add($"SetEventFlag({subscriptRunFlag.id}, FlagState.{toggle})");
                                break;
                            }
                        case Papyrus.Call.Type.Goodbye:
                            {
                                // End conversation promptly
                                lines.Add($"return 0");
                                break;
                            }
                        default:
                            { 
                                if(!debugUnsupportedPapyrusCallLogging.Contains(call.type.ToString()))
                                {
                                    Lort.Log($" ## WARNING ## Unsupported papyrus call {call.type}", Lort.Type.Debug);
                                    debugUnsupportedPapyrusCallLogging.Add(call.type.ToString());
                                }
                                break;
                            }
                    }
                }

                if(lines.Count() <= 0) { return ""; } // if empty just return nothing lol lmao

                string space = "";
                for (int i = 0; i < indent; i++)
                {
                    space += " ";
                }

                return $"{space}{string.Join($"\r\n{space}", lines)}\r\n";
            }


            /* Very specially handled call */
            /* This papyrus function is always singular and last in a dialog script so i can safely store as it's own thing */
            public class PapyrusChoice
            {
                public readonly List<Tuple<int, string>> choices;

                public PapyrusChoice(Papyrus.Call call)
                {
                    choices = new();

                    for(int i=0;i<call.parameters.Count();i+=2)
                    {
                        int ind = int.Parse(call.parameters[i + 1]);
                        string text = call.parameters[i];
                        choices.Add(new(ind, text));
                    }
                }
            }
        }

        public class DialogFilter
        {
            public enum Type { NotLocal, Journal, Dead, Item, Function, NotId, Global, Local, NotFaction, NotCell, NotRace, NotClass }
            public enum Operator { Equal, NotEqual, GreaterEqual, LessEqual, Less, Greater }
            public enum Function
            {
                VariableCompare, JournalType, DeadType, ItemType, Choice, NotIdType, PcExpelled, NotFaction, SameFaction, RankRequirement,
                PcSex, SameRace, PcHealthPercent, PcHealth, PcReputation, NotCell, PcVampire, NotRace, PcSpeechcraft, PcLevel, NotClass, PcCrimeLevel,
                SameSex, PcMercantile, PcClothingModifier, FactionRankDifference, PcCorprus, PcPersonality, ShouldAttack, PcAgility, PcSneak, TalkedToPc,
                PcIntelligence, Alarmed, Global, Detected, Attacked, Level, PcBlightDisease, PcCommonDisease, PcBluntWeapon, Reputation, PcStrength,
                CreatureTarget, Weather, ReactionHigh, ReactionLow, HealthPercent, FriendHit
            }

            public readonly Type type;
            public readonly Function function;
            public readonly Operator op;
            public readonly string id;
            public readonly int value;

            public DialogFilter(JsonNode json)
            {
                Enum.TryParse(json["filter_type"].ToString(), out type);
                Enum.TryParse(json["function"].ToString(), out function);
                Enum.TryParse(json["comparison"].ToString(), out op);

                id = json["id"].ToString().ToLower();

                if (json["value"]["type"].ToString() == "Integer")
                {
                    value = int.Parse(json["value"]["data"].ToString());
                }
                else
                {
                    Lort.Log($"## ERROR ## UNSUPPORTED FILTER VALUE TYPE '{json["value"]["type"].ToString()}' DISCARDED IN '{type} {function} {op} {id}'!", Lort.Type.Debug);
                    value = 0;
                }

            }

            /* Resolve the comparison value for 0=False / 1=True style filter conditions. EX: SameRace or SameFaction */
            public string ResolveBinaryComparison()
            {
                bool compare = value == 1;
                if (op == DialogFilter.Operator.NotEqual) { compare = !compare; }
                return compare ? "True" : "False";
            }

            /* Actually resolve a comparison operation from this filter with a given value */
            public bool ResolveOperator(int leftValue)
            {
                switch (op)
                {
                    case Operator.Equal: return leftValue == value;
                    case Operator.NotEqual: return leftValue != value;
                    case Operator.GreaterEqual: return leftValue >= value;
                    case Operator.Greater: return leftValue > value;
                    case Operator.LessEqual: return leftValue <= value;
                    case Operator.Less: return leftValue < value;
                    default: return false;
                }
            }

            /* Returns the esd version of the operator type as a string */
            public string OperatorString()
            {
                switch (op)
                {
                    case Operator.Equal: return "CompareType.Equal";
                    case Operator.NotEqual: return "CompareType.NotEqual";
                    case Operator.GreaterEqual: return "CompareType.GreaterOrEqual";
                    case Operator.Greater: return "CompareType.Greater";             // for the comparetype operators the mismatch only applys to the Less and LessOrEqual ops
                    case Operator.Less: return "CompareType.LessOrEqual";
                    case Operator.LessEqual: return "CompareType.Less";
                    default: throw new Exception("Invalid operator type! This should not happen!");
                }
            }

            /* same as above but symbol instead of string */
            public string OperatorSymbol()
            {
                switch (op)
                {
                    case Operator.Equal: return "==";
                    case Operator.NotEqual: return "!=";
                    case Operator.GreaterEqual: return ">";    // due to an issue with esd compiling >= and > are swapped. same issue with < and <= as well
                    case Operator.Greater: return ">=";
                    case Operator.LessEqual: return "<=";
                    case Operator.Less: return "<";
                    default: throw new Exception("Invalid operator type! This should not happen!");
                }
            }
        }
    }
}
