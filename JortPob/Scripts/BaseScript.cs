using JortPob.Common;
using SoulsFormats;
using SoulsIds;
using System.Collections.Generic;

namespace JortPob.Scripts
{
    using ScriptFlagLookupKey = (Script.Flag.Designation, string);

    public abstract class BaseScript
    {
        public readonly ScriptManager manager;

        public readonly Events AUTO;

        public readonly EMEVD emevd;
        public readonly EMEVD.Event init;

        public readonly List<Script.Flag> flags;

        /**
        * This is just used to speed up searches for flags. It is a 1:1 mapping, so duplicate designated/named
        * flags will result in us just using the first one. This is okay (for now), because that is the same logic
        * that GetFlag already uses elsewhere.
        */
        public readonly Dictionary<ScriptFlagLookupKey, Script.Flag> flagsByLookupKey;

        public readonly List<CharacterContent> npcs; // list of npcs that are registered in this areascript, used to do some script generation
        public readonly Dictionary<uint, string> entityIdMapping; // used for debuggin, just records a string (usually a record id) as a description for created entity ids

        public BaseScript(ScriptManager manager)
        {
            this.manager = manager;
            AUTO = new(Utility.ResourcePath(@"script\er-common.emedf.json"), true, true);

            emevd = new EMEVD();
            emevd.Compression = Compression.KRAK();
            emevd.Format = EMEVD.Game.Sekiro;
            LinkFiles(emevd, FilesToLink()); // Linked file offsets are stored as bytes of a UTF16 string pointing to other EMEVD scripts which we want to share events with

            init = new EMEVD.Event(0);
            emevd.Events.Add(init);

            flags = new();
            flagsByLookupKey = new();

            npcs = new();
            entityIdMapping = new();
        }

        public void LinkFiles(EMEVD em, string[] files)
        {
            byte[][] fileBytes = new byte[files.Length][];
            for (int i = 0; i < files.Length; i++)
            {
                fileBytes[i] = System.Text.Encoding.Unicode.GetBytes(files[i]);
            }

            List<byte> combined = new();
            foreach (byte[] fb in fileBytes)
            {
                combined.AddRange(fb);
            }

            int offset = 0;
            em.LinkedFileOffsets = new();
            em.StringData = combined.ToArray();
            foreach (byte[] fb in fileBytes)
            {
                em.LinkedFileOffsets.Add(offset);
                offset += fb.Length;
            }
        }

        public abstract string[] FilesToLink();

        public Script.Flag FindFlagByLookupKey(ScriptFlagLookupKey key)
        {
            return flagsByLookupKey.GetValueOrDefault(key);
        }

        public void RegisterNpcHostility(CharacterContent npc)
        {
            GetOrCreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Nibble, Script.Flag.Designation.FriendHitCounter, npc); // setup friendly hit counter
            Script.Flag hostileFlag = GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.Hostile, npc, npc.IsHostile() ? 1u : 0u);
            Script.Flag crimeFlag = GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.CrimeEvent, npc);
            Script.Flag hostileQuipFlag = GetOrCreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.HostileQuip, npc);
            Script.Flag hasBeenAttackedFlag = GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.HasBeenAttacked, npc);
            Script.Flag helloFlag = GetOrCreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.Hello, npc);
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.NpcHostilityHandler]}, {hostileFlag.id}, {npc.entity}, {npc.entity}, {hostileFlag.id}, {npc.entity}, {npc.entity});"));
            npcs.Add(npc);
        }

        /* Dead body */
        public void RegisterDeadNpc(NpcContent npc)
        {
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.DeadBody]}, {npc.entity}, {npc.entity}, {npc.entity});"));
        }

        public void RegisterCharacter(Paramanager paramanager, CharacterContent npc, Script.Flag count)
        {
            Script.Flag deadFlag = GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.Dead, npc);
            Script.Flag disableFlag = manager.GetFlag(Script.Flag.Designation.Disabled, npc);

            if (IsInterior())
            {
                uint cellAreaEntityId = manager.areas[npc.cell];

                // NPC spawn handler for phased npcs
                if (npc is PhasedNpcContent)
                {
                    PhasedNpcContent pnpc = (PhasedNpcContent)npc;
                    Script.Flag phaseFlag = manager.GetFlag(Script.Flag.Designation.Phase, pnpc);
                    List<string> parameters = new()
                    {
                        cellAreaEntityId.ToString(),
                        pnpc.entity.ToString(),
                        deadFlag.id.ToString(),
                        pnpc.entity.ToString(),
                        pnpc.entity.ToString(),
                        disableFlag.id.ToString(),
                        phaseFlag.id.ToString(),
                        phaseFlag.Bits().ToString(),
                        pnpc.phase.ToString(),
                        pnpc.entity.ToString(),
                        pnpc.entity.ToString(),
                        deadFlag.id.ToString(),
                        count.id.ToString(),
                        count.Bits().ToString(),
                        count.MaxValue().ToString()
                    };
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.IntSpawnHandlerPhased]}, {string.Join(", ", parameters)});"));
                }
                // NPC spawn handler for NPCS that can't be disabled
                else if (disableFlag == null)
                {
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.IntSpawnHandler]}, {cellAreaEntityId}, {npc.entity}, {deadFlag.id}, {npc.entity}, {npc.entity}, {deadFlag.id}, {count.id}, {count.Bits()}, {count.MaxValue()});"));
                }
                // NPC spawn handler for NPCS that can be disabled
                else
                {
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.IntSpawnHandlerDisableable]}, {cellAreaEntityId}, {npc.entity}, {deadFlag.id}, {npc.entity}, {disableFlag.id}, {npc.entity}, {npc.entity}, {deadFlag.id}, {count.id}, {count.Bits()}, {count.MaxValue()});"));
                }
            }
            else
            {
                // NPC spawn handler for phased npcs
                if (npc is PhasedNpcContent)
                {
                    PhasedNpcContent pnpc = (PhasedNpcContent)npc;
                    Script.Flag phaseFlag = manager.GetFlag(Script.Flag.Designation.Phase, pnpc);
                    List<string> parameters = new()
                    {
                        deadFlag.id.ToString(),
                        pnpc.entity.ToString(),
                        pnpc.entity.ToString(),
                        disableFlag.id.ToString(),
                        phaseFlag.id.ToString(),
                        phaseFlag.Bits().ToString(),
                        pnpc.phase.ToString(),
                        pnpc.entity.ToString(),
                        pnpc.entity.ToString(),
                        deadFlag.id.ToString(),
                        count.id.ToString(),
                        count.Bits().ToString(),
                        count.MaxValue().ToString()
                    };
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.SpawnHandlerPhased]}, {string.Join(", ", parameters)});"));
                }
                // NPC spawn handler for NPCS that can't be disabled
                else if (disableFlag == null)
                {
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.SpawnHandler]}, {deadFlag.id}, {npc.entity}, {npc.entity}, {deadFlag.id}, {count.id}, {count.Bits()}, {count.MaxValue()});"));
                }
                // NPC spawn handler for NPCS that can be disabled
                else
                {
                    init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.SpawnHandlerDisableable]}, {deadFlag.id}, {npc.entity}, {disableFlag.id}, {npc.entity}, {npc.entity}, {deadFlag.id}, {count.id}, {count.Bits()}, {count.MaxValue()});"));
                }
            }

            if (npc.essential)
            {
                int tutorialPopupId = paramanager.GenerateMessage("", "With this character's death, the thread of prophecy is severed. You are doomed.");
                init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.Essential]}, {deadFlag.id}, {deadFlag.id}, {tutorialPopupId});"));
            }
        }

        public void RegisterHaltEvent(CharacterContent npc)
        {
            Script.Flag deadFlag = manager.GetFlag(Script.Flag.Designation.Dead, npc);
            Script.Flag hostileFlag = manager.GetFlag(Script.Flag.Designation.Hostile, npc);

            List<string> parameters = new()
            {
                deadFlag.id.ToString(),
                npc.entity.ToString(),
                hostileFlag.id.ToString(),
                npc.entity.ToString(),
                npc.entity.ToString(),
                npc.entity.ToString(),
                hostileFlag.id.ToString(),
                npc.entity.ToString(),
                npc.entity.ToString(),
            };
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.Halt]}, {string.Join(", ", parameters)});"));
        }

        /* Register a modStat call here so that it is permanently applied to npc. Flag returned is the trigger for it to be on. */
        public Script.Flag RegisterModStat(uint entityId, int speffId)
        {
            Script.Flag modStatFlag = CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.NpcModStat, $"{entityId}->{speffId}");
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.NpcModStat]}, {modStatFlag.id}, {entityId}, {speffId});"));
            return modStatFlag;
        }

        /* Register NpcInfight event for StartCombat and StopCombat calls */
        public Script.Flag GetOrRegisterInfight(CharacterContent content)
        {
            Script.Flag fightFlag = manager.GetFlag(Script.Flag.Designation.NpcInfight, content);
            if (fightFlag != null) { return fightFlag; } // already exists, return flag

            fightFlag = CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.NpcInfight, content, 0, true);
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.NpcInfight]}, {fightFlag.id}, {content.entity}, {content.entity}, {fightFlag.id}, {content.entity}, {content.entity});"));
            return fightFlag;
        }

        public Script.Flag RegisterStaticDisable(StaticContent content)
        {
            Script.Flag disableFlag = manager.GetFlag(Script.Flag.Designation.Disabled, content);
            if (disableFlag == null) { return null; } // disable flags only get created for objects that have disable calls referencing them.
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.StaticDisable]}, {disableFlag.id}, {content.entity});"));
            return disableFlag;
        }

        /* This cannot be a common event. It seems that calling "initializeevent" from within a common func does not work so... */
        public Script.Flag RegisterTriggerAiPackageSwitch(CharacterContent content, Script.Flag switchEventFlag, Script.Flag packageFlag)
        {
            Script.Flag triggerFlag = CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.TriggerSwitchAiPackage, packageFlag.id.ToString());
            Script.Flag deadFlag = manager.GetFlag(Script.Flag.Designation.Dead, content);

            /* Create an event to trigger and ai package switch from dialog result via a flag */
            Script.Flag triggerEventFlag = CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"TriggerSwitchAiPackage->{packageFlag.id}");
            EMEVD.Event triggerEvent = new(triggerEventFlag.id);

            string[] triggerEventRaw = new string[]
            {
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {deadFlag.id.ToString()});",   // if npc is dead ...
                $"EndUnconditionally(EventEndType.End);",                                              // kill event

                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {triggerFlag.id.ToString()});",     // blocking wait until trigger flag is set on
                $"InitializeEvent(0, {switchEventFlag.id.ToString()}, {packageFlag.id.ToString()});",     // initialize ai package switch event

                $"SetEventFlag(TargetEventFlagType.EventFlag, {triggerFlag.id.ToString()}, OFF);",      // reset trigger flag

                $"EndUnconditionally(EventEndType.Restart);",    // restart event
            };

            for (int i = 0; i < triggerEventRaw.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = AUTO.ParseAddArg(triggerEventRaw[i], i);
                triggerEvent.Parameters.AddRange(newPs);
                triggerEvent.Instructions.Add(instr);
            }

            emevd.Events.Add(triggerEvent);
            init.Instructions.Add(AUTO.ParseAdd($"InitializeEvent(0, {triggerEventFlag.id});"));
            return triggerFlag;
        }

        /* Used by ESD to disable an object via a flag */
        public Script.Flag GetOrRegisterTriggerDisable(Content content)
        {
            Script.Flag triggerDisableFlag = manager.GetFlag(Script.Flag.Designation.TriggerDisable, content);
            if (triggerDisableFlag == null)
            {
                triggerDisableFlag = CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.TriggerDisable, content.id);
                init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.TriggerDisable]}, {triggerDisableFlag.id}, {content.entity}, {content.entity}, {triggerDisableFlag.id});"));
            }
            return triggerDisableFlag;
        }

        /* Used by ESD to enable an object via a flag */
        public Script.Flag GetOrRegisterTriggerEnable(Content content)
        {
            Script.Flag triggerEnableFlag = manager.GetFlag(Script.Flag.Designation.TriggerEnable, content);
            if (triggerEnableFlag == null)
            {
                triggerEnableFlag = CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.TriggerEnable, content.id);
                init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.TriggerEnable]}, {triggerEnableFlag.id}, {content.entity}, {content.entity}, {triggerEnableFlag.id});"));
            }
            return triggerEnableFlag;
        }

        /* Registered from Main.cs in msb generation sections for NPCs and Creatures */
        /* Register a simple event where if an npc dies and they have a flex inventory we award an itemlot to the player with that inv */
        public void RegisterCharacterFlexInventory(Paramanager paramanager, CharacterContent content)
        {
            /* Get characters dead flag */
            Script.Flag charDead = manager.GetFlag(Script.Flag.Designation.Dead, content);
            /* And generate an itemlot for the flex inventory */
            int itemLot = paramanager.GenerateFlexItemLot(this, content);

            /* Register event */
            List<string> parameters = new()
            {
                charDead.id.ToString(),
                charDead.id.ToString(),
                itemLot.ToString()
            };
            init.Instructions.Add(AUTO.ParseAdd($"InitializeCommonEvent(0, {manager.common.events[ScriptCommon.Event.CharacterFlexInventory]}, {string.Join(", ", parameters)});"));
        }

        /* Abstracts supported by only ScriptArea */
        public abstract  (uint bed, uint respawn) RegisterBed();
        public abstract void RegisterLoadDoor(Paramanager paramanager, DoorContent door);
        public abstract void RegisterItemAsset(Paramanager paramanager, ItemContent item);
        public abstract void RegisterContainerAsset(Paramanager paramanager, ContainerContent container, int totalValue);
        public abstract Script.Flag GetOrRegisterPlaySE(uint entity, int seId);

        /* Abstracts supported by both ScriptArea and ScriptCommon */
        public abstract Script.Flag CreateFlagLocal(Content content, string name, uint value = 0);
        public abstract Script.Flag CreateFlag(Script.Flag.Category category, Script.Flag.Type type, Script.Flag.Designation designation, Content content, uint value = 0, bool allowPhased = false);
        public abstract Script.Flag CreateFlag(Script.Flag.Category category, Script.Flag.Type type, Script.Flag.Designation designation, string name, uint value = 0);
        public abstract Script.Flag GetOrCreateFlag(Script.Flag.Category category, Script.Flag.Type type, Script.Flag.Designation designation, Content content, uint value = 0, bool allowPhased = false);
        public abstract Script.Flag GetOrCreateFlag(Script.Flag.Category category, Script.Flag.Type type, Script.Flag.Designation designation, string name, uint value = 0);
        public abstract uint CreateEntity(Script.EntityType type, string name);
        public abstract bool IsInterior();
        public abstract void Write();
    }
}
