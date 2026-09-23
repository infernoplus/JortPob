using JortPob.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JortPob
{
    public class Papyrus
    {
        public readonly string id;
        public readonly Scope scope;

        public Papyrus(JsonNode json)
        {
            id = json["id"].GetValue<string>().ToLower();

            Stack<Call> stack = new();
            string raw = json["text"].GetValue<string>();
            string[] rawLines = raw.Split("\r\n");
            bool condFail = false;
            for(int i=0;i< rawLines.Length;i++)
            {
                string sanitize = rawLines[i].ToLower().Trim();

                // Replace any tabs with spaces for consistency
                if (sanitize.Contains("\t")) { sanitize = sanitize.Replace("\t", " "); }

                // Remove trailing comments
                if (sanitize.Contains(";"))
                {
                    sanitize = sanitize.Split(";")[0].Trim();
                }

                // Remove any multi spaces
                while (sanitize.Contains("  "))
                {
                    sanitize = sanitize.Replace("  ", " ");
                }

                // Skip comments and empty lines
                if (sanitize.Contains(";") || sanitize == "")
                {
                    continue;
                }

                /* Fun begins here */

                // Handle IF statement
                if (sanitize.StartsWith("if"))
                {
                    Conditional call = new(sanitize);

                    Call parent = stack.Peek();
                    if (parent is Conditional)
                    {
                        if (condFail) { ((Conditional)parent).fail.Add(call); }
                        else { ((Conditional)parent).pass.Add(call); }
                    }
                    else { ((Main)parent).scope.Add(call); }

                    stack.Push(call);
                    condFail = false;
                }
                // Handle ELSEIF statement
                else if(sanitize.StartsWith("elseif"))
                {
                    ConditionalBranch call = new(sanitize);
                    ((Conditional)stack.Peek()).fail.Add(call);
                    stack.Push(call);
                    condFail = false;
                }
                // Handle ELSE statement
                else if (sanitize.StartsWith("else"))
                {
                    condFail = true;
                }
                // Handle ENDIF
                else if (sanitize.StartsWith("endif")) {
                    while(stack.Peek() is ConditionalBranch)
                    {
                        stack.Pop(); // pop all elseif branches
                    }

                    stack.Pop();
                    condFail = false;
                }
                // Handle script BEGIN
                else if (sanitize.StartsWith("begin"))
                {
                    Main call = new(sanitize);
                    stack.Push(call);
                }
                // Handle script END
                else if (sanitize.StartsWith("end"))
                {
                    scope = ((Main)stack.Pop()).scope;
                    break;
                }
                // Handle normal call
                else
                {
                    Call call = new(sanitize);
                    Call parent = stack.Peek();
                    if (parent is Conditional)
                    {
                        if (condFail) { ((Conditional)parent).fail.Add(call); }
                        else { ((Conditional)parent).pass.Add(call); }
                    }
                    else { ((Main)parent).scope.Add(call); }
                }
            }
        }

        /* Looks through a papyrus script and checks if it has a call of the given type */
        public bool HasCall(Call.Type type)
        {
            bool RecursiveCheck(Scope scope)
            {
                foreach(Call call in scope.calls)
                {
                    if (call is Conditional)
                    {
                        Conditional conditional = (Conditional)call;
                        if (conditional.left.type == type) { return true; }
                        if (conditional.right.type == type) { return true; }
                        if (RecursiveCheck(conditional.pass)) { return true; }
                        if (RecursiveCheck(conditional.fail)) { return true; }
                    }
                    else
                    {
                        if(call.type == type) { return true; }
                    }
                }
                return false;
            }

            return RecursiveCheck(scope);
        }

        /* Looks through a papyrus script and check if it has any variables with names that match the given list */
        public bool HasVariable(List<string> vars)
        {
            bool CheckCall(Call call)
            {
                if (call.type == Call.Type.Variable)
                {
                    if (vars.Contains(call.parameters[0].ToLower())) { return true; }
                }

                if (call.type == Call.Type.Set)
                {
                    foreach (string parameter in call.parameters)
                    {
                        if (vars.Contains(parameter.ToLower())) { return true; }
                    }
                }

                return false;
            }

            bool RecursiveCheck(Scope scope)
            {
                foreach (Call call in scope.calls)
                {
                    if (call is Conditional)
                    {
                        Conditional conditional = (Conditional)call;
                        if (CheckCall(conditional.left)) { return true; }
                        if (CheckCall(conditional.right)) { return true; }
                        if (RecursiveCheck(conditional.pass)) { return true; }
                        if (RecursiveCheck(conditional.fail)) { return true; }
                    }
                    else
                    {
                        if (CheckCall(call)) { return true; }
                    }
                }
                return false;
            }

            return RecursiveCheck(scope);
        }

        /* Looks through a papyrus script and check if it has any literals with negative integers in them */
        public bool HasSignedInt()
        {
            bool CheckCall(Call call)
            {
                if (call.type == Call.Type.Set)
                {
                    foreach (string parameter in call.parameters)
                    {
                        int val = 0;
                        int.TryParse(parameter, out val);
                        if (val < 0) { return true; }
                    }
                }

                if(call.type == Call.Type.Literal)
                {
                    int val = 0;
                    int.TryParse(call.parameters[0], out val);
                    if (val < 0) { return true; }
                }

                return false;
            }

            bool RecursiveCheck(Scope scope)
            {
                foreach (Call call in scope.calls)
                {
                    if (call is Conditional)
                    {
                        Conditional conditional = (Conditional)call;
                        if (CheckCall(conditional.left)) { return true; }
                        if (CheckCall(conditional.right)) { return true; }
                        if (RecursiveCheck(conditional.pass)) { return true; }
                        if (RecursiveCheck(conditional.fail)) { return true; }
                    }
                    else
                    {
                        if (CheckCall(call)) { return true; }
                    }
                }
                return false;
            }

            return RecursiveCheck(scope);
        }

        /* Looks through a papyrus script and returns every call of the given type in a list */
        public List<Call> GetCalls(Call.Type type)
        {
            List<Call> ret = new();
            void RecursiveCheck(Scope scope)
            {
                foreach (Call call in scope.calls)
                {
                    if (call is Conditional conditional)
                    {
                        if (conditional.left.type == type) { ret.Add(conditional.left); }
                        if (conditional.right.type == type) { ret.Add(conditional.right); }
                        RecursiveCheck(conditional.pass);
                        RecursiveCheck(conditional.fail);
                    }
                    else if (call.type == type) { ret.Add(call); }
                }
            }
            RecursiveCheck(scope);
            return ret;
        }

        public List<Call> GetCalls()
        {
            List<Call> ret = new();
            void RecursiveCheck(Scope scope)
            {
                foreach (Call call in scope.calls)
                {
                    if (call is Conditional conditional)
                    {
                        ret.Add(conditional.left);
                        ret.Add(conditional.right);
                        RecursiveCheck(conditional.pass);
                        RecursiveCheck(conditional.fail);
                    }
                    else { ret.Add(call); }
                }
            }
            RecursiveCheck(scope);
            return ret;
        }

        public class Scope
        {
            public readonly List<Call> calls;

            public Scope()
            {
                calls = new();
            }

            public void Add(Call call)
            {
                calls.Add(call);
            }
        }

        [DebuggerDisplay("Call :: {type}")]
        public class Call
        {
            /* Any calls in this list cannot be implemented from EMEVD. If a script contains one of these calls we instead compile it to ESD */
            public static Type[] EMEVD_BLACKLIST = new Type[] { };

            public enum Type
            {
                None,

                Set,
                Journal, Choice, AddTopic,
                ModDisposition, SetDisposition, ModReputation,
                ModPcFacRep, PcJoinFaction, PcClearExpelled, PcRaiseRank, PcExpell,
                SetPcCrimeLevel, MessageBox,

                Goodbye,
                StartCombat,
                AddItem, RemoveItem,
                PayFine, GoToJail,

                Disable, Enable,
                Activate, Playgroup, Say, GetTarget, GetItemCount, Lock, Unlock, GetSpell, SayDone, GetRace, ModRegion, GetDetected, ForceSneak, ClearForceSneak,
                ChangeWeather, GetPos, RotateWorld, DontSaveObject, HasSoulGem, WakeUpPc, Resurrect, PlayBink, SetAtStart, RemoveSoulGem, Fall,
                MoveWorld, Position, StreamMusic, GetDisposition, Move, LoopGroup, FadeOut, FadeIn, GetFlee,
                GetDistance, Drop, GetDisabled, OnDeath, SetFlee, GetBlightDisease, OnActivate, HurtStandingActor,
                GetPcRank, SetPos, GetAttacked, GetCommonDisease, GetEffect, SetFight, ShowMap, AddSpell, RemoveSpell, RaiseRank, StopCombat,
                ModFactionReaction, ModFlee, SetAlarm, PlaceAtPc, ClearInfoActor, Cast, ForceGreeting, SetHello, GetJournalIndex, PayFineThief,
                AiWander, AiFollow, AiFollowCell, AiEscort, AiEscortCell, GetAiPackageDone, GetCurrentAiPackage, AiTravel, AiFollowCellPlayer, PositionCell, ModFight,
                GetPcCell, MenuMode, OnPcSoulGemUse, GetLOS, GetLineOfSight, GetDeadCount, CellChanged, HitOnMe, OnPcHitMe, OnPcEquip, OnPcAdd, GetStandingPc,
                GetPcCrimeLevel, GetCollidingPC, GetWaterLevel, GetPcInJail, GetPcTraveling, GetButtonPressed,
                OnKnockout, GetSpellEffects, GetSoundPlaying, ScriptRunning, GetCurrentWeather, OnMurder, GetPcSleep, PcVampire, PcExpelled, GetLocked,
                PlaceItem, SetScale, ModResistParalysis, ModResistPoison, ModResistMagicka, ModResistFire, ModResistFrost, SetDelete, ExplodeSpell, TurnMoonRed, TurnMoonWhite, BecomeWerewolf,
                Random,
                Xbox,

                GetSecondsPassed,

                GetHealth, GetMagicka, GetFatigue,

                SetHealth, SetMagicka, SetFatigue,
                ModHealth, ModMagicka, ModFatigue, ModCurrentHealth, ModCurrentMagicka, ModCurrentFatigue,

                GetStrength, GetIntelligence, GetWillpower, GetAgility, GetSpeed, GetEndurance, GetPersonality, GetLuck,
                SetStrength, SetIntelligence, SetWillpower, SetAgility, SetSpeed, SetEndurance, SetPersonality, SetLuck,
                ModStrength, ModIntelligence, ModWillpower, ModAgility, ModSpeed, ModEndurance, ModPersonality, ModLuck,

                GetAcrobatics, GetAlchemy, GetAlteration, GetArmorer, GetAthletics, GetAxe, GetBlock, GetBluntWeapon, GetConjuration, GetDestruction, GetEnchant, GetHandToHand, GetHeavyArmor, GetIllusion, GetLightArmor, GetLongBlade, GetMarksman, GetMediumArmor, GetMercantile, GetMysticism, GetRestoration, GetSecurity, GetShortBlade, GetSneak, GetSpear, GetSpeechcraft, GetUnarmored,
                ModAcrobatics, ModAlchemy, ModAlteration, ModArmorer, ModAthletics, ModAxe, ModBlock, ModBluntWeapon, ModConjuration, ModDestruction, ModEnchant, ModHandToHand, ModHeavyArmor, ModIllusion, ModLightArmor, ModLongBlade, ModMarksman, ModMediumArmor, ModMercantile, ModMysticism, ModRestoration, ModSecurity, ModShortBlade, ModSneak, ModSpear, ModSpeechcraft, ModUnarmored,
                SetAcrobatics, SetAlchemy, SetAlteration, SetArmorer, SetAthletics, SetAxe, SetBlock, SetBluntWeapon, SetConjuration, SetDestruction, SetEnchant, SetHandToHand, SetHeavyArmor, SetIllusion, SetLightArmor, SetLongBlade, SetMarksman, SetMediumArmor, SetMercantile, SetMysticism, SetRestoration, SetSecurity, SetShortBlade, SetSneak, SetSpear, SetSpeechcraft, SetUnarmored,

                ShowRestMenu,
                EnableStatsMenu, EnableMapMenu, EnableRaceMenu, EnableMagicMenu, EnableStatReviewMenu, EnableBirthMenu, EnableClassMenu, EnableInventoryMenu, EnableNameMenu,
                EnableVanityMode, EnableRest, EnablePlayerJumping, EnablePlayerFighting, EnablePlayerControls, EnablePlayerMagic, EnableTeleporting, EnablePlayerViewSwitch,
                DisablePlayerViewSwitch, DisableTeleporting, DisablePlayerFighting, DisablePlayerJumping, DisablePlayerControls, DisableVanityMode, DisablePlayerMagic,
                PlaySound3D, PlaySound3DVP, StopSound, PlayLoopSound3D, PlayLoopSound3DVP, PlaySound, PlayLoopSoundD3DVP, PlaySoundVP,

                GetPlayerControlsDisabled, GetPlayerFightingDisabled, GetPlayerJumpingDisabled, GetPlayerLookingDisabled, GetPlayerMagicDisabled, GetPlayerViewSwitch,

                /* Papyrus calls we (probably) cannot implement and will discard */
                Rotate, SetAngle, GetAngle,

                /* Variable declarations, vars, and literals */
                Short, Float,
                Literal, Variable,  // only used by if conditions

                /* Conditionals and Scopes */
                Begin, End, If, Else, ElseIf, EndIf,

                /* Misc */
                StartScript, StopScript,
                Return
            }

            public readonly string RAW; // raw original data for debug only

            public readonly Type type;
            public readonly string target;         // can be null, this is set if a papyrus call is on an object like "player->additem cbt 1"
            public readonly string[] parameters;

            public Call(string line)
            {
                RAW = line;

                // Go ahead and trim
                string sanitize = line.Trim();

                // Force lowercase for anything that is NOT in a string literal "like dis"
                sanitize = Utility.StringAwareLower(sanitize);

                // Check if likne is literally blank or a comment
                if (sanitize.StartsWith(";") || sanitize == "") { type = Type.None; target = null; parameters = new string[0]; return; } // line does nothing

                // Replace any tabs with spaces for consistency
                if (sanitize.Contains("\t")) { sanitize = sanitize.Replace("\t", " "); }

                // Remove trailing comments
                if (sanitize.Contains(";"))
                {
                    sanitize = Utility.StringAwareSplit(sanitize, ';')[0].Trim();
                }

                // Remove parens. Papyrus doesn't really use them for anything. As far as i can tell they don't use it for order of operations or multi expression conditionals
                sanitize = Utility.StringAwareReplace(sanitize, '(', ' ');
                sanitize = Utility.StringAwareReplace(sanitize, ')', ' ');

                // Remove any commas as they are not actually needed for papyrus syntax and are used somewhat randomly lol
                sanitize = Utility.StringAwareReplace(sanitize, ',', ' ');

                // Add spaces between operators. This is notably an issue when conditionals have a missing space like 'if var >=3`  that missing space makes parsing messy so we fix this here
                if (sanitize.StartsWith("if") || sanitize.StartsWith("elseif"))
                {
                    List<char> operator_list = new() { '=', '>', '<', '!' }; // this issue seems to exclusively affect if conditions so limiting it to those
                    for (int i = 0; i < sanitize.Length - 1; i++)
                    {
                        char c = sanitize[i];
                        char next = sanitize[i + 1];

                        if (c == '-' && next == '>') { i++; continue; } // skip this particular operator combo

                        bool a = operator_list.Contains(c);
                        bool b = operator_list.Contains(next);

                        // If c is an op and b is not an op and b is not whitespace, add whitespace
                        if(a && !b && !char.IsWhiteSpace(next)) { sanitize = sanitize.Insert(i + 1, " "); }
                        // Opposite of that
                        else if(!a && b && !char.IsWhiteSpace(c)) { sanitize = sanitize.Insert(i + 1, " "); }
                    }
                }

                // Remove any multi spaces
                while (sanitize.Contains("  "))
                {
                    sanitize = sanitize.Replace("  ", " ");
                }

                // Fix a specific single case where a stupid -> has a space in it
                if (sanitize.Contains("-> ")) { sanitize = sanitize.Replace("-> ", "->"); }

                // Fix a specific single case of weird syntax
                if (sanitize.Contains("\"1 ")) { sanitize = sanitize.Replace("\"1 ", "\" 1 "); }

                // Fix a specific case where a single quote is used at random for no fucking reason
                if (sanitize.Contains("land deed'")) { sanitize = sanitize.Replace("land deed'", "land deed\""); }

                // Fix a specific case in Tribunal.esm where some weirdo used a colon after the choice command for no reason
                if (sanitize.Contains("choice:")) { sanitize = sanitize.Replace("choice:", "choice"); }

                // And another one in Bloodmoon.esm, this time with a space. What the fuck is wrong with these scripters lmao
                if(sanitize.StartsWith("choice :")) { sanitize = sanitize.Replace("choice :", "choice"); }

                // Fix a specific case in Tribunal.esm where some freak wrote moddispoistion calls with a subtraction to the function (????) instead of negative value
                if(sanitize.StartsWith("moddisposition") && sanitize.Contains("- ")) { sanitize = sanitize.Replace("- ", "-"); }

                // Handle literal
                if (Utility.StringIsFloat(sanitize))  // doing stringisfloat because a literal can be an int or a float and this covers both cases
                {
                    type = Type.Literal;
                    parameters = new string[] { sanitize };
                }
                //  Handle If
                else if (sanitize.StartsWith("if"))
                {
                    type = Type.If;
                    parameters = Utility.StringAwareSplit(sanitize[2..].Trim(), ' ');
                }
                //  Handle ElseIf
                else if (sanitize.StartsWith("elseif"))
                {
                    type = Type.ElseIf;
                    parameters = Utility.StringAwareSplit(sanitize[6..].Trim(), ' ');
                }
                // Handle targeted call
                else if (sanitize.Contains("->") && !sanitize.StartsWith("set"))
                {
                    // Special split because targets can be in quotes and have spaces in them
                    string[] split = sanitize.Split("->");
                    List<string> ps = Regex.Matches(split[1], @"[\""].+?[\""]|[^ ]+")
                        .Cast<Match>()
                        .Select(m => m.Value)
                        .ToList();

                    type = (Type)Enum.Parse(typeof(Type), ps[0], true);
                    target = split[0].Replace("\"", "").ToLower().Trim();
                    ps.RemoveAt(0);
                    parameters = ps.ToArray();
                }
                // Handle variable
                else if (!Enum.TryParse(typeof(Type), sanitize.Split(" ")[0], true, out object _))
                {
                    type = Type.Variable;
                    parameters = Utility.StringAwareSplit(sanitize, ' ');
                }
                // Handle normal call
                else
                {
                    List<string> cut = sanitize.Split(" ").ToList();

                    type = (Type)Enum.Parse(typeof(Type), cut[0], true);
                    target = null;
                    cut.RemoveAt(0);
                    string s = string.Join(" ", cut);

                    /* Handle special case where you have a call like this :: Set "Manilian Scerius".slaveStatus to 2 */
                    /* Seems to be fairly rare that we have syntax like this but it does happen. */
                    /* Recombine the 2 halves of that "name" and remove the quotes */
                    parameters = Utility.StringAwareSplit(s, ' ');
                }

                // remove quotes around parameters, this is to fix some weird situations where mw devs wrote scripts that have random unnescary quotes around vars/literals
                for(int i=0;i<parameters.Length;i++)
                {
                    parameters[i] = parameters[i].Replace("\"", "").Trim();
                }
            }
        }

        [DebuggerDisplay("Main Scope :: MAIN")]
        public class Main : Call
        {
            public readonly Scope scope;
            public Main(string line) : base(line)
            {
                scope = new();
            }
        }

        [DebuggerDisplay("Conditional :: IF")]
        public class Conditional : Call
        {
            public readonly string op;
            public readonly Call left, right; // left and right of the op
            public readonly Scope pass, fail;

            public Conditional(string line) : base(line)
            {
                string l = "", r = "";
                foreach(string p in parameters)
                {
                    if(p == "==" || p == "!=" || p == ">" || p == "<" || p == ">=" || p == "<=" || p == "=") { op = p; }
                    else if (op == null && p.Contains(" ") && p.Contains("->")) { l += $"\"{p.Replace("->", "\"->")} "; }
                    else if(op == null && p.Contains(" ")) { l += $"\"{p}\" "; }
                    else if(op == null) { l += $"{p} "; }
                    else if(p.Contains(" ")) { r += $"\"{p}\" "; }
                    else { r += $"{p} "; }
                }
                if (op == null) { throw new Exception($"Failed to parse conditional :: {RAW}"); } // oop all bugs

                left = new(l.Trim());
                right = new(r.Trim());

                pass = new();
                fail = new();
            }

            /* Convert operator symbol of this conditional to an index for EMEVD functions to use */
            public string OperatorIndex()
            {
                switch(op)
                {
                    case "==":
                    case "=":
                        return "0";
                    case "!=":
                        return "1";
                    case ">":
                        return "2";
                    case "<":
                        return "3";
                    case ">=":
                        return "4";
                    case "<=":
                        return "5";

                    default: Lort.Log($"## PAPYRUS OPERATOR UNK ## ' {op} '", Lort.Type.Debug); return "0";
                }
            }

            /* Same as above but flips greater and less than. Used in a special case where i want to flip a conditional the other way around */
            public string ReversedOperatorIndex()
            {
                switch (op)
                {
                    case "==":
                    case "=":
                        return "0";
                    case "!=":
                        return "1";
                    case "<":
                        return "2";
                    case ">":
                        return "3";
                    case "<=":
                        return "4";
                    case ">=":
                        return "5";

                    default: Lort.Log($"## PAPYRUS OPERATOR UNK ## ' {op} '", Lort.Type.Debug); return "0";
                }
            }

            /* Actually resolve a comparison operation from this conditional with the given left side value */
            public bool ResolveOperator(int leftValue)
            {
                if(right.type != Type.Literal) { throw new Exception($"Tried to 'ResolveOperator()' for call '{RAW}'"); }
                int rightValue = int.Parse(right.parameters[0]);
                switch (op)
                {
                    case "==": return leftValue == rightValue;
                    case "!=": return leftValue != rightValue;
                    case ">=": return leftValue >= rightValue;
                    case ">": return leftValue > rightValue;
                    case "<=": return leftValue <= rightValue;
                    case "<": return leftValue < rightValue;
                    default: return false;
                }
            }
        }

        [DebuggerDisplay("ConditionalBranch :: ELIF")]
        public class ConditionalBranch : Conditional
        {
            public ConditionalBranch(string line) : base(line)
            {

            }
        }
    }
}
