using JortPob.Common;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.IO;

namespace JortPob
{

    /* Handles fxr files for the morrowind world space, things like candle flames, fires, smoke, etc */
    /* Morrowind world space doesn't really have a lot of effects so this should be a pretty simple class */
    /* I've decided to write our fxr files to the map group specific ffxbnd. we are not going to write to common as that feels like bad practice */
    /* So when we write to file we need to write our custom ffx to each map group. m60 being the overworld etc etc */
    public class FxrManager
    {

        /* some notes on fxr ids */
        // campfire from church of elleh = 7000
        // single candle from round table, no dynamic light = 800401

        public static List<FXR> FXRS = new List<FXR>() // @TODO: this could probably be a json file
        {
            new FXR(Const.FXR_START_ID, "candleflame emitter"),
            new FXR(Const.FXR_START_ID+1, "fire emitter"),
            new FXR(Const.FXR_START_ID+2, "superspray01 emitter"),
            new FXR(Const.FXR_START_ID+3, "bluecandleflame emitter")
        };

        public static List<LightFXR> LIGHT_FXRS = new List<LightFXR>();

        //@TODO: other emitters we are not covering:::
        // sparks without fire (used once or twice)
        // ash without fire  (used maybe 8 times?)
        // superspray02 (1 off used by lava, unknown, prolly a really big fire?)
        // pcloud01 (1 off seems to be used for mist or something, unsure)
        // chimney_smoke02
        // ex_waterfall_mist_01
        // blizzard01
        // ex_gg_particles_01
        // ex_waterfall_mist_s_01
        // ex_volcano_steam_emitter
        // terrain_lava_ventlg
        // terrain_lava_vent
        // superspray03
        // steam_lavariver01
        // steam_lavariver

        private static List<string> UNK_EMITTERS = new(); // so we dont spam logs with the same unk emitter name 1000 times
        public static int GetFXR(string emitterName)
        {
            if (emitterName.Contains("attachlight")) { return -1; } // get outtaaaaa heeeereeeee

            foreach(FXR fxr in FXRS)
            {
                if(fxr.match == emitterName)
                {
                    return fxr.id;
                }
            }

            if(!UNK_EMITTERS.Contains(emitterName)) { Lort.Log($"## WARNING ## Unknown emitter name {emitterName}", Lort.Type.Debug); UNK_EMITTERS.Add(emitterName); }
            return -1; 
        }

        public static int NEXT_LIGHT_FXR_ID = Const.FXR_START_ID + 10;
        public static int GetLightFXR(EmitterInfo emitterInfo)
        {
            LightFXR fxr = new(NEXT_LIGHT_FXR_ID++);

            fxr.OverwriteInt32(fxr.id, 0x0000000c); // FXR id

            fxr.OverwriteFloat(emitterInfo.color[0] / 255f, 0x00000E80); // Diffuse Red
            fxr.OverwriteFloat(emitterInfo.color[1] / 255f, 0x00000E84); // Diffuse Green
            fxr.OverwriteFloat(emitterInfo.color[2] / 255f, 0x00000E88); // Diffuse Blue
            fxr.OverwriteFloat(1f, 0x00000E8C); // Diffuse Alpha

            fxr.OverwriteFloat(emitterInfo.color[0] / 255f, 0x00000E90); // Specular Red
            fxr.OverwriteFloat(emitterInfo.color[1] / 255f, 0x00000E94); // Specular Green
            fxr.OverwriteFloat(emitterInfo.color[2] / 255f, 0x00000E98); // Specular Blue
            fxr.OverwriteFloat(1f, 0x00000E9C); // Specular Alpha

            fxr.OverwriteFloat(emitterInfo.radius, 0x00000EA0); // Radius

            bool flicker = emitterInfo.mode == LightContent.Mode.Flicker || emitterInfo.mode == LightContent.Mode.FlickerSlow;
            fxr.OverwriteByte(flicker ? (byte)0x01 : (byte)0x00, 0x000000DD4); // Flicker On/Off

            LIGHT_FXRS.Add(fxr);

            return fxr.id;
        }

        /* Write all the ffxbnds for all the map groups */
        public static void Write(Layout layout)
        {
            BND4 ffxbnd = new();
            ffxbnd.Compression = Compression.KRAK();
            ffxbnd.Version = "25I10A23";

            int fxrid = -1; int texid = 99999;
            foreach(BinderFile file in ffxbnd.Files)
            {
                if (file.Name.ToLower().EndsWith(".fxr") && file.ID > fxrid) { fxrid = file.ID; }
                if (file.Name.ToLower().EndsWith(".tpf") && file.ID > texid) { texid = file.ID; }
            }
            fxrid++; texid++;

            foreach (FXR fxr in FXRS)
            {
                // fxr file
                {
                    BinderFile file = new();
                    file.Bytes = File.ReadAllBytes(Utility.ResourcePath($"fxr\\{fxr.fxr}"));
                    file.ID = fxrid;
                    file.Name = $"N:\\GR\\data\\INTERROOT_win64\\sfx\\effect\\f{fxr.id}.fxr";
                    ffxbnd.Files.Add(file);
                }
                // ffxreslist
                {
                    BinderFile file = new();
                    file.Bytes = File.ReadAllBytes(Utility.ResourcePath($"fxr\\{fxr.res}"));
                    file.ID = 400000 + fxrid++;
                    file.Name = $"N:\\GR\\data\\INTERROOT_win64\\sfx\\ResourceList\\f{fxr.id}.ffxreslist";
                    ffxbnd.Files.Add(file);
                }
                // tpfs
                foreach(string tpf in fxr.tpfs)
                {
                    BinderFile file = new();
                    file.Bytes = File.ReadAllBytes(Utility.ResourcePath($"fxr\\{tpf}"));
                    file.ID = texid++;
                    file.Name = $"N:\\GR\\data\\INTERROOT_win64\\sfx\\tex\\{Path.GetFileNameWithoutExtension(tpf)}.tpf";
                    ffxbnd.Files.Add(file);
                }
            }

            /* These are just point lights inside an fxr. used for the attachlight node in models */
            foreach(LightFXR fxr in LIGHT_FXRS)
            {
                // fxr file
                {
                    BinderFile file = new();
                    file.Bytes = fxr.GetBytes();
                    file.ID = fxrid;
                    file.Name = $"N:\\GR\\data\\INTERROOT_win64\\sfx\\effect\\f{fxr.id}.fxr";
                    ffxbnd.Files.Add(file);
                }

                // ffxreslist  (it's fucking blank but it's still required idk fucking fromsoft lmao)
                {
                    BinderFile file = new();
                    file.Bytes = new byte[0]; // lol, lmao even
                    file.ID = 400000 + fxrid++;
                    file.Name = $"N:\\GR\\data\\INTERROOT_win64\\sfx\\ResourceList\\f{fxr.id}.ffxreslist";
                    ffxbnd.Files.Add(file);
                }
            }

            // for some reason bnds have to be sorted by ID
            Utility.SortBND4(ffxbnd);

            List<int> maps =
            [
                60, // for the overworld
            ];
            foreach(InteriorGroup group in layout.Interiors)
            {
                if (group.IsEmpty) { continue; }
                if (maps.Contains(group.map)) { continue; }
                maps.Add(group.map);
            }

            Lort.Log($"Writing {maps.Count} FFX Binder files... ", Lort.Type.Main);
            Lort.NewTask($"Writing {maps.Count} FFX Binder files... ", maps.Count);
            foreach (int map in maps)
            {
                ffxbnd.Write(Path.Combine(Const.OUTPUT_PATH, $@"sfx\sfxbnd_m{map:D2}.ffxbnd.dcx"));
                Lort.TaskIterate();
            }
        }


        public class FXR
        {
            public readonly int id;
            public readonly string match;  // string to match from dummy node name EX: "emitter candleflame"
            public readonly string fxr;    // fxr file path
            public readonly string res;    // resource list path
            public readonly List<string> tpfs; // tpf file paths

            public FXR(int id, string name)
            {
                this.id = id;
                this.match = name;
                fxr = $"{name}\\f{id}.fxr";
                res = $"{name}\\f{id}.ffxreslist";

                tpfs = new();
                string[] lines = File.ReadAllLines(Utility.ResourcePath($"fxr\\{res}"));
                foreach (string line in lines)
                {
                    tpfs.Add($"{name}\\{line.ToLower().Replace(".tif", ".tpf")}");
                }
            }
        }

        public class LightFXR
        {
            public readonly int id;
            private byte[] bytes;

            public LightFXR(int id)
            {
                this.id = id;
                bytes = File.ReadAllBytes(Utility.ResourcePath($"fxr\\attachlight template.fxr"));
            }

            public void OverwriteInt32(int value, int address)
            {
                byte[] buffer = BitConverter.GetBytes(value);

                for(int i=0;i<buffer.Length;i++)
                {
                    bytes[address + i] = buffer[i];
                }
            }

            public void OverwriteFloat(float value, int address)
            {
                byte[] buffer = BitConverter.GetBytes(value);

                for (int i = 0; i < buffer.Length; i++)
                {
                    bytes[address + i] = buffer[i];
                }
            }

            public void OverwriteByte(byte value, int address)
            {
                bytes[address] = value;
            }

            public byte[] GetBytes() { return bytes; }
        }
    }
}
