using JortPob;
using JortPob.Common;
using JortPob.Logging;
using JortPob.Worker;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

public class MapInfoTexWorker : Worker
{
    private MapInfoTexWorker()
    {
        _thread = new Thread(Replace);
        _thread.Start();
    }

    private struct Tile
    {
        public int MapId;
        public int X;
        public int Y;
        public Bitmap Image;
    }

    // this is strictly used for testing if the outputed chunks are stitched correctly
    public static void StitchProcess()
    {
        var instance = new MapInfoTexWorker();
        var bmp = Stitch(Path.Combine(Const.ELDEN_PATH, "Game", "other", "mapinfotex"));

        bmp.Save(Path.Combine(Const.CACHE_PATH, "mapinfotex.bmp"), ImageFormat.Bmp);
    }

    private void Replace()
    {
        using var perf = PerformanceMonitor.TrackPerformance();
        Lort.Log("Replacing weather map... ", Lort.Type.Main);
        try
        {
            var mapPath = Utility.ResourcePath(@"other\mapinfotex.bmp");
            var map = Bitmap.FromFile(mapPath) as Bitmap;
            var output = Path.Combine(Const.OUTPUT_PATH, "other", "mapinfotex");
            SplitToBND(
                source: map, 
                outputFolder: output,
                mapId: 60,
                minX: 8,
                minY: 8,
                maxX: 14,
                maxY: 16,
                tileWidth: 256,
                tileHeight: 256
            );
        } catch (Exception ex)
        {
            Lort.Log($"Failed to Replace weather map: {ex.Message}", Lort.Type.Debug);
        }
        IsDone = true;
    }

    private static readonly Regex FileRegex =
        new Regex(@"(\d{2})_(\d{2})_(\d{2})_(\d{2})", RegexOptions.Compiled);

    public static Bitmap Stitch(string folderPath)
    {
        var tiles = new List<Tile>();

        foreach (var file in Directory.GetFiles(folderPath, "*.dcx"))
        {
            var name = Path.GetFileName(file);
            var match = FileRegex.Match(name);

            if (!match.Success)
                continue;

             int mapId = int.Parse(match.Groups[1].Value);
            int x = int.Parse(match.Groups[2].Value);
            int y = int.Parse(match.Groups[3].Value);

            if (mapId != 60) continue;

            var bmp = ExtractBmpFromBnd(file);
            if (bmp == null)
                continue;

            tiles.Add(new Tile
            {
                MapId = mapId,
                X = x,
                Y = y,
                Image = bmp
            });
        }

        if (tiles.Count == 0)
            return null;

        return StitchTiles(tiles);
    }

    private static Bitmap ExtractBmpFromBnd(string dcxPath)
    {
        try
        {
            var bnd = BND4.Read(dcxPath);

            var bmpEntry = bnd.Files[0];

            if (bmpEntry == null)
                return null;

            using (var ms = new MemoryStream(bmpEntry.Bytes))
            {
                return new Bitmap(ms);
            }
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap StitchTiles(List<Tile> tiles)
    {
        int minX = tiles.Min(t => t.X);
        int minY = tiles.Min(t => t.Y);
        int maxX = tiles.Max(t => t.X);
        int maxY = tiles.Max(t => t.Y);

        int tileWidth = tiles[0].Image.Width;
        int tileHeight = tiles[0].Image.Height;

        int width = (maxX - minX + 1) * tileWidth;
        int height = (maxY - minY + 1) * tileHeight;

        var final = new Bitmap(width, height);

        using (var g = Graphics.FromImage(final))
        {
            foreach (var tile in tiles)
            {
                int drawX = (tile.X - minX) * tileWidth;

                int drawY = (maxY - tile.Y) * tileHeight;

                g.DrawImage(tile.Image, drawX, drawY, tileWidth, tileHeight);
            }
        }

        return final;
    }

    public static void SplitToBND(
        Bitmap source,
        string outputFolder,
        int mapId,
        int minX,
        int minY,
        int maxX,
        int maxY,
        int tileWidth,
        int tileHeight)
    {
        Directory.CreateDirectory(outputFolder);

        var inputFolder = Path.Combine(Const.ELDEN_PATH, "Game", "other", "mapinfotex");

        foreach (var path in Directory.GetFiles(inputFolder, "*.dcx"))
        {
            var name = Path.GetFileName(path);
            var match = FileRegex.Match(name);

            if (!match.Success)
                continue;

            int fileMapId = int.Parse(match.Groups[1].Value);
            int x = int.Parse(match.Groups[2].Value);
            int y = int.Parse(match.Groups[3].Value);

            if (fileMapId != mapId)
                continue;

            int srcX = (x - minX) * tileWidth;
            int srcY = (maxY - y) * tileHeight;

            if (srcX < 0 || srcY < 0 ||
                srcX + tileWidth > source.Width ||
                srcY + tileHeight > source.Height)
            {
                continue;
            }

            var rect = new Rectangle(srcX, srcY, tileWidth, tileHeight);

            using (var tile = source.Clone(rect, PixelFormat.Format24bppRgb))  // convert to 24bit to match elden ring textures
            using (var ms = new MemoryStream())
            {
                tile.Save(ms, ImageFormat.Bmp);
                byte[] bmpBytes = ms.ToArray();

                var bnd = BND4.Read(path);
                
                bnd.Files[0].Bytes = bmpBytes;

                byte[] outBytes = bnd.Write();

                string outPath = Path.Combine(outputFolder, name);

                File.WriteAllBytes(outPath, outBytes);
            }
        }
    }

    internal static void Go()
    {
        using var perf = PerformanceMonitor.TrackPerformance();
        MapInfoTexWorker worker = new();

        while (!worker.IsDone)
        {
            // wait...
            Thread.Yield();
        }
    }
}