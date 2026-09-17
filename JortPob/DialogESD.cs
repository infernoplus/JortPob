using JortPob.Common;
using JortPob.Scripts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static JortPob.Dialog;

namespace JortPob
{
    /* Handles python state machine code generation for a dialog ESD */
    public class DialogESD
    {
        private readonly ESM esm;
        private readonly Layout layout;
        private readonly SoulsFormats.MSBE msb;
        private readonly MainSoundBank sound;
        private readonly ScriptManager scriptManager;
        private readonly Paramanager paramanager;
        private readonly TextManager textManager;
        private readonly ItemManager itemManager;
        private readonly SpeffManager speffManager;
        private readonly BaseScript areaScript;
        private readonly CharacterContent npcContent;

        private readonly List<string> defs;
        private readonly List<string> generatedStates;
        private readonly Dictionary<NpcManager.TopicData.TalkData, int> choiceMap; // this is a fix for recursive choices. if we generate a choice and another dialog refs it we return the id of the alraedy gen'd one
        private int nxtGenStateId;

        public DialogESD(ESM esm, Layout layout, SoulsFormats.MSBE msb, MainSoundBank sound, ScriptManager scriptManager, Paramanager paramanager, TextManager textManager, ItemManager itemManager, SpeffManager speffManager, BaseScript areaScript, uint id, CharacterContent npcContent, List<NpcManager.TopicData> topicData)
        {
            this.esm = esm;
            this.layout = layout;
            this.msb = msb;
            this.sound = sound;
            this.itemManager = itemManager;
            this.speffManager = speffManager;
            this.scriptManager = scriptManager;
            this.paramanager = paramanager;
            this.textManager = textManager;
            this.areaScript = areaScript;
            this.npcContent = npcContent;

            defs = new();

            // Register a halt event for this npc
            areaScript.RegisterHaltEvent(npcContent);

            // Split up talk data by type
            NpcManager.TopicData greeting = GetTalk(topicData, DialogRecord.Type.Greeting).FirstOrDefault() ?? new();
            NpcManager.TopicData hit = GetTalk(topicData, DialogRecord.Type.Hit).FirstOrDefault() ?? new();
            NpcManager.TopicData attack = GetTalk(topicData, DialogRecord.Type.Attack).FirstOrDefault() ?? new();
            NpcManager.TopicData thief = GetTalk(topicData, DialogRecord.Type.Thief).FirstOrDefault() ?? new();
            NpcManager.TopicData idle = GetTalk(topicData, DialogRecord.Type.Idle).FirstOrDefault() ?? new(); ;
            NpcManager.TopicData hello = GetTalk(topicData, DialogRecord.Type.Hello).FirstOrDefault() ?? new();
            List<NpcManager.TopicData> talk = GetTalk(topicData, DialogRecord.Type.Topic);

            NpcManager.TopicData admireSuccess = GetTalk(topicData, DialogRecord.Type.AdmireSuccess).FirstOrDefault() ?? new();
            NpcManager.TopicData admireFail = GetTalk(topicData, DialogRecord.Type.AdmireFail).FirstOrDefault() ?? new();
            NpcManager.TopicData intimidateSuccess = GetTalk(topicData, DialogRecord.Type.IntimidateSuccess).FirstOrDefault() ?? new();
            NpcManager.TopicData intimidateFail = GetTalk(topicData, DialogRecord.Type.IntimidateFail).FirstOrDefault() ?? new();
            NpcManager.TopicData tauntSuccess = GetTalk(topicData, DialogRecord.Type.TauntSuccess).FirstOrDefault() ?? new();
            NpcManager.TopicData tauntFail = GetTalk(topicData, DialogRecord.Type.TauntFail).FirstOrDefault() ?? new();
            NpcManager.TopicData bribeSuccess = GetTalk(topicData, DialogRecord.Type.BribeSuccess).FirstOrDefault() ?? new();
            NpcManager.TopicData bribeFail = GetTalk(topicData, DialogRecord.Type.BribeFail).FirstOrDefault() ?? new();

            choiceMap = new();
            generatedStates = new();
            nxtGenStateId = Common.Const.ESD_STATE_HARDCODE_CHOICE;

            generatedStates.Add(GeneratedState_ModDisposition(id, Common.Const.ESD_STATE_HARDCODE_MODDISPOSITION));
            generatedStates.Add(GeneratedState_ModFacRep(id, Common.Const.ESD_STATE_HARDCODE_MODFACREP));
            generatedStates.Add(GeneratedState_PersuadeMenu(
                id, Common.Const.ESD_STATE_HARDCODE_PERSUADEMENU,
                admireSuccess, admireFail,
                intimidateSuccess, intimidateFail,
                tauntSuccess, tauntFail,
                bribeSuccess, bribeFail
            ));

            if (npcContent.travel.Count() > 0)
            {
                generatedStates.Add(GeneratedState_TravelMenu(id, Common.Const.ESD_STATE_HARDCODE_TRAVELMENU));
            }

            if (npcContent.faction != null)
            {
                generatedStates.Add(GeneratedState_RankReq(id, Common.Const.ESD_STATE_HARDCODE_RANKREQUIREMENT));
                generatedStates.Add(GeneratedState_ReactionCalc(id, Common.Const.ESD_STATE_HARDCODE_REACTIONCALC));
            }

            generatedStates.Add(GeneratedState_HandleCrime(id, Common.Const.ESD_STATE_HARDCODE_HANDLECRIME));
            generatedStates.Add(GeneratedState_CombatDialogSelection(id, Common.Const.ESD_STATE_HARDCODE_COMBATDIALOGSELECT));
            generatedStates.Add(GeneratedState_CombatTalk(id, Common.Const.ESD_STATE_HARDCODE_COMBATTALK));
            generatedStates.Add(GeneratedState_DoAttackTalk(id, Common.Const.ESD_STATE_HARDCODE_DOATTACKTALK, attack));
            generatedStates.Add(GeneratedState_DoHitTalk(id, Common.Const.ESD_STATE_HARDCODE_DOHITTALK, hit));
            generatedStates.Add(GeneratedState_DoThiefTalk(id, Common.Const.ESD_STATE_HARDCODE_DOTHIEFTALK, thief));
            generatedStates.Add(GeneratedState_IdleTalk(id, Common.Const.ESD_STATE_HARDCODE_IDLETALK, idle, hello));
            generatedStates.Add(GeneratedState_Pickpocket(id, Common.Const.ESD_STATE_HARDCODE_PICKPOCKET));

            int talkActionButtonId = paramanager.GenerateActionButtonInteractParam(npcContent, $"Talk to {npcContent.name}");

            defs.Add($"# dialog esd : {npcContent.id}\r\n");

            defs.Add(State_1(id, talkActionButtonId));

            defs.Add(State_1000(id));
            defs.Add(State_1001(id));
            defs.Add(State_1101(id));
            defs.Add(State_1102(id));
            defs.Add(State_1103(id));
            defs.Add(State_2000(id));

            defs.Add(State_x0(id, talkActionButtonId));
            defs.Add(State_x1(id));
            defs.Add(State_x2(id));
            defs.Add(State_x3(id));
            defs.Add(State_x4(id));
            defs.Add(State_x5(id, talkActionButtonId));
            defs.Add(State_x6(id, talkActionButtonId));
            defs.Add(State_x7(id));
            defs.Add(State_x8(id));
            defs.Add(State_x9(id, talkActionButtonId));

            defs.Add(State_x10(id));
            defs.Add(State_x11(id));
            defs.Add(State_x12(id));
            defs.Add(State_x13(id));
            defs.Add(State_x14(id));
            defs.Add(State_x15(id));
            defs.Add(State_x16(id));
            defs.Add(State_x17(id));
            defs.Add(State_x18(id));
            defs.Add(State_x19(id));

            defs.Add(State_x20(id));
            defs.Add(State_x21(id));
            defs.Add(State_x22(id, talkActionButtonId));
            defs.Add(State_x23(id));
            defs.Add(State_x24(id));
            defs.Add(State_x25(id));
            defs.Add(State_x26(id));
            defs.Add(State_x27(id));
            defs.Add(State_x28(id));
            defs.Add(State_x29(id, greeting));

            defs.Add(State_x30(id));
            defs.Add(State_x31(id));
            defs.Add(State_x32(id));
            defs.Add(State_x33(id));
            defs.Add(State_x34(id));
            defs.Add(State_x35(id));
            defs.Add(State_x36(id));
            defs.Add(State_x37(id));
            defs.Add(State_x38(id));
            defs.Add(State_x39(id));

            defs.Add(State_x40(id));
            defs.Add(State_x41(id, hit));
            defs.Add(State_x42(id, talkActionButtonId));
            defs.Add(State_x44(id, talk));

            foreach (string genState in generatedStates)
            {
                defs.Add(genState);
            }
        }

        /* Returns all topics that match the given type. */
        private List<NpcManager.TopicData> GetTalk(List<NpcManager.TopicData> topicData, DialogRecord.Type type)
        {
            List<NpcManager.TopicData> matches = new();
            foreach(NpcManager.TopicData topic in topicData)
            {
                if (topic.dialog.type == type) { matches.Add(topic); }
            }

            return matches;
        }

        public void Write(string pyPath)
        {
            if (!Directory.Exists(Path.GetDirectoryName(pyPath))) { Directory.CreateDirectory(Path.GetDirectoryName(pyPath)); }
            System.IO.File.WriteAllLines(pyPath, defs);
        }



        /* WARNING: SHITCODE BELOW! YOU WERE WARNED! */



        /* Starting state, top level */
        private string State_1(uint id, int talkActionButtonId)
        {
            string id_s = id.ToString("D9");
            Script.Flag hostile = scriptManager.GetFlag(Script.Flag.Designation.Hostile, npcContent);
            return $"def t{id_s}_1():\r\n    \"\"\"State 0,1\"\"\"\r\n    # actionbutton:6000:\"Talk\"\r\n    t{id_s}_x5(flag6=4743, flag7={hostile.id}, val1=5, val2=10, val3=12, val4=10, val5=12, actionbutton1={talkActionButtonId},\r\n                  flag9=6000, flag10=6001, flag11=6000, flag12=6000, flag13=6000, z1=1, z2=1000000, z3=1000000,\r\n                  z4=1000000, mode1=1, mode2=1)\r\n    Quit()\r\n";
        }

        private string State_1000(uint id)
        {
            Script.Flag playerTalkingFlag = scriptManager.GetFlag(Script.Flag.Designation.PlayerIsTalking, "PlayerIsTalking");
            Script.Flag guardGreetingFlag = scriptManager.GetFlag(Script.Flag.Designation.GuardIsGreeting, "GuardIsGreeting");

                string s = $""""
                       def t{id:D9}_1000():
                           """State 0,2,3"""
                           SetEventFlag({playerTalkingFlag.id}, FlagState.On)
                           assert t{id:D9}_x37()
                           """State 1"""
                           SetEventFlag({playerTalkingFlag.id}, FlagState.Off)
                           SetEventFlag({guardGreetingFlag.id}, FlagState.Off)
                           EndMachine(1000)
                           Quit()

                       """";
            return s;
        }

        private string State_1001(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_1001():\r\n    \"\"\"State 0,2,3\"\"\"\r\n    assert t{id_s}_x38()\r\n    \"\"\"State 1\"\"\"\r\n    EndMachine(1001)\r\n    Quit()\r\n";
        }

        private string State_1101(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_1101():\r\n    \"\"\"State 0,2\"\"\"\r\n    assert t{id_s}_x39()\r\n    \"\"\"State 1\"\"\"\r\n    EndMachine(1101)\r\n    Quit()\r\n";
        }

        private string State_1102(uint id)
        {
            string id_s = id.ToString("D9");
            Script.Flag hostileQuipFlag = scriptManager.GetFlag(Script.Flag.Designation.HostileQuip, npcContent);
            return $"def t{id_s}_1102():\r\n    \"\"\"State 0,2\"\"\"\r\n    assert t{id_s}_x40(flag4={hostileQuipFlag.id})\r\n    t{id_s}_x{Const.ESD_STATE_HARDCODE_COMBATDIALOGSELECT:D2}()\r\n    Quit()\r\n";
        }

        private string State_1103(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_1103():\r\n    \"\"\"State 0,2\"\"\"\r\n    assert t{id_s}_x41()\r\n    \"\"\"State 1\"\"\"\r\n    EndMachine(1103)\r\n    Quit()\r\n";
        }

        private string State_2000(uint id)
        {
            string id_s = id.ToString("D9");
            int unk0Flag = 1043332706;
            int unk1Flag = 1043332707;
            return $"def t{id_s}_2000():\r\n    \"\"\"State 0,2,3\"\"\"\r\n    assert t{id_s}_x42(flag2={unk0Flag}, flag3={unk1Flag})\r\n    \"\"\"State 1\"\"\"\r\n    EndMachine(2000)\r\n    Quit()\r\n";
        }

        private string State_x0(uint id, int talkActionButtonId)
        {
            int pickpocketActionId = paramanager.GenerateActionButtonInteractParam(npcContent, $"Pickpocket {npcContent.name}");
            Script.Flag crimeLevel = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel");
            Script.Flag playerIsSneaking = scriptManager.GetFlag(Script.Flag.Designation.PlayerIsSneaking, "PlayerIsSneaking");
            Script.Flag pickpocketedFlag = scriptManager.GetFlag(Script.Flag.Designation.Pickpocketed, npcContent);
            Script.Flag guardTalkingFlag = scriptManager.GetFlag(Script.Flag.Designation.GuardIsGreeting, "GuardIsGreeting");
            string actionButtonCheck;
            if(npcContent.IsGuard()) { actionButtonCheck = $"CheckActionButtonArea(actionbutton1) or (GetDistanceToPlayer() < 4 and GetEventFlagValue({crimeLevel.id}, {crimeLevel.Bits()}) >= 1) and (not GetEventFlag({guardTalkingFlag.id}))"; }
            else { actionButtonCheck = $"CheckActionButtonArea(actionbutton1)"; }
            string forceGreetBypassSneak;
            if (npcContent.IsGuard()) { forceGreetBypassSneak = $" or GetEventFlagValue({crimeLevel.id}, {crimeLevel.Bits()}) >= 1"; }
            else { forceGreetBypassSneak = ""; }

            string s = $""""
                        def t{id:D9}_x0(actionbutton1={talkActionButtonId}, flag10=6001, flag14=6000, flag15=6000, flag16=6000, flag17=6000, flag9=6000):
                            """State 0"""
                            while True:
                                """State 1"""
                                assert not GetOneLineHelpStatus() and not IsClientPlayer() and not IsPlayerDead() and not IsCharacterDisabled()
                                """State 3"""
                                assert (GetEventFlag(flag10) or GetEventFlag(flag14) or GetEventFlag(flag15) or GetEventFlag(flag16) or
                                        GetEventFlag(flag17))
                                """State 4"""
                                assert not GetEventFlag(flag9)
                                """State 2"""
                                # actionbutton:{talkActionButtonId}:"Talk"
                                if not GetEventFlag({playerIsSneaking.id}) or GetEventFlag({pickpocketedFlag.id}){forceGreetBypassSneak}:
                                    call = t{id:D9}_x{Const.ESD_STATE_HARDCODE_IDLETALK:D2}()
                                    if (GetEventFlag(flag9) or not (not GetOneLineHelpStatus() and not IsClientPlayer() and not IsPlayerDead() and not IsCharacterDisabled()) or (not GetEventFlag(flag10) and not GetEventFlag(flag14) and not GetEventFlag(flag15) and not GetEventFlag(flag16) and not GetEventFlag(flag17))):
                                        continue
                                    elif {actionButtonCheck}:
                                        break
                                    elif GetEventFlag({playerIsSneaking.id}):
                                        continue
                                # actionbutton:{pickpocketActionId}:"Pickpocket"
                                elif GetEventFlag({playerIsSneaking.id}):
                                    call = t{id:D9}_x{Const.ESD_STATE_HARDCODE_IDLETALK:D2}()
                                    if (GetEventFlag(flag9) or not (not GetOneLineHelpStatus() and not IsClientPlayer() and not IsPlayerDead() and not IsCharacterDisabled()) or (not GetEventFlag(flag10) and not GetEventFlag(flag14) and not GetEventFlag(flag15) and not GetEventFlag(flag16) and not GetEventFlag(flag17))):
                                        continue
                                    elif CheckActionButtonArea({pickpocketActionId}):
                                        assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_PICKPOCKET:D2}()
                                        continue
                                    elif not GetEventFlag({playerIsSneaking.id}){forceGreetBypassSneak}:
                                        continue
                            """State 5"""
                            return 0

                        """";
            return s;
        }

        private string State_x1(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x1():\r\n    \"\"\"State 0,1\"\"\"\r\n    if not CheckSpecificPersonTalkHasEnded(0):\r\n        \"\"\"State 7\"\"\"\r\n        ClearTalkProgressData()\r\n        StopEventAnimWithoutForcingConversationEnd(0)\r\n        \"\"\"State 6\"\"\"\r\n        ReportConversationEndToHavokBehavior()\r\n    else:\r\n        pass\r\n    \"\"\"State 2\"\"\"\r\n    if CheckSpecificPersonGenericDialogIsOpen(0):\r\n        \"\"\"State 3\"\"\"\r\n        ForceCloseGenericDialog()\r\n    else:\r\n        pass\r\n    \"\"\"State 4\"\"\"\r\n    if CheckSpecificPersonMenuIsOpen(-1, 0) and not CheckSpecificPersonGenericDialogIsOpen(0):\r\n        \"\"\"State 5\"\"\"\r\n        ForceCloseMenu()\r\n    else:\r\n        pass\r\n    \"\"\"State 8\"\"\"\r\n    return 0\r\n";
        }

        private string State_x2(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x2():\r\n    \"\"\"State 0,1\"\"\"\r\n    ClearTalkProgressData()\r\n    StopEventAnimWithoutForcingConversationEnd(0)\r\n    ForceCloseGenericDialog()\r\n    ForceCloseMenu()\r\n    ReportConversationEndToHavokBehavior()\r\n    \"\"\"State 2\"\"\"\r\n    return 0\r\n";
        }

        private string State_x3(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x3(val2=10, val3=12):\r\n    \"\"\"State 0,1\"\"\"\r\n    assert GetDistanceToPlayer() < val2 and GetCurrentStateElapsedFrames() > 1\r\n    \"\"\"State 2\"\"\"\r\n    if PlayerDiedFromFallInstantly() == False and PlayerDiedFromFallDamage() == False:\r\n        \"\"\"State 3,6\"\"\"\r\n        call = t{id_s}_x19()\r\n        if call.Done():\r\n            pass\r\n        elif GetDistanceToPlayer() > val3 or GetTalkInterruptReason() == 6:\r\n            \"\"\"State 5\"\"\"\r\n            assert t{id_s}_x1()\r\n    else:\r\n        \"\"\"State 4,7\"\"\"\r\n        call = t{id_s}_x32()\r\n        if call.Done():\r\n            pass\r\n        elif GetDistanceToPlayer() > val3 or GetTalkInterruptReason() == 6:\r\n            \"\"\"State 8\"\"\"\r\n            assert t{id_s}_x1()\r\n    \"\"\"State 9\"\"\"\r\n    return 0\r\n";
        }

        private string State_x4(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x4():\r\n    \"\"\"State 0,1\"\"\"\r\n    assert t{id_s}_x1()\r\n    \"\"\"State 2\"\"\"\r\n    return 0\r\n";
        }

        private string State_x5(uint id, int talkActionButtonId)
        {
            string id_s = id.ToString("D9");
            Script.Flag hostile = scriptManager.GetFlag(Script.Flag.Designation.Hostile, npcContent);
            return $"def t{id_s}_x5(flag6=4743, flag7={hostile.id}, val1=5, val2=10, val3=12, val4=10, val5=12, actionbutton1={talkActionButtonId},\r\n                  flag9=6000, flag10=6001, flag11=6000, flag12=6000, flag13=6000, z1=1, z2=1000000, z3=1000000,\r\n                  z4=1000000, mode1=1, mode2=1):\r\n    \"\"\"State 0\"\"\"\r\n    assert GetCurrentStateElapsedTime() > 1.5\r\n    while True:\r\n        \"\"\"State 2\"\"\"\r\n        call = t{id_s}_x22(flag6=flag6, flag7=flag7, val1=val1, val2=val2, val3=val3, val4=val4,\r\n                              val5=val5, actionbutton1=actionbutton1, flag9=flag9, flag10=flag10, flag11=flag11,\r\n                              flag12=flag12, flag13=flag13, z1=z1, z2=z2, z3=z3, z4=z4, mode1=mode1, mode2=mode2)\r\n        assert IsClientPlayer()\r\n        \"\"\"State 1\"\"\"\r\n        call = t{id_s}_x21()\r\n        assert not IsClientPlayer()\r\n";
        }

        private string State_x6(uint id, int talkActionButtonId)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x6(val1=5, val2=10, val3=12, val4=10, val5=12, actionbutton1={talkActionButtonId}, flag9=6000, flag10=6001, flag11=6000,\r\n                  flag12=6000, flag13=6000, z1=1, z2=1000000, z3=1000000, z4=1000000, mode1=1, mode2=1):\r\n    \"\"\"State 0\"\"\"\r\n    while True:\r\n        \"\"\"State 2\"\"\"\r\n        call = t{id_s}_x9(actionbutton1=actionbutton1, flag9=flag9, flag10=flag10, z2=z2, z3=z3, z4=z4)\r\n        def WhilePaused():\r\n            RemoveMyAggroIf(IsAttackedBySomeone() and (DoesSelfHaveSpEffect(9626) and DoesSelfHaveSpEffect(9627)))\r\n            GiveSpEffectToPlayerIf(not CheckSpecificPersonTalkHasEnded(0), 9640)\r\n        if call.Done():\r\n            \"\"\"State 4\"\"\"\r\n            Label('L0')\r\n            ChangeCamera(1000000)\r\n            call = t{id_s}_x13(val1=val1, z1=z1)\r\n            def WhilePaused():\r\n                ChangeCameraIf(GetDistanceToPlayer() > 2.5, -1)\r\n                RemoveMyAggroIf(IsAttackedBySomeone() and (DoesSelfHaveSpEffect(9626) and DoesSelfHaveSpEffect(9627)))\r\n                GiveSpEffectToPlayer(9640)\r\n                SetLookAtEntityForTalkIf(mode1 == 1, -1, 0)\r\n                SetLookAtEntityForTalkIf(mode2 == 1, 0, -1)\r\n            def ExitPause():\r\n                ChangeCamera(-1)\r\n            if call.Done():\r\n                continue\r\n            elif IsAttackedBySomeone():\r\n                pass\r\n        elif IsAttackedBySomeone() and not DoesSelfHaveSpEffect(9626) and not DoesSelfHaveSpEffect(9627):\r\n            pass\r\n        elif GetEventFlag(flag13):\r\n            Goto('L0')\r\n        elif GetEventFlag(flag11) and not GetEventFlag(flag12) and GetDistanceToPlayer() < val4:\r\n            \"\"\"State 5\"\"\"\r\n            call = t{id_s}_x15(val5=val5)\r\n            if call.Done():\r\n                continue\r\n            elif IsAttackedBySomeone():\r\n                pass\r\n        elif ((GetDistanceToPlayer() > val5 or GetTalkInterruptReason() == 6) and not CheckSpecificPersonTalkHasEnded(0)\r\n              and not DoesSelfHaveSpEffect(9625)):\r\n            \"\"\"State 6\"\"\"\r\n            assert t{id_s}_x26() and CheckSpecificPersonTalkHasEnded(0)\r\n            continue\r\n        elif GetEventFlag(9000):\r\n            \"\"\"State 1\"\"\"\r\n            assert not GetEventFlag(9000)\r\n            continue\r\n        \"\"\"State 3\"\"\"\r\n        def ExitPause():\r\n            RemoveMyAggro()\r\n        assert t{id_s}_x11(val2=val2, val3=val3)\r\n";
        }

        private string State_x7(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x7(val2=10, val3=12):\r\n    \"\"\"State 0,1\"\"\"\r\n    call = t{id_s}_x17(val2=val2, val3=val3)\r\n    assert IsPlayerDead()\r\n    \"\"\"State 2\"\"\"\r\n    t{id_s}_x3(val2=val2, val3=val3)\r\n    Quit()\r\n";
        }

        private string State_x8(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x8(flag6=4743, val2=10, val3=12):\r\n    \"\"\"State 0,8\"\"\"\r\n    assert t{id_s}_x36()\r\n    \"\"\"State 1\"\"\"\r\n    if GetEventFlag(flag6):\r\n        \"\"\"State 2\"\"\"\r\n        pass\r\n    else:\r\n        \"\"\"State 3\"\"\"\r\n        if GetDistanceToPlayer() < val2:\r\n            \"\"\"State 4,6\"\"\"\r\n            call = t{id_s}_x20()\r\n            if call.Done():\r\n                pass\r\n            elif GetDistanceToPlayer() > val3 or GetTalkInterruptReason() == 6:\r\n                \"\"\"State 7\"\"\"\r\n                assert t{id_s}_x1()\r\n        else:\r\n            \"\"\"State 5\"\"\"\r\n            pass\r\n    \"\"\"State 9\"\"\"\r\n    return 0\r\n";
        }

        private string State_x9(uint id, int talkActionButtonId)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x9(actionbutton1={talkActionButtonId}, flag9=6000, flag10=6001, z2=1000000, z3=1000000, z4=1000000):\r\n    \"\"\"State 0,1\"\"\"\r\n    call = t{id_s}_x10(machine1=2000, val6=2000)\r\n    if call.Get() == 1:\r\n        \"\"\"State 2\"\"\"\r\n        assert (t{id_s}_x0(actionbutton1=actionbutton1, flag10=flag10, flag14=6000, flag15=6000, flag16=6000,\r\n                flag17=6000, flag9=flag9))\r\n    elif call.Done():\r\n        pass\r\n    \"\"\"State 3\"\"\"\r\n    return 0\r\n";
        }

        private string State_x10(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x10(machine1=_, val6=_):\r\n    \"\"\"State 0,1\"\"\"\r\n    if MachineExists(machine1):\r\n        \"\"\"State 2\"\"\"\r\n        assert GetCurrentStateElapsedFrames() > 1\r\n        \"\"\"State 4\"\"\"\r\n        def WhilePaused():\r\n            RunMachine(machine1)\r\n        assert GetMachineResult() == val6\r\n        \"\"\"State 5\"\"\"\r\n        return 0\r\n    else:\r\n        \"\"\"State 3,6\"\"\"\r\n        return 1\r\n";
        }

        private string State_x11(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x11(val2=10, val3=12):\r\n    \"\"\"State 0\"\"\"\r\n    assert GetCurrentStateElapsedFrames() > 1\r\n    \"\"\"State 5\"\"\"\r\n    assert t{id_s}_x1()\r\n    \"\"\"State 3\"\"\"\r\n    if GetDistanceToPlayer() < val2:\r\n        \"\"\"State 1\"\"\"\r\n        if IsPlayerAttacking():\r\n            \"\"\"State 6\"\"\"\r\n            call = t{id_s}_x12()\r\n            if call.Done():\r\n                pass\r\n            elif GetDistanceToPlayer() > val3 or GetTalkInterruptReason() == 6:\r\n                \"\"\"State 7\"\"\"\r\n                assert t{id_s}_x27()\r\n        else:\r\n            \"\"\"State 4\"\"\"\r\n            pass\r\n    else:\r\n        \"\"\"State 2\"\"\"\r\n        pass\r\n    \"\"\"State 8\"\"\"\r\n    return 0\r\n";
        }

        private string State_x12(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x12():\r\n    \"\"\"State 0,1\"\"\"\r\n    assert t{id_s}_x10(machine1=1101, val6=1101)\r\n    \"\"\"State 2\"\"\"\r\n    return 0\r\n";
        }

        private string State_x13(uint id)
        {
            Script.Flag crimeLevelFlag = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel");
            Script.Flag guardGreetFlag = scriptManager.GetFlag(Script.Flag.Designation.GuardIsGreeting, "GuardIsGreeting");
            string fleeGuardForceGreet;
            if (npcContent.IsGuard()) { fleeGuardForceGreet = $"    if GetEventFlag({guardGreetFlag.id}) and GetEventFlagValue({crimeLevelFlag.id}, {crimeLevelFlag.Bits()}) >= 1:\r\n        ## player tried to flee guard by walking away\r\n        assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_HANDLECRIME:D2}(crimeGold={Const.CRIME_GOLD_PICKPOCKET},violent=True)\r\n    else:\r\n        pass"; }
            else { fleeGuardForceGreet = ""; }

            return $""""
                   def t{id:D9}_x13(val1=5.5, z1=1):
                       """State 0,2"""
                       assert t{id:D9}_x23()
                       """State 1"""
                       call = t{id:D9}_x14()
                       if call.Done():
                           pass
                       elif (GetDistanceToPlayer() > val1 or GetTalkInterruptReason() == 6) and not DoesSelfHaveSpEffect(9625):
                           """State 3"""
                           assert t{id:D9}_x25()
                       """State 4"""
                   {fleeGuardForceGreet}
                       return 0

                   """";
        }

        private string State_x14(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x14():\r\n    \"\"\"State 0,1\"\"\"\r\n    assert t{id_s}_x10(machine1=1000, val6=1000)\r\n    \"\"\"State 2\"\"\"\r\n    return 0\r\n";
        }

        private string State_x15(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x15(val5=12):\r\n    \"\"\"State 0,1\"\"\"\r\n    call = t{id_s}_x16()\r\n    if call.Done():\r\n        pass\r\n    elif GetDistanceToPlayer() > val5 or GetTalkInterruptReason() == 6:\r\n        \"\"\"State 2\"\"\"\r\n        assert t{id_s}_x26()\r\n    \"\"\"State 3\"\"\"\r\n    return 0\r\n";
        }

        private string State_x16(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x16():\r\n    \"\"\"State 0,1\"\"\"\r\n    assert t{id_s}_x10(machine1=1100, val6=1100)\r\n    \"\"\"State 2\"\"\"\r\n    return 0\r\n";
        }

        private string State_x17(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x17(val2=10, val3=12):\r\n    \"\"\"State 0,5\"\"\"\r\n    assert t{id_s}_x36()\r\n    \"\"\"State 2\"\"\"\r\n    assert not GetEventFlag(3000)\r\n    while True:\r\n        \"\"\"State 1\"\"\"\r\n        assert GetDistanceToPlayer() < val2\r\n        \"\"\"State 3\"\"\"\r\n        call = t{id_s}_x18()\r\n        if call.Done():\r\n            pass\r\n        elif GetDistanceToPlayer() > val3 or GetTalkInterruptReason() == 6:\r\n            \"\"\"State 4\"\"\"\r\n            assert t{id_s}_x28()\r\n";
        }

        private string State_x18(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x18():\r\n    \"\"\"State 0,2\"\"\"\r\n    call = t{id_s}_x10(machine1=1102, val6=1102)\r\n    if call.Get() == 1:\r\n        \"\"\"State 1\"\"\"\r\n        Quit()\r\n    elif call.Done():\r\n        \"\"\"State 3\"\"\"\r\n        return 0\r\n";
        }

        private string State_x19(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x19():\r\n    \"\"\"State 0,1\"\"\"\r\n    assert t{id_s}_x10(machine1=1001, val6=1001)\r\n    \"\"\"State 2\"\"\"\r\n    return 0\r\n";
        }

        private string State_x20(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x20():\r\n    \"\"\"State 0,1\"\"\"\r\n    assert t{id_s}_x10(machine1=1103, val6=1103)\r\n    \"\"\"State 2\"\"\"\r\n    return 0\r\n";
        }

        private string State_x21(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x21():\r\n    \"\"\"State 0\"\"\"\r\n    Quit()\r\n";
        }

        private string State_x22(uint id, int talkActionButtonId)
        {
            string id_s = id.ToString("D9");
            Script.Flag hostile = scriptManager.GetFlag(Script.Flag.Designation.Hostile, npcContent);
            return $"def t{id_s}_x22(flag6=4743, flag7={hostile.id}, val1=5, val2=10, val3=12, val4=10, val5=12, actionbutton1={talkActionButtonId},\r\n                   flag9=6000, flag10=6001, flag11=6000, flag12=6000, flag13=6000, z1=1, z2=1000000, z3=1000000,\r\n                   z4=1000000, mode1=1, mode2=1):\r\n    \"\"\"State 0\"\"\"\r\n    while True:\r\n        \"\"\"State 1\"\"\"\r\n        RemoveMyAggro()\r\n        call = t{id_s}_x6(val1=val1, val2=val2, val3=val3, val4=val4, val5=val5, actionbutton1=actionbutton1,\r\n                             flag9=flag9, flag10=flag10, flag11=flag11, flag12=flag12, flag13=flag13, z1=z1, z2=z2,\r\n                             z3=z3, z4=z4, mode1=mode1, mode2=mode2)\r\n        if CheckSelfDeath() or GetEventFlag(flag6):\r\n            \"\"\"State 3\"\"\"\r\n            Label('L0')\r\n            call = t{id_s}_x8(flag6=flag6, val2=val2, val3=val3)\r\n            if not CheckSelfDeath() and not GetEventFlag(flag6):\r\n                continue\r\n            elif GetEventFlag(9000):\r\n                pass\r\n        elif GetEventFlag(flag7):\r\n            \"\"\"State 2\"\"\"\r\n            call = t{id_s}_x7(val2=val2, val3=val3)\r\n            if CheckSelfDeath() or GetEventFlag(flag6):\r\n                Goto('L0')\r\n            elif not GetEventFlag(flag7):\r\n                continue\r\n            elif GetEventFlag(9000):\r\n                pass\r\n        elif GetEventFlag(9000) or IsPlayerDead():\r\n            pass\r\n        \"\"\"State 4\"\"\"\r\n        assert t{id_s}_x35() and not GetEventFlag(9000)\r\n";
        }

        private string State_x23(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x23():\r\n    \"\"\"State 0,1\"\"\"\r\n    assert t{id_s}_x24()\r\n    \"\"\"State 2\"\"\"\r\n    return 0\r\n";
        }

        private string State_x24(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x24():\r\n    \"\"\"State 0,1\"\"\"\r\n    assert t{id_s}_x10(machine1=1104, val6=1104)\r\n    \"\"\"State 2\"\"\"\r\n    return 0\r\n";
        }

        private string State_x25(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x25():\r\n    \"\"\"State 0,1\"\"\"\r\n    call = t{id_s}_x10(machine1=1201, val6=1201)\r\n    if call.Get() == 1:\r\n        \"\"\"State 2\"\"\"\r\n        assert t{id_s}_x4()\r\n    elif call.Done():\r\n        pass\r\n    \"\"\"State 3\"\"\"\r\n    return 0\r\n";
        }

        private string State_x26(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x26():\r\n    \"\"\"State 0,1\"\"\"\r\n    call = t{id_s}_x10(machine1=1300, val6=1300)\r\n    if call.Get() == 1:\r\n        \"\"\"State 2\"\"\"\r\n        assert t{id_s}_x4()\r\n    elif call.Done():\r\n        pass\r\n    \"\"\"State 3\"\"\"\r\n    return 0\r\n";
        }

        private string State_x27(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x27():\r\n    \"\"\"State 0,1\"\"\"\r\n    call = t{id_s}_x10(machine1=1301, val6=1301)\r\n    if call.Get() == 1:\r\n        \"\"\"State 2\"\"\"\r\n        assert t{id_s}_x4()\r\n    elif call.Done():\r\n        pass\r\n    \"\"\"State 3\"\"\"\r\n    return 0\r\n";
        }

        private string State_x28(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x28():\r\n    \"\"\"State 0,1\"\"\"\r\n    call = t{id_s}_x10(machine1=1302, val6=1302)\r\n    if call.Get() == 1:\r\n        \"\"\"State 2\"\"\"\r\n        assert t{id_s}_x4()\r\n    elif call.Done():\r\n        pass\r\n    \"\"\"State 3\"\"\"\r\n    return 0\r\n";
        }

        private string State_x29(uint id, NpcManager.TopicData greeting)
        {
            string id_s = id.ToString("D9");
            string s = $"def t{id_s}_x29(mode6=1):\r\n    \"\"\"State 0,4\"\"\"\r\n    assert t{id_s}_x2() and CheckSpecificPersonTalkHasEnded(0)\r\n    ShuffleRNGSeed(100)\r\n    SetRNGSeed()\r\n";

            // set arrest flag if this is a crime related greet
            if(npcContent.IsGuard())
            {
                Script.Flag crimeLevelFlag = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel");
                Script.Flag arrestFlag = scriptManager.GetFlag(Script.Flag.Designation.Arrest, "Arrest");
                
                s += $"    if GetEventFlagValue({crimeLevelFlag.id}, {crimeLevelFlag.Bits()}) >= 1:\r\n"; // if player has crime gold
                s += $"        SetEventFlag({arrestFlag.id}, FlagState.On)\r\n";                         // mark that an arrest attempt was made
                s += $"    else:\r\n";
                s += $"        pass\r\n";
            }

            // add rankreq call if npc hasa faction
            if (npcContent.faction != null)
            {
                s += $"    # rankreq and reactioncalc call: \"{npcContent.faction}\"\r\n";
                s += $"    assert t{id_s}_x{Common.Const.ESD_STATE_HARDCODE_RANKREQUIREMENT}()\r\n";
                s += $"    assert t{id_s}_x{Common.Const.ESD_STATE_HARDCODE_REACTIONCALC}()\r\n";
            }

            // Build an if-else tree for each possible greeting and its conditions
            string ifop = "if";
            for (int i = 0; i < greeting.talks.Count(); i++)
            {
                NpcManager.TopicData.TalkData talkData = greeting.talks[i];
                if (talkData.IsChoice()) { continue; }

                string filters = $" {talkData.dialogInfo.GenerateCondition(itemManager, speffManager, scriptManager, npcContent)}";
                string greetLine = "";
                if (filters == " " || !(i < greeting.talks.Count() - 1)) { ifop = "else"; filters = ""; }
                if (greeting.talks.Count() == 1) { ifop = "if"; filters = " True"; }

                greetLine += $"    {ifop}{filters}:\r\n";
                greetLine += $"        # greeting: \"{Common.Utility.SanitizeTextForComment(talkData.dialogInfo.text)}\"\r\n";
                greetLine += $"        TalkToPlayer({talkData.primaryTalkRow}, -1, -1, 0)\r\n        assert CheckSpecificPersonTalkHasEnded(0)\r\n";

                foreach (DialogRecord dialog in talkData.dialogInfo.unlocks)
                {
                    greetLine += $"        SetEventFlag({dialog.flag.id}, FlagState.On)\r\n";
                }

                if(talkData.dialogInfo.script != null)
                {
                    if (talkData.dialogInfo.script.calls.Count() > 0)
                    {
                        greetLine += talkData.dialogInfo.script.GenerateEsdSnippet(esm, layout, msb, sound, paramanager, itemManager, speffManager, scriptManager, npcContent, id, 8);
                    }
                    if (talkData.dialogInfo.script.choice != null)
                    {
                        int genChoiceStateId = GeneratedState_Choice(id, talkData, greeting);
                        greetLine += $"        call = t{id_s}_x{genChoiceStateId}()\r\n";
                        greetLine += $"        if call.Get() == 0:\r\n";
                        greetLine += $"            return 0\r\n";
                        greetLine += $"        elif call.Done():\r\n";
                        greetLine += $"            pass\r\n";
                    }
                        
                }

                s += greetLine;

                if (ifop == "if") { ifop = "elif"; }
                if (ifop == "else") { break; }
            }
            s += "    \"\"\"State 3\"\"\"\r\n    if mode6 == 0:\r\n        pass\r\n    else:\r\n        \"\"\"State 2\"\"\"\r\n        ReportConversationEndToHavokBehavior()\r\n    \"\"\"State 5\"\"\"\r\n";
            // Also make sure to flag the TalkedToPC flag as it should be marked true once the player has finished the greeting with an npc for the first time
            s += $"    SetEventFlag({scriptManager.GetFlag(Script.Flag.Designation.TalkedToPc, npcContent).id}, FlagState.On)\r\n";
            s += "    return 1\r\n";
            return s;
        }

        private string State_x30(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x30(text3=_, mode5=1):\r\n    \"\"\"State 0,4\"\"\"\r\n    assert t{id_s}_x31() and CheckSpecificPersonTalkHasEnded(0)\r\n    \"\"\"State 1\"\"\"\r\n    TalkToPlayer(text3, -1, -1, 1)\r\n    \"\"\"State 3\"\"\"\r\n    if mode5 == 0:\r\n        pass\r\n    else:\r\n        \"\"\"State 2\"\"\"\r\n        ReportConversationEndToHavokBehavior()\r\n    \"\"\"State 5\"\"\"\r\n    return 0\r\n";
        }

        private string State_x31(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x31():\r\n    \"\"\"State 0,1\"\"\"\r\n    ClearTalkProgressData()\r\n    StopEventAnimWithoutForcingConversationEnd(0)\r\n    ReportConversationEndToHavokBehavior()\r\n    \"\"\"State 2\"\"\"\r\n    return 0\r\n";
        }

        private string State_x32(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x32():\r\n    \"\"\"State 0,1\"\"\"\r\n    assert t{id_s}_x10(machine1=1002, val6=1002)\r\n    \"\"\"State 2\"\"\"\r\n    return 0\r\n";
        }

        private string State_x33(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x33(text2=_, mode4=1):\r\n    assert t{id_s}_x2() and CheckSpecificPersonTalkHasEnded(0)\r\n    TalkToPlayer(text2, -1, -1, 0)\r\n    assert CheckSpecificPersonTalkHasEnded(0)\r\n    if mode4 == 0:\r\n        pass\r\n    else:\r\n        ReportConversationEndToHavokBehavior()\r\n    return 0\r\n";
        }

        private string State_x34(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x34(text1=_, flag3=_, mode3=1):\r\n    \"\"\"State 0,5\"\"\"\r\n    assert t{id_s}_x31() and CheckSpecificPersonTalkHasEnded(0)\r\n    \"\"\"State 2\"\"\"\r\n    SetEventFlag(flag3, FlagState.On)\r\n    \"\"\"State 1\"\"\"\r\n    TalkToPlayer(text1, -1, -1, 1)\r\n    \"\"\"State 4\"\"\"\r\n    if mode3 == 0:\r\n        pass\r\n    else:\r\n        \"\"\"State 3\"\"\"\r\n        ReportConversationEndToHavokBehavior()\r\n    \"\"\"State 6\"\"\"\r\n    return 0\r\n";
        }

        private string State_x35(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x35():\r\n    \"\"\"State 0,1\"\"\"\r\n    assert t{id_s}_x1()\r\n    \"\"\"State 2\"\"\"\r\n    return 0\r\n";
        }

        private string State_x36(uint id)
        {
            string id_s = id.ToString("D9");
            return $"def t{id_s}_x36():\r\n    \"\"\"State 0,1\"\"\"\r\n    if CheckSpecificPersonGenericDialogIsOpen(0):\r\n        \"\"\"State 2\"\"\"\r\n        ForceCloseGenericDialog()\r\n    else:\r\n        pass\r\n    \"\"\"State 3\"\"\"\r\n    if CheckSpecificPersonMenuIsOpen(-1, 0) and not CheckSpecificPersonGenericDialogIsOpen(0):\r\n        \"\"\"State 4\"\"\"\r\n        ForceCloseMenu()\r\n    else:\r\n        pass\r\n    \"\"\"State 5\"\"\"\r\n    return 0\r\n";
        }

        /* Greeting -> Dialog parent state */
        private string State_x37(uint id)
        {
            Script.Flag npcHelloFlag = scriptManager.GetFlag(Script.Flag.Designation.Hello, npcContent);

            string id_s = id.ToString("D9");
            string s = $"def t{id:D9}_x37():\r\n    \"\"\"State 0,1\"\"\"\r\n";
            s += $"    SetEventFlag({npcHelloFlag.id}, FlagState.On)";  // set hello flag when the player hits a to talk to a npc, this locks the npc out of using a "hello" line until you walk away and come back
            if(npcContent.IsGuard())
            {
                Script.Flag guardTalkingFlag = scriptManager.GetFlag(Script.Flag.Designation.GuardIsGreeting, "GuardIsGreeting");
                s += $"    ## a guard is greeting\r\n    SetEventFlag({guardTalkingFlag.id}, FlagState.On)\r\n";
            }
            s += $"    ## if the call to greeting returns 0 it was interupted by player pressing B or a Goodbye call, if it returns 1 it was finished correctly\r\n    call = t{id:D9}_x29()\r\n    if call.Get() == 0:\r\n";
            if (npcContent.IsGuard())
            {
                Script.Flag guardTalkingFlag = scriptManager.GetFlag(Script.Flag.Designation.GuardIsGreeting, "GuardIsGreeting");
                Script.Flag crimeLevel = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel");
                s += $"        ## if player attempted to flee set crime\r\n        if GetEventFlagValue({crimeLevel.id}, {crimeLevel.Bits()}) >= 1:\r\n            SetEventFlag({guardTalkingFlag.id}, FlagState.Off)\r\n            assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_HANDLECRIME:D2}(crimeGold={Const.CRIME_GOLD_RESIST},violent=True)\r\n";
            }
            s += $"        return 0\r\n    elif call.Done():\r\n        pass\r\n    \"\"\"State 2\"\"\"\r\n";
            if (npcContent.IsGuard())
            {
                Script.Flag guardTalkingFlag = scriptManager.GetFlag(Script.Flag.Designation.GuardIsGreeting, "GuardIsGreeting");
                s += $"    ## guard greeting finished successfully\r\n    SetEventFlag({guardTalkingFlag.id}, FlagState.Off)\r\n";
            }
            s += $"    ## go to dialog menu\r\n    assert t{id:D9}_x44()\r\n    \"\"\"State 3\"\"\"\r\n    return 0\r\n";
            return s;
        }

        /* On killing the player */
        private string State_x38(uint id)
        {
            return $"def t{id:D9}_x38():\r\n    ## on player kill talk\r\n    assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_DOATTACKTALK:D2}()\r\n    return 0\r\n";
        }

        // Character gets hurt by player
        private string State_x39(uint id)
        {
            string id_s = id.ToString("D9");

            string s = $"def t{id_s}_x39():\r\n    ShuffleRNGSeed(100)\r\n    SetRNGSeed()\r\n";

            /* lower disposition */
            Script.Flag dvar = scriptManager.GetFlag(Script.Flag.Designation.Disposition, npcContent);
            s += $"    # lower disposition from being hit\r\n";
            s += $"    assert t{id_s}_x{Const.ESD_STATE_HARDCODE_MODDISPOSITION}(dispositionflag={dvar.id}, value={-25})\r\n\r\n";

            /* decide if we go hostile from being hit */  // doesn't actually set the hostile flag directly but sets the crime flag which will trigger it
            Script.Flag friendHitCounter = scriptManager.GetFlag(Script.Flag.Designation.FriendHitCounter, npcContent);
            Script.Flag disposition = scriptManager.GetFlag(Script.Flag.Designation.Disposition, npcContent);
            s += $"    # decide if we are going hostile from this attack\r\n";
            s += $"    if GetEventFlagValue({friendHitCounter.id}, {friendHitCounter.Bits()}) == 1 and GetEventFlagValue({disposition.id}, {disposition.Bits()}) < 10:\r\n";
            s += $"        assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_HANDLECRIME:D2}(crimeGold={Const.CRIME_GOLD_ASSAULT},violent=True)\r\n";
            s += $"    elif GetEventFlagValue({friendHitCounter.id}, {friendHitCounter.Bits()}) == 2 and GetEventFlagValue({disposition.id}, {disposition.Bits()}) < 15:\r\n";
            s += $"        assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_HANDLECRIME:D2}(crimeGold={Const.CRIME_GOLD_ASSAULT},violent=True)\r\n";
            s += $"    elif GetEventFlagValue({friendHitCounter.id}, {friendHitCounter.Bits()}) == 3 and GetEventFlagValue({disposition.id}, {disposition.Bits()}) < 20:\r\n";
            s += $"        assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_HANDLECRIME:D2}(crimeGold={Const.CRIME_GOLD_ASSAULT},violent=True)\r\n";
            s += $"    elif GetEventFlagValue({friendHitCounter.id}, {friendHitCounter.Bits()}) >= 4:\r\n";
            s += $"        assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_HANDLECRIME:D2}(crimeGold={Const.CRIME_GOLD_ASSAULT},violent=True)\r\n";
            s += $"    else:\r\n";
            s += $"        assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_HANDLECRIME:D2}(crimeGold={Const.CRIME_GOLD_ASSAULT/2},violent=False)\r\n";

            /* set hasbeenattacked flag */
            Script.Flag hasBeenAttacked = scriptManager.GetFlag(Script.Flag.Designation.HasBeenAttacked, npcContent);
            s += $"    # set HasBeenAttacked flag\r\n";
            s += $"    SetEventFlag({hasBeenAttacked.id}, FlagState.On)\r\n";

            /* pick voice line to play */
            s += $"    # play a voice line in response to being hit\r\n";
            s += $"    assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_DOHITTALK:D2}()\r\n\r\n";
            s += $"    # increment hit counter\r\n";
            s += $"    SetEventFlagValue({friendHitCounter.id}, {friendHitCounter.Bits()}, GetEventFlagValue({friendHitCounter.id}, {friendHitCounter.Bits()}) + 1 )\r\n"; // increment friend hit value
            s += $"    return 0\r\n";
            return s;
        }

        /* Hostile quip */
        private string State_x40(uint id)
        {
            Script.Flag theifFlag = scriptManager.GetFlag(Script.Flag.Designation.ThiefCrime, npcContent);
            string s = $"def t{id:D9}_x40(flag4=_):\r\n    ## single angry quip triggered when a character becomes hostile\r\n";
            s += $"    if not GetEventFlag(flag4):\r\n";
            s += $"       if GetEventFlag({theifFlag.id}):\r\n";
            s += $"           ## aggrod by a thievery crime\r\n";
            s += $"           assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_DOTHIEFTALK:D2}()\r\n";
            s += $"       else:\r\n";
            s += $"           ## aggrod by a violent crime\r\n";
            s += $"           assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_DOATTACKTALK:D2}()\r\n";
            s += $"       SetEventFlag(flag4, FlagState.On)\r\n";
            s += $"    else:\r\n";
            s += $"        pass\r\n";
            s += $"    return 0\r\n";
            return s;
        }

        /* On death talk */
        private string State_x41(uint id, NpcManager.TopicData topic)
        {
            string s = $""""
                       def t{id:D9}_x41():
                           ## on death talk##
                           ShuffleRNGSeed(100)
                           SetRNGSeed()

                           ## handle crime of murder if applicable
                           if not DoesSelfHaveSpEffect({(int)SpeffManager.Functional.VoidMurder}):    ## void murder speff means murder bounty is not awarded at this time. used by 'StartCombat'
                               assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_HANDLECRIME:D2}(crimeGold={Const.CRIME_GOLD_MURDER},violent=True)
                           else:
                               pass

                           ## Do death talk

                       """";

            string ifop = "if";
            for (int i = 0; i < topic.talks.Count(); i++)
            {
                NpcManager.TopicData.TalkData talk = topic.talks[i];

                string filters = talk.dialogInfo.GenerateCondition(itemManager, speffManager, scriptManager, npcContent);
                if (filters == "") { filters = "True"; }

                s += $"    {ifop} {filters}:\r\n";
                s += $"        # death: \"{Common.Utility.SanitizeTextForComment(talk.dialogInfo.text)}\"\r\n";
                s += $"        assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_COMBATTALK}(combatText={talk.primaryTalkRow})\r\n";

                if (ifop == "if") { ifop = "elif"; }
            }
            s += "    return 0\r\n";
            return s;
        }

        private string State_x42(uint id, int talkActionButtonId)
        {
            string id_s = id.ToString("D9");
            int unk0Flag = 1043332706;
            int unk1Flag = 1043332707;
            return $"def t{id_s}_x42(flag2={unk0Flag}, flag3={unk1Flag}):\r\n    \"\"\"State 0\"\"\"\r\n    while True:\r\n        \"\"\"State 1\"\"\"\r\n        # actionbutton:6000:\"Talk\"\r\n        call = t{id_s}_x0(actionbutton1={talkActionButtonId}, flag10=6001, flag14=6000, flag15=6000, flag16=6000, flag17=6000,\r\n                             flag9=6000)\r\n        if call.Done():\r\n            break\r\n        elif GetEventFlag(flag2) and not GetEventFlag(flag3):\r\n            \"\"\"State 2\"\"\"\r\n            # talk:80181010:\"What are you playing at! Stop this!\"\r\n            assert t{id_s}_x34(text1=80181010, flag3=flag3, mode3=1)\r\n    \"\"\"State 3\"\"\"\r\n    return 0\r\n";
        }

        // Some ESD calls have custom state machine calls because they are complex (EX: rankreq)
        // These are hardcoded in Const the Papyrus region
        private string State_x44(uint id, List<NpcManager.TopicData> topics)
        {
            string id_s = id.ToString("D9");
            int barterShopId = itemManager.CreateShop(npcContent.barter);
            int spellShopId = itemManager.CreateShop(npcContent.spells);

            StringBuilder s = new();

            s.Append("def t")
                .Append(id_s)
                .Append(
                    "_x44():\r\n    \"\"\"State 0\"\"\"\r\n    while True:\r\n        \"\"\"State 1\"\"\"\r\n        ClearPreviousMenuSelection()\r\n        ClearTalkActionState()\r\n        ClearTalkListData()\r\n        \"\"\"State 2\"\"\"\r\n"
                );

            if (npcContent.faction != null)
            {
                s.Append($"        # rankreq call: \"{npcContent.faction}\"\r\n");
                s.Append($"        assert t{id_s}_x{Common.Const.ESD_STATE_HARDCODE_RANKREQUIREMENT}()");
            }

            int listCount = 1; // starts at 1 because guh

            // Add barter if npc offers it
            if(barterShopId > 0)
            {
                int barterMenuTopicId = textManager.GetTopic("Barter");
                s.Append($"        # action:{barterMenuTopicId}:\"Barter\"\r\n        AddTalkListData({listCount++}, {barterMenuTopicId}, -1)\r\n        # action:20000011:\"Sell\"\r\n        AddTalkListData({listCount++}, 20000011, -1)\r\n");
            }

            // Add smithing if npc offers it
            if(npcContent.OffersSmithing())
            {
                int smithingMenuTopicId = textManager.GetTopic("Smith Weapons");
                s.Append($"        # action:{smithingMenuTopicId}:\"Smith Weapons\"\r\n        AddTalkListData({listCount++}, {smithingMenuTopicId}, -1)\r\n");
            }

            // Add tailoring if npc offers it
            if(npcContent.OffersTailoring())
            {
                int tailorMenuTopicId = textManager.GetTopic("Alter Garments");
                s.Append($"        # action:{tailorMenuTopicId}:\"Alter Garments\"\r\n        AddTalkListData({listCount++}, {tailorMenuTopicId}, -1)\r\n");
            }

            // Add spell shop if npc offers it
            if (spellShopId > 0)
            {
                int spellMenuTopicId = textManager.GetTopic("Learn Spells");
                s.Append($"        # action:{spellMenuTopicId}:\"Learn Spells\"\r\n        AddTalkListData({listCount++}, {spellMenuTopicId}, -1)\r\n");
            }

            // Add memorize spells if npc offers it
            if (npcContent.OffersMemorize())
            {
                int memorizeMenuTopicId = textManager.GetTopic("Memorize Spells");
                s.Append($"        # action:{memorizeMenuTopicId}:\"Memorize Spells\"\r\n        AddTalkListData({listCount++}, {memorizeMenuTopicId}, -1)\r\n");
            }

            // Add enchanting if npc offers it
            if (npcContent.OffersEnchanting())
            {
                int learnEnchantMenuTopicId = textManager.GetTopic("Learn Enchantments");
                int createEnchantMenuTopicId = textManager.GetTopic("Enchant Weapons");
                s.Append($"        # action:{learnEnchantMenuTopicId}:\"Learn Enchantments\"\r\n        AddTalkListData({listCount++}, {learnEnchantMenuTopicId}, -1)\r\n");
                s.Append($"        # action:{createEnchantMenuTopicId}:\"Enchant Weapons\"\r\n        AddTalkListData({listCount++}, {createEnchantMenuTopicId}, -1)\r\n");
            }

            // Add alchemy recipe book shop if npc offers it
            if (npcContent.OffersAlchemy())
            {
                int alchemyMenuTopicId = textManager.GetTopic("Learn Alchemy Recipes");
                s.Append($"        # action:{alchemyMenuTopicId}:\"\"Learn Alchemy Recipes\"\r\n        AddTalkListData({listCount++}, {alchemyMenuTopicId}, -1)\r\n");
            }

            // Add travel option if npc offers it
            if (npcContent.travel.Count() > 0)
            {
                int travelMenuTopicId = textManager.GetTopic("Travel");
                s.Append($"        # action:{travelMenuTopicId}:\"Travel\"\r\n        AddTalkListData({listCount++}, {travelMenuTopicId}, -1)\r\n");
            }

            // Add persuasion option
            int persuasionMenuTopicId = textManager.GetTopic("Persuade");
            s.Append($"        # action:{persuasionMenuTopicId}:\"Persuasion\"\r\n        AddTalkListData({listCount++}, {persuasionMenuTopicId}, -1)\r\n");

            for (int i = 0; i < topics.Count(); i++)
            {
                NpcManager.TopicData topic = topics[i];
                if (topic.IsOnlyChoice()) { continue; } // skip these as they aren't valid or reachable

                List<string> filters = new();
                foreach(NpcManager.TopicData.TalkData talk in topic.talks)
                {
                    string filter = talk.dialogInfo.GenerateCondition(itemManager, speffManager, scriptManager, npcContent);
                    if(filter == "") { filters.Clear(); break; }
                    filters.Add(filter);
                }
                StringBuilder combinedFilters = new();
                for(int j = 0;j<filters.Count();j++)
                {
                    string filter = filters[j];
                    combinedFilters.Append($"({filter})");
                    if (j < filters.Count() - 1) { combinedFilters.Append(" or "); }
                }

                if (combinedFilters.Length > 0) { combinedFilters.Insert(0, " and (").Append(')'); }

                s.Append($"        # topic: \"{topic.dialog.id}\"\r\n        if GetEventFlag({topic.dialog.flag.id}){combinedFilters}:\r\n            AddTalkListData({i+listCount}, {topic.topicText}, -1)\r\n        else:\r\n            pass\r\n");
            }

            s.Append($"        # action:20000009:\"Leave\"\r\n        AddTalkListData(99, 20000009, -1)\r\n        \"\"\"State 3\"\"\"\r\n        ShowShopMessage(TalkOptionsType.Regular)\r\n        \"\"\"State 4\"\"\"\r\n        assert not (CheckSpecificPersonMenuIsOpen(1, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))\r\n        \"\"\"State 5\"\"\"\r\n");


            string ifopA = "if";
            listCount = 1; // reset
            // barter options
            if (barterShopId > 0)
            {
                s.Append($"        {ifopA} GetTalkListEntryResult() == {listCount++}:\r\n            OpenRegularShop({barterShopId}, {barterShopId+99})\r\n            assert not (CheckSpecificPersonMenuIsOpen(5, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))\r\n        elif GetTalkListEntryResult() == {listCount++}:\r\n            OpenSellShop(-1, -1)\r\n            assert not (CheckSpecificPersonMenuIsOpen(6, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))\r\n");
                ifopA = "elif";
            }

            // smithing options
            if (npcContent.OffersSmithing())
            {
                s.Append($"        {ifopA} GetTalkListEntryResult() == {listCount++}:\r\n            OpenEnhanceShop(EnhanceType.UnlimitedRange)\r\n            assert not (CheckSpecificPersonMenuIsOpen(9, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))\r\n");
                ifopA = "elif";
            }

            // tailoring options
            if (npcContent.OffersTailoring())
            {
                s.Append($"        {ifopA} GetTalkListEntryResult() == {listCount++}:\r\n            OpenTailoringShop(111000, 111399)\r\n            assert not (CheckSpecificPersonMenuIsOpen(26, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))\r\n");
                ifopA = "elif";
            }

            // spell options
            if (spellShopId > 0)
            {
                s.Append($"        {ifopA} GetTalkListEntryResult() == {listCount++}:\r\n            OpenRegularShop({spellShopId}, {spellShopId + 99})\r\n            assert not (CheckSpecificPersonMenuIsOpen(5, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))\r\n");
                ifopA = "elif";
            }

            // memorize options
            if (npcContent.OffersMemorize())
            {
                s.Append($"        {ifopA} GetTalkListEntryResult() == {listCount++}:\r\n            OpenMagicEquip(-1, -1)\r\n            assert not (CheckSpecificPersonMenuIsOpen(11, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))\r\n");
                ifopA = "elif";
            }

            // enchanting options
            if (npcContent.OffersEnchanting())
            {
                int enchantShopId = itemManager.CreateShop(npcContent.stats.GetTier(CharacterContent.Stats.Skill.Enchant));
                s.Append($"        {ifopA} GetTalkListEntryResult() == {listCount++}:\r\n            OpenRegularShop({enchantShopId}, {enchantShopId + 99})\r\n            assert not (CheckSpecificPersonMenuIsOpen(5, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))\r\n");
                s.Append($"        {ifopA} GetTalkListEntryResult() == {listCount++}:\r\n            OpenEquipmentChangeOfPurposeShop()\r\n            assert not (CheckSpecificPersonMenuIsOpen(7, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))\r\n");
                ifopA = "elif";
            }

            // alchemy options
            if (npcContent.OffersAlchemy())
            {
                s.Append($"        {ifopA} GetTalkListEntryResult() == {listCount++}:\r\n");

                foreach (CharacterContent.Stats.Tier tier in Enum.GetValues(typeof(CharacterContent.Stats.Tier)))
                {
                    RecipeManager.RecipeBookInfo book = itemManager.recipeManager.GetBook(tier);
                    s.Append($"            if ComparePlayerStat(PlayerStat.Intelligence, CompareType.Greater, {(int)(((int)tier) * Const.ALCHEMY_TIER_REQUIREMENT_SCALE)}):\r\n");
                    s.Append($"                SetEventFlag({book.visible.id}, FlagState.On)\r\n");
                    s.Append($"            else:\r\n");
                    s.Append($"                pass\r\n");
                }

                int alchemyShopId = itemManager.recipeManager.GetShop();
                s.Append($"            OpenRegularShop({alchemyShopId}, {alchemyShopId + 99})\r\n            assert not (CheckSpecificPersonMenuIsOpen(5, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))\r\n");
                ifopA = "elif";
            }

            // travel options
            if (npcContent.travel.Count() > 0)
            {
                s.Append($"        {ifopA} GetTalkListEntryResult() == {listCount++}:\r\n            # travel menu\r\n            assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_TRAVELMENU:D2}()\r\n");
                ifopA = "elif";
            }

            // persuasion options
            s.Append($"        {ifopA} GetTalkListEntryResult() == {listCount++}:\r\n            # persuade menu\r\n            assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_PERSUADEMENU:D2}()\r\n");
            ifopA = "elif";

            for (int i = 0; i<topics.Count();i++)
            {
                NpcManager.TopicData topic = topics[i];
                if (topic.IsOnlyChoice()) { continue; } // skip these as they aren't valid or reachable

                s.Append($"        {ifopA} GetTalkListEntryResult() == {i + listCount}:\r\n");
                s.Append($"            # topic: \"{topic.dialog.id}\"\r\n");

                string ifopB = "if";
                foreach (NpcManager.TopicData.TalkData talk in topic.talks)
                {
                    if (talk.IsChoice()) { continue; } // choice dialogs are unreachable from this context, discard

                    string filters = talk.dialogInfo.GenerateCondition(itemManager, speffManager, scriptManager, npcContent);
                    if (filters == "") { filters = "True"; }

                    s.Append($"            {ifopB} {filters}:\r\n");
                    s.Append($"                # talk: \"{Common.Utility.SanitizeTextForComment(talk.dialogInfo.text)}\"\r\n");
                    s.Append($"                assert t{id_s}_x33(text2={talk.primaryTalkRow}, mode4=1)\r\n");

                    foreach (DialogRecord dialog in talk.dialogInfo.unlocks)
                    {
                        s.Append($"                SetEventFlag({dialog.flag.id}, FlagState.On)\r\n");
                    }

                    if (talk.dialogInfo.script != null)
                    {
                        if (talk.dialogInfo.script.calls.Count() > 0)
                        {
                            s.Append(talk.dialogInfo.script.GenerateEsdSnippet(esm, layout, msb, sound, paramanager, itemManager, speffManager, scriptManager, npcContent, id, 16));
                        }
                        if(talk.dialogInfo.script.choice != null)
                        {
                            int genChoiceStateId = GeneratedState_Choice(id, talk, topic);
                            s.Append($"                assert t{id_s}_x{genChoiceStateId}()\r\n");
                        }
                    }
                    if (ifopB == "if") { ifopB = "elif"; }
                }

                s.Append($"            else:\r\n                pass\r\n");  // not needed?
                if (ifopA == "if") { ifopA = "elif"; }
            }

            s.Append($"        else:\r\n            return 0\r\n\r\n");

            return s.ToString();
        }

        private string GeneratedState_ModDisposition(uint id, int x)
        {
            string id_s = id.ToString("D9");
            string s = $"def t{id_s}_x{x}(dispositionflag=_, value=_):\r\n    if GetEventFlagValue(dispositionflag, 8) + value < 0:\r\n        # disposition change will result in a negative number so set to 0\r\n        SetEventFlagValue(dispositionflag, 8, 0)\r\n    elif GetEventFlagValue(dispositionflag, 8) + value > 100:\r\n        # disposition change will result in a value higher than 100\r\n        SetEventFlagValue(dispositionflag, 8, 100)\r\n    else:\r\n        # disposition change is within acceptable bounds!\r\n        SetEventFlagValue(dispositionflag, 8, GetEventFlagValue(dispositionflag, 8) + value)\r\n    return 0\r\n\r\n";
            return s;
        }

        private string GeneratedState_ModFacRep(uint id, int x)
        {
            string id_s = id.ToString("D9");
            string s = $"def t{id_s}_x{x}(facrepflag=_, value=_):\r\n    if GetEventFlagValue(facrepflag, 8) + value < 0:\r\n        # faction reputation change will result in a negative number so set to 0\r\n        SetEventFlagValue(facrepflag, 8, 0)\r\n    elif GetEventFlagValue(facrepflag, 8) + value > 255:\r\n        # faction reputation change will result in a value higher than 255\r\n        SetEventFlagValue(facrepflag, 8, 255)\r\n    else:\r\n        # faction reputation change is within acceptable bounds!\r\n        SetEventFlagValue(facrepflag, 8, GetEventFlagValue(facrepflag, 8) + value)\r\n    return 0\r\n\r\n";
            return s;
        }

        private string GeneratedState_PersuadeMenu(uint id, int x, NpcManager.TopicData admireSuccess, NpcManager.TopicData admireFail, NpcManager.TopicData intimidateSuccess, NpcManager.TopicData intimidateFail, NpcManager.TopicData tauntSuccess, NpcManager.TopicData tauntFail, NpcManager.TopicData bribeSuccess, NpcManager.TopicData bribeFail)
        {
            string CreatePersuadeTalkIfTree(NpcManager.TopicData topic)
            {
                StringBuilder sb = new();
                string ifop = "if";
                for (int i = 0; i < topic.talks.Count(); i++)
                {
                    NpcManager.TopicData.TalkData talk = topic.talks[i];

                    string filters = talk.dialogInfo.GenerateCondition(itemManager, speffManager, scriptManager, npcContent);
                    if (filters == "") { filters = "True"; }

                    sb.Append($"                {ifop} {filters}:\r\n");
                    sb.Append($"                    # talk: \"{Common.Utility.SanitizeTextForComment(talk.dialogInfo.text)}\"\r\n");
                    sb.Append($"                    assert t{id:D9}_x33(text2={talk.primaryTalkRow}, mode4=1)");
                    if(i<topic.talks.Count()-1) { sb.Append("\r\n"); } // don't append \r\n on last line as the newline is implicit in string literal below
                    if(ifop == "if") { ifop = "elif"; }
                }
                return sb.ToString();
            }

            int dispText0 = textManager.GetTopic("Disposition: 0");  // all 100 dispoition texts are sequential so we grab the first one

            Script.Flag dvar = scriptManager.GetFlag(Script.Flag.Designation.Disposition, npcContent);
            Script.Flag hvar = scriptManager.GetFlag(Script.Flag.Designation.Hostile, npcContent);
            string s = $""""
                       def t{id:D9}_x{x:D2}():
                           while True:
                               ClearPreviousMenuSelection()
                               ClearTalkActionState()
                               ClearTalkListData()
                               ShuffleRNGSeed(100)
                               SetRNGSeed()
                               # action:##:"Disposition"
                               AddTalkListData(1, {dispText0} + GetEventFlagValue({dvar.id}, {dvar.Bits()}), -1)
                               # action:##:"Admire"
                               AddTalkListData(2, {textManager.GetTopic("Admire")}, -1)
                               # action:##:"Intimidate"
                               AddTalkListData(3, {textManager.GetTopic("Intimidate")}, -1)
                               # action:##:"Taunt"
                               AddTalkListData(4, {textManager.GetTopic("Taunt")}, -1)
                               # action:##:"Bribe"
                               AddTalkListData(5, {textManager.GetTopic("Bribe")}, -1)
                               # action:##:"Cancel"
                               AddTalkListData(99, {textManager.GetTopic("Back")}, -1)
                               ShowShopMessage(TalkOptionsType.Regular)
                               assert not (CheckSpecificPersonMenuIsOpen(1, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))
                               if GetTalkListEntryResult() == 1:
                                   ## Disposition display value
                                   pass
                               elif GetTalkListEntryResult() == 2:
                                   ## "Admire Check"
                                   if CompareRNGValue(CompareType.Greater, 50):
                                       ## "Admire Success"
                       {CreatePersuadeTalkIfTree(admireSuccess)}
                                       assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_MODDISPOSITION:D2}(dispositionflag={dvar.id}, value=10)
                                   else:
                                       ## "Admire Fail"
                       {CreatePersuadeTalkIfTree(admireFail)}
                                       assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_MODDISPOSITION:D2}(dispositionflag={dvar.id}, value=-10)
                               elif GetTalkListEntryResult() == 3:
                                   ## "Intimidate Check"
                                   if CompareRNGValue(CompareType.Greater, 50):
                                       ## "Intimidate Success"
                       {CreatePersuadeTalkIfTree(intimidateSuccess)}
                                       assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_MODDISPOSITION:D2}(dispositionflag={dvar.id}, value=10)
                                   else:
                                       ## "Intimidate Fail"
                       {CreatePersuadeTalkIfTree(intimidateFail)}
                                       assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_MODDISPOSITION:D2}(dispositionflag={dvar.id}, value=-10)
                               elif GetTalkListEntryResult() == 4:
                                   ## "Taunt Check"
                                   if CompareRNGValue(CompareType.Greater, 50):
                                       ## "Taunt Success"
                       {CreatePersuadeTalkIfTree(tauntSuccess)}
                                       assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_MODDISPOSITION:D2}(dispositionflag={dvar.id}, value=-25)
                                       SetEventFlag({hvar.id}, FlagState.On)
                                   else:
                                       ## "Taunt Fail"
                       {CreatePersuadeTalkIfTree(tauntFail)}
                                       assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_MODDISPOSITION:D2}(dispositionflag={dvar.id}, value=-10)
                               elif GetTalkListEntryResult() == 5:
                                   ## "Bribe Check"
                                   if CompareRNGValue(CompareType.Greater, 50):
                                       ## "Bribe Success"
                       {CreatePersuadeTalkIfTree(bribeSuccess)}
                                       ChangePlayerStat(PlayerStat.RunesCollected, ChangeType.Subtract, 100)
                                       assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_MODDISPOSITION:D2}(dispositionflag={dvar.id}, value=10)
                                   else:
                                       ## "Bribe Fail"
                       {CreatePersuadeTalkIfTree(bribeFail)}
                                       assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_MODDISPOSITION:D2}(dispositionflag={dvar.id}, value=-5)
                               else:
                                   ## "Cancel"
                                   return 0
                       
                       """";
            return s;
        }

        private string GeneratedState_TravelMenu(uint id, int x)
        {
            StringBuilder s = new();

            string a = $""""
                        def t{id:D9}_x{x:D2}():
                            while True:
                                ClearPreviousMenuSelection()
                                ClearTalkActionState()
                                ClearTalkListData()
                                ShuffleRNGSeed(100)
                                SetRNGSeed()

                        """";
            s.Append(a);

            int i = 1;
            foreach (CharacterContent.Travel travel in npcContent.travel)
            {
                string b = $""""
                                    # action:##:"{travel.name}"
                                    if ComparePlayerStat(PlayerStat.RunesCollected, CompareType.Greater, {travel.cost}):
                                        AddTalkListData({i++}, {textManager.GetTopic(travel.name)}, -1)
                                    else:
                                        pass

                            """";
                s.Append(b);
            }

            string c = $""""
                                # action:##:"Cancel"
                                AddTalkListData(99, {textManager.GetTopic("Back")}, -1)

                                ShowShopMessage(TalkOptionsType.Regular)
                                assert not (CheckSpecificPersonMenuIsOpen(1, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))

                        """";
            s.Append(c);

            i = 1;
            string ifop = "if";
            foreach (CharacterContent.Travel travel in npcContent.travel)
            {
                Script.Flag warpFlag = scriptManager.common.GetOrRegisterTravelWarp(travel);

                string d = $""""
                                    {ifop} GetTalkListEntryResult() == {i++}:
                                        ## Travel :: {travel.name}
                                        ChangePlayerStat(PlayerStat.RunesCollected, ChangeType.Subtract, {travel.cost})
                                        assert GetCurrentStateElapsedTime() > 0.25
                                        SetEventFlag({warpFlag.id}, FlagState.On)
                                        assert GetCurrentStateElapsedTime() > 1
                                        return 0

                            """";
                ifop = "elif";
                s.Append(d);
            }

            string e = $""""
                                else:
                                    ## "Cancel"
                                    return 0
                       
                        """";
            s.Append(e);
            return s.ToString();
        }

        /* Handles crimes */
        private string GeneratedState_HandleCrime(uint id, int x)
        {
            Script.Flag crimeFlag = scriptManager.GetFlag(Script.Flag.Designation.CrimeEvent, npcContent); // reports crime to nearby npcs if flagged and turns npcs hostile
            Script.Flag hostileFlag = scriptManager.GetFlag(Script.Flag.Designation.Hostile, npcContent); // turns this npc hostile but does not flag as crime
            Script.Flag crimeLevel = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel"); // crime gold flag
            Script.Flag crimeNotif = scriptManager.common.GetOrRegisterNotification(paramanager, "Your crime was reported!");
            Script.Flag arrestFlag = scriptManager.GetFlag(Script.Flag.Designation.Arrest, "Arrest");

            string s;
            switch(npcContent.witness)
            {
                case NpcContent.Witness.Guard:
                    s = $""""
                        def t{id:D9}_x{x:D2}(crimeGold=_,violent=_):
                            if violent or GetEventFlag({arrestFlag.id}):
                                SetEventFlag({crimeFlag.id}, FlagState.On)     ## flag crime to all nearby npcs, and turn us hostile
                            else:
                                pass
                            SetEventFlag({crimeNotif.id}, FlagState.On)    ## notify player crime was reported
                            SetEventFlagValue({crimeLevel.id}, {crimeLevel.Bits()}, GetEventFlagValue({crimeLevel.id}, {crimeLevel.Bits()}) + crimeGold)
                            GiveSpEffectToPlayer({(int)SpeffManager.Functional.Alarming})  ## add alarm speff to player since they did a crime
                            return 0

                        """";
                    break;
                case NpcContent.Witness.Citizen:
                    s = $""""
                        def t{id:D9}_x{x:D2}(crimeGold=_,violent=_):
                            SetEventFlag({crimeFlag.id}, FlagState.On)     ## flag crime to all nearby npcs
                            SetEventFlag({hostileFlag.id}, FlagState.On)   ## turn hostile to player
                            SetEventFlag({crimeNotif.id}, FlagState.On)    ## notify player crime was reported
                            SetEventFlagValue({crimeLevel.id}, {crimeLevel.Bits()}, GetEventFlagValue({crimeLevel.id}, {crimeLevel.Bits()}) + crimeGold)
                            GiveSpEffectToPlayer({(int)SpeffManager.Functional.Alarming})  ## add alarm speff to player since they did a crime
                            return 0

                        """";
                    break;
                default:
                case NpcContent.Witness.None:
                    s = $""""
                        def t{id:D9}_x{x:D2}(crimeGold=_,violent=_):
                            SetEventFlag({hostileFlag.id}, FlagState.On)   ## make us hostile to the player but don't report crime at all
                            GiveSpEffectToPlayer({(int)SpeffManager.Functional.Alarming})  ## add alarm speff to player since they did a crime
                            return 0

                        """";
                    break;
            }

            return s;
        }

        /* State that randomly has npc use talk lines in combat */
        private string GeneratedState_CombatDialogSelection(uint id, int x)
        {
            Script.Flag hostileFlag = scriptManager.GetFlag(Script.Flag.Designation.Hostile, npcContent);
            string s = $""""
                       def t{id:D9}_x{x:D2}():
                           while True:
                               if (not GetEventFlag({hostileFlag.id})) or IsPlayerDead() or IsCharacterDisabled():
                                   break
                               else:
                                   pass
                               ## randomly decide if we should do a combat talk
                               ShuffleRNGSeed(100)
                               SetRNGSeed()
                               if GetDistanceToPlayer() < 6 and GetCurrentStateElapsedTime() > 2:
                                   ## hostile quip
                                   if CompareRNGValue(CompareType.GreaterOrEqual, 70):
                                       assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_DOATTACKTALK:D2}()
                                   else:
                                       pass
                               elif GetDistanceToPlayer() < 6 and IsPlayerAttacking():
                                   ## hurt response
                                   if CompareRNGValue(CompareType.GreaterOrEqual, 30):
                                       assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_DOHITTALK:D2}()
                                       assert GetCurrentStateElapsedTime() > 1
                                   else:
                                       pass
                                   RemoveMyAggro()
                               elif (not GetEventFlag({hostileFlag.id})) or IsPlayerDead() or IsCharacterDisabled():
                                   ## get outta here
                                   pass
                           return 0
                       
                       """";

            return s;
        }

        private string GeneratedState_CombatTalk(uint id, int x)
        {
            string s = $""""
                       def t{id:D9}_x{x:D2}(combatText=_):
                           assert t{id:D9}_x31() and CheckSpecificPersonTalkHasEnded(0)
                           TalkToPlayer(combatText, -1, -1, 1)
                           ReportConversationEndToHavokBehavior()
                           return 0
                       
                       """";
            return s;
        }

        private string GeneratedState_DoAttackTalk(uint id, int x, NpcManager.TopicData topic)
        {
            string s = $"def t{id:D9}_x{x:D2}():\r\n    ## pick an attack line and talk it\r\n    ShuffleRNGSeed(100)\r\n    SetRNGSeed()\r\n";

            string ifop = "if";
            for (int i = 0; i < topic.talks.Count(); i++)
            {
                NpcManager.TopicData.TalkData talk = topic.talks[i];

                string filters = $" {talk.dialogInfo.GenerateCondition(itemManager, speffManager, scriptManager, npcContent)}";
                if (filters == " " || !(i < topic.talks.Count() - 1)) { filters = ""; ifop = "else"; i = topic.talks.Count(); }
                if (topic.talks.Count() == 1) { ifop = "if"; filters = " True"; } // special stupid case. does actually happen (rolls eyes)

                s += $"    {ifop}{filters}:\r\n";
                s += $"        # attack talk:{talk.primaryTalkRow}:\"{Utility.SanitizeTextForComment(talk.dialogInfo.text)}\"\r\n";
                s += $"        assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_COMBATTALK:D2}(combatText={talk.primaryTalkRow})\r\n";
                ifop = "elif";
            }
            s += $"    return 0\r\n";
            return s;
        }

        private string GeneratedState_DoHitTalk(uint id, int x, NpcManager.TopicData topic)
        {
            string s = $"def t{id:D9}_x{x:D2}():\r\n    ## pick a hit line and talk it\r\n    ShuffleRNGSeed(100)\r\n    SetRNGSeed()\r\n";

            string ifop = "if";
            for (int i = 0; i < topic.talks.Count(); i++)
            {
                NpcManager.TopicData.TalkData talk = topic.talks[i];

                string filters = $" {talk.dialogInfo.GenerateCondition(itemManager, speffManager, scriptManager, npcContent)}";
                if (filters == " " || !(i < topic.talks.Count() - 1)) { filters = ""; ifop = "else"; i = topic.talks.Count(); }
                if(topic.talks.Count() == 1) { ifop = "if"; filters = " True"; } // special stupid case. does actually happen (rolls eyes)

                s += $"    {ifop}{filters}:\r\n";
                s += $"        # hit talk:{talk.primaryTalkRow}:\"{Utility.SanitizeTextForComment(talk.dialogInfo.text)}\"\r\n";
                s += $"        assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_COMBATTALK:D2}(combatText={talk.primaryTalkRow})\r\n";
                ifop = "elif";
            }
            s += $"    return 0\r\n";
            return s;
        }

        private string GeneratedState_DoThiefTalk(uint id, int x, NpcManager.TopicData topic)
        {
            Script.Flag thiefFlag = scriptManager.GetFlag(Script.Flag.Designation.ThiefCrime, npcContent);

            string s = $"def t{id:D9}_x{x:D2}():\r\n    ## pick a thief line and talk it\r\n    ShuffleRNGSeed(100)\r\n    SetRNGSeed()\r\n";

            if(topic.talks.Count() == 1) // special case. does happen.
            {
                s += $"    assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_COMBATTALK:D2}(combatText={topic.talks[0].primaryTalkRow})\r\n";
                s += $"    return 0\r\n";
                return s;
            }

            string ifop = "if";
            for (int i = 0; i < topic.talks.Count(); i++)
            {
                NpcManager.TopicData.TalkData talk = topic.talks[i];

                string filters = $" {talk.dialogInfo.GenerateCondition(itemManager, speffManager, scriptManager, npcContent)}";
                if (filters == " " || !(i < topic.talks.Count() - 1)) { filters = ""; ifop = "else"; i = topic.talks.Count(); }


                s += $"    {ifop}{filters}:\r\n";
                s += $"        # thief talk:{talk.primaryTalkRow}:\"{Utility.SanitizeTextForComment(talk.dialogInfo.text)}\"\r\n";
                s += $"        assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_COMBATTALK:D2}(combatText={talk.primaryTalkRow})\r\n";
                ifop = "elif";
            }
            s += $"    SetEventFlag({thiefFlag.id}, FlagState.Off)\r\n";  // turn off thiefcrime event once we do our thief line
            s += $"    return 0\r\n";
            return s;
        }

        private string GeneratedState_IdleTalk(uint id, int x, NpcManager.TopicData idle, NpcManager.TopicData hello)
        {
            string idleCode = "";
            string ifop = "if";
            for (int i = 0; i < idle.talks.Count(); i++)
            {
                NpcManager.TopicData.TalkData talk = idle.talks[i];

                if(idle.talks.Count() == 1) { idleCode = $"            assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_COMBATTALK:D2}(combatText={talk.primaryTalkRow})"; break; } // special case. does happen.

                string filters = $" {talk.dialogInfo.GenerateCondition(itemManager, speffManager, scriptManager, npcContent)}";
                if (filters == " " || !(i < idle.talks.Count() - 1)) { filters = ""; ifop = "else"; i = idle.talks.Count(); }

                idleCode += $"            {ifop}{filters}:\r\n";
                idleCode += $"                # idle talk:{talk.primaryTalkRow}:\"{Utility.SanitizeTextForComment(talk.dialogInfo.text)}\"\r\n";
                idleCode += $"                assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_COMBATTALK:D2}(combatText={talk.primaryTalkRow})";
                if (i < idle.talks.Count() - 1) { idleCode += "\r\n"; }
                ifop = "elif";
            }

            string helloCode = "";
            ifop = "if";
            for (int i = 0; i < hello.talks.Count(); i++)
            {
                NpcManager.TopicData.TalkData talk = hello.talks[i];

                string filters = $" {talk.dialogInfo.GenerateCondition(itemManager, speffManager, scriptManager, npcContent)}";
                if (filters == " " || !(i < hello.talks.Count() - 1)) { filters = ""; ifop = "else"; i = hello.talks.Count(); }
                if (hello.talks.Count() == 1) { ifop = "if"; filters = " True"; } // special stupid case. does actually happen (rolls eyes)

                helloCode += $"            {ifop}{filters}:\r\n";
                helloCode += $"                # hello talk:{talk.primaryTalkRow}:\"{Utility.SanitizeTextForComment(talk.dialogInfo.text)}\"\r\n";
                helloCode += $"                assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_COMBATTALK:D2}(combatText={talk.primaryTalkRow})";
                if(i < hello.talks.Count() - 1) { helloCode += "\r\n"; }
                ifop = "elif";
            }

            Script.Flag playerIsSneaking = scriptManager.GetFlag(Script.Flag.Designation.PlayerIsSneaking, "PlayerIsSneaking");
            Script.Flag playerTalkingFlag = scriptManager.GetFlag(Script.Flag.Designation.PlayerIsTalking, "PlayerIsTalking");
            Script.Flag npcHelloFlag = scriptManager.GetFlag(Script.Flag.Designation.Hello, npcContent);
            Script.Flag thiefFlag = scriptManager.GetFlag(Script.Flag.Designation.ThiefCrime, npcContent);

            string s = $""""
                       def t{id:D9}_x{x:D2}():
                           ## occasionally do idle lines and if a player approaches us we hello them
                           while True:
                               if IsPlayerDead() or IsCharacterDisabled():
                                   break
                               else:
                                   pass
                               
                               ShuffleRNGSeed(100)
                               SetRNGSeed()
                               if GetEventFlag({thiefFlag.id}):
                                   ## yell about a thievery crime
                                   assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_DOTHIEFTALK:D2}()
                                   SetEventFlag({thiefFlag.id}, FlagState.Off)
                               elif (not GetEventFlag({playerIsSneaking.id})) and GetDistanceToPlayer() < {Const.NPC_HELLO_DIST_IN} and (not GetEventFlag({playerTalkingFlag.id})) and CompareRNGValue(CompareType.GreaterOrEqual, 15) and (not GetEventFlag({npcHelloFlag.id})):
                                   ShuffleRNGSeed(100)
                                   SetRNGSeed()
                                   SetEventFlag({npcHelloFlag.id}, FlagState.On)
                       {helloCode}
                                   assert GetCurrentStateElapsedTime() > 15 or GetEventFlag({thiefFlag.id})
                               elif GetDistanceToPlayer() > {Const.NPC_HELLO_DIST_OUT} and GetDistanceToPlayer() < {Const.NPC_IDLE_DIST_OUT} and (not GetEventFlag({playerTalkingFlag.id})) and CompareRNGValue(CompareType.GreaterOrEqual, 90) and GetCurrentStateElapsedTime() > 10:
                                   ShuffleRNGSeed(100)
                                   SetRNGSeed()
                       {idleCode}
                                   assert GetCurrentStateElapsedTime() > 25 or GetEventFlag({thiefFlag.id})
                               elif GetCurrentStateElapsedTime() > 10:
                                   pass

                               assert GetCurrentStateElapsedTime() > 0.25 or GetEventFlag({thiefFlag.id})
                           return 0

                       """";
            return s;
        }

        private string GeneratedState_Pickpocket(uint id, int x)
        {
            Script.Flag dispositionFlag = scriptManager.GetFlag(Script.Flag.Designation.Disposition, npcContent);
            Script.Flag pickpocketedFlag = scriptManager.GetFlag(Script.Flag.Designation.Pickpocketed, npcContent);
            Script.Flag thiefFlag = scriptManager.GetFlag(Script.Flag.Designation.ThiefCrime, npcContent);
            string s = $""""
                       def t{id:D9}_x{x:D2}():
                           ShuffleRNGSeed(100)
                           SetRNGSeed()

                           if CompareRNGValue(CompareType.GreaterOrEqual, 50):
                               ## picpocket success
                               SetEventFlag({pickpocketedFlag.id}, FlagState.On)
                               ChangePlayerStat(PlayerStat.RunesCollected, ChangeType.Add, 100)
                           else:
                               ## pickpocket fail
                               assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_MODDISPOSITION}(dispositionflag={dispositionFlag.id}, value={-10})
                               assert t{id:D9}_x{Const.ESD_STATE_HARDCODE_HANDLECRIME:D2}(crimeGold={Const.CRIME_GOLD_PICKPOCKET},violent=False)
                               SetEventFlag({thiefFlag.id}, FlagState.On)

                           return 0

                       """";
            return s;
        }

        private string GeneratedState_RankReq(uint id, int x)
        {
            string id_s = id.ToString("D9");
            string s = "";
            s += $"def t{id_s}_x{x}():\r\n";
            s += $"    #rankreq state: \"{npcContent.faction}\"\r\n";

            Script.Flag repFlag = scriptManager.GetFlag(Script.Flag.Designation.FactionReputation, npcContent.faction);
            Script.Flag rankFlag = scriptManager.GetFlag(Script.Flag.Designation.FactionRank, npcContent.faction);
            Script.Flag returnValue = areaScript.GetOrCreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Nibble, Script.Flag.Designation.ReturnValueRankReq, npcContent, 0, true);
            FactionInfo faction = esm.GetFaction(npcContent.faction);

            // First rank
            s += $"    if GetEventFlagValue({rankFlag.id}, {rankFlag.Bits()}) == 0:\r\n";
            s += $"         SetEventFlagValue({returnValue.id}, {returnValue.Bits()}, 3)\r\n";

            for (int i = 0; i < faction.ranks.Count()-1; i++)
            {
                FactionInfo.Rank rank = faction.ranks[i];
                FactionInfo.Rank nextRank = faction.ranks[i + 1];

                // Not max rank
                if (rank != nextRank)
                {
                    s += $"    elif GetEventFlagValue({rankFlag.id}, {rankFlag.Bits()}) == {rank.level}:\r\n";
                    s += $"        if GetEventFlagValue({repFlag.id}, {repFlag.Bits()}) >= {nextRank.reputation}:\r\n";
                    s += $"            SetEventFlagValue({returnValue.id}, {returnValue.Bits()}, 3)\r\n";
                    s += $"        else:\r\n";
                    s += $"            SetEventFlagValue({returnValue.id}, {returnValue.Bits()}, 1)\r\n";
                }
            }

            // Max rank
            s += $"    else:\r\n";
            s += $"        SetEventFlagValue({returnValue.id}, {returnValue.Bits()}, 0)\r\n";
            s += $"    return 0\r\n\r\n";

            return s;
        }

        // calculates values for reactionhigh and reactionlow and saves them in temp flags for filtercond to read from
        private string GeneratedState_ReactionCalc(uint id, int x)
        {
            string id_s = id.ToString("D9");
            string s = "";
            s += $"def t{id_s}_x{x}():\r\n";
            s += $"    #reactioncalc state: \"{npcContent.faction}\"\r\n";

            FactionInfo npcFaction = esm.GetFaction(npcContent.faction);
            Script.Flag returnLow = areaScript.GetOrCreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Nibble, Script.Flag.Designation.ReturnReactionLow, npcContent, 0, true);
            Script.Flag returnHigh = areaScript.GetOrCreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Nibble, Script.Flag.Designation.ReturnReactionHigh, npcContent, 0, true);

            // Special case where a faction has no reaction table (Talos Cult moment)
            if(!npcFaction.HasReactions())
            {
                s += $""""
                          SetEventFlagValue({returnLow.id}, {returnLow.Bits()}, 0)
                          SetEventFlagValue({returnHigh.id}, {returnHigh.Bits()}, 0)
                          return 0


                      """";
                return s;
            }

            string ifop = "if";
            List<(string id, int value)> lowReactions = npcFaction.GetLowReactions();
            foreach ((string id, int value) reaction in lowReactions)
            {
                Script.Flag playerInFactionFlag = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, reaction.id);

                s += $""""
                          {ifop} GetEventFlag({playerInFactionFlag.id}) == True:
                              # faction: {reaction.id} :: low reaction {reaction.value}
                              SetEventFlagValue({returnLow.id}, {returnLow.Bits()}, {Math.Min(7, Math.Max(0, -reaction.value))})

                      """";

                ifop = "elif";
            }

            s += $""""
                      else:
                          # no reaction result
                          SetEventFlagValue({returnLow.id}, {returnLow.Bits()}, 0)


                  """";

            ifop = "if";
            List<(string id, int value)> highReactions = npcFaction.GetHighReactions();
            foreach ((string id, int value) reaction in highReactions)
            {
                Script.Flag playerInFactionFlag = scriptManager.GetFlag(Script.Flag.Designation.FactionJoined, reaction.id);

                s += $""""
                          {ifop} GetEventFlag({playerInFactionFlag.id}) == True:
                              # faction: {reaction.id} :: high reaction {reaction.value}
                              SetEventFlagValue({returnHigh.id}, {returnHigh.Bits()}, {Math.Min(7, Math.Max(0, reaction.value))})

                      """";

                ifop = "elif";
            }

            s += $""""
                      else:
                          # no reaction result
                          SetEventFlagValue({returnHigh.id}, {returnHigh.Bits()}, 0)


                  """";

            s += $"    return 0\r\n\r\n";
            return s;
        }

        // If choice hasn't been generated already it creates it and returns the state # to call it.
        private int GeneratedState_Choice(uint id, NpcManager.TopicData.TalkData talk, NpcManager.TopicData topic)
        {
            if(choiceMap.ContainsKey(talk)) { return choiceMap[talk]; } // prevents recursive loops breaking everything

            string id_s = id.ToString("D9");
            int x = nxtGenStateId++;
            choiceMap.Add(talk, x);
            DialogPapyrus.PapyrusChoice choice = talk.dialogInfo.script.choice;

            string s = "";
            s += $"def t{id_s}_x{x}():\r\n    \"\"\"State 0\"\"\"\r\n    while True:\r\n        \"\"\"State 1\"\"\"\r\n        ClearPreviousMenuSelection()\r\n        ClearTalkActionState()\r\n        ClearTalkListData()\r\n        \"\"\"State 2\"\"\"\r\n";

            string createList = "";
            string executeList = "";

            string ifop = "if";
            List<int> addedChoices = new(); // to prevent adding duplicate choices to the same id. does not cause issues ingame, just a code cleanliness thing. multiple results on same id is fine, this just does the choice part
            foreach (Tuple<int, string> tuple in choice.choices)
            {
                int choiceId = tuple.Item1;
                string choiceText = tuple.Item2;

                for (int i = 0; i < topic.talks.Count(); i++)
                {
                    NpcManager.TopicData.TalkData talkData = topic.talks[i];
                    if (talkData.dialogInfo.type != DialogRecord.Type.Choice) { continue; }

                    // check if talkdata has the correct choice index
                    bool match = false;
                    foreach(Dialog.DialogFilter filter in talkData.dialogInfo.filters)
                    {
                        if(filter.type == DialogFilter.Type.Function && filter.function == DialogFilter.Function.Choice && filter.value == choiceId)
                        {
                            match = true; break;
                        } 
                    }
                    if (!match) { continue; }

                    if (!addedChoices.Contains(choiceId))
                    {
                        int choiceTextId = textManager.AddChoice(choiceText);
                        createList += $"        # action:{choiceTextId}:\"{choiceText}\"\r\n        AddTalkListData({choiceId}, {choiceTextId}, -1)\r\n";
                    }

                    string optFilters = talkData.dialogInfo.GenerateCondition(itemManager, speffManager, scriptManager, npcContent);
                    if(optFilters != "") { optFilters = $" and ({optFilters})"; }
                    executeList += $"        {ifop} GetTalkListEntryResult() == {choiceId}{optFilters}:\r\n            # choice: \"{Common.Utility.SanitizeTextForComment(talkData.dialogInfo.text)}\"\r\n            assert t{id_s}_x33(text2={talkData.primaryTalkRow}, mode4=1)\r\n";

                    foreach (DialogRecord dialog in talkData.dialogInfo.unlocks)
                    {
                        executeList += $"            SetEventFlag({dialog.flag.id}, FlagState.On)\r\n";
                    }

                    if (talkData.dialogInfo.script != null)
                    {
                        if (talkData.dialogInfo.script.calls.Count() > 0)
                        {
                            executeList += talkData.dialogInfo.script.GenerateEsdSnippet(esm, layout, msb, sound, paramanager, itemManager, speffManager, scriptManager, npcContent, id, 12);
                        }
                        if (talkData.dialogInfo.script.choice != null) // rare situation where a choice option goes into another choice option
                        {
                            int genChoiceStateId = GeneratedState_Choice(id, talkData, topic);
                            executeList += $"            assert t{id_s}_x{genChoiceStateId}()\r\n";
                        }
                    }

                    addedChoices.Add(choiceId);
                    if (ifop == "if") { ifop = "elif"; }
                }
            }

            // Bethesda bug workaround!
            // It is possible for a choice to have no valid options. Bethesda has some bugs like this in the base game
            // In this stupid case we just return a blank-ish def that just returns 0.
            if(createList == "")
            {
                generatedStates.Add($"def t{id_s}_x{x}():\r\n    \"\"\"State 0\"\"\"\r\n    return 0\r\n\r\n");
                return x;
            }

            s += createList;
            s += $"        \"\"\"State 3\"\"\"\r\n        ShowShopMessage(TalkOptionsType.Regular)\r\n        \"\"\"State 4\"\"\"\r\n        assert not (CheckSpecificPersonMenuIsOpen(1, 0) and not CheckSpecificPersonGenericDialogIsOpen(0))\r\n        \"\"\"State 5\"\"\"\r\n";
            s += executeList;
            s += "        else:\r\n            return 0\r\n";
            s += $"        \"\"\"State 10,11\"\"\"\r\n        return 1\r\n\r\n";

            generatedStates.Add(s);
            return x;
        }
    }
}
