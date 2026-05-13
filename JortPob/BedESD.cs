using JortPob.Scripts;
using System.IO;

namespace JortPob
{
    public class BedESD
    {
        private readonly string def;

        /* generate an esd script for beds */
        public BedESD(Layout layout, ScriptManager scriptManager, Paramanager paramanager, TextManager textManager, BedContent content, int id) {
            int interactAction = paramanager.GenerateActionButtonInteractParam(content, "Use Bed");
            int restAction = textManager.GetTopic("Rest");
            int longRestAction = textManager.GetTopic("Long Rest");
            int storageAction = textManager.GetTopic("Organize Storage");

            Script.Flag dayFlag = scriptManager.GetFlag(Script.Flag.Designation.Global, "day");
            Script.Flag daysPassedFlag = scriptManager.GetFlag(Script.Flag.Designation.Global, "dayspassed");
            Script.Flag messageFlag = scriptManager.common.GetOrRegisterNotification(paramanager, "This bed is owned by someone else!");
            Script.Flag resetFlag = scriptManager.GetFlag(Script.Flag.Designation.ResetHostility, "ResetHostility");

            /* Find owner if exists */
            Script.Flag ownerFlag;
            if (content.ownerNpc != null)
            {
                Content owner = layout.FindScriptReference(content, content.ownerNpc);
                if(owner != null) { ownerFlag = scriptManager.GetFlag(Script.Flag.Designation.Dead, owner); }
                else { ownerFlag = null; }
            }
            else { ownerFlag = null; }

            /* Find faction owner if exists */
            Script.Flag factionFlag;
            if(content.ownerFaction != null)
            {
                factionFlag = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, content.ownerFaction.ToLower().Trim());
            }
            else { factionFlag = null; }

            /* Find global var owner if exists */
            Script.Flag globalFlag;
            if (content.ownerGlobal != null)
            {
                globalFlag = scriptManager.GetFlag(Script.Flag.Designation.Global, content.ownerGlobal.ToLower().Trim());
            }
            else { globalFlag = null; }

            /* Generate snippet for interaction */
            string interact;

            
            // A global var (inn) owns this bed
            if (globalFlag != null)
            {
                interact = $""""
                                        if GetEventFlag({globalFlag.id}):
                                            break
                                        else:
                                            SetEventFlag({messageFlag.id}, FlagState.On)
                                            pass
                            """";
            }
            // A faction and a npc both own this bed (edge case idk if this happens)
            else if (ownerFlag != null && factionFlag != null)
            {
                interact = $""""
                                        if GetEventFlag({factionFlag.id}) and GetEventFlag({ownerFlag.id}):
                                            break
                                        else:
                                            SetEventFlag({messageFlag.id}, FlagState.On)
                                            pass
                            """";
            }
            // An NPC owns this bed
            else if (ownerFlag != null)
            {
                interact = $""""
                                        if GetEventFlag({ownerFlag.id}):
                                            break
                                        else:
                                            SetEventFlag({messageFlag.id}, FlagState.On)
                                            pass
                            """";
            }
            // A faction owns this bed
            else if (factionFlag != null)
            {
                interact = $""""
                                        if GetEventFlag({factionFlag.id}):
                                            break
                                        else:
                                            SetEventFlag({messageFlag.id}, FlagState.On)
                                            pass
                            """";
            }
            // No owner for this bed
            else
            {
                interact = $""""
                                        break
                            """";
            }

            /* Create ESD code */
            def = $""""
             # -*- coding: utf-8 -*-
             # Init
             def t{id:D9}_1():
                 """State 0,1"""
                 t{id:D9}_x1()
                 Quit()

             # Await Interaction
             def t{id:D9}_x0():
                 """State 0"""
                 while True:
                     assert not GetOneLineHelpStatus() and not IsClientPlayer() and not IsPlayerDead() and not IsCharacterDisabled()
                     if (not (not GetOneLineHelpStatus() and not IsClientPlayer() and not IsPlayerDead() and not IsCharacterDisabled())):
                         pass
                     # actionbutton:{interactAction}:"Use Bed"
                     elif CheckActionButtonArea({interactAction}):
             {interact}
                 return 0

             # Main Loop
             def t{id:D9}_x1():
                 while True:
                     assert t{id:D9}_x0()
                     assert t{id:D9}_x2() or GetDistanceToPlayer() >= 2.25
                     assert t{id:D9}_x10()
                     assert GetCurrentStateElapsedTime() > 1
                 return 0

             # Bed Menu
             def t{id:D9}_x2():
                 ClearPreviousMenuSelection()
                 while True:
                     ClearTalkListData()
                     # action:{restAction}:"Rest"
                     AddTalkListData(1, {restAction}, -1)
                     # action:{longRestAction}:"Long Rest"
                     AddTalkListData(2, {longRestAction}, -1)
                     # action:15000540:"Level Up"
                     AddTalkListData(3, 15000540, -1)
                     # action:{storageAction}:"Organize Storage"
                     AddTalkListData(4, {storageAction}, -1)
                     # action:20000009:"Leave"
                     AddTalkListData(99, 20000009, -1)

                     SetCanOpenMap(True)
                     ShowShopMessage(TalkOptionsType.Regular)
                     assert not (CheckSpecificPersonMenuIsOpen(1, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))

                     # 1 - "Rest"
                     if GetTalkListEntryResult() == 1:
                         SetCanOpenMap(False)
                         ClearPreviousMenuSelection()
                         def ExitPause():
                             ClearPreviousMenuSelection()
                         assert t{id:D9}_x3()
                     # 2 - "Long Rest"
                     elif GetTalkListEntryResult() == 2:
                         SetCanOpenMap(False)
                         ClearPreviousMenuSelection()
                         def ExitPause():
                             ClearPreviousMenuSelection()
                         def ExitPause():
                             HideClock(0.5)
                         assert t{id:D9}_x45(val1=0)
                         SetEventFlagValue({dayFlag.id}, {dayFlag.Bits()}, GetEventFlagValue({dayFlag.id}, {dayFlag.Bits()}) + 5 )
                         SetEventFlagValue({daysPassedFlag.id}, {daysPassedFlag.Bits()}, GetEventFlagValue({daysPassedFlag.id}, {daysPassedFlag.Bits()}) + 5 )
                     # 3 - "Level Up"
                     elif GetTalkListEntryResult() == 3:
                         OpenSoul()
                         assert not (CheckSpecificPersonMenuIsOpen(10, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))
                     # 4 - "Organize Storage"
                     elif GetTalkListEntryResult() == 4:
                         OpenRepository()
                         assert not (CheckSpecificPersonMenuIsOpen(3, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))
                     # 99 - "Leave"
                     else:
                         return 0

             # Pass Time Sub-Menu
             def t{id:D9}_x3():
                 """State 0,5"""
                 CloseShopMessage()
                 while True:
                     ClearTalkListData()
                     # action:15000430:"Until morning"
                     AddTalkListData(1, 15000430, -1)
                     # action:15000440:"Until noon"
                     AddTalkListData(2, 15000440, -1)
                     # action:15000450:"Until nightfall"
                     AddTalkListData(3, 15000450, -1)
                     # action:15000460:"Cancel"
                     AddTalkListData(99, 15000460, -1)
                     ShowShopMessage(TalkOptionsType.Regular)
                     assert not (CheckSpecificPersonMenuIsOpen(1, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))
                     if GetTalkListEntryResult() == 1:
                         def ExitPause():
                             HideClock(0.5)
                         assert t{id:D9}_x45(val1=0)
                         SetEventFlagValue({dayFlag.id}, {dayFlag.Bits()}, GetEventFlagValue({dayFlag.id}, {dayFlag.Bits()}) + 1 )
                         SetEventFlagValue({daysPassedFlag.id}, {daysPassedFlag.Bits()}, GetEventFlagValue({daysPassedFlag.id}, {daysPassedFlag.Bits()}) + 1 )
                         return 0
                     elif GetTalkListEntryResult() == 2:
                         def ExitPause():
                             HideClock(0.5)
                         assert t{id:D9}_x45(val1=1)
                         SetEventFlagValue({dayFlag.id}, {dayFlag.Bits()}, GetEventFlagValue({dayFlag.id}, {dayFlag.Bits()}) + 1 )
                         SetEventFlagValue({daysPassedFlag.id}, {daysPassedFlag.Bits()}, GetEventFlagValue({daysPassedFlag.id}, {daysPassedFlag.Bits()}) + 1 )
                         return 0
                     elif GetTalkListEntryResult() == 3:
                         def ExitPause():
                             HideClock(0.5)
                         assert t{id:D9}_x45(val1=2)
                         SetEventFlagValue({dayFlag.id}, {dayFlag.Bits()}, GetEventFlagValue({dayFlag.id}, {dayFlag.Bits()}) + 1 )
                         SetEventFlagValue({daysPassedFlag.id}, {daysPassedFlag.Bits()}, GetEventFlagValue({daysPassedFlag.id}, {daysPassedFlag.Bits()}) + 1 )
                         return 0
                     else:
                         assert t{id:D9}_x72()
                         SetEventFlagValue({dayFlag.id}, {dayFlag.Bits()}, GetEventFlagValue({dayFlag.id}, {dayFlag.Bits()}) + 1 )
                         SetEventFlagValue({daysPassedFlag.id}, {daysPassedFlag.Bits()}, GetEventFlagValue({daysPassedFlag.id}, {daysPassedFlag.Bits()}) + 1 )
                         return 0

             # Force Exit
             def t{id:D9}_x10():
                 ClearTalkProgressData()
                 StopEventAnimWithoutForcingConversationEnd(0)
                 ForceCloseGenericDialog()
                 ForceCloseMenu()
                 ReportConversationEndToHavokBehavior()
                 return 0

             # Pass Time UNK Stuff
             def t{id:D9}_x13():
                 if DoesPlayerHaveSpEffect(8990):
                     GiveSpEffectToPlayer(8998)
                     SetEventFlag(100210, FlagState.Off)
                     SetEventFlag(100200, FlagState.Off)
                 else:
                     pass
                 return 0

             # Pass Time UNK Stuff
             def t{id:D9}_x29(z30=_):
                 RemoveDynamicCharacter(z30, 1.4)
                 SetEventFlag(4651, FlagState.Off)
                 SetEventFlag(4652, FlagState.Off)
                 SetEventFlag(4653, FlagState.Off)
                 SetEventFlag(4654, FlagState.Off)
                 SetEventFlag(4655, FlagState.Off)
                 SetEventFlag(4656, FlagState.Off)
                 SetEventFlag(4657, FlagState.Off)
                 return 0

             # Pass Time UNK Stuff
             def t{id:D9}_x45(val1=_):
                 assert t{id:D9}_x13()
                 assert t{id:D9}_x74(val1=val1)
                 if val1 == 0:
                     ShowClock(2)
                     SetCurrentTime(0, 0, 0, 0, GetMorningHours(), GetMorningMinutes(), GetMorningSeconds(), 2.5, 0.75, 2, 0, 0.75, 0.5)
                 elif val1 == 1:
                     ShowClock(2)
                     SetCurrentTime(0, 0, 0, 0, GetNoonHours(), GetNoonMinutes(), GetNoonSeconds(), 2.5, 0.75, 2, 0, 0.75, 0.5)
                 elif val1 == 2:
                     ShowClock(2)
                     SetCurrentTime(0, 0, 0, 0, GetNightHours(), GetNightMinutes(), GetNightSeconds(), 2.5, 0.75, 2, 0, 0.75, 0.5)
                 assert GetCurrentStateElapsedTime() > 0.8
                 assert t{id:D9}_x29(z30=0)
                 assert IsTimePassFadeOutInProgress() == False
                 GiveSpEffectToPlayer(100)
                 GiveSpEffectToPlayer(101)
                 GiveSpEffectToPlayer(102)
                 GiveSpEffectToPlayer(103)
                 GiveSpEffectToPlayer(104)
                 GiveSpEffectToPlayer(105)
                 GiveSpEffectToPlayer(106)
                 GiveSpEffectToPlayer(107)
                 GiveSpEffectToPlayer(108)
                 GiveSpEffectToPlayer(109)
                 UpdatePlayerRespawnPoint()
                 ClearPlayerDamageInfo()
                 SetEventFlag({resetFlag.id}, FlagState.On)   ## reset any npcs that the player made hostile
                 assert GetCurrentStateElapsedTime() > 2.5
                 return 0

             # Pass Time UNK Stuff
             def t{id:D9}_x72():
                 SetEventFlag(4820, FlagState.Off)
                 SetEventFlag(4821, FlagState.Off)
                 SetEventFlag(4822, FlagState.Off)
                 SetEventFlag(4825, FlagState.Off)
                 SetEventFlag(4826, FlagState.Off)
                 SetEventFlag(4827, FlagState.Off)
                 return 0

             # Pass Time UNK Stuff
             def t{id:D9}_x73(flag1=_, flag2=_, flag3=_):
                 SetEventFlag(flag1, FlagState.On)
                 SetEventFlag(flag2, FlagState.Off)
                 SetEventFlag(flag3, FlagState.Off)
                 return 0

             # Pass Time UNK Stuff
             def t{id:D9}_x74(val1=_):
                 if val1 == 0:
                     assert t{id:D9}_x73(flag1=4825, flag2=4826, flag3=4827)
                     if IsTimeOfDayInRange(6, 0, 0, 11, 59, 59):
                         Label('L0')
                         assert t{id:D9}_x73(flag1=4822, flag2=4820, flag3=4821)
                     elif IsTimeOfDayInRange(12, 0, 0, 19, 59, 59):
                         Label('L1')
                         assert t{id:D9}_x73(flag1=4821, flag2=4820, flag3=4822)
                     elif IsTimeOfDayInRange(20, 0, 0, 5, 59, 59):
                         Label('L2')
                         assert t{id:D9}_x73(flag1=4820, flag2=4821, flag3=4822)
                     else:
                         Label('L3')
                 elif val1 == 1:
                     assert t{id:D9}_x73(flag1=4826, flag2=4825, flag3=4827)
                     if IsTimeOfDayInRange(6, 0, 0, 11, 59, 59):
                         Goto('L2')
                     elif IsTimeOfDayInRange(12, 0, 0, 19, 59, 59):
                         Goto('L0')
                     elif IsTimeOfDayInRange(20, 0, 0, 5, 59, 59):
                         Goto('L1')
                     else:
                         Goto('L3')
                 elif val1 == 2:
                     assert t{id:D9}_x73(flag1=4827, flag2=4825, flag3=4826)
                     if IsTimeOfDayInRange(6, 0, 0, 11, 59, 59):
                         Goto('L1')
                     elif IsTimeOfDayInRange(12, 0, 0, 19, 59, 59):
                         Goto('L2')
                     elif IsTimeOfDayInRange(20, 0, 0, 5, 59, 59):
                         Goto('L0')
                     else:
                         Goto('L3')
                 else:
                     Goto('L3')
                 return 0

             """";
        }

        public void Write(string pyPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pyPath));
            System.IO.File.WriteAllText(pyPath, def);
        }
    }
}
