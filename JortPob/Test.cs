using JortPob.Common;
using JortPob.Model;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.IO;
using static JortPob.LiquidManager;

namespace JortPob
{
    public class Test
    {
        /* This entire class i just for adding temporary test code for things */
        /* Nothing in here actually matters and will not be used in actual builds. some of these may be debug flags in the const */
        /* Basically just have this to keep the resst of the program clean and free of random test code */

        /* Test ESD creation */
        public static void GenerateCharacterESD(Paramanager param, uint id)
        {
            /*
            SoundBank soundBank = new();
            soundBank.AddSound(@"sound\test_sound.wav", "Greeeeetings gamer girl owo cutie");
            soundBank.AddSound(@"sound\test_sound.wav", "Ouchie ow!");
            soundBank.AddSound(@"sound\test_sound.wav", "Oof stop!");
            soundBank.AddSound(@"sound\test_sound.wav", "Thhat's it, i'm gonna glomp you silly boy!");
            soundBank.AddSound(@"sound\test_sound.wav", "Guhhhhhh!! I'm dead!!");
            soundBank.AddSound(@"sound\test_sound.wav", "I killed you idiottitit!");
            soundBank.AddSound(@"sound\test_sound.wav", "Talking about things number one talking!");
            soundBank.AddSound(@"sound\test_sound.wav", "Talking about things number two talking!");
            soundBank.AddSound(@"sound\test_sound.wav", "I just super loooove talking about things!");
            soundBank.Write($"{Const.CACHE_PATH}sound\\vc315.bnk");

            TextManager textManager = new();
            param.GenerateTalkParam(textManager, soundBank);
            textManager.Write($"{Const.OUTPUT_PATH}msg\\engus\\menu_dlc02.msgbnd.dcx");

            DialogESD dialog = new(id, soundBank.sounds, true);
            string pyPath = $"{Const.CACHE_PATH}esd\\test_esd.py";
            dialog.Write(pyPath);

            string tempPy = Utility.ResourcePath(@"tools\ESDTool\temp.py");
            string tempESD = Utility.ResourcePath(@"tools\ESDTool\temp.esd");
            string esdData = System.IO.File.ReadAllText(pyPath);
            System.IO.File.WriteAllText(tempPy, esdData);

            ProcessStartInfo startInfo = new(Utility.ResourcePath(@"tools\ESDTool\esdtool.exe"), $"-er -basedir \"I:\\SteamLibrary\\steamapps\\common\\ELDEN RING\\Game\" -moddir \"I:\\SteamLibrary\\steamapps\\common\\ELDEN RING\\Game\\empty\" -i {tempPy} -writeloose {tempESD}")
            {
                WorkingDirectory = Utility.ResourcePath(@"tools\ESDTool"),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var process = Process.Start(startInfo);
            process.WaitForExit();

            ESD esd = ESD.Read(tempESD);

            BND4 bnd = BND4.Read(Utility.ResourcePath(@"esd\m60_00_00_00.talkesdbnd.dcx"));
            BinderFile file = new();
            file.Bytes = esd.Write();
            file.Name = $"N:\\GR\\data\\INTERROOT_win64\\script\\talk\\m60_00_00_00\\t{id.ToString("D9")}.esd";
            file.ID = bnd.Files.Last().ID + 1;

            bnd.Files.Add(file);
            bnd.Write($"{Const.OUTPUT_PATH}script\\talk\\m60_00_00_00.talkesdbnd.dcx");

            System.IO.File.Delete(tempPy);
            System.IO.File.Delete(tempESD);
            */
        }

        public static void RegeneratLandscape(ESM esm, Layout layout, Cache cache)
        {
            List<Tuple<Int2, int>> REGEN = new()
            {
                new(new Int2(-8, 8), 117), // ashimanu egg mine ext
                new(new Int2(-2, -9), 461), // seyda neen upper
                new(new Int2(-2, -10), 428) // seyda neen lower
            };

            Lort.Log("## DEBUG ## Regenerating some specific landscapes!", Lort.Type.Main);
            MaterialContext materialContext = new();

            List<Tuple<TerrainInfo, int>> OUTPUT = new();
            foreach (Tuple<Int2, int> values in REGEN)
            {
                Int2 coord = values.Item1;
                int id = values.Item2;

                TerrainInfo terrainInfo = cache.GetTerrain(coord);
                Landscape landscape = esm.GetLandscape(coord);
                ModelConverter.LANDSCAPEtoFLVER(materialContext, terrainInfo, landscape,
                    Path.Combine(Const.CACHE_PATH, terrainInfo.path));
                OUTPUT.Add(new(terrainInfo, id));
            }

            materialContext.WriteAll(); // while we dont need to regenerate textures, the matbins are needed so guh
            materialContext = null; // dispose
            Bind.BindMaterials(Path.Combine(Const.OUTPUT_PATH, "material", "allmaterial.matbinbnd.dcx"));

            // all our terrain map pieces are now in the super overworld so this is easier lol
            string map = "60";
            string name = "60_00_00_99";
            foreach (Tuple<TerrainInfo, int> values in OUTPUT)
            {
                TerrainInfo terrainInfo = values.Item1;
                int mpid = values.Item2;

                FLVER2 flver = FLVER2.Read(Path.Combine(Const.CACHE_PATH, terrainInfo.path));

                BND4 bnd = new();
                bnd.Compression = Compression.KRAK();
                bnd.Version = "07D7R6";

                BinderFile file = new();
                file.ID = 200;
                file.Name =
                    $"N:\\GR\\data\\INTERROOT_win64\\map\\m{name}\\m{name}_{mpid.ToString("D8")}\\Model\\m{name}_{mpid.ToString("D8")}.flver";
                file.Bytes = flver.Write();
                bnd.Files.Add(file);

                bnd.Write(Path.Combine(Const.OUTPUT_PATH,
                    $@"map\m60\m{name}\m{name}_{mpid.ToString("D8")}.mapbnd.dcx"));
            }


            Lort.Log("## DEBUG ## Done! Exit now via breakpoint pls~", Lort.Type.Main);
        }

        /* Regenerates all terrain without fully rebuilding cahce */
        public static void RegenerateLandscapes(ESM esm, Layout layout, Cache cache)
        {
            Lort.Log("## DEBUG ## Regenerating all landscapes!", Lort.Type.Main);
            //MATBIN test1 = MATBIN.Read(@"I:\SteamLibrary\steamapps\common\ELDEN RING\Game\material\allmaterial-matbinbnd-dcx\GR\data\INTERROOT_win64\material\matbin\Map_m60_00\matxml\AEG110_243_ID014.matbin");
            //MATBIN test2 = MATBIN.Read(@"I:\SteamLibrary\steamapps\common\ELDEN RING\Game\material\allmaterial_dlc02-matbinbnd-dcx\GR\data\INTERROOT_win64\material\matbin_DLC02\Map_m20_00\matxml\m20_00_801.matbin");

            MaterialContext materialContext = new();

            //LandscapeWorker.Go(materialContext, esm);

            materialContext.WriteAll(); // while we dont need to regenerate textures, the matbins are needed so guh
            materialContext = null; // dispose


            List<ResourcePool> pools = new();
            foreach (BaseTile tile in layout.AllTiles)
            {
                if (tile.assets.Count <= 0 && tile.terrain.Count <= 0)
                {
                    continue;
                } // Skip empty tiles.

                ResourcePool pool = new(tile, null, null);

                /* Add terrain */
                foreach (Tuple<Vector3, TerrainInfo> tuple in tile.terrain)
                {
                    Vector3 position = tuple.Item1;
                    TerrainInfo terrainInfo = tuple.Item2;

                    /* Terrain and terrain collision */
                    // Render goes in hugetile for long view distance. Collision goes in tile for optimization
                    if (tile.GetType() == typeof(HugeTile))
                    {
                        pool.mapIndices.Add(new Tuple<int, string>(terrainInfo.id, terrainInfo.path));
                    }
                }

                pools.Add(pool);
            }

            Lort.NewTask("Binding map pieces...", pools.Count);
            //TestWorker.Go(pools);
            Bind.BindMaterials(Path.Combine(Const.OUTPUT_PATH, @"material\allmaterial.matbinbnd.dcx"));
            Lort.Log("## DEBUG ## Done! Exit now via breakpoint pls~", Lort.Type.Main);
        }

        /* Just rebuilds the lava visual mesh. Used for rapid testing changes to lava material params */
        public static void RegenerateLava(Cache cache, ESM esm)
        {
            void ScoopEmUp(List<Cell> cells)
            {
                foreach (Cell cell in cells)
                {
                    void Scoop(List<Content> contents)
                    {
                        foreach (Content content in contents)
                        {
                            if (content.mesh == null)
                            {
                                continue;
                            } // skip content with no mesh

                            LiquidManager
                                .AddCutout(
                                    content); // check if this is a lava or swamp mesh and add it to cutouts if it is
                        }
                    }

                    Scoop(cell.contents);
                }
            }

            ScoopEmUp(esm.exterior);

            LiquidInfo lavaInfo = cache.GetLava();
            MaterialContext materialContext = new();
            WetMesh hotmesh = new WetMesh(LiquidManager.GetCutoutType(LiquidManager.Cutout.Type.Lava, true),
                LiquidManager.Cutout.Type.Lava);
            hotmesh.ToDebugObj().write(@"I:\SteamLibrary\steamapps\common\ELDEN RING\Game\mod\lavadebug.obj");
            FLVER2 lavaFlver = LiquidManager.GenerateLavaFlver(hotmesh, materialContext);
            lavaFlver.Write(Path.Combine(Const.CACHE_PATH, lavaInfo.path));
            Bind.BindAsset(lavaInfo, Path.Combine(Const.OUTPUT_PATH, $@"asset\aeg\{lavaInfo.AssetPath()}.geombnd.dcx"));
        }

        /* Just rebuilds the swamp visual mesh. Used for rapid testing changes to swamp material params */
        public static void RegenerateSwamp(Cache cache, ESM esm)
        {
            void ScoopEmUp(List<Cell> cells)
            {
                foreach (Cell cell in cells)
                {
                    void Scoop(List<Content> contents)
                    {
                        foreach (Content content in contents)
                        {
                            if (content.mesh == null)
                            {
                                continue;
                            } // skip content with no mesh

                            LiquidManager
                                .AddCutout(
                                    content); // check if this is a lava or swamp mesh and add it to cutouts if it is
                        }
                    }

                    Scoop(cell.contents);
                }
            }

            ScoopEmUp(esm.exterior);

            LiquidInfo swampInfo = cache.GetSwamp();
            MaterialContext materialContext = new();
            WetMesh swampMesh = new WetMesh(LiquidManager.GetCutoutType(LiquidManager.Cutout.Type.Swamp, true),
                LiquidManager.Cutout.Type.Swamp);
            swampMesh.ToDebugObj().write(@"I:\SteamLibrary\steamapps\common\ELDEN RING\Game\mod\swampdebug.obj");
            FLVER2 swampFlver = LiquidManager.GenerateSwampFlver(swampMesh, materialContext);
            swampFlver.Write(Path.Combine(Const.CACHE_PATH, swampInfo.path));
            Bind.BindAsset(swampInfo,
                Path.Combine(Const.OUTPUT_PATH, $@"asset\aeg\{swampInfo.AssetPath()}.geombnd.dcx"));

            materialContext.WriteAll(); // while we dont need to regenerate textures, the matbins are needed so guh
            materialContext = null; // dispose
            Bind.BindMaterials(Path.Combine(Const.OUTPUT_PATH, @"material\allmaterial.matbinbnd.dcx"));
        }

    }
}
