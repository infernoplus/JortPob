using DirectXTexNet;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;

namespace JortPob.Common
{
    // most credit goes to https://github.com/Pear0533/ERMapGenerator
    // its where most of my understanding (and this code) comes from
    public class MapGenerator : IDisposable
    {
        // image size constants for each zoom level
        private const int L0_SIZE = 10496;
        private const int L1_SIZE = 7936;
        private const int L2_SIZE = 2816;

        // L1 scaling parameters
        private const int L1_SCALE_SUBTRACT = 78;
        private const int L1_OFFSET_X = 1;
        private const int L1_OFFSET_Y = 1;

        // L2 scaling parameters
        private const int L2_SCALE_SUBTRACT = 731;
        private const int L2_OFFSET_X = 4;
        private const int L2_OFFSET_Y = 516;

        // Tile size in pixels
        private const int TILE_SIZE = 256;

        // grid sizes for each zoom level
        private static readonly Dictionary<ZoomLevel, int> ZoomLevelGridSizes = new()
        {
            { ZoomLevel.L0, 41 },
            { ZoomLevel.L1, 31 },
            { ZoomLevel.L2, 11 }
        };

        private BND4 mapTileMaskBnd;
        private BXF4 mapTileTpfBxf;
        private BXF4 mapTileBxf;
        private MapTileMatrix tileFlags;
        private XmlNode mapTileMaskRoot;

        private Bitmap blankTileL0;
        private Bitmap blankTileL1;
        private Bitmap blankTileL2;

        private Bitmap scaledL1Image;
        private Bitmap scaledL2Image;

        private Action progressCallback;

        public static (byte[] bhdBytes, byte[] bdtBytes) ReplaceMapTiles(
            Bitmap sourceImage,
            string[] groundLevels,
            ZoomLevel[] zoomLevels,
            string mapTileMaskBndPath,
            string mapTileTpfBhdPath,
            string mapTileTpfBtdPath,
            Action progressCallback = null,
            CancellationToken cancellationToken = default)
        {
            using var replacer = new MapGenerator();
            return replacer.ReplaceMapTilesInternal(
                sourceImage,
                groundLevels,
                zoomLevels,
                mapTileMaskBndPath,
                mapTileTpfBhdPath,
                mapTileTpfBtdPath,
                progressCallback,
                cancellationToken);
        }

        private MapGenerator()
        {
            mapTileBxf = new BXF4();
            tileFlags = new MapTileMatrix();
        }

        private (byte[] bhdBytes, byte[] bdtBytes) ReplaceMapTilesInternal(
            Bitmap sourceImage,
            string[] groundLevels,
            ZoomLevel[] zoomLevels,
            string mapTileMaskBndPath,
            string mapTileTpfBhdPath,
            string mapTileTpfBtdPath,
            Action progressCallback,
            CancellationToken cancellationToken)
        {
            this.progressCallback = progressCallback;

            mapTileMaskBnd = BND4.Read(mapTileMaskBndPath);
            mapTileTpfBxf = BXF4.Read(mapTileTpfBhdPath, mapTileTpfBtdPath);

            LoadBlankTilesFromGameFiles();

            // scale the map to L1 and L2
            if (zoomLevels.Contains(ZoomLevel.L0))
            {
                scaledL1Image?.Dispose();
                scaledL1Image = GetScaledMapForZoomLevel(sourceImage, ZoomLevel.L1);
            }
            if (zoomLevels.Contains(ZoomLevel.L1))
            {
                scaledL2Image?.Dispose();
                scaledL2Image = GetScaledMapForZoomLevel(sourceImage, ZoomLevel.L2);
            }

            // export tiles for each ground level and zoom level
            foreach (string groundLevel in groundLevels)
            {
                foreach (var zoomLevel in zoomLevels)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ExportTilesForLevel(sourceImage, groundLevel, zoomLevel, cancellationToken);
                }
            }

            var result = GetBinderBytes();

            scaledL1Image?.Dispose();
            scaledL1Image = null;
            scaledL2Image?.Dispose();
            scaledL2Image = null;

            return result;
        }

        private void LoadBlankTilesFromGameFiles()
        {
            try
            {
                if (mapTileTpfBxf == null || mapTileTpfBxf.Files.Count == 0) throw new FileNotFoundException("Map files weren't loaded correctly");

                BinderFile? l0File = mapTileTpfBxf.Files.FirstOrDefault(f => f.Name.Contains("MENU_MapTile_M00_L0_00_00_00000000"));
                BinderFile? l1File = mapTileTpfBxf.Files.FirstOrDefault(f => f.Name.Contains("MENU_MapTile_M00_L1_00_00_00000000"));
                BinderFile? l2File = mapTileTpfBxf.Files.FirstOrDefault(f => f.Name.Contains("MENU_MapTile_M00_L2_00_00_00000000"));

                if (l0File != null)
                {
                    TPF.Texture texFile = TPF.Read(l0File.Bytes).Textures[0];
                    blankTileL0 = JortPob.Common.DDS.DDStoBitmap(texFile.Bytes);
                }

                if (l1File != null)
                {
                    TPF.Texture texFile = TPF.Read(l1File.Bytes).Textures[0];
                    blankTileL1 = JortPob.Common.DDS.DDStoBitmap(texFile.Bytes);
                }

                if (l2File != null)
                {
                    TPF.Texture texFile = TPF.Read(l2File.Bytes).Textures[0];
                    blankTileL2 = JortPob.Common.DDS.DDStoBitmap(texFile.Bytes);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error loading blank tiles from game files: {ex.Message}", ex);
            }
        }

        private Bitmap GetScaledMapForZoomLevel(Bitmap sourceImage, ZoomLevel zoomLevel)
        {
            int targetSize = zoomLevel switch
            {
                ZoomLevel.L0 => L0_SIZE,
                ZoomLevel.L1 => L1_SIZE,
                ZoomLevel.L2 => L2_SIZE,
                _ => L0_SIZE
            };

            switch (zoomLevel)
            {
                case ZoomLevel.L0 when sourceImage.Width == L0_SIZE && sourceImage.Height == L0_SIZE:
                    return new Bitmap(sourceImage);
                case ZoomLevel.L1 when scaledL1Image is not null:
                    return new Bitmap(scaledL1Image);
                case ZoomLevel.L2 when scaledL2Image is not null:
                    return new Bitmap(scaledL2Image);
            }

            Bitmap workingImage = new Bitmap(sourceImage);

            // only level 0 has a cleared chunk
            if (zoomLevel != ZoomLevel.L0)
            {
                using (Graphics g = Graphics.FromImage(workingImage))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    using (SolidBrush transparentBrush = new SolidBrush(Color.Transparent))
                    {
                        g.FillRectangle(transparentBrush, 0, workingImage.Height - TILE_SIZE, TILE_SIZE, TILE_SIZE);
                    }
                }
            }

            Bitmap scaledImage = new Bitmap(targetSize, targetSize);
            using (Graphics g = Graphics.FromImage(scaledImage))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;

                if (zoomLevel == ZoomLevel.L1)
                {
                    int scaledSize = targetSize - L1_SCALE_SUBTRACT;
                    g.DrawImage(workingImage, L1_OFFSET_X, L1_OFFSET_Y, scaledSize, scaledSize);
                }
                else if (zoomLevel == ZoomLevel.L2)
                {
                    int scaledSize = targetSize - L2_SCALE_SUBTRACT;
                    g.DrawImage(workingImage, L2_OFFSET_X, L2_OFFSET_Y, scaledSize, scaledSize);
                }
                else
                {
                    g.DrawImage(workingImage, 0, 0, targetSize, targetSize);
                }

                Bitmap? blankTile = zoomLevel switch
                {
                    ZoomLevel.L0 => blankTileL0,
                    ZoomLevel.L1 => blankTileL1,
                    ZoomLevel.L2 => blankTileL2,
                    _ => null
                };

                if (blankTile != null)
                {
                    g.DrawImage(blankTile, 0, scaledImage.Height - TILE_SIZE, TILE_SIZE, TILE_SIZE);
                }
            }

            workingImage.Dispose();

            // cache the scaled images
            if (zoomLevel == ZoomLevel.L1)
            {
                scaledL1Image?.Dispose();
                scaledL1Image = new Bitmap(scaledImage);
            }
            else if (zoomLevel == ZoomLevel.L2)
            {
                scaledL2Image?.Dispose();
                scaledL2Image = new Bitmap(scaledImage);
            }

            return scaledImage;
        }

        private void ExportTilesForLevel(Bitmap sourceImage, string groundLevel, ZoomLevel zoomLevel, CancellationToken cancellationToken)
        {
            int gridSize = ZoomLevelGridSizes.GetValueOrDefault(zoomLevel);
            using Bitmap mapImage = GetScaledMapForZoomLevel(sourceImage, zoomLevel);

            ReadMapTileMask(groundLevel);
            SetTileFlags();

            for (int x = 0; x < gridSize; x++)
            {
                for (int y = 0; y < gridSize; y++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    Rectangle tileRect = new(
                        x * TILE_SIZE,
                        y * TILE_SIZE,
                        Math.Min(TILE_SIZE, mapImage.Width - x * TILE_SIZE),
                        Math.Min(TILE_SIZE, mapImage.Height - y * TILE_SIZE)
                    );

                    using Bitmap tileImage = new(TILE_SIZE, TILE_SIZE);
                    using (Graphics g = Graphics.FromImage(tileImage))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.DrawImage(mapImage, new Rectangle(0, 0, TILE_SIZE, TILE_SIZE), tileRect, GraphicsUnit.Pixel);
                    }

                    string tileXPos = x.ToString("D2");
                    string tileYPos = (gridSize - y - 1).ToString("D2");
                    string tileName = $"MENU_MapTile_{groundLevel}_{zoomLevel}_{tileXPos}_{tileYPos}";
                    string bitFlags = tileFlags[(int)zoomLevel, x, gridSize - y - 1].ToString("X8");

                    progressCallback?.Invoke();
                    // all praise the map tile mask
                    // skip tiles with all flags set (0xFFFFFFFF means tile is transparent/blank)
                    if (bitFlags == "FFFFFFFF") continue;

                    string newTileName = $"{tileName}_{bitFlags}";
                    WriteTileToPackage(tileImage, newTileName);
                }
            }
        }

        private void ReadMapTileMask(string groundLevel)
        {
            BinderFile file = mapTileMaskBnd.Files.Find(i => i.Name.Contains(groundLevel));
            if (file != null)
            {
                string fileName = Path.GetFileName(file.Name);

                XmlDocument doc = new();
                doc.Load(new MemoryStream(file.Bytes));
                mapTileMaskRoot = doc.LastChild;

                if (mapTileMaskRoot == null)
                {
                    throw new Exception($"{fileName} contains no root XML node.");
                }
            }
        }

        private void SetTileFlags()
        {
            tileFlags = new MapTileMatrix();

            if (mapTileMaskRoot == null) return;

            for (int i = 0; i < mapTileMaskRoot.ChildNodes.Count; ++i)
            {
                XmlNode node =  mapTileMaskRoot.ChildNodes[i];
                if (node == null) continue;
                if (node.Attributes == null || node.Attributes.Count < 2) continue;

                string coord = $"{int.Parse(node.Attributes[1].Value):00000}";
                int zoomLevel = int.Parse(coord[..1]);
                int x = int.Parse(coord.Substring(1, 2));
                int y = int.Parse(coord.Substring(3, 2));
                
                tileFlags[zoomLevel, x, y] = int.Parse(node.Attributes[2].Value);
            }
        }

        private void WriteTileToPackage(Bitmap tileImage, string tileName)
        {
            byte[] ddsBytes = JortPob.Common.DDS.BitmapToDDS(tileImage, format: DXGI_FORMAT.BC2_UNORM, texCompFlag: DirectXTexNet.TEX_COMPRESS_FLAGS.BC7_USE_3SUBSETS);

            TPF.Texture texture = new()
            {
                Bytes = ddsBytes,
                Name = tileName,
                Format = (byte)JortPob.Common.DDS.GetTpfFormatFromDdsBytes(ddsBytes)
            };

            TPF tpf = new() { Compression = Compression.KRAK() };
            tpf.Textures.Add(texture);
            byte[] tpfBytes = tpf.Write();

            BinderFile file = new()
            {
                Name = $"71_MapTile\\{tileName}.tpf.dcx",
                Bytes = tpfBytes
            };

            mapTileBxf.Files.Add(file);
        }

        private (byte[] bhdBytes, byte[] bdtBytes) GetBinderBytes()
        {
            // remove old tiles that are being replaced
            IEnumerable<int> files = mapTileBxf.Files.Select(file =>
                mapTileTpfBxf.Files.FindIndex(i =>
                    string.Equals(i.Name, file.Name, StringComparison.OrdinalIgnoreCase)));

            foreach (int i in files.Where(index => index != -1))
                mapTileTpfBxf.Files.RemoveAt(i);

            // add new tiles
            mapTileTpfBxf.Files.AddRange(mapTileBxf.Files);

            // sort and re-index
            mapTileTpfBxf.Files = mapTileTpfBxf.Files.OrderBy(i => i.Name).ToList();
            for (int i = 0; i < mapTileTpfBxf.Files.Count; i++)
                mapTileTpfBxf.Files[i].ID = i;

            mapTileTpfBxf.Write(out var bhdBytes, out var bdtBytes);

            return (bhdBytes, bdtBytes);
        }

        public void Dispose()
        {
            blankTileL0?.Dispose();
            blankTileL1?.Dispose();
            blankTileL2?.Dispose();
            scaledL1Image?.Dispose();
            scaledL2Image?.Dispose();

            // hacky but works
            foreach (var file in mapTileMaskBnd.Files)
            {
                file.Bytes = null;
            }
            mapTileTpfBxf.Files.Clear();

            foreach (var file in mapTileTpfBxf.Files)
            {
                file.Bytes = null;
            }
            mapTileTpfBxf.Files.Clear();
        }

        private class MapTileMatrix
        {
            private readonly Dictionary<string, int> Data = new();

            public int this[int zoomLevel, int x, int y]
            {
                get
                {
                    string key = GetKey(zoomLevel, x, y);
                    return Data.TryGetValue(key, out var value) ? value : -1;
                }
                set
                {
                    string key = GetKey(zoomLevel, x, y);
                    Data[key] = value;
                }
            }

            private static string GetKey(int zoomLevel, int x, int y)
            {
                return string.Join(",", new[] { zoomLevel, x, y });
            }
        }

        public enum ZoomLevel
        {
            L0 = 0, L1 = 1, L2 = 2
        }
    }

}
