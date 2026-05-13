using JortPob.Common;
using SoulsFormats;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;

namespace JortPob.Scripts
{
    /* Handles CommonEvent and CommonFunc EMEVD. These are different from map scripts so I decided to give them a seperate class */

    public class ScriptCommon : BaseScript
    {
        public readonly EMEVD func;

        private Dictionary<Script.Flag.Category, uint> flagUsedCounts = new()
            {
                { Script.Flag.Category.Event, 0 },
                { Script.Flag.Category.Saved, 0 },
                { Script.Flag.Category.Temporary, 0 }
            };
        private Dictionary<Script.EntityType, uint> entityUsedCounts = new()
            {
                { Script.EntityType.Enemy, 0 },
                { Script.EntityType.Asset, 0 },
                { Script.EntityType.Region, 0 },
                { Script.EntityType.Event, 0 },
                { Script.EntityType.Collision, 0 },
                { Script.EntityType.Group, 0 }
            };

        public enum Event
        {
            SpawnHandler, SpawnHandlerDisableable, SpawnHandlerPhased, IntSpawnHandler, IntSpawnHandlerDisableable, IntSpawnHandlerPhased, Halt,
            LoadDoor, NpcHostilityHandler, Message, Essential, DeadBody, 
            ItemAsset, OwnedItemAsset, ItemAssetWithDisable, OwnedItemAssetWithDisable, OwnedContainer, TravelWarp, RemoveItem, PermanentSpeff,
            StaticDisable, PlaySE, TriggerEnable, TriggerDisable, NpcModStat, NpcInfight, GetSecondsPassed
        }
        public readonly Dictionary<Event, uint> events = new();
        public readonly Dictionary<int, Script.Flag> messages = new();  // hash of message text as key, value is flag that when set to true triggers a message to display

        public ScriptCommon(ScriptManager manager) : base(manager)
        {
            // Create a fresh common_func.emevd
            func = new EMEVD();
            func.Compression = Compression.KRAK();
            func.Format = EMEVD.Game.Sekiro;

            // Add preconstructor with a few specific calls from the base game common
            EMEVD.Event precon = new(50);
            precon.Instructions.Add(AUTO.ParseAdd("SetEventFlag(TargetEventFlagType.EventFlag, 6000, OFF);"));
            precon.Instructions.Add(AUTO.ParseAdd("SetEventFlag(TargetEventFlagType.EventFlag, 6001, ON);"));
            precon.Instructions.Add(AUTO.ParseAdd("SetEventFlag(TargetEventFlagType.EventFlag, 9000, OFF);"));
            precon.Instructions.Add(AUTO.ParseAdd("SetEventFlag(TargetEventFlagType.EventFlag, 9001, OFF);"));
            precon.Instructions.Add(AUTO.ParseAdd("SetEventFlag(TargetEventFlagType.EventFlag, 280, OFF);"));
            precon.Instructions.Add(AUTO.ParseAdd("SetEventFlag(TargetEventFlagType.EventFlag, 909, OFF);"));
            emevd.Events.Add(precon);

            // Add the vanilla event 1020 for BGM to work correctly
            EMEVD source = EMEVD.Read(Path.Combine(Const.ELDEN_PATH, @"game\event\common.emevd.dcx"));
            EMEVD.Event source1020 = source.Events.First(e => e.ID == 1020);
            emevd.Events.Add(source1020);
            init.Instructions.Add(AUTO.ParseAdd("InitializeEvent(0, 1020);"));

            RegisterAllTemplateScripts();
        }

        private void RegisterTemplateEvent(Event eventId, string name, Func<Script.Flag, SoulsIds.Events, Func<string>, EMEVD.Event> createEventFunc)
        {
            var pc = 0;
            var flag = CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, name);
            var @event = createEventFunc(flag, AUTO, () => $"X{pc++ * 4}_4");
            func.Events.Add(@event);
            events.Add(eventId, flag.id);
        }

        private void RegisterAllTemplateScripts()
        {
            /* Create an event for handling creature/npc spawn/respawn*/
            RegisterTemplateEvent(Event.SpawnHandler, "CommonFunc:SpawnHandler", TemplateEMEVD.CreateSpawnHandlerEvent);

            /* Create an event for going through load doors */
            RegisterTemplateEvent(Event.LoadDoor, "CommonFunc:DoorLoad", TemplateEMEVD.CreateLoadDoorEvent);

            /* Create an event for handling npc halting */
            RegisterTemplateEvent(Event.Halt, "CommonFunc:Halt", TemplateEMEVD.CreateHaltEvent);

            /* Create an event for handling creature/npc spawn/respawn and disable/enable */
            RegisterTemplateEvent(Event.SpawnHandlerDisableable, "CommonFunc:SpawnHandlerDisableable", TemplateEMEVD.CreateSpawnHandlerWithDisableEvent);

            /* Create an event for handling phased creature/npc spawn/respawn and disable/enable */
            RegisterTemplateEvent(Event.SpawnHandlerPhased, "CommonFunc:SpawnHandlerPhased", TemplateEMEVD.CreateSpawnHandlerPhasedEvent);

            /* Create an event for handling creature/npc spawn/respawn in interiors */
            RegisterTemplateEvent(Event.IntSpawnHandler, "CommonFunc:IntSpawnHandler", TemplateEMEVD.CreateInteriorSpawnHandlerEvent);

            /* Create an event for handling creature/npc spawn/respawn and disable/enable */
            RegisterTemplateEvent(Event.IntSpawnHandlerDisableable, "CommonFunc:IntSpawnHandlerDisableable", TemplateEMEVD.CreateInteriorSpawnHandlerWithDisableEvent);

            /* Create an event for handling phased creature/npc spawn/respawn and disable/enable */
            RegisterTemplateEvent(Event.IntSpawnHandlerPhased, "CommonFunc:IntSpawnHandlerPhased", TemplateEMEVD.CreateInteriorSpawnHandlerPhasedEvent);

            /* Create an event for handling friendly npc hostility */
            RegisterTemplateEvent(Event.NpcHostilityHandler, "CommonFunc:NpcHostilityHandler", TemplateEMEVD.CreateHostileEvent);

            /* Create an event for handling messages */
            RegisterTemplateEvent(Event.Message, "CommonFunc:Message", TemplateEMEVD.CreateMessageEvent);

            /* Create an event for displaying a message when the player kills an essential npc */
            RegisterTemplateEvent(Event.Essential, "CommonFunc:Essential", TemplateEMEVD.CreateEssentialEvent);

            /* Create an event for intitializing dead bodys. To be specific, any NPC in morrowind that has the "dead" flag and is just a lootable body */
            RegisterTemplateEvent(Event.DeadBody, "CommonFunc:DeadBody", TemplateEMEVD.CreateDeadBodyEvent);

            /* Create an event for making itemcontent assets placed on the map dissapear when the item is actually taken by the player */
            RegisterTemplateEvent(Event.ItemAssetWithDisable, "CommonFunc:ItemAssetWithDisable", TemplateEMEVD.CreateItemAssetWithDisableEvent);

            /* Same as above but also triggers a crime on the player when the item is taken */
            RegisterTemplateEvent(Event.OwnedItemAssetWithDisable, "CommonFunc:OwnedItemAssetWithDisable", TemplateEMEVD.CreateOwnedItemAssetWithDisableEvent);

            /* Create an event for making itemcontent assets placed on the map dissapear when the item is actually taken by the player */
            RegisterTemplateEvent(Event.ItemAsset, "CommonFunc:ItemAsset", TemplateEMEVD.CreateItemAssetEvent);

            /* Same as above but also triggers a crime on the player when the item is taken */
            RegisterTemplateEvent(Event.OwnedItemAsset, "CommonFunc:OwnedItemAsset", TemplateEMEVD.CreateOwnedItemAssetEvent);

            /* Same as above but for containers instead of items */
            RegisterTemplateEvent(Event.OwnedContainer, "CommonFunc:OwnedContainer", TemplateEMEVD.CreateOwnedContainerEvent);

            /* Create an event for travel npc warps */
            RegisterTemplateEvent(Event.TravelWarp, "CommonFunc:TravelWarp", TemplateEMEVD.CreateTravelWarpEvent);

            /* Create an event for removing an item from the player (you cant do this from ESD so a trigger event like this is fine */
            RegisterTemplateEvent(Event.RemoveItem, "CommonFunc:RemoveItem", TemplateEMEVD.CreateRemoveItemEvent);

            /* Create an event for handling a permanent speff on the player */
            RegisterTemplateEvent(Event.PermanentSpeff, "CommonFunc:PermanentSpeff", TemplateEMEVD.CreatePermanentSpeffEvent);

            /* Create an event for handling startcombat and stopcombat calls */
            RegisterTemplateEvent(Event.NpcInfight, "CommonFunc:NpcInfight", TemplateEMEVD.CreateNpcInfightEvent);

            /* Create an event for handling a permanent mod stat on an npcs */
            RegisterTemplateEvent(Event.NpcModStat, "CommonFunc:NpcModStat", TemplateEMEVD.CreateNpcModStatEvent);

            /* Create an event for handling disable/enable of statics */
            RegisterTemplateEvent(Event.StaticDisable, "CommonFunc:StaticDisable", TemplateEMEVD.CreateStaticDisableEvent);

            /* Create an event for playing an SE. Used by ESD to trigger sound effects */
            RegisterTemplateEvent(Event.PlaySE, "CommonFunc:PlaySE", TemplateEMEVD.CreatePlaySEEvent);

            /* Create an event for esd to trigger an object enable*/
            RegisterTemplateEvent(Event.TriggerEnable, "CommonFunc:TriggerEnable", TemplateEMEVD.CreateTriggerEnableEvent);

            /* Create an event for esd to trigger an object disable*/
            RegisterTemplateEvent(Event.TriggerDisable, "CommonFunc:TriggerDisable", TemplateEMEVD.CreateTriggerDisableEvent);

            /* Create event for emulating the GetSecondsPassed papyrus call */
            RegisterTemplateEvent(Event.GetSecondsPassed, "CommonFunc:GetSecondsPassed", TemplateEMEVD.CreateGetSecondsPassedEvent);
        }

        public override string[] FilesToLink()
        {
            return new string[]
            {
                @"N:\GR\data\Param\event\common_func.emevd" + "\0",
                @"N:\GR\data\Param\event\common_macro.emevd" + "\0"
            };
        }

        /* Register a tutorial popup message with given text */
        /* Returns a flag that when set to true shows the message */
        /* Stores a mapping of texthashes to prevent duplicates. */
        public Script.Flag GetOrRegisterMessage(Paramanager paramanager, string title, string text)
        {
            int textHash = (title+text).GetHashCode();
            if (messages.ContainsKey(textHash)) { return messages[textHash]; }

            Script.Flag messageFlag = CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.Message, text);
            int param = paramanager.GenerateMessage(title, text);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeCommonEvent(0, {events[Event.Message]}, {messageFlag.id}, {param}, {messageFlag.id});"));
            messages.Add(textHash, messageFlag);
            return messageFlag;
        }

        /* Register a right side of the screen non pausing message with given text */
        /* Returns a flag that when set to true shows the notification */
        /* Stores a mapping of texthashes to prevent duplicates. */
        public Script.Flag GetOrRegisterNotification(Paramanager paramanager, string text)
        {
            int textHash = text.GetHashCode();
            if (messages.ContainsKey(textHash)) { return messages[textHash]; }

            Script.Flag messageFlag = CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.Message, text);
            int param = paramanager.GenerateNotification(text);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeCommonEvent(0, {events[Event.Message]}, {messageFlag.id}, {param}, {messageFlag.id});"));
            messages.Add(textHash, messageFlag);
            return messageFlag;
        }

        /* Create an event for travel npcs to warp the player to a specific location. Returns the flag that when set to ON will trigger this event */
        public Script.Flag GetOrRegisterTravelWarp(CharacterContent.Travel travel)
        {
            string flagName = $"{travel.name}:{(int)travel.position.X},{(int)travel.position.X}";
            Script.Flag warpToFlag = manager.GetFlag(Script.Flag.Designation.TravelWarp, flagName);
            if (warpToFlag != null) { return warpToFlag; }

            warpToFlag = CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.TravelWarp, flagName);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeCommonEvent(0, {events[Event.TravelWarp]}, {warpToFlag.id}, {travel.map}, {travel.x}, {travel.y}, {travel.block}, {travel.entity});"));
            return warpToFlag;
        }

        /* Create an event for removing an item from the player */
        public Script.Flag GetOrRegisterRemoveItem(ItemManager.ItemInfo itemInfo, int quantity)
        {
            string flagName = $"{itemInfo.type}:{itemInfo.row}:{quantity}";
            Script.Flag removeItemFlag = manager.GetFlag(Script.Flag.Designation.RemoveItem, flagName);
            if (removeItemFlag != null) { return removeItemFlag; }

            removeItemFlag = CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Bit, Script.Flag.Designation.RemoveItem, flagName);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeCommonEvent(0, {events[Event.RemoveItem]}, {removeItemFlag.id}, {(int)itemInfo.type}, {itemInfo.row}, {quantity}, {removeItemFlag.id});"));
            return removeItemFlag;
        }

        /* Handler that maintains a permanent SPEFF on the player. Used for things that persist like Diseases or Abilities */
        public Script.Flag CreatePermanentSpeff(SpeffManager.SpeffSpell spell)
        {
            Script.Flag speffFlag = CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Bit, Script.Flag.Designation.PermanentSpeff, spell.id);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeCommonEvent(0, {events[Event.PermanentSpeff]}, {speffFlag.id}, {spell.row}, {spell.row}, {speffFlag.id}, {spell.row}, {spell.row});"));
            return speffFlag;
        }

        /* Return a Random papyrus call handler */
        public Script.Flag GetOrRegisterRandom(int max)
        {
            Script.Flag randomFlag = manager.GetFlag(Script.Flag.Designation.Random, max.ToString());
            if (randomFlag != null) { return randomFlag; }
            randomFlag = CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Short, Script.Flag.Designation.Random, max.ToString());

            EMEVD.Event randomEvent = new();
            Script.Flag randomEventFlag = CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"RandomHandlerEvent");
            randomEvent.ID = randomEventFlag.id;

            List<int> randomValues = Enumerable.Range(0, max).ToList();
            randomValues.Shuffle();

            foreach (int i in randomValues) {
                randomEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({randomFlag.id}, {randomFlag.Bits()}, {i}, 0, 1, 5);")); // assign random value to flag
                randomEvent.Instructions.Add(AUTO.ParseAdd($"WaitFixedTimeFrames(1);"));  // wait 1 frame then repeat
            }
            randomEvent.Instructions.Add(AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);"));  // restart

            emevd.Events.Add(randomEvent);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeEvent(0, {randomEvent.ID}, 0);"));

            return randomFlag;
        }

        /* Create a fixed common event that handles the players ability to use the crafting menu based on what alchemy equipment they have */
        public void CreateAlchemyHandler(List<ItemManager.ItemInfo> items)
        {
            EMEVD.Event alchemyEvent = new();
            Script.Flag alchemyEventFlag = CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"AlchemyHandlerEvent");
            alchemyEvent.ID = alchemyEventFlag.id;

            alchemyEvent.Instructions.Add(AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, 60120, OFF);"));  // initialize as crafting disabled

            foreach(ItemManager.ItemInfo item in items)
            {
                alchemyEvent.Instructions.Add(AUTO.ParseAdd($"IfPlayerHasdoesntHaveItem(OR_01, ItemType.Goods, {item.row}, OwnershipState.Owns);"));  // does player have alchemy tool
                alchemyEvent.Instructions.Add(AUTO.ParseAdd($"SkipIfConditionGroupStateUncompiled(1, FAIL, OR_01);"));
                alchemyEvent.Instructions.Add(AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, 60120, ON);"));                    // if they do then enable crafting
                alchemyEvent.Instructions.Add(AUTO.ParseAdd($"IfElapsedSeconds(MAIN, 0);"));                                                 // reset condition group
            }

            alchemyEvent.Instructions.Add(AUTO.ParseAdd($"WaitFixedTimeSeconds(3);"));  // only do this check every few seconds as its not high priority
            alchemyEvent.Instructions.Add(AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);"));  // restart

            emevd.Events.Add(alchemyEvent);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeEvent(0, {alchemyEvent.ID}, 0);"));
        }

        /* Create time handler. These events track minutes, seconds, days, months, and years */
        /* In order to simplify coding I'm making all months have 30 days */
        public void TimeHandler()
        {
            Script.Flag hour = GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Short, Script.Flag.Designation.Global, "GameHour");
            Script.Flag day = GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Short, Script.Flag.Designation.Global, "Day");
            Script.Flag month = GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Short, Script.Flag.Designation.Global, "Month");
            Script.Flag year = GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Short, Script.Flag.Designation.Global, "Year");
            Script.Flag daysPassedFlag = GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Short, Script.Flag.Designation.Global, "DaysPassed");

            /* Hour handler */
            EMEVD.Event hourEvent = new();
            Script.Flag hourEventFlag = CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"TimeHourEvent");
            hourEvent.ID = hourEventFlag.id;

            for (int i = 0; i < 24; i++)
            {
                hourEvent.Instructions.Add(AUTO.ParseAdd($"IfElapsedSeconds(MAIN, 0);"));                                                 // reset condition group
                hourEvent.Instructions.Add(AUTO.ParseAdd($"IfTimeOfDayInRange(OR_01, {i}, 0, 0, {i}, 59, 59);"));                         // check a time range...
                hourEvent.Instructions.Add(AUTO.ParseAdd($"SkipIfConditionGroupStateUncompiled(1, FAIL, OR_01);"));
                hourEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({hour.id}, {hour.Bits()}, {i}, 0, 1, 5);"));               // set gamehour global value
            }

            hourEvent.Instructions.Add(AUTO.ParseAdd($"WaitFixedTimeSeconds(1);"));  // update once a second
            hourEvent.Instructions.Add(AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);"));  // restart

            emevd.Events.Add(hourEvent);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeEvent(0, {hourEvent.ID}, 0);"));

            /* Day, month, year handler */
            EMEVD.Event dateEvent = new();
            Script.Flag dateEventFlag = CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"TimeDateEvent");
            dateEvent.ID = dateEventFlag.id;

            dateEvent.Instructions.Add(AUTO.ParseAdd($"IfTimeOfDayInRange(MAIN, 12, 0, 0, 23, 59, 59);"));  // if we are in the latter half of a day
            dateEvent.Instructions.Add(AUTO.ParseAdd($"IfTimeOfDayInRange(MAIN, 0, 0, 0, 11, 59, 59);"));   // and the clock rolls over to 0 (12:00AM~)  // Both of these are blocking waits
            dateEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({day.id}, {day.Bits()}, 1, 0, 1, 0);"));  // a day has passed
            dateEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({daysPassedFlag.id}, {daysPassedFlag.Bits()}, 1, 0, 1, 0);"));

            dateEvent.Instructions.Add(AUTO.ParseAdd($"IfEventValue(OR_01, {day.id}, {day.Bits()}, 2, 29);"));   // if the day is the 30th
            dateEvent.Instructions.Add(AUTO.ParseAdd($"SkipIfConditionGroupStateUncompiled(2, FAIL, OR_01);"));
            dateEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({month.id}, {month.Bits()}, 1, 0, 1, 0);"));  // a month has passed
            dateEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({day.id}, {day.Bits()}, 0, 0, 1, 5);"));  // the day is 0 again

            dateEvent.Instructions.Add(AUTO.ParseAdd($"IfEventValue(OR_02, {month.id}, {month.Bits()}, 2, 11);"));   // if the month is the 12th
            dateEvent.Instructions.Add(AUTO.ParseAdd($"SkipIfConditionGroupStateUncompiled(2, FAIL, OR_02);"));
            dateEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({year.id}, {year.Bits()}, 1, 0, 1, 0);"));  // a year has passed
            dateEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({month.id}, {month.Bits()}, 0, 0, 1, 5);"));  // the month is 0 again

            dateEvent.Instructions.Add(AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);"));  // restart

            emevd.Events.Add(dateEvent);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeEvent(0, {dateEvent.ID}, 0);"));
        }

        /* Create a simple common event that tracks the current weather and writes it to a flag for dialog filter conditions to read from */
        public enum WeatherEMEVD
        {
            None = -1, Default = 0, Rain = 1, Snow = 2, WindyRain = 3, Fog = 4, Cloudless = 5, FlatClouds = 6, PuffyClouds = 7, RainyClouds = 8, WindyFog = 9, HeavySnow = 10,
            HeavyFog = 11, WindyPuffyClouds = 12, Default2 = 13, Default3 = 14, RainyHeavyFog = 15, SnowyHeavyFog = 16, ScatteredRain = 17, Unknown18 = 18, Unknown19 = 19,
            Unknown20 = 20, Unknown21 = 21, Unknown22 = 22, Unknown23 = 23
        }

        public enum WeatherPapyrus
        {
            Clear = 0, Cloudy = 1, Foggy = 2, Overcast = 3, Rain = 4, Thunder = 5, Ash = 6, Blight = 7, Snow = 8, Blizzard = 9
        }

        public void CreateWeatherTracker()
        {
            EMEVD.Event weatherEvent = new();
            Script.Flag weatherEventFlag = CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"WeatherTracker");
            weatherEvent.ID = weatherEventFlag.id;

            Script.Flag weatherValue = CreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Byte, Script.Flag.Designation.CurrentWeather, "CurrentWeather");

            List<(WeatherEMEVD emevd, WeatherPapyrus papyrus)> weatherRemaps = [
                (WeatherEMEVD.None, WeatherPapyrus.Clear),
                (WeatherEMEVD.Default, WeatherPapyrus.Clear),
                (WeatherEMEVD.Rain, WeatherPapyrus.Rain),
                (WeatherEMEVD.Snow, WeatherPapyrus.Snow),
                (WeatherEMEVD.WindyRain, WeatherPapyrus.Thunder),
                (WeatherEMEVD.Fog, WeatherPapyrus.Foggy),
                (WeatherEMEVD.Cloudless, WeatherPapyrus.Clear),
                (WeatherEMEVD.FlatClouds, WeatherPapyrus.Overcast),
                (WeatherEMEVD.PuffyClouds, WeatherPapyrus.Cloudy),
                (WeatherEMEVD.RainyClouds, WeatherPapyrus.Rain),
                (WeatherEMEVD.WindyFog, WeatherPapyrus.Foggy),
                (WeatherEMEVD.HeavySnow, WeatherPapyrus.Blizzard),
                (WeatherEMEVD.HeavyFog, WeatherPapyrus.Foggy),
                (WeatherEMEVD.WindyPuffyClouds, WeatherPapyrus.Cloudy),
                (WeatherEMEVD.Default2, WeatherPapyrus.Clear),
                (WeatherEMEVD.Default3, WeatherPapyrus.Clear),
                (WeatherEMEVD.RainyHeavyFog, WeatherPapyrus.Thunder),
                (WeatherEMEVD.SnowyHeavyFog, WeatherPapyrus.Blizzard),
                (WeatherEMEVD.ScatteredRain, WeatherPapyrus.Rain),
                (WeatherEMEVD.Unknown18, WeatherPapyrus.Ash),
                (WeatherEMEVD.Unknown19, WeatherPapyrus.Blight),
                (WeatherEMEVD.Unknown20, WeatherPapyrus.Clear),
                (WeatherEMEVD.Unknown21, WeatherPapyrus.Clear),
                (WeatherEMEVD.Unknown22, WeatherPapyrus.Clear),
                (WeatherEMEVD.Unknown23, WeatherPapyrus.Clear),
            ];

            foreach(var remap in weatherRemaps)
            {
                weatherEvent.Instructions.Add(AUTO.ParseAdd($"IfWeatherActive(OR_01, {(int)remap.emevd}, 0, 0);"));         // if weather is active
                weatherEvent.Instructions.Add(AUTO.ParseAdd($"SkipIfConditionGroupStateUncompiled(1, FAIL, OR_01);"));
                weatherEvent.Instructions.Add(AUTO.ParseAdd($"EventValueOperation({weatherValue.id}, {weatherValue.Bits()}, {(int)remap.papyrus}, 0, 1, 5);"));  // set flag value for that weather
                weatherEvent.Instructions.Add(AUTO.ParseAdd($"IfElapsedSeconds(MAIN, 0);"));                                                 // reset condition group
            }

            weatherEvent.Instructions.Add(AUTO.ParseAdd($"WaitFixedTimeSeconds(10);"));  // only do this check every few seconds as its not high priority
            weatherEvent.Instructions.Add(AUTO.ParseAdd($"EndUnconditionally(EventEndType.Restart);"));  // restart

            emevd.Events.Add(weatherEvent);
            init.Instructions.Insert(0, AUTO.ParseAdd($"InitializeEvent(0, {weatherEvent.ID}, 0);"));
        }


        public override Script.Flag GetOrCreateFlag(Script.Flag.Category category, Script.Flag.Type type, Script.Flag.Designation designation, Content content, uint value = 0, bool allowPhased = false)
        {
            Script.Flag flag = manager.GetFlag(designation, content);
            if (flag != null) { return flag; }
            return CreateFlag(category, type, designation, content, value, allowPhased);
        }

        public override Script.Flag GetOrCreateFlag(Script.Flag.Category category, Script.Flag.Type type, Script.Flag.Designation designation, string name, uint value = 0)
        {
            Script.Flag flag = manager.GetFlag(designation, name);
            if (flag != null) { return flag; }
            return CreateFlag(category, type, designation, name, value);
        }

        /* There are some bugs with this system. It defo wastes some flag space. We have lots tho. Maybe fix later */
        private static readonly uint[] COMMON_FLAG_BASES = new uint[]  // using flags from every msb slot along the bottom most edge of the world
        {
            1030290000, 1031290000, 1032290000, 1033290000, 1034290000, 1035290000, 1036290000, 1037290000, 1038290000, 1039290000 // if we run out of flag space it will throw an exception. adding more is easy tho
        };
        private static readonly Dictionary<Script.Flag.Category, uint[]> FLAG_TYPE_OFFSETS = new()
        {
            { Script.Flag.Category.Event, new uint[] { 1000, 3000, 6000 } },
            { Script.Flag.Category.Saved, new uint[] { 0, 4000, 7000, 8000, 9000 } },
            { Script.Flag.Category.Temporary, new uint[] { 2000, 5000 } }
        };

        public override Script.Flag CreateFlag(Script.Flag.Category category, Script.Flag.Type type, Script.Flag.Designation designation, Content content, uint value = 0, bool allowPhased = false)
        {
            if (content is PhasedNpcContent && !allowPhased) { throw new Exception("Cannot create flags for phased content in this manner! See CreateFlagLocal or use allowePhased if you are certain it's okay."); }
            else if (content is PhasedNpcContent) { return CreateFlag(category, type, designation, manager.routing[(PhasedNpcContent)content], value); }
            return CreateFlag(category, type, designation, content.entity.ToString(), value);
        }

        public override Script.Flag CreateFlagLocal(Content content, string name, uint value = 0)
        {
            if (content is PhasedNpcContent)
            {
                PhasedNpcContent pnpc = (PhasedNpcContent)content;
                return GetOrCreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Short, Script.Flag.Designation.Local, $"{manager.routing[pnpc]}.{name}", value); // this is one of the few places where a phased npc creates new flags
            }
            return CreateFlag(Script.Flag.Category.Saved, Script.Flag.Type.Short, Script.Flag.Designation.Local, $"{content.entity.ToString()}.{name}", value);
        }

        public override Script.Flag CreateFlag(Script.Flag.Category category, Script.Flag.Type type, Script.Flag.Designation designation, string name, uint value = 0)
        {
            /* Cap off a group of 1000 flags if it's near full. For example: This is to prevent us adding a multi bit flag like a byte when there is only 3 flags left */
            uint rawCount = flagUsedCounts[category];
            if (rawCount % 1000 + (uint)type >= 1000)
            {
                flagUsedCounts[category] += 1000 - rawCount % 1000;
                rawCount = flagUsedCounts[category];
            }

            /* Calculate next flag */
            uint perThou = rawCount / 1000 % (uint)FLAG_TYPE_OFFSETS[category].Length;
            uint perMsb = rawCount / 1000 / (uint)FLAG_TYPE_OFFSETS[category].Length;
            uint mod = rawCount % 1000;
            uint mapOffset = COMMON_FLAG_BASES[perMsb];
            uint id = mapOffset + FLAG_TYPE_OFFSETS[category][perThou] + mod;
            flagUsedCounts[category] += (uint)type;

            // Check for a collision with a common event flag, if we find a collision we recursviely try making another flag
            if (ScriptManager.DO_NOT_USE_FLAGS.Contains(id))
            {
                Lort.Log($" ## WARNING ## Script.Flag collision with commonevent found: {id}", Lort.Type.Debug);
                return CreateFlag(category, type, designation, name, value);
            }

            Script.Flag flag = new(category, type, designation, name, id, value);
            flags.Add(flag);
            flagsByLookupKey.TryAdd(Script.GetLookupKeyForFlag(flag), flag);
            return flag;
        }

        /* Create a unique entity id, this is primarily used as an overflow for other msbs when they run out of room. */
        public override uint CreateEntity(Script.EntityType type, string name)
        {
            uint rawCount = entityUsedCounts[type]++;
            uint newid = COMMON_FLAG_BASES[rawCount / 1000] + (uint)type + rawCount;

            return newid;
        }

        // script common is only ever used for exteriors in the case of msb promoted npcs
        public override bool IsInterior()
        {
            return false;
        }

        public override void Write()
        {
            emevd.Write(Path.Combine(Const.OUTPUT_PATH, "event", "common.emevd.dcx"));
            func.Write(Path.Combine(Const.OUTPUT_PATH, "event", "common_func.emevd.dcx"));
        }

        /* Abstracts scripts that ScriptCommon does not support */
        public override (uint bed, uint respawn) RegisterBed() { throw new NotImplementedException(); }
        public override void RegisterLoadDoor(Paramanager paramanager, DoorContent door) { throw new NotImplementedException(); }
        public override void RegisterItemAsset(Paramanager paramanager, ItemContent item) { throw new NotImplementedException(); }
        public override void RegisterContainerAsset(Paramanager paramanager, ContainerContent container, int totalValue) { throw new NotImplementedException(); }
        public override Script.Flag GetOrRegisterPlaySE(uint entity, int seId) { throw new NotImplementedException(); }
    }
}
