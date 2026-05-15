using DirectXTexNet;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using TeximpNet.DDS;

namespace JortPob.Common
{
    public class DDS
    {
        public static int GetTpfFormatFromDdsBytes(byte[] ddsBytes)
        {
            using (MemoryStream ddsStream = new(ddsBytes))
            {
                DXGIFormat format = DDSFile.Read(ddsStream).Format;

                switch (format)
                {
                    //Elden Ring:
                    case TeximpNet.DDS.DXGIFormat.BC7_UNorm:
                    case TeximpNet.DDS.DXGIFormat.BC7_UNorm_SRGB:
                        return 102;
                    //DSR:
                    case DXGIFormat.BC1_UNorm:
                    case DXGIFormat.BC1_UNorm_SRGB:
                        return 0;
                    case DXGIFormat.BC2_UNorm:
                    case DXGIFormat.BC2_UNorm_SRGB:
                        return 3;
                    case DXGIFormat.BC3_UNorm:
                    case DXGIFormat.BC3_UNorm_SRGB:
                        return 5;
                    case DXGIFormat.R16G16_Float:
                        return 35;
                    case DXGIFormat.BC5_UNorm:
                        return 36;
                    case DXGIFormat.BC6H_UF16:
                        return 37;
                    //case DXGIFormat.BC7_UNorm:  // wrong for elden ring possibly?
                    //case DXGIFormat.BC7_UNorm_SRGB:
                        //return 38;
                    //DS3:
                    case DXGIFormat.B5G5R5A1_UNorm:
                        return 6;
                    case DXGIFormat.B8G8R8A8_UNorm:
                    case DXGIFormat.B8G8R8A8_UNorm_SRGB:
                        return 9;
                    case DXGIFormat.B8G8R8X8_UNorm:
                    case DXGIFormat.B8G8R8X8_UNorm_SRGB:
                        return 10;
                    case DXGIFormat.R16G16B16A16_Float:
                        return 22;
                    default:
                        return 0;
                }
            }

        }

        public static byte[] MakeVolumeTexture(int size, byte R, byte G, byte B, byte A)  // very very VERY slow function
        {
            DDS_FLAGS ddsFlags = DDS_FLAGS.NONE;
            ScratchImage sImage = TexHelper.Instance.Initialize3D(DirectXTexNet.DXGI_FORMAT.R8G8B8A8_UNORM, size, size, size, 1, CP_FLAGS.NONE);          

            // cock pain
            unsafe
            {
                for (int i = 0; i < sImage.GetImageCount(); i++)
                {
                    DirectXTexNet.Image layer = sImage.GetImage(i);
                    int byteCount = layer.Width * layer.Height * 4;
                    byte* P = (byte*)layer.Pixels.ToPointer();

                    for (int j = 0; j < byteCount; j += 4)
                    {
                        byte* r = P + j;
                        byte* g = P + j + 1;
                        byte* b = P + j + 2;
                        byte* a = P + j + 3;


                        *r = R;
                        *g = G;
                        *b = B;
                        *a = A;
                    }

                }
            }

            DirectXTexNet.DXGI_FORMAT format = DirectXTexNet.DXGI_FORMAT.BC7_UNORM;
            sImage = sImage.Compress(format, TEX_COMPRESS_FLAGS.BC7_QUICK, 0.5f);
            sImage.OverrideFormat(format);

            /* Save the DDS to memory stream and then read the stream into a byte array. */
            byte[] bytes;
            using (UnmanagedMemoryStream uStream = sImage.SaveToDDSMemory(ddsFlags))
            {
                bytes = new byte[uStream.Length];
                uStream.Read(bytes);
            }
            sImage.Dispose();
            return bytes;
        }

        /// <summary>
        /// Takes in a Byte4 width and height and returns an BC2_UNORM_SRGB DDS file. There are optional parameters,
        /// including scale (of which you have to provide both x and y for it to scale). Most of the Texture format and
        /// flags are also optionally available. Defaults: format:BC2_UNORM_SRGB texCompFlag:DEFAULT ddsFlags: FORCE_DX10_EXT
        /// filterFlags: LINEAR
        /// </summary>
        /// <returns>DDS texture as bytes.</returns>
        public static Object _lock = new Object();
        public static byte[] MakeTextureFromPixelData(Byte4[] pixels, int width, int height, int? scaleX = null, int? scaleY = null,
            DirectXTexNet.DXGI_FORMAT format = DirectXTexNet.DXGI_FORMAT.BC2_UNORM_SRGB, TEX_COMPRESS_FLAGS texCompFlag = TEX_COMPRESS_FLAGS.DEFAULT, DDS_FLAGS ddsFlags = DDS_FLAGS.FORCE_DX10_EXT,
            TEX_FILTER_FLAGS filterFlags = TEX_FILTER_FLAGS.LINEAR)
        {
            /* @TODO: Jank. This function is not thread safe and I don't really know why since the crash isn't in my code. */
            /* At some point maybe fix this issue or have a single thread handler that does the dds conversions after the landscape workers finish */
            lock(_lock) {

                /* For some damn reason the System.Drawing.Common is a NuGet dll. Something something windows only something */
                Bitmap img = new(width, height);
                for (int x = 0; x < img.Width; x++)
                {
                    for (int y = 0; y < img.Height; y++)
                    {
                        Byte4 color = pixels[y * img.Width + x];
                        Color pixelColor = Color.FromArgb(color.w, color.x, color.y, color.z);
                        img.SetPixel(x, y, pixelColor);
                    }
                }
                /* Bitmap only supports saving to a file or a stream. Let's just save to a stream and get the stream as and array */
                byte[] pngBytes;
                using (MemoryStream stream = new())
                {
                    img.Save(stream, ImageFormat.Png);
                    pngBytes = stream.ToArray();
                }

                /* pin the array to memory so the garbage collector can't mess with it, */
                GCHandle pinnedArray = GCHandle.Alloc(pngBytes, GCHandleType.Pinned);
                ScratchImage sImage = TexHelper.Instance.LoadFromWICMemory(pinnedArray.AddrOfPinnedObject(), pngBytes.Length, WIC_FLAGS.DEFAULT_SRGB);

                if (scaleX != null && scaleY != null)
                    sImage = sImage.Resize(0, scaleX.Value, scaleY.Value, filterFlags);

                sImage = sImage.Compress(format, texCompFlag, 0.5f);
                sImage.OverrideFormat(format);

                /* Save the DDS to memory stream and then read the stream into a byte array. */
                byte[] bytes;
                using (UnmanagedMemoryStream uStream = sImage.SaveToDDSMemory(ddsFlags))
                {
                    bytes = new byte[uStream.Length];
                    uStream.Read(bytes);
                }

                pinnedArray.Free(); //We have to manually free pinned stuff, or it will never be collected.
                img.Dispose();
                sImage.Dispose();
                return bytes;
            }
        }

        /* dds file bytes in, rescaled dds bytes out */
        /* returns exactly halfsize, since thats what we need for a low detail texture */
        public static byte[] Scale(byte[] dds)
        {
            GCHandle pinnedArray = GCHandle.Alloc(dds, GCHandleType.Pinned);
            try
            {
                ScratchImage img = TexHelper.Instance.LoadFromDDSMemory(pinnedArray.AddrOfPinnedObject(), dds.Length, DDS_FLAGS.NONE);
                int w = img.GetMetadata().Width / 2;
                int h = img.GetMetadata().Height / 2;
                if (TexHelper.Instance.IsCompressed(img.GetMetadata().Format))
                {
                    img = img.Decompress(DirectXTexNet.DXGI_FORMAT.R8G8B8A8_UNORM);
                }
                img = img.Resize(0, w, h, TEX_FILTER_FLAGS.LINEAR);
                img = img.Compress(DirectXTexNet.DXGI_FORMAT.BC1_UNORM, TEX_COMPRESS_FLAGS.DEFAULT, 0.5f);
                //img.OverrideFormat(DirectXTexNet.DXGI_FORMAT.BC2_UNORM_SRGB);
            
                byte[] scaled;
                using (UnmanagedMemoryStream uStream = img.SaveToDDSMemory(DDS_FLAGS.FORCE_DX10_EXT))
                {
                    scaled = new byte[uStream.Length];
                    uStream.Read(scaled);
                }
                pinnedArray.Free();
                img.Dispose();
                return scaled;
            } catch (Exception ex) {
                Lort.Log($"{ex.Message} {ex.StackTrace} {ex.Source}", Lort.Type.Debug);
                return null;
            }
        }

        /* dds file bytes in, bitmap object out */
        public static Bitmap DDStoBitmap(byte[] dds, int width = 0, int height = 0)
        {
            GCHandle pinnedArray = GCHandle.Alloc(dds, GCHandleType.Pinned);
            DirectXTexNet.ScratchImage scratchImage = DirectXTexNet.TexHelper.Instance.LoadFromDDSMemory(pinnedArray.AddrOfPinnedObject(), dds.Length, DirectXTexNet.DDS_FLAGS.NONE);
            if (TexHelper.Instance.IsCompressed(scratchImage.GetMetadata().Format))
            {
                scratchImage = scratchImage.Decompress(DirectXTexNet.DXGI_FORMAT.R8G8B8A8_UNORM);
            }

            if(width > 0 || height > 0)
            {
                scratchImage = scratchImage.Resize(0, width, height, TEX_FILTER_FLAGS.CUBIC);
            }

            Bitmap bitmap = new(scratchImage.GetImage(0).Width, scratchImage.GetImage(0).Height);

            int bytesPerPixel = (int)scratchImage.GetImage(0).RowPitch / scratchImage.GetImage(0).Width;
            for (int y = 0; y < scratchImage.GetImage(0).Height; y++)
            {
                for (int x = 0; x < scratchImage.GetImage(0).Width; x++)
                {
                    int offset = (int)((y * scratchImage.GetImage(0).Width * bytesPerPixel) + (x * bytesPerPixel));
                    byte r = Marshal.ReadByte(scratchImage.GetImage(0).Pixels, offset);
                    byte g = Marshal.ReadByte(scratchImage.GetImage(0).Pixels, offset + 1);
                    byte b = Marshal.ReadByte(scratchImage.GetImage(0).Pixels, offset + 2);
                    byte a = Marshal.ReadByte(scratchImage.GetImage(0).Pixels, offset + 3);

                    bitmap.SetPixel(x, y, Color.FromArgb(a, r, g, b));
                }
            }

            pinnedArray.Free();
            scratchImage.Dispose();

            return bitmap;
        }

        /* c# bitmap object in, bytes for a dds file out */
        public static byte[] BitmapToDDS
        (
            Bitmap bitmap,
            DirectXTexNet.DXGI_FORMAT format = DirectXTexNet.DXGI_FORMAT.BC2_UNORM_SRGB,
            TEX_COMPRESS_FLAGS texCompFlag = TEX_COMPRESS_FLAGS.DEFAULT,
            DDS_FLAGS ddsFlags = DDS_FLAGS.FORCE_DX10_EXT,
            TEX_FILTER_FLAGS filterFlags = TEX_FILTER_FLAGS.LINEAR
        )
        {
            /* Bitmap only supports saving to a file or a stream. Let's just save to a stream and get the stream as and array */
            byte[] pngBytes;
            using (MemoryStream stream = new())
            {
                bitmap.Save(stream, ImageFormat.Png);
                pngBytes = stream.ToArray();
            }

            /* pin the array to memory so the garbage collector can't mess with it, */
            GCHandle pinnedArray = GCHandle.Alloc(pngBytes, GCHandleType.Pinned);
            ScratchImage sImage = TexHelper.Instance.LoadFromWICMemory(pinnedArray.AddrOfPinnedObject(), pngBytes.Length, WIC_FLAGS.DEFAULT_SRGB);

            //sImage = sImage.Compress(DXGI_FORMAT.BC2_UNORM_SRGB, texCompFlag, 0.5f);
           // sImage = sImage.Decompress(DirectXTexNet.DXGI_FORMAT.R8G8B8A8_UNORM);
            sImage = sImage.Compress(format, texCompFlag, 0.5f);
            sImage.OverrideFormat(format);

            /* Save the DDS to memory stream and then read the stream into a byte array. */
            byte[] bytes;
            using (UnmanagedMemoryStream uStream = sImage.SaveToDDSMemory(ddsFlags))
            {
                bytes = new byte[uStream.Length];
                uStream.Read(bytes);
            }

            pinnedArray.Free(); //We have to manually free pinned stuff, or it will never be collected.
            sImage.Dispose();
            return bytes;
        }

        public static bool IsAlpha(string texPath)
        {
            byte[] texBytes = File.ReadAllBytes(texPath);
            return IsAlpha(texBytes);
        }
        public static bool IsAlpha(byte[] texBytes)
        {
            int texFormat = GetTpfFormatFromDdsBytes(texBytes);
            return IsAlpha(texFormat);
        }
        public static bool IsAlpha(int texFormat)
        {
            return texFormat == 3;
        }
    }
}