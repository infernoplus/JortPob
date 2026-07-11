using JortPob.Common;
using JortPob.Scripts;
using Microsoft.Scripting.Utils;
using PortJob;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using static SoulsFormats.PARAM;

namespace JortPob
{
    public class WarpZone
    {
        /* Create debug warp area inside stranded graveyard. Also delete some connect colision to isolate that msb fully */
        public static void Generate(Layout layout, ScriptManager scriptManager, Paramanager paramanager)
        {

            /* DEBUG - Add a warp from stranded graveyard to various useful locations for debuggin */
            /* @TODO: DELETE THIS WHEN IT IS NO LONGER NEEDED! */
            MSBE debugMSB = MSBE.Read(Utility.ResourcePath(@"test\m18_00_00_00.msb.dcx"));
            MSBE.Part.Asset debugThingToDupe = null;
            uint debugEntityIdNext = 1042360750;
            foreach (MSBE.Part.Asset asset in debugMSB.Parts.Assets)
            {
                if (asset.Name == "AEG004_693_2000")
                {
                    debugThingToDupe = asset;
                    break;
                }
            }

            debugMSB.Parts.ConnectCollisions.Clear(); // delete all connection collision to isolate this msb as a debug area
            foreach(MSBE.Part.Player playerPart in debugMSB.Parts.Players)  // move player spawn points to debug room
            {
                playerPart.Position = new Vector3(-117.722f, 14.178f, 14.289f);
                playerPart.Rotation = new Vector3(0f, -58.686f, 0f);
            }
            foreach (MSBE.Region.SpawnPoint spawnPart in debugMSB.Regions.SpawnPoints) 
            {
                spawnPart.Position = new Vector3(-117.722f, 14.178f, 14.289f);
                spawnPart.Rotation = new Vector3(0f, -58.686f, 0f);
            }

            Vector3 lineStart = new(-110.107f, 14.5f, 11.222f);
            Vector3 lineEnd = new(-123.839f, 14.5f, 4.42f);

            Script debugScript = new(scriptManager, 18, 0, 0, 0);
            debugScript.init.Instructions.Add(debugScript.AUTO.ParseAdd($"RegisterBonfire(18000000, 18001950, 0, 0, 0, 5);"));
            debugScript.init.Instructions.Add(debugScript.AUTO.ParseAdd($"RegisterBonfire(18000001, 18001951, 0, 0, 0, 5);"));
            List<String> debugWarpCellList = new() { "Seyda Neen", "Balmora", "Tel Mora", "Pelagiad", "Caldera", "Khuul", "Gnisis", "Ald-ruhn", "Hla Oad" };
            int debugCounty = 0;
            for (int i = 0; i < debugWarpCellList.Count; i++)
            {
                string areaName = debugWarpCellList[i];
                Tile area = layout.GetTile(areaName);
                if (area != null && area.warps.Count > 0)
                {
                    MSBE.Part.Asset debugAsset = (MSBE.Part.Asset)(debugThingToDupe.DeepCopy());
                    debugAsset.ModelName = "AEG020_992"; // little candle
                    debugAsset.Position = Vector3.Lerp(lineStart, lineEnd, (1f / debugWarpCellList.Count) * i);
                    debugAsset.EntityID = debugEntityIdNext++;
                    debugAsset.Name = $"{debugAsset.ModelName}_{9001 + debugCounty}";
                    debugAsset.UnkPartNames[4] = debugAsset.Name;
                    debugAsset.UnkPartNames[5] = debugAsset.Name;
                    debugAsset.UnkT54PartName = debugAsset.Name;
                    debugAsset.InstanceID++;
                    debugMSB.Parts.Assets.Add(debugAsset);

                    Script.Flag debugEventFlag = debugScript.CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"m{debugScript.map}_{debugScript.x}_{debugScript.y}_{debugScript.block}::DebugWarp");
                    EMEVD.Event debugWarpEvent = new(debugEventFlag.id);

                    int actionButtonId = paramanager.GenerateActionButtonInteractParam($"Debug Warp: {areaName}");
                    debugWarpEvent.Instructions.Add(debugScript.AUTO.ParseAdd($"IfActionButtonInArea(MAIN, {actionButtonId}, {debugAsset.EntityID});"));
                    debugWarpEvent.Instructions.Add(debugScript.AUTO.ParseAdd($"WarpPlayer({area.map}, {area.coordinate.x}, {area.coordinate.y}, 0, {area.warps[0].id}, 0)"));

                    debugScript.init.Instructions.Add(debugScript.AUTO.ParseAdd($"InitializeEvent(0, {debugEventFlag.id})"));

                    debugScript.emevd.Events.Add(debugWarpEvent);
                    debugCounty++;
                }
            }

            /* Create a mass flag reset button next to the warps */
            List<Script.Flag> allFlags = [.. scriptManager.common.flags];
            foreach (Script script in scriptManager.scripts)
            {
                allFlags.AddRange(script.flags);
            }

            MSBE.Part.Asset debugResetAsset = (MSBE.Part.Asset)(debugThingToDupe.DeepCopy());
            debugResetAsset.ModelName = "AEG004_693"; // big candle
            debugResetAsset.Position = new Vector3(-114.354f, 14.5f, 16.841f);
            debugResetAsset.EntityID = debugEntityIdNext++;
            debugResetAsset.Name = $"{debugResetAsset.ModelName}_{9001 + debugCounty}";
            debugResetAsset.UnkPartNames[4] = debugResetAsset.Name;
            debugResetAsset.UnkPartNames[5] = debugResetAsset.Name;
            debugResetAsset.UnkT54PartName = debugResetAsset.Name;
            debugResetAsset.InstanceID++;
            debugMSB.Parts.Assets.Add(debugResetAsset);

            Script.Flag debugResetFlag = debugScript.CreateFlag(Script.Flag.Category.Event, Script.Flag.Type.Bit, Script.Flag.Designation.Event, $"m{debugScript.map}_{debugScript.x}_{debugScript.y}_{debugScript.block}::DebugReset");
            int actionButtonId2 = paramanager.GenerateActionButtonInteractParam($"Debug: Reset Save Data!");
            EMEVD.Event debugResetEvent = new(debugResetFlag.id);
            debugResetEvent.Instructions.Add(debugScript.AUTO.ParseAdd($"IfActionButtonInArea(MAIN, {actionButtonId2}, {debugResetAsset.EntityID});"));

            int delayCounter = 0; // if you do to much in a single frame the game crashes so we sometimes wait a frame
            foreach (Script.Flag flag in allFlags)
            {

                if (flag.category == Script.Flag.Category.Event) { continue; } // not values, used for event ids
                if (flag.category == Script.Flag.Category.Temporary) { continue; } // not even saved anyways so skip
                if (flag.designation == Script.Flag.Designation.PlayerRace) { continue; } // do not reset these as they are only set at character creation
                if (flag.designation == Script.Flag.Designation.Local && flag.name == "main.runonce") { continue; } // don't reset the papyrus main runonce flag until the very end (so the main startup script doesn't get started until we are done resetting all flag memory)
                if (flag.designation == Script.Flag.Designation.Hardcode && flag.name == "GameInit") { continue; } // another event we should not reset

                if (flag.type == Script.Flag.Type.Bit)
                {
                    debugResetEvent.Instructions.Add(debugScript.AUTO.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {flag.id}, {(flag.value == 1 ? "ON" : "OFF")});"));
                }
                else
                {
                    debugResetEvent.Instructions.Add(debugScript.AUTO.ParseAdd($"EventValueOperation({flag.id}, {flag.Bits()}, {flag.value}, 0, 1, CalculationType.Assign);"));
                }
                if (delayCounter++ > 512)
                {
                    debugResetEvent.Instructions.Add(debugScript.AUTO.ParseAdd($"WaitFixedTimeFrames(1);"));
                    delayCounter = 0;
                }
            }
            Script.Flag mainRunOnceFlag = scriptManager.GetFlag(Script.Flag.Designation.Local, "main.runonce");
            if (mainRunOnceFlag != null)  // runonce flag is actually a custom thing i wrote in to the main script in an esp so make this optional to prevent crash
            {
                debugResetEvent.Instructions.Add(debugScript.AUTO.ParseAdd($"EventValueOperation({mainRunOnceFlag.id}, {mainRunOnceFlag.Bits()}, {mainRunOnceFlag.value}, 0, 1, CalculationType.Assign);")); // after all other flags, reset main runonce so main can rerun initializer scripts
            }
            debugResetEvent.Instructions.Add(debugScript.AUTO.ParseAdd($"DisplayBanner(31);")); // display a banner when save data reset is done. it takes a secondish

            debugScript.emevd.Events.Add(debugResetEvent);
            debugScript.init.Instructions.Add(debugScript.AUTO.ParseAdd($"InitializeEvent(0, {debugResetFlag.id}, 0)"));

            debugScript.Write();
            AutoResource.Generate(18, 0, 0, 0, debugMSB);
            debugMSB.Write(Path.Combine(Const.OUTPUT_PATH, @"map\mapstudio\m18_00_00_00.msb.dcx"));
            Lort.Log($"Created {debugCounty} debug warps...", Lort.Type.Main);
        }
    }
}
