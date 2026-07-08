using JortPob.Common;
using JortPob.Scripts;
using JortPob.Worker;
using Newtonsoft.Json.Linq;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using static JortPob.Dialog;

namespace JortPob
{
    public class NpcManager
    {
        /* This class is responsible for managing the creation of ``Enemy`` objects in an MSB */
        /* In morrowind this is both the NPC and Creature record types */
        /* This class handles both the creation and assignment of params (NPCPARAM, NPCTHINKPARAM, CHARAINITPARAM) and manages ESDs and data for dialog generation and compiling */
        /* Morrowind makes a distinction between these 2 types of characters but ER does not */

        /* Bonus soda: This class also handles Beds because the morrowind c1000 object is technically an enemy so guh gughhh */

        /* Extra Bonus soda: This calls also now resolves default AiPackages */

        private readonly ESM esm;
        private readonly Layout layout;
        private readonly SoundManager soundManager;
        private readonly Paramanager paramanager;
        private readonly TextManager textManager;
        private readonly ItemManager itemManager;
        private readonly SpeffManager speffManager;
        private readonly ScriptManager scriptManager;

        private readonly Dictionary<string, int> topicText; // topic text id map
        private readonly List<EsdInfo> esds; // npcs, beds

        private int nextNpcParamId, nextNpcThinkParamId, nextCharInitId;  // increment by 10
        private int nextBedId;

        public NpcManager(ESM esm, Layout layout, SoundManager sound, Paramanager param, TextManager text, ItemManager item, SpeffManager speff, ScriptManager scriptManager)
        {
            this.esm = esm;
            this.layout = layout;
            this.soundManager = sound;
            this.paramanager = param;
            this.textManager = text;
            this.itemManager = item;
            this.speffManager = speff;
            this.scriptManager = scriptManager;

            esds = new();
            topicText = new();

            nextNpcParamId = 544900010;
            nextNpcThinkParamId = 544900010;
            nextCharInitId = 2050000;
            nextBedId = 90000;
        }

        public (int npc, int think, int init) GetParams(ItemManager itemManager, BaseScript script, NpcContent content)
        {
            int npcRow = GetNpcParam(itemManager, script, content);
            int thinkRow = GetThinkParam(itemManager, script, content);
            int charInitRow = GetCharInitParam(itemManager, content);

            return (npcRow, thinkRow, charInitRow);
        }

        private int GetNpcParam(ItemManager itemManager, BaseScript script, NpcContent content)
        {
            int id = nextNpcParamId += 10;
            paramanager.GenerateNpcParam(itemManager, script, content, id);
            return id;
        }

        private int GetThinkParam(ItemManager itemManager, BaseScript script, NpcContent content)
        {
            int id = nextNpcThinkParamId += 10;
            paramanager.GenerateThinkParam(itemManager, script, content, id);
            return id;
        }

        private int GetCharInitParam(ItemManager itemManager, NpcContent content)
        {
            int id = nextCharInitId += 10;
            paramanager.GenerateCharInitParam(itemManager, content, id);
            return id;
        }

        public (int npc, int think, int init) GetParams(ItemManager itemManager, BaseScript script, CreatureContent content, Override.EnemyRemap remap)
        {
            int npcRow = GetNpcParam(itemManager, script, content, remap);
            int thinkRow = GetThinkParam(itemManager, script, content, remap);

            return (npcRow, thinkRow, -1);
        }

        private int GetNpcParam(ItemManager itemManager, BaseScript script, CreatureContent content, Override.EnemyRemap remap)
        {
            int id = nextNpcParamId += 10;
            paramanager.GenerateNpcParam(itemManager, script, content, id, remap);
            return id;
        }

        private int GetThinkParam(ItemManager itemManager, BaseScript script, CreatureContent content, Override.EnemyRemap remap)
        {
            int id = nextNpcThinkParamId += 10;
            paramanager.GenerateThinkParam(itemManager, script, content, id, remap);
            return id;
        }

        public int GetESD(BaseTile tile, MSBE msb, CharacterContent content) { return GetESD(tile.IdList(), msb, content); }
        public int GetESD(InteriorGroup group, MSBE msb, CharacterContent content) { return GetESD(group.IdList(), msb, content); }

        /* Creates an ESD for the given instance of a npc */
        /* ESDs are generally 1 to 1 with characters but there are some exceptions like guards */
        public int GetESD(int[] msbIdList, MSBE msb, CharacterContent content)
        {
            if (content.race == CharacterContent.Race.Creature && !esm.HasDialog((CreatureContent)content)) { return 0; } // if this is a creature, verify it has dialog lines to build dialog for

            // First check if we even need one, hostile or dead npcs dont' get talk data for now
            if (content.dead) { return 0; }

            /* There used to be a check here that looked for an esd tied to the record id of the npc, i'm removing this */
            /* Every instance of an npc needs its own esd. Sharing esd's will only lead to horrible bugs long term */
            /* ESDs and event flags now use the entity id of the individual npc as their unique identifying value NOT THE RECORD ID */

            List<Tuple<DialogRecord, List<DialogInfoRecord>>> dialog = esm.GetDialog(scriptManager, content);
            SoundManager.SoundBankInfo bankInfo = soundManager.GetBank(content);

            List<TopicData> data = [];
            foreach ((DialogRecord dia, List<Dialog.DialogInfoRecord> infos) in dialog)
            {
                int topicId = 20000000; // generic "talk" as default, should never actually end up being used
                if (dia.type == DialogRecord.Type.Topic)
                {
                    if (!topicText.TryGetValue(dia.id, out topicId))
                    {
                        topicId = textManager.AddTopic(dia.id);
                        topicText.Add(dia.id, topicId);
                    }
                }

                TopicData topicData = new(dia, topicId);

                foreach (DialogInfoRecord info in infos)
                {
                    /* If this dialog is too long for a single subtitle we split it into pieces */
                    List<string> lines = Utility.CivilizedSplit(info.text);
                    List<int> talkRows = new();
                    int baseRow = -1;
                    for (int i = 0; i < lines.Count(); i++)
                    {
                        string line = lines[i];
                        /* Search existing soundbanks for the specific dialoginfo we are about to generate. if it exists just yoink it instead of generating a new one */
                        /* If we generate a new talkparam row for every possible line we run out of talkparam rows entirely and the project fails to build */
                        /* This sharing is required, and unfortunately it had to be added in at the end so its not a great implementation */
                        SoundBank.Sound snd = soundManager.FindSound(content, info.id + i); // look for a generated wem sound that matches the npc (race/sex) and dialog line (dialoginforecord id)

                        // Use an existing wem and talkparam we already generated because it's a match
                        if (snd != null) { talkRows.Add(bankInfo.bank.AddSound(snd)); continue; } // and continue here

                        /* Debug voice acting using SAM */
                        string wemFile;
                        uint nxtid = (uint)(info.id + i);
                        string lineHash = $"{content.race}{content.sex}{line}";
                        if (Override.CheckCustomVoice(content.name))
                        {
                            lineHash = $"{content.name}{line}";
                        }
                        var hashName = $"{lineHash.GetMD5Hash}+{i}";
                        if (Const.USE_SAM && !Const.DEBUG_SKIP_SOUND) { wemFile = soundManager.GenerateLine(dia, info, line, hashName, content); }
                        else { wemFile = Const.DEFAULT_DIALOG_WEM; }

                        // If this is not the first line in a talkparam group we must generate with sequential ids!
                        if (baseRow >= 0)
                        {
                            talkRows.Add(bankInfo.bank.AddSound(wemFile, info.id + i, line, (uint)(baseRow + i)));
                        }
                        // Make a new sound and talkparam row because no suitable match was found!
                        else
                        {
                            baseRow = bankInfo.bank.AddSound(wemFile, info.id + i, line);
                            talkRows.Add(baseRow);
                        }
                    }
                    // The parmanager function will automatically skip duplicates when addign talkparam rows so we don't need to do anything here. the esd gen needs those dupes so ye
                    topicData.talks.Add(new(info, talkRows, lines));
                }

                if (topicData.talks.Count > 0) { data.Add(topicData); } // if no valid lines for a topic, discard
            }
            paramanager.GenerateTalkParam(data);

            int esdId = int.Parse($"{bankInfo.id.ToString("D3")}{bankInfo.uses++.ToString("D2")}{msbIdList[0]:D2}{(msbIdList[0] == 60 ? 0 : msbIdList[1]):D2}");  // i know guh guhhhhh

            BaseScript areaScript = scriptManager.GetScript(msbIdList[0], msbIdList[1], msbIdList[2], msbIdList[3]); // get area script for this npc

            DialogESD dialogEsd = new(esm, layout, msb, soundManager.main, scriptManager, paramanager, textManager, itemManager, speffManager, areaScript, (uint)esdId, content, data);
            string pyPath = Path.Combine(Const.CACHE_PATH, "esd", $"t{esdId:D9}.py");
            string esdPath = Path.Combine(Const.CACHE_PATH, "esd", $"t{esdId:D9}.esd");
            dialogEsd.Write(pyPath);

            EsdInfo esdInfo = new(pyPath, esdPath, content.id, esdId);
            esds.Add(esdInfo);

            return esdId;
        }

        /* Setup a default ai package for an character */
        // does not support time of day values for packages. they are basically unused so it shouldnt even matter
        // does not support "Escort" package, literally unused in base game
        // does not support follow endpoint goals, i have no idea why you would use those in a default package anyawys so yeah
        // follow only supports player. i dont think its possible to do npcs following eachother in elden ring
        public void SetupPackages(MSBE msb, BaseScript script, CharacterContent content)
        {
            // Function for creating events for duration based timers
            Script.Flag CreateDurationEvent(Script.Flag packageIndexFlag, int i, float duration)
            {
                Script.Flag timerEvtFlag = script.CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"AiPackageTimer::{content.entity}::{i:D2}");
                EMEVD.Event timerEvt = new();
                timerEvt.ID = timerEvtFlag.id;
                timerEvt.Instructions.Add(script.AUTO.ParseAdd($"WaitFixedTimeSeconds({duration});"));                                                    // wait for duration
                timerEvt.Instructions.Add(script.AUTO.ParseAdd($"EventValueOperation({packageIndexFlag.id}, {packageIndexFlag.Bits()}, 1, 0, 1, 0);"));  // increment package index by 1
                script.emevd.Events.Add(timerEvt);
                content.packageEventFlags.Add(timerEvtFlag);
                return timerEvtFlag;
            }

            // Quick check to make sure we should even...
            if (content.dead) { return; } // get the fuck outttaaa heeeereeeee

            // This is the "Do nothing forever" check. In this case we don't need to create a packageFlag or do any scripts. They will just stand there!
            if (!content.IsGuard() && (content.packages.Count() <= 0 || (content.packages[0].type == CharacterContent.AiPackage.Type.Wander && content.packages[0].distance == 0 && content.packages[0].duration == 0))) { return; }

            // Create flag for package index
            Script.Flag packageFlag = script.GetOrCreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Nibble, Script.Flag.Designation.AiPackage, content.entity.ToString());  // purposefully avoid phased rerouting
            List<string> code = new();

            // Generate emevd code
            for (int i = 0; i < content.packages.Count() && i < 16; i++)  // max allowed packages is 16. we could expand this if needed but lmao, 99% of npcs have 1 package
            {
                CharacterContent.AiPackage package = content.packages[i];

                if (package.type == CharacterContent.AiPackage.Type.Wander && package.distance <= 0)
                {
                    // This is the "DO NOTHING" package
                    if (package.duration <= 0)
                    {
                        code.Add($"EndUnconditionally(EventEndType.End);");  // do nothing forever!
                    }
                    // If our DO NOTHING package has a duration we need some code to wait till its done
                    else
                    {
                        List<string> scope = new();

                        if (package.duration > 0)
                        {
                            // Initialize a timer event if this event has a duration set
                            float duration = 2.5f * 60f * package.duration; // mw uses hours, er uses seconds. 1 hour in morrowind is 2.5~ minutes
                            Script.Flag timerEvtFlag = CreateDurationEvent(packageFlag, i, duration);
                            scope.Add($"InitializeEvent(0, {timerEvtFlag.id}, 0);");
                        }

                        code.Add($"IfElapsedSeconds(MAIN, 0);");                                              // reset conditions groups
                        code.Add($"IfEventValue(OR_01, {packageFlag.id}, {packageFlag.Bits()}, 0, {i});");   // if package flag matches this particular packages index
                        code.Add($"SkipIfConditionGroupStateUncompiled({scope.Count()}, FAIL, OR_01);");    // skip if fails, do if pass
                        code.AddRange(scope);
                    }
                }
                else if (package.type == CharacterContent.AiPackage.Type.Wander)
                {
                    List<string> scope = new();
                    List<Layout.PathGridPoint> paths = layout.GetWanderable(content, package.distance);
                    paths.Shuffle();                                            // randomize
                    if (paths.Count() > 15) { paths = paths.GetRange(0, 15); } // truncate to max size of nibble
                    Script.Flag wanderFlag = script.GetOrCreateFlag(Script.Flag.Category.Temporary, Script.Flag.Type.Nibble, Script.Flag.Designation.Wander, content, 0, true);

                    //const float MOVESPEED = 1.1f; // npc walking averages around 2~ units per second. we use this to estimate a worst case duration

                    if (package.duration > 0)
                    {
                        // Initialize a timer event if this event has a duration set
                        float duration = 2.5f * 60f * package.duration; // mw uses hours, er uses seconds. 1 hour in morrowind is 2.5~ minutes
                        Script.Flag timerEvtFlag = CreateDurationEvent(packageFlag, i, duration);
                        scope.Add($"InitializeEvent(0, {timerEvtFlag.id}, 0);");
                    }

                    scope.Add($"WaitRandomTimeSeconds(0, 3);");    // chill for a bit between wandering around

                    // Regular wander but no pathgrid so just improvise with type 6 patrol "Randomly wander around"
                    if (paths.Count() <= 0)
                    {
                        MSBE.Event.PatrolInfo patrol = MakePart.PatrolRandom();
                        patrol.EntityID = script.CreateEntity(Script.EntityType.Event, $"Random->{patrol.Name}");
                        msb.Events.Add(patrol);

                        scope.Add($"ChangeCharacterPatrolBehavior({content.entity}, {patrol.EntityID});");          // set route to "wander randomly"
                        scope.Add($"WaitFixedTimeSeconds(3);");                                                    // Little wait between loops
                    }
                    // Regular wander on pathgrid
                    else
                    {
                        Vector3 last = content.relative;
                        for (int j = 0; j < paths.Count(); j++)
                        {
                            Layout.PathGridPoint path = paths[j];
                            float distance = Vector3.Distance(last, path.position);
                            last = path.position;

                            MSBE.Event.PatrolInfo patrol = MakePart.PatrolTo(path);
                            patrol.EntityID = script.CreateEntity(Script.EntityType.Event, $"Goto->{patrol.Name}");
                            msb.Events.Add(patrol);

                            scope.Add($"IfElapsedSeconds(MAIN, 0);");                                                                // reset conditions groups
                            scope.Add($"IfEventValue(OR_01, {wanderFlag.id}, {wanderFlag.Bits()}, 0, {j});");                       // if wander flag equals the index of this path
                            scope.Add($"SkipIfConditionGroupStateUncompiled(2, FAIL, OR_01);");                                    // ...
                            scope.Add($"ChangeCharacterPatrolBehavior({content.entity}, {patrol.EntityID})");                     // move to new wander position
                            scope.Add($"IfInoutsideArea(MAIN, InsideOutsideState.Inside, {content.entity}, {path.entity}, 1);"); // block until arrived at location
                                                                                                                                 //scope.Add($"WaitFixedTimeSeconds({MOVESPEED * distance});");
                        }

                        scope.Add($"IfElapsedSeconds(MAIN, 0);");                                                                // reset conditions groups
                        scope.Add($"IfEventValue(OR_01, {wanderFlag.id}, {wanderFlag.Bits()}, 4, {paths.Count() - 1});");         // if wander flag is great than the number of paths...
                        scope.Add($"SkipIfConditionGroupStateUncompiled(2, PASS, OR_01);");                                    // ...
                        scope.Add($"EventValueOperation({wanderFlag.id}, {wanderFlag.Bits()}, 1, 0, 1, 0);");                 // increment wander flag +1
                        scope.Add($"SkipUnconditionally(1);");                                                               // else ...
                        scope.Add($"EventValueOperation({wanderFlag.id}, {wanderFlag.Bits()}, 0, 0, 1, 5);");               // wander flag back to 0
                        scope.Add($"WaitRandomTimeSeconds(1.5, 10);");                                                     // chill for a bit between wandering around again
                    }

                    code.Add($"IfElapsedSeconds(MAIN, 0);");                                              // reset conditions groups
                    code.Add($"IfEventValue(OR_01, {packageFlag.id}, {packageFlag.Bits()}, 0, {i});");   // if package flag matches this particular packages index
                    code.Add($"SkipIfConditionGroupStateUncompiled({scope.Count()}, FAIL, OR_01);");    // skip if fails, do if pass
                    code.AddRange(scope);

                    if (package.duration <= 0) { break; } // wander FOREVER
                }
                else if (package.type == CharacterContent.AiPackage.Type.Travel)
                {
                    List<string> scope = new();

                    Layout.TravelPoint tp = layout.FindTravelable(content, package.position);
                    if (tp == null) { break; } // partial build result, or travel point was in a differnt msb so we can't ref it in a patrol

                    MSBE.Event.PatrolInfo patrol = MakePart.PatrolTo(tp);
                    patrol.EntityID = script.CreateEntity(Script.EntityType.Event, $"Goto->{patrol.Name}");
                    msb.Events.Add(patrol);

                    scope.Add($"ChangeCharacterPatrolBehavior({content.entity}, {patrol.EntityID})");                      // move to travel position
                    scope.Add($"RequestCharacterAIReplan({content.entity});");                                            // replan request to make pathing not stupid
                    scope.Add($"IfInoutsideArea(MAIN, InsideOutsideState.Inside, {content.entity}, {tp.entity}, 1);");   // block until arrived at location
                    scope.Add($"EventValueOperation({packageFlag.id}, {packageFlag.Bits()}, 1, 0, 1, 0);");             // Increment package index +1

                    code.Add($"IfElapsedSeconds(MAIN, 0);");                                              // reset conditions groups
                    code.Add($"IfEventValue(OR_01, {packageFlag.id}, {packageFlag.Bits()}, 0, {i});");   // if package flag matches this particular packages index
                    code.Add($"SkipIfConditionGroupStateUncompiled({scope.Count()}, FAIL, OR_01);");    // skip if fails, do if pass
                    code.AddRange(scope);
                }
                else if (package.type == CharacterContent.AiPackage.Type.Follow)
                {
                    List<string> scope = new();

                    if (package.duration > 0)
                    {
                        // Initialize a timer event if this event has a duration set
                        float duration = 2.5f * 60f * package.duration; // mw uses hours, er uses seconds. 1 hour in morrowind is 2.5~ minutes
                        Script.Flag timerEvtFlag = CreateDurationEvent(packageFlag, i, duration);
                        scope.Add($"InitializeEvent(0, {timerEvtFlag.id}, 0);");
                    }

                    scope.Add($"SetSpEffect({content.entity}, {(int)SpeffManager.Functional.NpcFollow});");     // add follower SPEFF to character

                    code.Add($"IfElapsedSeconds(MAIN, 0);");                                              // reset conditions groups
                    code.Add($"IfEventValue(OR_01, {packageFlag.id}, {packageFlag.Bits()}, 0, {i});");   // if package flag matches this particular packages index
                    code.Add($"SkipIfConditionGroupStateUncompiled({scope.Count()}, FAIL, OR_01);");    // skip if fails, do if pass
                    code.AddRange(scope);

                    if (package.duration <= 0) { break; } // wander FOREVER
                }
                else if (package.type == CharacterContent.AiPackage.Type.Escort)
                {
                    // not implemented for now. not used in morrowind.esm ever as an ai package. only ever used from script calls
                }
            }

            if (code.Count() <= 0 && !content.IsGuard()) { return; } // if we generated a nothing burger lets not bother compiling it

            // Inject a few lines at the top for dead/disable/phase handling
            Script.Flag deadFlag = scriptManager.GetFlag(Script.Flag.Designation.Dead, content);
            Script.Flag disableFlag = scriptManager.GetFlag(Script.Flag.Designation.Disabled, content);
            List<string> startup = [
                $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {deadFlag.id});",      // if dead...
                $"EndUnconditionally(EventEndType.End);",                                      // kill event early
            ];
            if (disableFlag != null) { startup.Add($"IfEventFlag(MAIN, OFF, TargetEventFlagType.EventFlag, {disableFlag.id});"); }  // blocking wait until character is not disabled
            if (content is PhasedNpcContent phased)
            {
                Script.Flag phaseFlag = scriptManager.GetFlag(Script.Flag.Designation.Phase, content);
                startup.Add($"IfEventValue(MAIN, {phaseFlag.id}, {phaseFlag.Bits()}, 0, {phased.phase});"); // blocking wait until phase matches this npcs phase
            }

            // Inject a few more lines here for guard chase down
            if (content.IsGuard())
            {
                Script.Flag crimeLevelFlag = scriptManager.GetFlag(Script.Flag.Designation.CrimeLevel, "CrimeLevel");
                Script.Flag hostileFlag = scriptManager.GetFlag(Script.Flag.Designation.Hostile, content);
                Script.Flag arrestFlag = scriptManager.GetFlag(Script.Flag.Designation.Arrest, "Arrest");
                startup.Add($"IfEventValue(AND_01, {crimeLevelFlag.id}, {crimeLevelFlag.Bits()}, 2, 0);");           // if player has a bounty AND...   (2 is Greater)
                startup.Add($"IfEventFlag(AND_01, OFF, TargetEventFlagType.EventFlag, {hostileFlag.id});");         // we are not hostile
                startup.Add($"SkipIfConditionGroupStateUncompiled(4, FAIL, AND_01);");
                startup.Add($"SetSpEffect({content.entity}, {(int)SpeffManager.Functional.NpcFollow});");  // apply follow player speff
                startup.Add($"RequestCharacterAIReplan({content.entity});");                              // make brain work
                startup.Add($"WaitFixedTimeFrames(15);");                                                // wait a few frames
                startup.Add($"EndUnconditionally(EventEndType.Restart);");                              // restart
            }

            code.InsertRange(0, startup);

            // Restart ai packages if we reach the end
            if (content.packages.Count() > 0)
            {
                code.Add($"IfEventValue(OR_01, {packageFlag.id}, {packageFlag.Bits()}, 0, {content.packages.Count()});");     // if package flag = the last index + 1 (we dont use > because 15 is "scripted")
                code.Add($"EventValueOperation({packageFlag.id}, {packageFlag.Bits()}, 0, 0, 1, 5);");                       // package flag back to 0
            }

            code.Add($"EndUnconditionally(EventEndType.Restart);");  // restart

            // Compile code
            Script.Flag evtFlag = script.CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"AiPackage::{content.entity}");
            EMEVD.Event evt = new();
            evt.ID = evtFlag.id;
            foreach (string line in code) { evt.Instructions.Add(script.AUTO.ParseAdd(line)); }
            script.emevd.Events.Add(evt);
            script.init.Instructions.Add(script.AUTO.ParseAdd($"InitializeEvent(0, {evtFlag.id}, 0);"));
            content.packageEventFlags.Add(evtFlag);
            content.packageDefaultFlag = evtFlag;
        }

        public int GetESD(BaseTile tile, MSBE msb, BedContent content) { return GetESD(tile.IdList(), msb, content); }
        public int GetESD(InteriorGroup group, MSBE msb, BedContent content) { return GetESD(group.IdList(), msb, content); }
        public int GetESD(int[] msbIdList, MSBE msb, BedContent content)
        {
            int esdId = int.Parse($"{nextBedId++}{msbIdList[0]:D2}{(msbIdList[0] == 60 ? 0 : msbIdList[1]):D2}");  // i know guh guhhhhh
            BedESD bedEsd = new(layout, scriptManager, paramanager, textManager, content, esdId);
            string pyPath = Path.Combine(Const.CACHE_PATH, "esd", $"t{esdId:D9}.py");
            string esdPath = Path.Combine(Const.CACHE_PATH, "esd", $"t{esdId:D9}.esd");
            bedEsd.Write(pyPath);

            EsdInfo esdInfo = new(pyPath, esdPath, "bed", esdId);
            esds.Add(esdInfo);

            return esdId;
        }

        /* ESDs are now 1 to 1 with individual placements of enemies/creatures so the file writing has been simplified */
        public void Write()
        {
            EsdWorker.Go(esds);

            Lort.Log($"Binding {esds.Count()} ESDs...", Lort.Type.Main);
            Lort.NewTask($"Binding ESDs", esds.Count());

            /* Create all needed bnds */
            Dictionary<(int, int), BND4> bnds = new();
            foreach (EsdInfo esd in esds)
            {
                if (!bnds.ContainsKey((esd.map, esd.area)))
                {
                    BND4 bnd = new();
                    bnd.Compression = Compression.KRAK();
                    bnd.Version = "07D7R6";
                    bnds.Add((esd.map, esd.area), bnd);
                }
            }

            /* Write esds to bnds */
            foreach (EsdInfo esd in esds)
            {
                BND4 bnd = bnds[(esd.map, esd.area)];
                BinderFile file = new();
                file.Bytes = System.IO.File.ReadAllBytes(esd.esd);
                file.Name = $"N:\\GR\\data\\INTERROOT_win64\\script\\talk\\m{esd.map:D2}_{esd.area:D2}_00_00\\{Path.GetFileNameWithoutExtension(esd.esd)}.esd";
                file.ID = bnd.Files.Count();

                bnd.Files.Add(file);
                Lort.TaskIterate();
            }

            /* Write bnds to file */
            Lort.Log($"Writing {bnds.Count} Binded ESDs... ", Lort.Type.Main);
            Lort.NewTask($"Writing {bnds.Count} Binded ESDs... ", bnds.Count);
            foreach (KeyValuePair<(int, int), BND4> kvp in bnds)
            {
                int map = kvp.Key.Item1;
                int area = kvp.Key.Item2;
                BND4 bnd = kvp.Value;

                bnd.Write(Path.Combine(Const.OUTPUT_PATH, $@"script\talk\m{map:D2}_{area:D2}_00_00.talkesdbnd.dcx"));
                Lort.TaskIterate();
            }
        }

        public class EsdInfo
        {
            public readonly string py, esd, content;
            public readonly int id;  // esd id
            public readonly int map, area; // msb ids

            public EsdInfo(string py, string esd, string content, int id)
            {
                this.py = py;                                // path to the python source file
                this.esd = esd;        // path to compiled esd
                this.content = content;
                this.id = id;
                string m = id.ToString().Substring(5, 2);
                string a = id.ToString().Substring(7, 2);
                map = int.Parse(m);
                area = int.Parse(a);
            }
        }

        public class TopicData
        {
            public readonly DialogRecord dialog;
            public readonly int topicText;
            public readonly List<TalkData> talks;

            public TopicData(DialogRecord dialog, int topicText)
            {
                this.dialog = dialog;
                this.topicText = topicText;
                this.talks = new();
            }

            /* Blank constructor only used by creature dialog generation for blank topics like voice */
            public TopicData()
            {
                dialog = null;
                topicText = -1;
                talks = new();
            }

            /* Special case where a topic contains only infos with the filter type "choice" making it unreachable */
            public bool IsOnlyChoice()
            {
                foreach (TalkData talk in talks)
                {
                    if (talk.dialogInfo.type != DialogRecord.Type.Choice) { return false; }
                }
                return true;
            }

            /* Check if a rank requirment filter is used anywhere in this topicdata */
            public bool HasRankRequirementFilter()
            {
                foreach (TalkData talk in talks)
                {
                    foreach (DialogFilter filter in talk.dialogInfo.filters)
                    {
                        if (filter.type == DialogFilter.Type.Function && filter.function == DialogFilter.Function.RankRequirement)
                        {
                            return true;
                        }
                    }
                }
                return false;
            }

            public class TalkData
            {
                public readonly DialogInfoRecord dialogInfo;
                public readonly int primaryTalkRow;  // first row for this talk, all that really matters game engine automatically plays subsequent rows in order
                public readonly List<int> talkRows;
                public readonly List<string> splitText;

                public TalkData(DialogInfoRecord dialogInfo, List<int> talkRows, List<string> splitText)
                {
                    this.dialogInfo = dialogInfo;
                    this.primaryTalkRow = talkRows[0];
                    this.talkRows = talkRows;
                    this.splitText = splitText;
                }

                public bool IsChoice()
                {
                    return dialogInfo.type == DialogRecord.Type.Choice;
                }
            }
        }


    }
}
