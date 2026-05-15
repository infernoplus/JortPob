using DirectXTexNet;
using JortPob.Common;
using SoulsFormats;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using static JortPob.SpeffManager.Speff.Effect;

namespace JortPob
{
    public class IconManager
    {
        public List<IconInfo> icons { get; init; }
        public List<BuffInfo> buffs { get; init; }

        public ushort nextIconId = 19000; // all ids after this are free to use
        public ushort nextBuffId = 20600; // all ids after this are free to use

        public IconManager(ESM esm)
        {
            if (Const.DEBUG_SKIP_MENU_TEXTURES) { return; }

            /* Item Icons */
            Lort.Log($"Loading icons...", Lort.Type.Main);
            Lort.NewTask("Loading Icons", 1);

            icons = new();

            Dictionary<string, IconInfo> iconsByPath = icons.ToDictionary(icon => icon.path, icon => icon); // one-off lookup to get an icon by path
            void FindIcons(ESM.Type recordType)
            {
                foreach (JsonNode json in esm.GetAllRecordsByType(recordType))
                {
                    if (json["icon"] != null && json["icon"].GetValue<string>().Trim() != "")
                    {
                        string recordId = json["id"].GetValue<string>().ToLower();
                        string iconPath = json["icon"].GetValue<string>().ToLower().Replace(".tga", ".dds");

                        // now we only replace once
                        if (iconsByPath.TryGetValue(iconPath, out IconInfo iconInfo))
                        {
                            iconInfo.referers.Add(recordId);
                        }
                        else
                        {
                            IconInfo ii = new(iconPath, nextIconId++);
                            ii.referers.Add(recordId);
                            icons.Add(ii);
                            iconsByPath.Add(iconPath, ii); // update our temporary lookup
                        }
                    }
                }
            }

            FindIcons(ESM.Type.Weapon);
            FindIcons(ESM.Type.Armor);
            FindIcons(ESM.Type.Clothing);
            FindIcons(ESM.Type.Ingredient);
            FindIcons(ESM.Type.Alchemy);
            FindIcons(ESM.Type.Book);
            FindIcons(ESM.Type.MiscItem);
            FindIcons(ESM.Type.Apparatus);
            FindIcons(ESM.Type.Probe);
            FindIcons(ESM.Type.Lockpick);
            FindIcons(ESM.Type.RepairItem);

            /* Buff icons */
            buffs = new();

            foreach (JsonNode json in esm.GetAllRecordsByType(ESM.Type.MagicEffect))
            {
                if (json["icon"] != null && json["icon"].GetValue<string>().Trim() != "")
                {
                    string iconPath = json["icon"].GetValue<string>().ToLower();
                    SpeffManager.Speff.Effect.MagicEffect magicEffect = (MagicEffect)System.Enum.Parse(typeof(MagicEffect), json["effect_id"].GetValue<string>());

                    BuffInfo buffInfo = new(magicEffect, iconPath, nextBuffId++);
                    buffs.Add(buffInfo);
                }
            }
        }

        /* This one is special, this is getting an icon based on a record that uses it. Multiple records can use the same icon so its a slow search */
        public IconInfo GetIconByRecord(string record)
        {
            if (Const.DEBUG_SKIP_MENU_TEXTURES) { return new IconInfo("Default", 0); }

            foreach (IconInfo icon in icons)
            {
                if (icon.referers.Contains(record.ToLower()))
                {
                    return icon;
                }
            }
            return null;
        }

        public BuffInfo GetBuffByType(SpeffManager.Speff.Effect.MagicEffect type)
        {
            if (Const.DEBUG_SKIP_MENU_TEXTURES) { return null; }

            foreach (BuffInfo buff in buffs)
            {
                if(buff.type == type)
                {
                    return buff;
                }
            }
            return null;
        }

        public (BXF4 hiBxf, BXF4 lowBxf) Write()
        {
            /* Do regular icons for use in the inventory */
            const int WIDTH = 4096;
            const int HEIGHT = 2048;
            const int ROWS = 12;
            const int COLS = 24;
            const int PAD = 4;
            const int ICON = 160;  // both width and height of individual icons
            const int FIRST_SHEET = 9;

            Lort.Log($"Generating sheets for {icons.Count() + buffs.Count()} icons...", Lort.Type.Main);
            Lort.NewTask("Stitching Icons", icons.Count() + buffs.Count());

            /* Load all icons into system.drawing.image objects */
            List<(IconInfo iconInfo, Bitmap bitmap)> bitmaps = new();
            foreach (IconInfo icon in icons)
            {
                byte[] ddsBytes = File.ReadAllBytes(Path.Combine(Const.MORROWIND_PATH, $@"Data Files\icons\{icon.path}"));
                using Bitmap bitmap = Common.DDS.DDStoBitmap(ddsBytes);
                Bitmap scaledBitmap = Utility.XbrzUpscale(bitmap, 5);
                bitmaps.Add((icon, scaledBitmap));
            }

            /* Make sheets */
            List<(Layout layout, Bitmap bitmap)> sheets = new();
            int i = 0; // icon index
            for (int sheetIndex = 0; i < bitmaps.Count(); sheetIndex++)  // item icon sheets
            {
                Bitmap sheet = new Bitmap(WIDTH, HEIGHT);
                IconLayout layout = new($"SB_Icon_{(FIRST_SHEET + sheetIndex):D2}");

                using (Graphics g = Graphics.FromImage(sheet))
                {
                    g.Clear(Color.Empty);

                    for (int y = 0; y < ROWS; y++)
                    {
                        for (int x = 0; x < COLS; x++)
                        {
                            if (i >= bitmaps.Count()) { break; } // Out of icons!

                            IconInfo icon = bitmaps[i].iconInfo;
                            Bitmap bitmap = bitmaps[i].bitmap;
                            g.DrawImage(bitmap, x * (ICON + PAD), y * (ICON + PAD));

                            layout.Add(icon.id, new Int2(x * (ICON + PAD), y * (ICON + PAD)), new Int2(ICON, ICON));

                            i++;
                            Lort.TaskIterate();
                        }
                    }
                }

                sheets.Add((layout, sheet));
            }

            /* Do buff icons for speffs to display */
            const int BUFF_SHEET = 2;
            const int BUFF_WIDTH = 1024;
            const int BUFF_HEIGHT = 512;
            const int BUFF_ROWS = 10;
            const int BUFF_COLS = 20;
            const int BUFF_PAD = 4;
            const int BUFF = 40; // buff icons are a 40x40 square texture

            /* Load all icons into system.drawing.image objects */
            List<(BuffInfo buffInfo, Bitmap bitmap)> buffBitmaps = new();
            foreach (BuffInfo buff in buffs)
            {
                byte[] ddsBytes = File.ReadAllBytes(Path.Combine(Const.MORROWIND_PATH, $@"Data Files\icons\{buff.path}"));
                Bitmap buffBitmap = Common.DDS.DDStoBitmap(ddsBytes);
                buffBitmap = Utility.XbrzUpscale(buffBitmap, 4);
                buffBitmap = Utility.ResizeBitmap(buffBitmap, BUFF, BUFF);
                buffBitmaps.Add((buff, buffBitmap));
            }

            /* Make buff sheets */
            Bitmap buffSheet = new Bitmap(BUFF_WIDTH, BUFF_HEIGHT);
            BuffLayout buffLayout = new($"SB_Status_{BUFF_SHEET:D2}");

            i = 0;
            using (Graphics g = Graphics.FromImage(buffSheet))
            {
                g.Clear(Color.Empty);

                for (int y = 0; y < BUFF_ROWS; y++)
                {
                    for (int x = 0; x < BUFF_COLS; x++)
                    {
                        if (i >= buffBitmaps.Count()) { break; } // Out of icons!

                        BuffInfo buff = buffBitmaps[i].buffInfo;
                        Bitmap buffBitmap = buffBitmaps[i].bitmap;
                        g.DrawImage(buffBitmap, x * (BUFF + BUFF_PAD), y * (BUFF + BUFF_PAD));

                        buffLayout.Add(buff.id, new Int2(x * (BUFF + BUFF_PAD), y * (BUFF + BUFF_PAD)), new Int2(BUFF, BUFF));

                        i++;
                        Lort.TaskIterate();
                    }
                }
            }
            sheets.Add((buffLayout, buffSheet));

            /* Write sheets and layouts to bnds and tpfs */
            const string hiLayoutPath = @"menu\hi\01_common.sblytbnd.dcx";
            const string hiTpfPath = @"menu\hi\01_common.tpf.dcx";
            const string lowLayoutPath = @"menu\low\01_common.sblytbnd.dcx";
            const string lowTpfPath = @"menu\low\01_common.tpf.dcx";

            void AddSheets(string layoutPath, string tpfPath, string hilow)
            {
                TPF tpf = TPF.Read(Path.Combine(Const.ELDEN_PATH, "Game", tpfPath));
                BND4 bnd = BND4.Read(Path.Combine(Const.ELDEN_PATH, "Game", layoutPath));

                foreach ((Layout layout, Bitmap bitmap) tuple in sheets)
                {
                    /* Add sheet to TPF */
                    TPF.Texture texture = new();
                    Bitmap linearBitmap = Common.Utility.LinearToSRGB(tuple.bitmap);
                    texture.Bytes = Common.DDS.BitmapToDDS(linearBitmap, DXGI_FORMAT.BC2_UNORM);
                    linearBitmap.Dispose();
                    texture.Format = (byte)Common.DDS.GetTpfFormatFromDdsBytes(texture.Bytes);
                    texture.Name = tuple.layout.name;
                    tpf.Textures.Add(texture);

                    /* Add layout xml to BND */
                    BinderFile bf = new();
                    bf.Bytes = tuple.layout.Write();
                    bf.ID = bnd.Files.Count();
                    bf.Name = $"N:\\GR\\data\\Menu\\ScaleForm\\SBLayout\\01_Common\\{hilow}\\{tuple.layout.name}.layout";
                    bnd.Files.Add(bf);
                }

                tpf.Write(Path.Combine(Const.OUTPUT_PATH, tpfPath));
                bnd.Write(Path.Combine(Const.OUTPUT_PATH, layoutPath));
            }

            Lort.Log($"Binding {sheets.Count()} sheets...", Lort.Type.Main);
            Lort.NewTask("Binding Sheets", 2);
            AddSheets(hiLayoutPath, hiTpfPath, "Hi"); Lort.TaskIterate();
            AddSheets(lowLayoutPath, lowTpfPath, "Low"); Lort.TaskIterate();

            /* Clean up */
            foreach ((Layout layout, Bitmap bitmap) tuple in sheets) { tuple.bitmap.Dispose(); }
            foreach ((IconInfo iconInfo, Bitmap bitmap) tuple in bitmaps) { tuple.bitmap.Dispose(); }

            /* Do large icons for use in the description area */
            const string hiPath = @"menu\hi\00_solo";
            const string lowPath = @"menu\low\00_solo";

            Lort.Log($"Generating {icons.Count()} previews...", Lort.Type.Main);
            Lort.NewTask("Generating Previews", icons.Count());

            BXF4 AddIcons(string path)
            {
                /* Load BXF4 from elden ring directory (requires game unpacked!) */
                BXF4 bxf = BXF4.Read(Path.Combine(Const.ELDEN_PATH, $@"Game\{path}.tpfbhd"), Path.Combine(Const.ELDEN_PATH, $@"Game\{path}.tpfbdt"));
                List<BinderFile> filesToInsert = new();

                /* Generate binder files to add from dds texture files */
                foreach(IconInfo icon in icons)
                {
                    byte[] data = File.ReadAllBytes(Path.Combine(Const.MORROWIND_PATH, $@"Data Files\icons\{icon.path}"));

                    Bitmap bitmap = Common.DDS.DDStoBitmap(data);
                    Bitmap scaledBitmap = Utility.XbrzUpscale(bitmap, 6); // 32x32x -> 192x192
                    Bitmap linearScaledBitmap = Utility.LinearToSRGB(scaledBitmap);
                    byte[] scaledDDS = Common.DDS.BitmapToDDS(linearScaledBitmap, DXGI_FORMAT.BC2_UNORM);
                    scaledBitmap.Dispose();
                    linearScaledBitmap.Dispose();

                    int format = JortPob.Common.DDS.GetTpfFormatFromDdsBytes(scaledDDS);

                    TPF tpf = new TPF();
                    tpf.Platform = TPF.TPFPlatform.PC;
                    tpf.Compression = Compression.KRAK();

                    TPF.Texture tex = new(icon.TextureName(), (byte)format, 0, scaledDDS, TPF.TPFPlatform.PC);
                    tpf.Textures.Add(tex);

                    BinderFile bf = new();
                    bf.Name = icon.FileName();
                    bf.Bytes = tpf.Write();
                    bf.ID = bxf.Files.Count();

                    filesToInsert.Add(bf);
                }

                /* Reindex after inserting new files */
                int insertAt = bxf.Files.FindLastIndex(bf => bf.Name.StartsWith("00_Solo\\MENU_Knowledge_"));
                bxf.Files.InsertRange(insertAt + 1, filesToInsert); // slam dunk those new filse into the bnd

                for (int i = 0; i < bxf.Files.Count(); i++)
                {
                    BinderFile bf = bxf.Files[i];
                    bf.ID = i;
                }

                /* Write */
                return bxf;
            }

            Lort.Log($"Binding {icons.Count()} previews...", Lort.Type.Main);
            Lort.NewTask("Binding Previews", 2);
            var hiBxf = AddIcons(hiPath); Lort.TaskIterate();
            var lowBxf = AddIcons(lowPath); Lort.TaskIterate();

            Lort.TaskIterate();

            return (hiBxf, lowBxf);
        }

        public class IconInfo
        {
            public readonly string path;
            public readonly ushort id;

            public readonly List<string> referers; // list of item record ids that use this icon

            public IconInfo(string path, ushort id)
            {
                this.path = path.Replace(".tga", ".dds");  // esm stores texture paths pointing to a TGA but the actual file on disk is DDS
                this.id = id;

                referers = new();
            }

            public string TextureName()
            {
                return $"MENU_Knowledge_{id:D5}";
            }

            public string FileName()
            {
                return $"00_Solo\\MENU_Knowledge_{id:D5}.tpf.dcx";
            }
        }

        public abstract class Layout
        {
            public readonly string name;
            protected readonly List<LayoutEntry> entries;

            public Layout(string name)
            {
                this.name = name;
                entries = new();
            }

            public abstract void Add(ushort id, Int2 position, Int2 size);

            public byte[] Write()
            {
                string output = $"<TextureAtlas imagePath=\"{name}.png\">\r\n";
                foreach (LayoutEntry entry in entries)
                {
                    output += entry.Write();
                }
                output += $"</TextureAtlas>";

                byte[] bytes;
                using (MemoryStream ms = new())
                {
                    using (TextWriter tw = new StreamWriter(ms))
                    {
                        tw.Write(output);
                        tw.Flush();
                        ms.Position = 0;
                        bytes = ms.ToArray();
                    }

                }

                return bytes;
            }

            public abstract class LayoutEntry
            {
                public readonly ushort id;
                public Int2 position, size;

                public LayoutEntry(ushort id, Int2 position, Int2 size)
                {
                    this.id = id;
                    this.position = position;
                    this.size = size;
                }

                public abstract string Write();
            }

            public class IconLayoutEntry : LayoutEntry
            {
                public IconLayoutEntry(ushort id, Int2 position, Int2 size) : base(id, position, size) { }

                public override string Write()
                {
                    return $"\t<SubTexture name=\"MENU_ItemIcon_{id:D5}.png\" x=\"{position.x}\" y=\"{position.y}\" width=\"{size.x}\" height=\"{size.y}\" half=\"0\"/>\r\n";
                }
            }

            public class BuffLayoutEntry : LayoutEntry
            {
                public BuffLayoutEntry(ushort id, Int2 position, Int2 size) : base(id, position, size) { }

                public override string Write()
                {
                    return $"\t<SubTexture name=\"MENU_StatusIcon_{id:D5}.png\" x=\"{position.x}\" y=\"{position.y}\" width=\"{size.x}\" height=\"{size.y}\" half=\"0\"/>\r\n";
                }
            }
        }

        public class IconLayout : Layout
        {
            public IconLayout(string name) : base(name) { }

            public override void Add(ushort id, Int2 position, Int2 size)
            {
                entries.Add(new IconLayoutEntry(id, position, size));
            }
        }

        public class BuffLayout : Layout
        {
            public BuffLayout(string name) : base(name) { }

            public override void Add(ushort id, Int2 position, Int2 size)
            {
                entries.Add(new BuffLayoutEntry(id, position, size));
            }
        }

        public class BuffInfo
        {
            public readonly SpeffManager.Speff.Effect.MagicEffect type;
            public readonly string path;
            public readonly ushort id;

            public BuffInfo(SpeffManager.Speff.Effect.MagicEffect type, string path, ushort id)
            {
                this.type = type;
                this.path = path.Replace(".tga", ".dds");  // esm stores texture paths pointing to a TGA but the actual file on disk is DDS
                this.id = id;
            }
        }
    }
}
