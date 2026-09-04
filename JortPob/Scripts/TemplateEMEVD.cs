using JortPob.Common;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JortPob.Scripts
{
    public static class TemplateEMEVD
    {
        private static EMEVD.Event CreateTemplatizedScript(Script.Flag flag, SoulsIds.Events events, params string[] rawScript)
        {
            EMEVD.Event scriptEvent = new(flag.id);

            for (int i = 0; i < rawScript.Length; i++)
            {
                (EMEVD.Instruction instr, List<EMEVD.Parameter> newPs) = events.ParseAddArg(rawScript[i], i);
                scriptEvent.Parameters.AddRange(newPs);
                scriptEvent.Instructions.Add(instr);
            }

            return scriptEvent;
        }

        public static EMEVD.Event CreateSpawnHandlerEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",   // check dead flag
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",
                $"IfCharacterHPValue(MAIN, {getNextParamName()}, 5, 0, 0, 1);", // check if hp is less or equal to 0. comparison values are in byte format so 5 is <= and 4 is >=
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);",  // set dead
                $"IncrementEventValue({getNextParamName()}, {getNextParamName()}, {getNextParamName()});", // count on kill record id flag
                $"EndUnconditionally(EventEndType.End);");
        }

        public static EMEVD.Event CreateLoadDoorEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"IfActionButtonInArea(MAIN, {getNextParamName()}, {getNextParamName()});",
                $"RotateCharacter(10000, {getNextParamName()}, 60000, false);",
                $"WaitFixedTimeSeconds(0.25);",
                $"PlaySE({getNextParamName()}, SoundType.Asset, 200);",
                $"WaitFixedTimeSeconds(0.75);",
                $"WarpPlayer({getNextParamName()}, {getNextParamName()}, {getNextParamName()}, {getNextParamName()}, {getNextParamName()}, -1);",
                $"EndUnconditionally(EventEndType.End);");
        }

        public static EMEVD.Event CreateHaltEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",  // if npc is dead ...
                $"EndUnconditionally(EventEndType.End);",                                           // kill event

                $"IfEntityInoutsideRadiusOfEntity(AND_01, InsideOutsideState.Inside, 10000, {getNextParamName()}, {Const.NPC_HELLO_DIST_IN}, 1);",   // blocking wait distance check for player close enough AND ...
                $"IfEventFlag(AND_01, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",                                                  // ... blocking wait until hostile flag is off
                $"IfConditionGroup(MAIN, PASS, AND_01);",
                $"SetCharacterAIState({getNextParamName()}, Disabled);",                                                                          // disable ai
                $"RotateCharacter({getNextParamName()}, 10000, -1, false)",                                                                      // rotate to face player

                $"IfEntityInoutsideRadiusOfEntity(OR_01, InsideOutsideState.Outside, 10000, {getNextParamName()}, {Const.NPC_HELLO_DIST_OUT}, 1);",  // blocking wait distance check for player far enough OR...
                $"IfEventFlag(OR_01, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",                                                    // ... blocking wait until hostile flag is on
                $"IfConditionGroup(MAIN, PASS, OR_01);",
                $"SetCharacterAIState({getNextParamName()}, Enabled);",                            // enable ai
                $"RequestCharacterAIReplan({getNextParamName()});",                               // make brain work good

                $"EndUnconditionally(EventEndType.Restart);");     // restart event)
        }

        public static EMEVD.Event CreateSpawnHandlerWithDisableEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",   // if dead flag is on disable and end event
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",   // if disable flag is on ...
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",                     // disable character

                $"IfCharacterHPValue(MAIN, {getNextParamName()}, 5, 0, 0, 1);", // check if hp is less or equal to 0. comparison values are in byte format so 5 is <= and 4 is >=
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);",  // set dead
                $"IncrementEventValue({getNextParamName()}, {getNextParamName()}, {getNextParamName()});", // count on kill record id flag
                $"EndUnconditionally(EventEndType.End);");
        }

        public static EMEVD.Event CreateSpawnHandlerPhasedEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",   // if dead flag is on disable and end event
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"ChangeCharacterEnableState({getNextParamName()}, 0);",                                                  // phased character starts disabled
                $"IfEventFlag(AND_01, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",                       // not disabled AND...
                $"IfEventValue(AND_01, {getNextParamName()}, {getNextParamName()}, 0, {getNextParamName()});",        // phase value matches this npcs phase
                $"IfConditionGroup(MAIN, PASS, AND_01);",                                                               // blocking wait...
                $"ChangeCharacterEnableState({getNextParamName()}, 1);",                                              // enable phased character

                $"IfCharacterHPValue(MAIN, {getNextParamName()}, 5, 0, 0, 1);", // check if hp is less or equal to 0. comparison values are in byte format so 5 is <= and 4 is >=
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);",  // set dead
                $"IncrementEventValue({getNextParamName()}, {getNextParamName()}, {getNextParamName()});", // count on kill record id flag
                $"EndUnconditionally(EventEndType.End);");
        }

        public static EMEVD.Event CreateInteriorSpawnHandlerEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfInoutsideArea(2, InsideOutsideState.Inside, 10000, {getNextParamName()}, 1);", // check if inside cell, disable and exit if not
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",   // check dead flag
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"IfCharacterHPValue(MAIN, {getNextParamName()}, 5, 0, 0, 1);", // check if hp is less or equal to 0. comparison values are in byte format so 5 is <= and 4 is >=
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);",  // set dead
                $"IncrementEventValue({getNextParamName()}, {getNextParamName()}, {getNextParamName()});", // count on kill record id flag
                $"EndUnconditionally(EventEndType.End);");
        }

        public static EMEVD.Event CreateInteriorSpawnHandlerWithDisableEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfInoutsideArea(2, InsideOutsideState.Inside, 10000, {getNextParamName()}, 1);", // check if inside cell, disable and exit if not
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",   // if dead flag is on disable and end event
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",   // if disable flag is on ...
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",                     // disable character

                $"IfCharacterHPValue(MAIN, {getNextParamName()}, 5, 0, 0, 1);", // check if hp is less or equal to 0. comparison values are in byte format so 5 is <= and 4 is >=
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);",  // set dead
                $"IncrementEventValue({getNextParamName()}, {getNextParamName()}, {getNextParamName()});", // count on kill record id flag
                $"EndUnconditionally(EventEndType.End);");
        }

        public static EMEVD.Event CreateInteriorSpawnHandlerPhasedEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfInoutsideArea(2, InsideOutsideState.Inside, 10000, {getNextParamName()}, 1);", // check if inside cell, disable and exit if not
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",   // if dead flag is on disable and end event
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",
                $"EndUnconditionally(EventEndType.End);",

                $"ChangeCharacterEnableState({getNextParamName()}, 0);",                                                  // phased character starts disabled
                $"IfEventFlag(AND_01, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",                       // not disabled AND...
                $"IfEventValue(AND_01, {getNextParamName()}, {getNextParamName()}, 0, {getNextParamName()});",        // phase value matches this npcs phase
                $"IfConditionGroup(MAIN, PASS, AND_01);",                                                               // blocking wait...
                $"ChangeCharacterEnableState({getNextParamName()}, 1);",                                              // enable phased character

                $"IfCharacterHPValue(MAIN, {getNextParamName()}, 5, 0, 0, 1);", // check if hp is less or equal to 0. comparison values are in byte format so 5 is <= and 4 is >=
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);",  // set dead
                $"IncrementEventValue({getNextParamName()}, {getNextParamName()}, {getNextParamName()});", // count on kill record id flag
                $"EndUnconditionally(EventEndType.End);");
        }

        public static EMEVD.Event CreateHostileEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",
                $"SetCharacterTeamType({getNextParamName()}, 27);",   // hostile flag on, hostile   >:(     // 27: TeamType.HostileNPC
                $"RequestCharacterAIReplan({getNextParamName()});",  // replan so we realize we are now enemies
                $"IfEventFlag(MAIN, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",
                $"SetCharacterTeamType({getNextParamName()}, 26);",   // hostile flag off, friendly :D       //  26: TeamType.FriendlyNPC
                $"RequestCharacterAIReplan({getNextParamName()});",  // replan so we realize we are now friends
                $"EndUnconditionally(EventEndType.Restart);");    // restart because it's possible for this to happen more than once
        }

        public static EMEVD.Event CreateMessageEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",  // wait for flag to trigger this popup to be set to true
                $"ShowTutorialPopup({getNextParamName()}, true, true);",   // show popup
                $"SetEventFlag(0, {getNextParamName()}, OFF)",              // set flag back to false
                $"EndUnconditionally(EventEndType.Restart);");    // restart so it's ready to go again if needed
        }

        public static EMEVD.Event CreateEssentialEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",    // if npc is already dead...
                $"EndUnconditionally(EventEndType.End);",                                            // end event
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",    // otherwise blocking wait for the dead flag to change
                $"ShowTutorialPopup({getNextParamName()}, true, true);");                          // then let the player know he's fucked
        }

        public static EMEVD.Event CreateDeadBodyEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"ForceAnimationPlayback({getNextParamName()}, 90100, false, false, false, 0, 1);",    // laying on ground dead animation (0 is Equals)
                $"ChangeCharacterCollisionState({getNextParamName()}, Disabled);",    // no-collide
                $"SetCharacterTeamType({getNextParamName()}, 26);");               // friendly npc team = 26
        }

        public static EMEVD.Event CreateItemAssetWithDisableEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",   // if disable flag is on ...
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",                     // disable static
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",
                $"ChangeAssetEnableState({getNextParamName()}, 0);");
        }

        public static EMEVD.Event CreateOwnedItemAssetWithDisableEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",   // if disable flag is on ...
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",                     // disable static

                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",  // if item is already taken
                $"ChangeAssetEnableState({getNextParamName()}, 0);",                              // hide asset
                $"EndUnconditionally(EventEndType.End);",                                      // end event early to preven crime retriggering

                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",    // wait till item picked up
                $"ChangeAssetEnableState({getNextParamName()}, 0);",                               // hide asset
                $"SkipIfEventFlag(3, ON, TargetEventFlagType.EventFlag, {getNextParamName()});", // skip if the owner is dead
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);", // flag this crime as thievery
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);", // flag crime comitted
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);", // flag crime reported notification
                $"EventValueOperation({getNextParamName()}, {getNextParamName()}, {getNextParamName()}, 0, 1, 0);", // add to bounty (last 0 is ADD operation type)
                $"SetSpEffect(10000, {(int)SpeffManager.Functional.Alarming});");           // add alarming speff to player since they did a crime
        }

        public static EMEVD.Event CreateItemAssetEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",
                $"ChangeAssetEnableState({getNextParamName()}, 0);");
        }

        public static EMEVD.Event CreateOwnedItemAssetEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfEventFlag(2, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",  // if item is already taken
                $"ChangeAssetEnableState({getNextParamName()}, 0);",                              // hide asset
                $"EndUnconditionally(EventEndType.End);",                                      // end event early to preven crime retriggering

                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",    // wait till item picked up
                $"ChangeAssetEnableState({getNextParamName()}, 0);",                               // hide asset
                $"SkipIfEventFlag(3, ON, TargetEventFlagType.EventFlag, {getNextParamName()});", // skip if the owner is dead
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);", // flag this crime as thievery
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);", // flag crime comitted
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);", // flag crime reported notification
                $"EventValueOperation({getNextParamName()}, {getNextParamName()}, {getNextParamName()}, 0, 1, 0);", // add to bounty (last 0 is ADD operation type)
                $"SetSpEffect(10000, {(int)SpeffManager.Functional.Alarming});");           // add alarming speff to player since they did a crime
        }

        public static EMEVD.Event CreateOwnedContainerEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",  // if continer is already looted 
                $"EndUnconditionally(EventEndType.End);",                                      // end event early to prevent crime retriggering
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",    // wait till container is looted
                $"SkipIfEventFlag(3, ON, TargetEventFlagType.EventFlag, {getNextParamName()});", // skip if the owner is dead
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);", // flag this crime as thievery
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);", // flag crime comitted
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, ON);", // flag crime reported notification
                $"EventValueOperation({getNextParamName()}, {getNextParamName()}, {getNextParamName()}, 0, 1, 0);"); // add to bounty (last 0 is ADD operation type)
        }

        public static EMEVD.Event CreateTravelWarpEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",
                $"WarpPlayer({getNextParamName()}, {getNextParamName()}, {getNextParamName()}, {getNextParamName()}, {getNextParamName()}, -1);");
        }

        public static EMEVD.Event CreateRemoveItemEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",
                $"RemoveItemFromPlayer({getNextParamName()}, {getNextParamName()}, {getNextParamName()});",
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, OFF);",
                $"EndUnconditionally(EventEndType.Restart);");    // restart so it's ready to go again if needed
        }

        public static EMEVD.Event CreatePermanentSpeffEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"IfEventFlag(AND_01, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",    // if flag is true
                $"IfCharacterHasSpEffect(AND_01, 10000, {getNextParamName()}, false, 0, 1);",        // and player does not have the speff
                $"IfConditionGroup(MAIN, PASS, AND_01);",
                $"SetSpEffect(10000, {getNextParamName()});",   // add speff to player
                $"IfEventFlag(AND_01, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",    // if flag is false
                $"IfCharacterHasSpEffect(AND_01, 10000, {getNextParamName()}, true, 0, 1);",        // and player does have the speff
                $"IfConditionGroup(MAIN, PASS, AND_01);",
                $"ClearSpEffect(10000, {getNextParamName()});",   // remove speff from player
                $"EndUnconditionally(EventEndType.Restart);");    // restart so it's ready to go again if needed
        }

        public static EMEVD.Event CreateNpcInfightEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",
                $"SetCharacterTeamType({getNextParamName()}, 29);",   // hostile flag on, hostile   >:(     // 29: TeamType.Indiscriminate
                $"SetSpEffect({getNextParamName()}, {(int)SpeffManager.Functional.VoidMurder});",
                $"IfEventFlag(MAIN, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",
                $"SetCharacterTeamType({getNextParamName()}, 26);",  // hostile flag off, friendly :D       //  26: TeamType.FriendlyNPC
                $"ClearSpEffect({getNextParamName()}, {(int)SpeffManager.Functional.VoidMurder});",
                $"EndUnconditionally(EventEndType.Restart);");    // restart because it's possible for this to happen more than once
        }

        public static EMEVD.Event CreateNpcModStatEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});", // if flag is on...
                $"SetSpEffect({getNextParamName()}, {getNextParamName()});",                    // apply speff to npc
                $"EndUnconditionally(EventEndType.End);");                                        // and that's all!
        }

        public static EMEVD.Event CreateStaticDisableEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {getNextParamName()});",   // if disable flag is on ...
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);");                    // disable static
        }

        public static EMEVD.Event CreatePlaySEEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",      // if play sound flag is set
                $"PlaySE({getNextParamName()}, {getNextParamName()}, {getNextParamName()});",      // play sound
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, OFF);",          // turn flag back off
                $"EndUnconditionally(EventEndType.Restart);");     // restart!
        }

        public static EMEVD.Event CreateTriggerEnableEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",        // blocking wait until flag set...
                $"ChangeCharacterEnableState({getNextParamName()}, Enabled);",                        // enable object
                $"ChangeAssetEnableState({getNextParamName()}, Enabled);",                           // @TODO: Fuck ass hack. please seperate functions for character/asset
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, OFF);",         // turn flag back off
                $"EndUnconditionally(EventEndType.Restart);");     // restart!
        }

        public static EMEVD.Event CreateTriggerDisableEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",        // blocking wait until flag set...
                $"ChangeCharacterEnableState({getNextParamName()}, Disabled);",                        // disable object
                $"ChangeAssetEnableState({getNextParamName()}, Disabled);",                           // @TODO: Fuck ass hack. please seperate functions for character/asset
                $"SetEventFlag(TargetEventFlagType.EventFlag, {getNextParamName()}, OFF);",         // turn flag back off
                $"EndUnconditionally(EventEndType.Restart);");     // restart!
        }

        public static EMEVD.Event CreateCharacterFlexInventoryEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"EndIfEventFlag(EventEndType.End, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",     // if npc is dead then end event
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {getNextParamName()});",                   // then sit and wait until it is turned on...
                $"AwardItemLot({getNextParamName()});");                                                         // award item lot of flex inventory
        }

        public static EMEVD.Event CreateGetSecondsPassedEvent(Script.Flag flag, SoulsIds.Events events, Func<string> getNextParamName)
        {
            return CreateTemplatizedScript(flag, events,
                $"WaitFixedTimeSeconds(1);", // wait 1 second
                $"EventValueOperation({getNextParamName()}, {getNextParamName()}, 1, 0, 1, 0);", // increment timer by 1
                $"EndUnconditionally(EventEndType.Restart);");     // restart!
        }
    }
}
