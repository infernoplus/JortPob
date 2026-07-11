using JortPob.Common;
using JortPob.Scripts;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json.Nodes;

namespace JortPob
{
    public class ScriptManager
    {
        public static readonly List<uint> DO_NOT_USE_FLAGS = new();

        public ScriptCommon common;
        public List<Script> scripts; // map scripts
        public Dictionary<string, uint> locations;
        public Dictionary<PhasedNpcContent, string> routing; // a routing dictionary for phased npcs to share flags
        public Dictionary<Cell, uint> areas; // dictionary of region entity ids that cover the volume of an interior cell
        public ScriptManager()
        {
            common = new(this);
            scripts = new();
            locations = new();
            routing = new();
            areas = new();

            // I wrote a little baby program to scan the common.emevd script and extract every number used in it.
            // I have these used numbers in a txt file and we parse that into a list
            // We avoid using any of these numbers as flag ids because it can cause collisions with base game common functions
            // This is technically a temporary measure @TODO: !! eventualy we will rewrite common.emevd and replace it with all custom code
            // That will be tough though and not a high priority task for dev.
            string[] lines = File.ReadAllLines(Utility.ResourcePath(@"script\common_event_used_values.txt"));            
            foreach(string line in lines)
            {
                DO_NOT_USE_FLAGS.Add(uint.Parse(line));
            }
        }

        public BaseScript GetScript(int map, int x, int y, int block)
        {
            if ((map == 60 || map == 61) && block != 0) { return common; } // big/huge tiles return scriptcommon as their area scripts.

            foreach (Script script in scripts)
            {
                if (script.map == map && script.x == x && script.y == y && script.block == block)
                {
                    return script;
                }
            }

            Script s = new(this, map, x, y, block);
            scripts.Add(s);
            return s;
        }

        public BaseScript GetScript(IMSBCompilableGroup group)
        {
            if (!group.IsInterior && group.GetType() != typeof(Tile)) { return common; } // big/huge tiles return scriptcommon as their area scripts.

            return GetScript(group.map, group.coordinate.x, group.coordinate.y, group.block);
        }

        /* Supports phased routing */
        public Script.Flag GetFlag(Script.Flag.Designation designation, Content content)
        {
            if (content is PhasedNpcContent pnpc && routing.TryGetValue(pnpc, out var route))
            {
                return GetFlag(designation, route);
            }
            else { return GetFlag(designation, content.entity.ToString()); }
        }

        /* Supports phased routing */
        public Script.Flag GetFlagLocal(Content content, string name)
        {
            if (content is PhasedNpcContent pnpc && routing.TryGetValue(pnpc, out var route))
            {
                return GetFlag(Script.Flag.Designation.Local, $"{route}.{name}");
            }
            else { return GetFlag(Script.Flag.Designation.Local, $"{content.entity.ToString()}.{name}"); }
        }

        /* Does not support phased routing */
        public Script.Flag GetFlag(Script.Flag.Designation designation, string name)
        {
            (Script.Flag.Designation, string) lookupKey = Script.FormatFlagLookupKey(designation, name.ToLower());

            Script.Flag f = common.FindFlagByLookupKey(lookupKey);
            if (f != null) { return f; }

            foreach (BaseScript script in scripts)
            {
                f = script.FindFlagByLookupKey(lookupKey);
                if (f != null) { return f; }
            }

            return null;
        }

        public void AddRoute(PhasedNpcContent phase, Content original)
        {
            routing.Add(phase, original.entity.ToString());
        }

        /* Sets up race and faction flags that are used globally and interact with specific papyrus calls */
        /* Also some other globalish vars we need for scripts like Reputation and CrimeLevel */
        public void SetupSpecialFlags(ESM esm)
        {
            /* Create some special common events */ // these have to wait until after ESM is loaded otherwise we'd just do it in the constructor
            common.CreateWeatherTracker();
            common.TimeHandler();

            // Create a one time event that sets some default flags at game startup + also moves player to debug area if they aren't there
            Script.Flag gameInitEventFlag = common.CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, "Global:GameInitEvent");
            Script.Flag gameInitFlag = common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.Hardcode, "GameInit");
            EMEVD.Event gameInitEvent = new();
            gameInitEvent.ID = gameInitEventFlag.id;
            gameInitEvent.Instructions.Add(common.AUTO.ParseAdd($"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {gameInitFlag.id});"));  // if init has been done
            gameInitEvent.Instructions.Add(common.AUTO.ParseAdd($"EndUnconditionally(EventEndType.End);"));                                       // end event
            gameInitEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, 6000, OFF);")); // Always off flag
            gameInitEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, 6001, ON);")); // Always on flag
            gameInitEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, 60120, OFF);")); // Flag that enables crafting
            List<int> setFlagsOn = new()
            {
                62010, 62011, 62012, 62020, 62021, 62022, 62030, 62031, 62032, 62040, 62041, 62050, 62051, 62052,  // Known world map pieces to unlock all major areas of map
                62053, 62004, 62005, 62006, 62007, 62008, 62009,                                                  // Unknown world map pieces unlocks a bunch of small areas on the sides of the map
                62000                                                                                            // Unlocks the map itself
            };
            foreach(int flagId in setFlagsOn)
            {
                gameInitEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {flagId}, ON);"));           // Add code to turn on flags
            }
            gameInitEvent.Instructions.Add(common.AUTO.ParseAdd($"IfPlayerInoutMap(OR_01, true, 18, 0, 0, 0);"));
            gameInitEvent.Instructions.Add(common.AUTO.ParseAdd($"SkipIfConditionGroupStateUncompiled(1, PASS, OR_01);")); // if player is not in the stranded graveyard
            gameInitEvent.Instructions.Add(common.AUTO.ParseAdd($"SetPlayerRespawnPoint(18002020);"));                     // set players respawn to the debug warp room
            gameInitEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {gameInitFlag.id}, ON);"));      // set initgame flag on so it's donezo
            gameInitEvent.Instructions.Add(common.AUTO.ParseAdd($"WarpPlayer(18, 0, 0, 0, 18000981, -1);"));               // and to wrap thuings up warp them to debug room
            common.emevd.Events.Add(gameInitEvent);
            common.init.Instructions.Add(common.AUTO.ParseAdd($"InitializeEvent(0, {gameInitEventFlag.id})"));  // initialize in common

            // A short for reputation, maybe could fit in a byte but lets just be safe here
            common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Short, Script.Flag.Designation.Reputation, "Reputation");

            // Crime gold to be paid to guards
            common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Short, Script.Flag.Designation.CrimeLevel, "CrimeLevel");

            // Arrest flat, set to true when a guard attempts to arrest you. Resets on game load. Makes it so guards attempt arrest once then just kill you if you resist
            common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.Arrest, "Arrest");

            // Crime absolved flag
            common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.CrimeAbsolved, "CrimeAbsolved"); // not temp since load screen happens if going to jail
            common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.ResetHostility, "ResetHostility");

            // Temp flag that is set when a guard is talking to the player, used to control some guard aggro stuff
            common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.GuardIsGreeting, "GuardIsGreeting");

            // Temp flag that is set true when a player is talking with an npc, used to prevent idle/hello lines from nearby npcs while you are talking with someone
            common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.PlayerIsTalking, "PlayerIsTalking");

            // Temp flag that is set to the players current soul/rune count. For use when comparing your cash dosh money count in EMEVD
            Script.Flag playerRuneCount = common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Int, Script.Flag.Designation.PlayerRuneCount, "PlayerRuneCount");

            // Temp flags that are set for various player stats
            Script.Flag playerMaxHP = common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Short, Script.Flag.Designation.PlayerStat, "MaxHP");
            Script.Flag playerVigor = common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Byte, Script.Flag.Designation.PlayerStat, "Vigor");
            Script.Flag playerMind = common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Byte, Script.Flag.Designation.PlayerStat, "Mind");
            Script.Flag playerEndurance = common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Byte, Script.Flag.Designation.PlayerStat, "Endurance");
            Script.Flag playerStrength = common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Byte, Script.Flag.Designation.PlayerStat, "Strength");
            Script.Flag playerDexterity = common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Byte, Script.Flag.Designation.PlayerStat, "Dexterity");
            Script.Flag playerIntelligence = common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Byte, Script.Flag.Designation.PlayerStat, "Intelligence");
            Script.Flag playerFaith = common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Byte, Script.Flag.Designation.PlayerStat, "Faith");
            Script.Flag playerArcane = common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Byte, Script.Flag.Designation.PlayerStat, "Arcane");

            // Temp flags that if written to will modify player stats
            Script.Flag setVigorFlag = common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.Hardcode, "SetVigor");
            Script.Flag setMindFlag = common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.Hardcode, "SetMind");
            Script.Flag setEnduranceFlag = common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.Hardcode, "SetEndurance");
            Script.Flag setStrengthFlag = common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.Hardcode, "SetStrength");
            Script.Flag setDexterityFlag = common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.Hardcode, "SetDexterity");
            Script.Flag setIntelligenceFlag = common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.Hardcode, "SetIntelligence");
            Script.Flag setFaithFlag = common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.Hardcode, "SetFaith");
            Script.Flag setArcaneFlag = common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.Hardcode, "SetArcane");


            // Temp flag that is set true when a player is sneaking
            Script.Flag playerIsSneakingFlag = common.CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.PlayerIsSneaking, "PlayerIsSneaking");

            // One flag for each race. Single bit. Name of the flag to identify it by is the same as the enum name from CharacterContent.Race
            // Reason for doing 10 bits instead of a single byte is because I don't want to set an eventvalueflag from HKS becasue lua is a cursed language
            List<JsonNode> raceJson = [.. esm.GetAllRecordsByType(ESM.Type.Race)];
            List<Script.Flag> raceFlags = new();
            foreach(JsonNode json in raceJson)
            {
                raceFlags.Add(common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.PlayerRace, json["id"].GetValue<string>().Replace(" ", ""), 0));
            }

            // Crete the HKS file that will set the correct raceflag after character creation
            // We do this by setting an unused value during character creation based on race and then reading that value in hks. Then we set a flag from that value and donezo.
            // This is defo some shitcode and maybe later we can improve this system in some kind of meaningful way
            string hksFile = File.ReadAllText(Utility.ResourcePath(@"script\c0000.hks"));
            string hksJankCall = "\t-- Jank auto-generated code: calls jank function above\r\n\tif not JankRaceInitDone then\r\n\t\tJankRaceInitDone = true\r\n\t\tJankRaceInit()\r\n\tend\r\n\t-- End of jank\r\n";
            string hksJankStart = "-- Jank auto-generated code: function to check burnscars value and set the correct race flag\r\nlocal JankRaceInitDone = false\r\nfunction JankRaceInit()\r\n\tlocal WritePointerChain = 10000\r\n\tlocal TraversePointerChain = 10000\r\n\tlocal SetEventFlag = 10003\r\n\tlocal CHR_INS_BASE = 1\r\n\tlocal PLAYER_GAME_DATA = 0x580\r\n    local BURN_SCAR = 0x876\r\n\tlocal UNSIGNED_BYTE = 0\r\n\tlocal DEBUG_PRINT = 10001\r\n\tlocal BURN_SCAR_VALUE = env(TraversePointerChain, CHR_INS_BASE, UNSIGNED_BYTE, PLAYER_GAME_DATA, BURN_SCAR)\r\n\tif BURN_SCAR_VALUE > 0 then\r\n";
            string hksJankEnd = "\t\tact(WritePointerChain, CHR_INS_BASE, UNSIGNED_BYTE, 0, PLAYER_GAME_DATA, BURN_SCAR)\r\n\tend\r\nend\r\n";

            string hksJankGen = "";
            foreach(Script.Flag flag in raceFlags)
            {
                CharacterContent.Race raceEnum = (CharacterContent.Race)System.Enum.Parse(typeof(CharacterContent.Race), flag.name);

                hksJankGen += $"\t\tif BURN_SCAR_VALUE == {(int)raceEnum} then\r\n\t\t\tact(DEBUG_PRINT, \"{raceEnum.ToString()}\")\r\n\t\t\tact(SetEventFlag, \"{flag.id}\", 1)\r\n\t\tend\r\n";
            }

            string hksSneakShitcode = $""""

                                          -- literally just writing if the player is sneaking or not to an emevd flag
                                          if env(IsCOMPlayer) == FALSE then
                                              if c_IsStealth == TRUE then
                                                  act(10003, "{playerIsSneakingFlag.id}", 1)
                                              else
                                                  act(10003, "{playerIsSneakingFlag.id}", 0)
                                              end
                                          end


                                      """";

            string playerRuneCountBase = playerRuneCount.id.ToString()[..7];
            string playerRuneCountOffset = playerRuneCount.id.ToString()[7..];
            string hksSoulCounterShitCode = $""""

                                            	-- writing the players rune count to a 32bit flag so emevd can look at It
                                                if env(IsCOMPlayer) == FALSE then
                                                    local SetEventFlag = 10003
                                                    local TraversePointerChain = 10000
                                                    local GAME_DATA_MAN = 0x3D5DF38
                                                    local PLAYER_GAME_INFO = 0x8
                                                    local SOUL_COUNT = 0x6c
                                            		local UNSIGNED_INT = 4
                                                    local currentRunes = env(TraversePointerChain, 0, UNSIGNED_INT, GAME_DATA_MAN, PLAYER_GAME_INFO, SOUL_COUNT)
                                            		for i = 0, 31 do
                                            			local flagBit = tostring("{playerRuneCountBase}".. string.format("%03d", i + {playerRuneCountOffset})) -- kill me
                                            			act(SetEventFlag, flagBit, value_of_bit(currentRunes, i))
                                            		end
                                                end


                                            """";

            string playerMaxHpBase = playerMaxHP.id.ToString()[..7];
            string playerMaxHpOffset = playerMaxHP.id.ToString()[7..];
            string hksPlayerStatShitcode = $""""

                                            -- writing the players max hp to a 16bit flag so esd/emevd can look at it
                                            -- and also writing players stats to 8bit flags so emevd can read them
                                            if env(IsCOMPlayer) == FALSE then
                                                -- vars
                                                local SetEventFlag = 10003
                                                local TraversePointerChain = 10000
                                                local WritePointerChain = 10000
                                                local CHR_INS_BASE = 1
                                                local SIGNED_INT = 5
                                                local PLAYER_GAME_DATA = 0x580
                                                local LEVEL = 0x68
                                                local VIGOR = 0x288
                                                local MIND = 0x28C
                                                local ENDURANCE = 0x290
                                                local STRENGTH = 0x298
                                                local DEXTERITY = 0x29C
                                                local INTELLIGENCE = 0x2A0
                                                local FAITH = 0x2A4
                                                local ARCANE = 0x2A8

                                                -- max hp
                                                    local maxHp = env(2013)
                                                    for i = 0, 15 do
                                                        local flagBit = tostring("{playerMaxHpBase}".. string.format("%03d", i + {playerMaxHpOffset}))
                                                        act(SetEventFlag, flagBit, value_of_bit(maxHp, i))
                                                    end

                                                local level = env(TraversePointerChain, CHR_INS_BASE, SIGNED_INT, PLAYER_GAME_DATA, LEVEL)
                                                local vig = env(TraversePointerChain, CHR_INS_BASE, SIGNED_INT, PLAYER_GAME_DATA, VIGOR)
                                                local mnd = env(TraversePointerChain, CHR_INS_BASE, SIGNED_INT, PLAYER_GAME_DATA, MIND)
                                                local edu = env(TraversePointerChain, CHR_INS_BASE, SIGNED_INT, PLAYER_GAME_DATA, ENDURANCE)
                                                local str = env(TraversePointerChain, CHR_INS_BASE, SIGNED_INT, PLAYER_GAME_DATA, STRENGTH)
                                                local dex = env(TraversePointerChain, CHR_INS_BASE, SIGNED_INT, PLAYER_GAME_DATA, DEXTERITY)
                                                local int = env(TraversePointerChain, CHR_INS_BASE, SIGNED_INT, PLAYER_GAME_DATA, INTELLIGENCE)
                                                local fth = env(TraversePointerChain, CHR_INS_BASE, SIGNED_INT, PLAYER_GAME_DATA, FAITH)
                                                local arc = env(TraversePointerChain, CHR_INS_BASE, SIGNED_INT, PLAYER_GAME_DATA, ARCANE)

                                                -- vig
                                                for i = 0, 7 do
                                                    local flagBit = tostring("{playerVigor.id.ToString()[..7]}".. string.format("%03d", i + {playerVigor.id.ToString()[7..]}))
                                                    act(SetEventFlag, flagBit, value_of_bit(vig, i))
                                                end
                                                -- mnd
                                                for i = 0, 7 do
                                                    local flagBit = tostring("{playerMind.id.ToString()[..7]}".. string.format("%03d", i + {playerMind.id.ToString()[7..]}))
                                                    act(SetEventFlag, flagBit, value_of_bit(mnd, i))
                                                end
                                                -- edu
                                                for i = 0, 7 do
                                                    local flagBit = tostring("{playerEndurance.id.ToString()[..7]}".. string.format("%03d", i + {playerEndurance.id.ToString()[7..]}))
                                                    act(SetEventFlag, flagBit, value_of_bit(edu, i))
                                                end
                                                -- str
                                                for i = 0, 7 do
                                                    local flagBit = tostring("{playerStrength.id.ToString()[..7]}".. string.format("%03d", i + {playerStrength.id.ToString()[7..]}))
                                                    act(SetEventFlag, flagBit, value_of_bit(str, i))
                                                end
                                                -- dex
                                                for i = 0, 7 do
                                                    local flagBit = tostring("{playerDexterity.id.ToString()[..7]}".. string.format("%03d", i + {playerDexterity.id.ToString()[7..]}))
                                                    act(SetEventFlag, flagBit, value_of_bit(dex, i))
                                                end
                                                -- int
                                                for i = 0, 7 do
                                                    local flagBit = tostring("{playerIntelligence.id.ToString()[..7]}".. string.format("%03d", i + {playerIntelligence.id.ToString()[7..]}))
                                                    act(SetEventFlag, flagBit, value_of_bit(int, i))
                                                end
                                                -- fth
                                                for i = 0, 7 do
                                                    local flagBit = tostring("{playerFaith.id.ToString()[..7]}".. string.format("%03d", i + {playerFaith.id.ToString()[7..]}))
                                                    act(SetEventFlag, flagBit, value_of_bit(fth, i))
                                                end
                                                -- arc
                                                for i = 0, 7 do
                                                    local flagBit = tostring("{playerArcane.id.ToString()[..7]}".. string.format("%03d", i + {playerArcane.id.ToString()[7..]}))
                                                    act(SetEventFlag, flagBit, value_of_bit(arc, i))
                                                end

                                                -- modstat functions. setter trigger thing
                                                local SET_LEVEL = 0x68
                                                local SET_VIG = 0x3C + (0 * 4)
                                                local SET_MND = 0x3C + (1 * 4)
                                                local SET_END = 0x3C + (2 * 4)
                                                local SET_STR = 0x3C + (3 * 4)
                                                local SET_DEX = 0x3C + (4 * 4)
                                                local SET_INT = 0x3C + (5 * 4)
                                                local SET_FTH = 0x3C + (6 * 4)
                                                local SET_ARC = 0x3C + (7 * 4)

                                                local setVig = get_event_value("{setVigorFlag.id}", {setVigorFlag.Bits()})
                                                if setVig > 0 then
                                                  local newVig = math.max(1, math.min(99, vig + (setVig - 100)))
                                                  local change = newVig - vig
                                                  local newLevel = math.max(1, level + change)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newVig, PLAYER_GAME_DATA, SET_VIG)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newLevel, PLAYER_GAME_DATA, SET_LEVEL)
                                                  set_event_value("{setVigorFlag.id}", {setVigorFlag.Bits()}, 0)
                                                end

                                                local setMnd = get_event_value("{setMindFlag.id}", {setMindFlag.Bits()})
                                                if setMnd > 0 then
                                                  local newMnd = math.max(1, math.min(99, mnd + (setMnd - 100)))
                                                  local change = newMnd - mnd
                                                  local newLevel = math.max(1, level + change)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newMnd, PLAYER_GAME_DATA, SET_MND)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newLevel, PLAYER_GAME_DATA, SET_LEVEL)
                                                  set_event_value("{setMindFlag.id}", {setMindFlag.Bits()}, 0)
                                                end

                                                local setEnd = get_event_value("{setEnduranceFlag.id}", {setEnduranceFlag.Bits()})
                                                if setEnd > 0 then
                                                  local newEnd = math.max(1, math.min(99, edu + (setEnd - 100)))
                                                  local change = newEnd - edu
                                                  local newLevel = math.max(1, level + change)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newEnd, PLAYER_GAME_DATA, SET_END)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newLevel, PLAYER_GAME_DATA, SET_LEVEL)
                                                  set_event_value("{setEnduranceFlag.id}", {setEnduranceFlag.Bits()}, 0)
                                                end

                                                local setStr = get_event_value("{setStrengthFlag.id}", {setStrengthFlag.Bits()})
                                                if setStr > 0 then
                                                  local newStr = math.max(1, math.min(99, str + (setStr - 100)))
                                                  local change = newStr - str
                                                  local newLevel = math.max(1, level + change)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newStr, PLAYER_GAME_DATA, SET_STR)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newLevel, PLAYER_GAME_DATA, SET_LEVEL)
                                                  set_event_value("{setStrengthFlag.id}", {setStrengthFlag.Bits()}, 0)
                                                end

                                                local setDex = get_event_value("{setDexterityFlag.id}", {setDexterityFlag.Bits()})
                                                if setDex > 0 then
                                                  local newDex = math.max(1, math.min(99, dex + (setDex - 100)))
                                                  local change = newDex - dex
                                                  local newLevel = math.max(1, level + change)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newDex, PLAYER_GAME_DATA, SET_DEX)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newLevel, PLAYER_GAME_DATA, SET_LEVEL)
                                                  set_event_value("{setDexterityFlag.id}", {setDexterityFlag.Bits()}, 0)
                                                end

                                                local setInt = get_event_value("{setIntelligenceFlag.id}", {setIntelligenceFlag.Bits()})
                                                if setInt > 0 then
                                                  local newInt = math.max(1, math.min(99, int + (setInt - 100)))
                                                  local change = newInt - int
                                                  local newLevel = math.max(1, level + change)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newInt, PLAYER_GAME_DATA, SET_INT)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newLevel, PLAYER_GAME_DATA, SET_LEVEL)
                                                  set_event_value("{setIntelligenceFlag.id}", {setIntelligenceFlag.Bits()}, 0)
                                                end

                                                local setFth = get_event_value("{setFaithFlag.id}", {setFaithFlag.Bits()})
                                                if setFth > 0 then
                                                  local newFth = math.max(1, math.min(99, fth + (setFth - 100)))
                                                  local change = newFth - fth
                                                  local newLevel = math.max(1, level + change)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newFth, PLAYER_GAME_DATA, SET_FTH)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newLevel, PLAYER_GAME_DATA, SET_LEVEL)
                                                  set_event_value("{setFaithFlag.id}", {setFaithFlag.Bits()}, 0)
                                                end

                                                local setArc = get_event_value("{setArcaneFlag.id}", {setArcaneFlag.Bits()})
                                                if setArc > 0 then
                                                  local newArc = math.max(1, math.min(99, arc + (setArc - 100)))
                                                  local change = newArc - arc
                                                  local newLevel = math.max(1, level + change)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newArc, PLAYER_GAME_DATA, SET_ARC)
                                                  act(WritePointerChain, CHR_INS_BASE, SIGNED_INT, newLevel, PLAYER_GAME_DATA, SET_LEVEL)
                                                  set_event_value("{setArcaneFlag.id}", {setArcaneFlag.Bits()}, 0)
                                                end

                                            end


                                          """";

            string hksBitwiseShitCode =    $""""

                                            -- get event value at flag, of length
                                            local function get_event_value(flag, length)
                                              local GetEventFlag = 10003
                                              local val = 0;
                                              local base = string.sub(flag, 1, 6)
                                              for i=0,length-1 do
                                                local id = tonumber(string.sub(flag, 7) + i)
                                                val = val + (env(GetEventFlag, base .. id) * 2^i)
                                              end
                                              return val
                                            end

                                            -- set event value at flag, of length, to value
                                            local function set_event_value(flag, length, value)
                                              local SetEventFlag = 10003
                                              local base = string.sub(flag, 1, 6)
                                              for i=0,length-1 do
                                                local id = tonumber(string.sub(flag, 7) + i)
                                                act(SetEventFlag, base .. id, value_of_bit(value, i))
                                              end
                                            end

                                            -- gets bit n of a number. bitwise ops dont' exist in this version of lua. code yoinked from google
                                            function value_of_bit(num, n)
                                                -- Calculate 2^n
                                                local power_of_two = 2^n 

                                                -- Shift the desired bit to the least significant position
                                                local shifted_num = math.floor(num / power_of_two)

                                                -- Get the value of the least significant bit
                                                local bit_value = shifted_num % 2

                                                -- Return value of that bit
                                            	return bit_value
                                            end


                                            """";

            hksFile = hksFile.Replace("-- $$ INJECT JANK UPDATE FUNCTION HERE $$ --", $"{hksJankStart}{hksJankGen}{hksJankEnd}{hksBitwiseShitCode}");
            hksFile = hksFile.Replace("-- $$ INJECT JANK UPDATE CALL HERE $$ --", $"{hksSneakShitcode}{hksSoulCounterShitCode}{hksPlayerStatShitcode}{hksJankCall}");
            string hksOutPath = Path.Combine(Const.OUTPUT_PATH, @"action\script\c0000.hks");
            if (File.Exists(hksOutPath)) { File.Delete(hksOutPath); }
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(hksOutPath));
            File.WriteAllText(hksOutPath, hksFile);

            // Max rep seems to be 120, may need to cap it incase you can somehow overflow that
            foreach (FactionInfo faction in esm.factions)
            {
                common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.FactionJoined, faction.id, 0);
                common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.FactionReputation, faction.id, 0);
                common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Byte, Script.Flag.Designation.FactionRank, faction.id, 0);
                common.CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.FactionExpelled, faction.id, 0);
            }
        }

        /* This event is triggered when player goes to jail or pays fines to a guard. Resets all crime stuff like npc hostility and crime gold */
        public void GenerateGlobalCrimeAbsolvedEvent()
        {
            List<Script.Flag> allFlags = [.. common.flags];
            foreach (Script script in scripts)
            {
                allFlags.AddRange(script.flags);
            }

            Script.Flag eventFlag = common.CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, "Global:AbsolveCrimeEvent");
            EMEVD.Event absolveEvent = new();
            absolveEvent.ID = eventFlag.id;

            Script.Flag absolveFlag = GetFlag(Script.Flag.Designation.CrimeAbsolved, "CrimeAbsolved");
            absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {absolveFlag.id});"));  // if absolve flag set

            Script.Flag crimeLevel = GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel");
            absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"EventValueOperation({crimeLevel.id}, {crimeLevel.Bits()}, 0, 0, 1, 5);")); // 5 is CalculationType.Assign

            Script.Flag arrestFlag = GetFlag(Script.Flag.Designation.Arrest, "Arrest");
            absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {arrestFlag.id}, OFF);"));  // arrest attempt flag is set back to off

            Script.Flag guardGreetFlag = GetFlag(Script.Flag.Designation.GuardIsGreeting, "GuardIsGreeting");
            absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {guardGreetFlag.id}, OFF);"));  // guard greet flag back off just incase it got stuck on

            absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"ClearSpEffect(10000, {(int)SpeffManager.Functional.Alarming});")); // remove "alarming" speff from players

            int delayCounter = 0; // if you do to much in a single frame the game crashes so every X~ flags we wait a frame
            foreach (Script.Flag flag in allFlags)
            {
                if (flag.designation != Script.Flag.Designation.Hostile) { continue; }  // only reset hostility flags
                string onOff = flag.value == 0 ? "OFF" : "ON";
                absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {flag.id}, {onOff});"));

                if(++delayCounter > 512)
                {
                    absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"WaitFixedTimeFrames(1);"));
                    delayCounter = 0;
                }
            }

            absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {absolveFlag.id}, OFF);"));
            absolveEvent.Instructions.Add(common.AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);")); // restart so its ready to go again when the player fucks up

            common.emevd.Events.Add(absolveEvent);
            common.init.Instructions.Add(common.AUTO.ParseAdd($"InitializeEvent(0, {eventFlag.id})"));  // initialize in common
        }

        /* This function is very similar to CrimeAbsolve above but only resets hostility. This is used when the player rests to make npcs that the player provoked return to neutral */
        public void GenerateGlobalResetHostilityEvent()
        {
            List<Script.Flag> allFlags = [.. common.flags];
            allFlags.AddRange(scripts.SelectMany(script => script.flags));

            Script.Flag eventFlag = common.CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, "Global:ResetHostilityEvent");
            EMEVD.Event resetEvent = new();
            resetEvent.ID = eventFlag.id;

            Script.Flag resetFlag = GetFlag(Script.Flag.Designation.ResetHostility, "ResetHostility");
            resetEvent.Instructions.Add(common.AUTO.ParseAdd($"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {resetFlag.id});"));  // if absolve flag set

            Script.Flag arrestFlag = GetFlag(Script.Flag.Designation.Arrest, "Arrest");
            resetEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {arrestFlag.id}, OFF);"));  // arrest attempt flag is set back to off

            Script.Flag guardGreetFlag = GetFlag(Script.Flag.Designation.GuardIsGreeting, "GuardIsGreeting");
            resetEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {guardGreetFlag.id}, OFF);"));  // guard greet flag back off just incase it got stuck on

            resetEvent.Instructions.Add(common.AUTO.ParseAdd($"ClearSpEffect(10000, {(int)SpeffManager.Functional.Alarming});")); // remove "alarming" speff from players

            int delayCounter = 0; // if you do to much in a single frame the game crashes so every X~ flags we wait a frame
            foreach (Script.Flag flag in allFlags)
            {
                if (flag.designation != Script.Flag.Designation.Hostile) { continue; }  // only reset hostility flags
                string onOff = flag.value == 0 ? "OFF" : "ON";
                resetEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {flag.id}, {onOff});"));

                if (++delayCounter > 512)
                {
                    resetEvent.Instructions.Add(common.AUTO.ParseAdd($"WaitFixedTimeFrames(1);"));
                    delayCounter = 0;
                }
            }

            resetEvent.Instructions.Add(common.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {resetFlag.id}, OFF);"));
            resetEvent.Instructions.Add(common.AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);")); // restart so its ready to go again when the player fucks up

            common.emevd.Events.Add(resetEvent);
            common.init.Instructions.Add(common.AUTO.ParseAdd($"InitializeEvent(0, {eventFlag.id})"));  // initialize in common
        }


        public void GenerateAreaEvents()
        {
            foreach(Script script in scripts)
            {
                script.GenerateCrimeEvents();
                script.GenerateThieveryEvent();
            }
        }

        /* Finds an area script for a piece of content */
        /* Called by script compiling in DialogESD.cs and PapyrusEMEVD.cs */
        public BaseScript FindScriptFor(Layout layout, Content content)
        {
            BaseTile tile = layout.FindTile(content);
            if (tile != null)
            {
                return GetScript(tile);
            }
            else
            {
                InteriorGroup.Chunk chunk = layout.FindChunk(content);
                return GetScript(chunk.group);
            }

            throw new Exception("Could not find area script for a content object"); // should be unreacahable
        }

        /* These 2 functions are used by PapyrusEMEVD for the GetPcCell check. This just indexes the regions of named locations for use by scripts */
        public void AddLocation(string name, uint entity)
        {
            if (locations.ContainsKey(name.ToLower().Trim())) { return; }
            locations.Add(name.ToLower().Trim(), entity);
        }

        public uint GetLocation(string name)
        {
            if (locations.TryGetValue(name.ToLower().Trim(), out var entity))
                return entity;
            return 0;
        }

        /* Retrieve info on a PhasedNpcContent based on a Papyrus call of Position or PositionCell */
        public PhasedNpcContent FindPhase(PhasedNpcContent pnpc, string location, Vector3 position) { return FindPhase(pnpc.source, location, position); }
        public PhasedNpcContent FindPhase(uint source, string location, Vector3 position)
        {
            foreach(var (pnpc, route) in routing)
            {
                if (
                    pnpc.source == source &&                                                                                                // phase is of the same character
                    ((pnpc.cell.IsExterior() && location == null) || (location?.ToLower().Trim() == pnpc.cell.name?.ToLower().Trim())) &&   // phase location matches the interior cell or is in the overworld
                    Vector3.Distance(pnpc.position, position) < 1f                                                                          // phase position is a close enough match
                )
                {
                    return pnpc;
                }
            }
            return null;
        }

        /* Write all EMEVD scripts this class has created */
        public void Write()
        {
            /* Debuggy thing */
            List<Script.Flag> allFlags = [.. common.flags];
            foreach (BaseScript script in scripts)
            {
                allFlags.AddRange(script.flags);
            }

            /* Output a cheatsheet with every flag and it's id and starting value */
            List<string> flagInfo = new();
            foreach (Script.Flag flag in allFlags)
            {
                /* If the description of a flag looks like it's a number, its probably an entity id, search the entityIdMappings and see if we have some info on it to include in this file */
                string desc = null;
                foreach (BaseScript script in scripts)
                {
                    if (!Utility.StringIsInteger(flag.name)) { break; } // dont bother checking unless flag name appears to be an entityid
                    if (script.entityIdMapping.ContainsKey(uint.Parse(flag.name))) { desc = script.entityIdMapping[uint.Parse(flag.name)]; break; }
                }
                /* Write */
                flagInfo.Add($"{flag.category.ToString().PadRight(16)} {flag.type.ToString().PadRight(16)} {flag.designation.ToString().PadRight(24)} {flag.name.ToString().PadRight(48)} {flag.value.ToString().PadRight(6)} {flag.id.ToString().PadRight(18)} {(desc!=null?desc:"")}");
            }
            File.WriteAllLines(Path.Combine(Const.OUTPUT_PATH, "flag information.txt"), flagInfo.ToArray());

            /* Write EMEVD scripts */
            Lort.Log($"Writing {scripts.Count + 1} EMEVDs...", Lort.Type.Main);
            common.Write();
            foreach(BaseScript script in scripts)
            {
                script.Write();
            }

            /* Write lua ai logic binds */
            BindLua();
        }

        /* Write ai logic luabnd */
        public void BindLua()
        {
            // Handles 030000_logic specifically
            BND4 bnd = BND4.Read(Path.Combine(Const.ELDEN_PATH, @"game\script\030000_logic.luabnd.dcx"));
            bnd.Files[0].Bytes = File.ReadAllBytes(Utility.ResourcePath(@"ai\030000_logic.lua"));
            bnd.Write(Path.Combine(Const.OUTPUT_PATH, @"script\030000_logic.luabnd.dcx"));
        }
    }
}
