using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JortPob.Common;
using SoulsFormats;

namespace JortPob.Worker;

public class MapInfoTexWorker : IWorker<Unit>
{
    private struct Tile
    {
        public int MapId;
        public int X;
        public int Y;
        public Bitmap Image;
    }

    // this is strictly used for testing if the outputed chunks are stitched correctly
    public void StitchProcess()
    {
        var instance = new MapInfoTexWorker();
        var bmp = Stitch(Path.Combine(Const.ELDEN_PATH, "Game", "other", "mapinfotex"));

        bmp.Save(Path.Combine(Const.CACHE_PATH, "mapinfotex.bmp"), ImageFormat.Bmp);
    }
    

    private Unit Replace()
    {
        try
        {
            var mapPath = Utility.ResourcePath(@"other\mapinfotex.bmp");
            var map = Image.FromFile(mapPath) as Bitmap;
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
        }
        catch (Exception ex)
        {
            Lort.Log($"Failed to Replace weather map: {ex.Message}", Lort.Type.Debug);
        }

        return Unit.Default;
    }

    private static readonly Regex FileRegex =
        new Regex(@"(\d{2})_(\d{2})_(\d{2})_(\d{2})", RegexOptions.Compiled);

    private Bitmap Stitch(string folderPath)
    {
        var tiles = new List<Tile>();
        Parallel.ForEach(Directory.GetFiles(folderPath, "*.dcx"), file =>
        {
            var name = Path.GetFileName(file);
            var match = FileRegex.Match(name);

            if (!match.Success)
                return;

            int mapId = int.Parse(match.Groups[1].Value);
            int x = int.Parse(match.Groups[2].Value);
            int y = int.Parse(match.Groups[3].Value);

            if (mapId != 60) return;

            var bmp = ExtractBmpFromBnd(file);
            if (bmp == null)
                return;

            tiles.Add(new Tile
            {
                MapId = mapId,
                X = x,
                Y = y,
                Image = bmp
            });
        });

        if (tiles.Count == 0)
            return null;
        
        return StitchTiles(tiles);
    }

    private Bitmap ExtractBmpFromBnd(string dcxPath)
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

    private Bitmap StitchTiles(List<Tile> tiles)
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

        var processed = tiles.AsParallel().Select(tile => new
        {
            X = (tile.X - minX) * tileWidth,
            Y = (maxY - tile.Y) * tileHeight,
            tile.Image
        }).ToList();
        
        using (var g = Graphics.FromImage(final))
        {
            foreach (var proc in processed)
            {
                g.DrawImage(proc.Image, proc.X, proc.Y, tileWidth, tileHeight);
            }
        }

        return final;
    }

    public void SplitToBND(
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
        
        // process
        var tiles = Directory.GetFiles(inputFolder, "*.dcx")
            .Select(path =>
            {
                var name = Path.GetFileName(path);
                var match = FileRegex.Match(name);

                if (!match.Success)
                    return null;

                int fileMapId = int.Parse(match.Groups[1].Value);
                int x = int.Parse(match.Groups[2].Value);
                int y = int.Parse(match.Groups[3].Value);

                if (fileMapId != mapId)
                    return null;

                int srcX = (x - minX) * tileWidth;
                int srcY = (maxY - y) * tileHeight;
                
                if (srcX < 0 || srcY < 0 ||
                    srcX + tileWidth > source.Width||
                    srcY + tileHeight > source.Height)
                {
                    return null;
                }

                var rect = new Rectangle(srcX, srcY, tileWidth, tileHeight);
                return new
                {
                    path,
                    name,
                    bitmap = source.Clone(rect, PixelFormat.Format24bppRgb)
                };
            })
            .Where(tile => tile != null).ToList();
        
        Parallel.ForEach(tiles, tile =>
        {
            using (tile.bitmap)
            using (var ms = new MemoryStream())
            {
                tile.bitmap.Save(ms, ImageFormat.Bmp);
                byte[] bmpBytes = ms.ToArray();

                var bnd = BND4.Read(tile.path);

                bnd.Files[0].Bytes = bmpBytes;

                byte[] outBytes = bnd.Write();

                string outPath = Path.Combine(outputFolder, tile.name);

                File.WriteAllBytes(outPath, outBytes);
            }
        });
    }

    public Unit Go()
    {
        return Replace();
    }
}