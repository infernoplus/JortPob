using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using WitchyFormats;
using Xbrz;

namespace JortPob.Common
{
    public static class Utility
    {
        private static byte[] LinSRGBLUT = new byte[256];

        public static void InitSRGBCache()
        {
            float ConvertValue(float colorValue)
            {
                if (colorValue <= 0.0031308f)
                {
                    return colorValue * 12.92f;
                }
                else
                {
                    return 1.055f * ((float)Math.Pow(colorValue, 1.0f / 2.4f)) - 0.055f;
                }
            }

            for (var i = 0; i < LinSRGBLUT.Length; i++)
            {
                LinSRGBLUT[i] = (byte)(Math.Min(1f, ConvertValue(i / 255f)) * 255);
            }
        }

        public static string ResourcePath(string path)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", path);
        }

        public static uint FNV1_32(byte[] data)
        {
            const uint FNV_PRIME = 16777619;
            const uint FNV_OFFSET_BASIS = 2166136261;

            uint hash = FNV_OFFSET_BASIS;

            foreach (byte b in data)
            {
                hash *= FNV_PRIME;
                hash ^= b;
            }

            return hash;
        }

        /* Try to split a piece of text on punctuation to fit within the max size of a subtitle in Elden Ring */
        public static List<string> CivilizedSplit(string text)
        {
            // Break apart all text by punctuation
            List<string> lines = new();
            string concat = "";
            for(int i=0;i<text.Length;i++)
            {
                char c = text[i];
                concat += c;

                bool isPunc = c == '.' || c == ';' || c == '?' || c == '!';
                if (isPunc) { lines.Add(concat); concat = ""; }
            }
            if (concat.Trim() != "") { lines.Add(concat.Trim()); }

            // See if any lines desperately need to be split on a comma or in the middle of a sentence
            for(int i=0;i<lines.Count();i++)
            {
                string line = lines[i];
                if(line.Length > Const.MAX_CHAR_PER_TALK)
                {
                    // search from center for a place split
                    int bestComma = -1;
                    int bestSpace = -1;
                    int middle = line.Length / 2;
                    for(int j=0;j<line.Length/2;j++)
                    {
                        int left = Math.Max(0, middle - j);
                        int right = Math.Min(middle + j, line.Length-1);

                        if (line[left] == ' ' && bestSpace == -1) { bestSpace = left; }
                        if (line[right] == ' ' && bestSpace == -1) { bestSpace = right; }
                        if (line[left] == ',' && bestComma == -1) { bestComma = left; }
                        if (line[right] == ',' && bestComma == -1) { bestComma = right; }
                    }

                    int splitAt; bool onSpace;
                    // Split on a comma
                    if (bestComma != -1) { splitAt = bestComma; onSpace = false; }
                    // Split on a space
                    else { splitAt = bestSpace; onSpace = true; }

                    string a = $"{line.Substring(0, splitAt+1).Trim()}{(onSpace?"--":"")}";
                    string b = $"{(onSpace ? "--" : "")}{line.Substring(splitAt+1, line.Length - splitAt - 1).Trim()}";

                    // Add lines back in the right spot
                    lines.RemoveAt(i);
                    lines.Insert(i, b);
                    lines.Insert(i, a);
                }
            }

            // Reassemble within tolerence of a single subtitle
            List<string> recombined = new();
            concat = "";
            foreach(string line in lines)
            {
                if(line.Length > Const.MAX_CHAR_PER_TALK)
                {
                    if(concat != "") { recombined.Add(concat); }
                    concat = line;
                    //Lort.Log($"## WARNING ## Line exceeds character limit: {text}", Lort.Type.Debug);
                }
                else if(concat.Length + line.Length <= Const.MAX_CHAR_PER_TALK)
                {
                    concat += $" {line}";
                }
                else
                {
                    recombined.Add(concat);
                    concat = line;
                }
            }
            recombined.Add(concat);

            if (recombined.Count() >= 8) { Lort.Log($"## WARNING ## Line exceeds split limit: {text}", Lort.Type.Debug); }

            return recombined;
        }

        public static string StringAwareLower(string s)
        {
            bool inQuote = false;
            string text = s;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '"') { inQuote = !inQuote; }
                else if (char.IsUpper(c) && !inQuote) { text = text.Remove(i, 1).Insert(i, $"{char.ToLower(c)}"); }
            }
            return text;
        }

        public static string[] StringAwareSplit(string text, char on)
        {
            if(text.Trim() == "") { return new string[0]; } // empty string = emptry array

            List<string> split = text.Split(on).ToList();
            List<string> recomb = new();
            for (int i = 0; i < split.Count(); i++)
            {
                string s = split[i];
                if (s.StartsWith("\""))
                {
                    if (s.Split("\"").Length - 1 == 2) { recomb.Add(s.Replace("\"", "")); }
                    else
                    {
                        string itrNxt = split[++i];
                        while (!itrNxt.Contains("\""))
                        {
                            itrNxt += $" {split[++i]}";
                        }
                        recomb.Add(($"{s} {itrNxt}").Replace("\"", ""));
                    }
                    continue;
                }

                recomb.Add(s);
            }
            return recomb.ToArray();
        }

        public static string StringAwareReplace(string s, char find, char replace)
        {
            bool inQuote = false;
            string text = s;
            for(int i=0;i<text.Length;i++)
            {
                char c = text[i];
                if (c == '"') { inQuote = !inQuote; }
                else if (c == find && !inQuote) { text = text.Remove(i, 1).Insert(i, $"{replace}"); }
            }
            return text;
        }
      
        public static string SanitizeTextForComment(string text)
        {
            return text.Replace("\r", "").Replace("\n", "");
        }

        public static bool StringIsInteger(string text)
        {
            return int.TryParse(text, out _);
        }

        public static bool StringIsFloat(string text)
        {
            return float.TryParse(text, out _);
        }

        public static bool StringIsOperator(string text)
        {
            string allowableLetters = "+=<>!-*/";

            foreach (char c in text)
            {
                // This is using String.Contains for .NET 2 compat.,
                //   hence the requirement for ToString()
                if (!allowableLetters.Contains(c.ToString()))
                    return false;
            }

            return true;
        }

        /* Rotation conversion from Morrowind worldspace to Elden Ring worldspace. right hand XYZ to left hand XZY (afaik, it's very hard to pinpoint this and lots of guesswork) */
        public static System.Numerics.Vector3 ConvertRotation(System.Numerics.Vector3 rotation)
        {
            /* The following unholy code converts morrowind (Z up) euler rotations into dark souls (Y up) euler rotations */
            /* Big thanks to katalash, dropoff, and the TESUnity dudes for helping me sort this out */

            /* Katalashes code from MapStudio */
            Vector3 MatrixToEulerXZY(Matrix4x4 m)
            {
                const float Pi = (float)Math.PI;
                const float Deg2Rad = Pi / 180.0f;
                Vector3 ret;
                ret.Z = MathF.Asin(-Math.Clamp(-m.M12, -1, 1));

                if (Math.Abs(m.M12) < 0.9999999)
                {
                    ret.X = MathF.Atan2(-m.M32, m.M22);
                    ret.Y = MathF.Atan2(-m.M13, m.M11);
                }
                else
                {
                    ret.X = MathF.Atan2(m.M23, m.M33);
                    ret.Y = 0;
                }
                ret.X = ret.X <= -180.0f * Deg2Rad ? ret.X + 360.0f * Deg2Rad : ret.X;
                ret.Y = ret.Y <= -180.0f * Deg2Rad ? ret.Y + 360.0f * Deg2Rad : ret.Y;
                ret.Z = ret.Z <= -180.0f * Deg2Rad ? ret.Z + 360.0f * Deg2Rad : ret.Z;
                return ret;
            }

            /* Adapted code from https://github.com/ColeDeanShepherd/TESUnity */
            Quaternion xRot = Quaternion.CreateFromAxisAngle(new Vector3(1.0f, 0.0f, 0.0f), rotation.X);
            Quaternion yRot = Quaternion.CreateFromAxisAngle(new Vector3(0.0f, 1.0f, 0.0f), rotation.Z);
            Quaternion zRot = Quaternion.CreateFromAxisAngle(new Vector3(0.0f, 0.0f, 1.0f), rotation.Y);
            Quaternion q = xRot * zRot * yRot;

            return MatrixToEulerXZY(Matrix4x4.CreateFromQuaternion(q));
        }

        public static System.Numerics.Vector3 ToRadians(System.Numerics.Vector3 rotation)
        {
            const float pi = (float)Math.PI;
            const float d2r = pi / 180.0f;
            return rotation * d2r;
        }

        public static System.Numerics.Vector3 ToDegrees(System.Numerics.Vector3 rotation)
        {
            const float pi = (float)Math.PI;
            const float r2d = 180.0f / pi;
            return rotation * r2d;
        }

        /* Convert parameters from papyrus call to a Vector 3. offset moves starting index away from 0. automatically converts xyz to xzy */
        public static System.Numerics.Vector3 Vector3FromParameters(string[] parameters, int offset = 0)
        {
            float x = float.Parse(parameters[0 + offset]);
            float y = float.Parse(parameters[2 + offset]);
            float z = float.Parse(parameters[1 + offset]);
            return new(x, y, z);
        }

        private static Random random;
        public static int RandomRange(int min, int max)
        {
            if(random == null) { random = new(Const.RANDOM_SEED); }

            return random.Next(min, max);
        }

        public static Bitmap XbrzUpscale(Bitmap bitmap, int factor)
        {
            // Convert to ARGB int[] array
            int width = bitmap.Width, height = bitmap.Height;
            int[] srcPixels = new int[width * height];
            Rectangle rect = new(0, 0, width, height);
            BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, srcPixels, 0, srcPixels.Length);
            bitmap.UnlockBits(data);

            // Perform scaling
            XbrzScaler scaler = new(factor, withAlpha: true);
            int[] scaledPixels = scaler.ScaleImage(srcPixels, null, width, height);

            // Convert back to Bitmap
            int scaledWidth = width * factor, scaledHeight = height * factor;
            Bitmap scaledBitmap = new(scaledWidth, scaledHeight, PixelFormat.Format32bppArgb);
            Rectangle scaledRect = new(0, 0, scaledWidth, scaledHeight);
            BitmapData scaledData = scaledBitmap.LockBits(scaledRect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            System.Runtime.InteropServices.Marshal.Copy(scaledPixels, 0, scaledData.Scan0, scaledPixels.Length);
            scaledBitmap.UnlockBits(scaledData);

            return scaledBitmap;
        }

        /* Converts a linear color space to SRGB */
        public static Bitmap LinearToSRGB(Bitmap bitmap)
        {
            Bitmap linearBitmap = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);

            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData linearBitmapData = linearBitmap.LockBits(rect, ImageLockMode.ReadWrite, linearBitmap.PixelFormat);
            BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadWrite, bitmap.PixelFormat);

            IntPtr linearPtr = linearBitmapData.Scan0;
            IntPtr bitmapPtr = bitmapData.Scan0;

            int linearBytes = linearBitmapData.Stride * linearBitmapData.Height;
            byte[] linearArgbVals = new byte[linearBytes];

            int bitmapBytes = bitmapData.Stride * bitmapData.Height;
            byte[] bitmapRgbVals = new byte[bitmapBytes];

            Marshal.Copy(bitmapPtr, bitmapRgbVals, 0, bitmapBytes);

            // The pixel strides between the og bitmap and the linear one may differ, we must homogenize to an index
            int stride = BitmapUtilities.GetStride(bitmap.PixelFormat);
            for (int i = 0; i < Math.Abs(bitmapBytes / stride); i++)
            {
                byte[] bitmapColors = BitmapUtilities.GetPixelValues(ref bitmapRgbVals, i * stride, bitmap.PixelFormat);
                linearArgbVals[i * 4 + 3] = bitmapColors[0]; // alpha
                linearArgbVals[i * 4 + 2] = LinSRGBLUT[bitmapColors[1]]; // r
                linearArgbVals[i * 4 + 1] = LinSRGBLUT[bitmapColors[2]]; // g
                linearArgbVals[i * 4] = LinSRGBLUT[bitmapColors[3]]; // b
            }

            Marshal.Copy(linearArgbVals, 0, linearPtr, linearBytes);

            bitmap.UnlockBits(bitmapData);
            linearBitmap.UnlockBits(linearBitmapData);

            return linearBitmap;
        }

        public static unsafe Bitmap LinearToSRGBAlt(Bitmap bitmap)
        {
            Bitmap srgbBitmap = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);

            Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);

            // technically this can be done in place, but better safe than sorry
            // due to bit manipulation shenanigens, bit locking is required
            BitmapData bmpDataIn = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);
            BitmapData bmpDataOut = srgbBitmap.LockBits(rect, ImageLockMode.WriteOnly, srgbBitmap.PixelFormat);

            try
            {
                byte* ptrIn = (byte*)bmpDataIn.Scan0;
                byte* ptrOut = (byte*)bmpDataOut.Scan0;

                // LUT calculation
                float[] sRGB_LUT = new float[256];
                for (int i = 0; i < 256; i++)
                {
                    float linearValue = i / 255.0f;
                    sRGB_LUT[i] = Value(linearValue);
                }

                int totalBytes = bmpDataIn.Stride * bitmap.Height;

                for (int i = 0; i < totalBytes; i += 4)
                {
                    // since colors are big endian, they're reversed as b g r a
                    byte linearB = ptrIn[i];
                    byte linearG = ptrIn[i + 1];
                    byte linearR = ptrIn[i + 2];
                    byte alphaA = ptrIn[i + 3];

                    // lut to the save
                    float srgbR_float = sRGB_LUT[linearR];
                    float srgbG_float = sRGB_LUT[linearG];
                    float srgbB_float = sRGB_LUT[linearB];

                    // squish the results back to range
                    ptrOut[i] = (byte)Math.Round(srgbB_float * 255f);
                    ptrOut[i + 1] = (byte)Math.Round(srgbG_float * 255f);
                    ptrOut[i + 2] = (byte)Math.Round(srgbR_float * 255f);

                    ptrOut[i + 3] = alphaA;
                }
            }
            finally
            {
                // MUST unlock the bits after processing
                bitmap.UnlockBits(bmpDataIn);
                srgbBitmap.UnlockBits(bmpDataOut);
            }

            return srgbBitmap;

            float Value(float linearValue)
            {
                linearValue = Math.Max(0f, Math.Min(1f, linearValue));

                if (linearValue <= 0.0031308f)
                {
                    return linearValue * 12.92f;
                }
                else
                {
                    return 1.055f * ((float)FastPow(linearValue, 1.0f / 2.4f)) - 0.055f;
                }
            }
        }

        // source: trust me bro
        public static double FastPow(double a, double b)
        {
            long tmp = BitConverter.DoubleToInt64Bits(a);
            long tmp2 = (long)(b * (tmp - 4606921280493453312L)) + 4606921280493453312L;
            return BitConverter.Int64BitsToDouble(tmp2);
        }

        /* Code borrowed from https://stackoverflow.com/questions/1922040/how-to-resize-an-image-c-sharp */
        public static Bitmap ResizeBitmap(Image image, int width, int height)
        {
            Rectangle destRect = new(0, 0, width, height);
            Bitmap destImage = new(width, height);

            destImage.SetResolution(image.HorizontalResolution, image.VerticalResolution);

            using (Graphics graphics = Graphics.FromImage(destImage))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                using (ImageAttributes wrapMode = new())
                {
                    wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(image, destRect, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, wrapMode);
                }
            }

            return destImage;
        }

        /* Sort binderfiles by id */
        public static void SortBND4(BND4 bnd)
        {
            bnd.Files = bnd.Files.OrderBy(file => file.ID).ToList();
        }

        public static void SortFsParam(FsParam param)
        {
            param.Rows = param.Rows.AsParallel().OrderBy(row => row.ID).ToList();
        }

        public static void SortPARAM(SoulsFormats.PARAM param)
        {
            param.Rows = param.Rows.AsParallel().OrderBy(row => row.ID).ToList();
        }

        public static void SortFMG(FMG fmg)
        {
            fmg.Entries = fmg.Entries.AsParallel().OrderBy(entry => entry.ID).ToList();
        }

        public static long Pow(int x, uint pow)
        {
            int ret = 1;
            while (pow != 0)
            {
                if ((pow & 1) == 1)
                    ret *= x;
                x *= x;
                pow >>= 1;
            }
            return ret;
        }

        public static void ExecuteProcess(ProcessStartInfo startInfo, bool allowTimeout)
        {
            if (allowTimeout) { ExecuteProcess(startInfo); }
            else { ExecuteProcess(startInfo, -1); }
        }

        public static void ExecuteProcess(ProcessStartInfo startInfo, int timeOutMillis = 0)
        {
            using Process process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException($"Failed to start process: {startInfo.FileName}");
            }

            Task<string> stdoutTask = startInfo.RedirectStandardOutput
                ? process.StandardOutput.ReadToEndAsync()
                : Task.FromResult(string.Empty);
            Task<string> stderrTask = startInfo.RedirectStandardError
                ? process.StandardError.ReadToEndAsync()
                : Task.FromResult(string.Empty);

            bool exited;
            if (timeOutMillis == 0) { exited = process.WaitForExit(TimeSpan.FromMilliseconds(Const.DEFAULT_PROCESS_TIMEOUT)); }
            else if (timeOutMillis < 0) { process.WaitForExit(); exited = true; }
            else { exited = process.WaitForExit(TimeSpan.FromMilliseconds(timeOutMillis)); }

            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(); // Wait for OS cleanup
                }
                catch (InvalidOperationException)
                {
                    // Process may have just exited before Kill() was called.
                }

                Task.WaitAll(stdoutTask, stderrTask);
                throw new TimeoutException($"Process timed out and was killed: {startInfo.FileName}");
            }

            Task.WaitAll(stdoutTask, stderrTask);

            // VITAL: Check the process exit code after successful exit or timeout kill
            if (process.ExitCode != 0)
            {
                string error = startInfo.RedirectStandardError ? stderrTask.Result : "N/A (Error stream not redirected)";

                // Throw a specific exception indicating execution failure
                throw new ApplicationException($"Process failed with exit code {process.ExitCode}. Error: {error}");
            }
        }
    }

    public static class MSBExtension
    {
        public static void AddRegions(this SoulsFormats.MSBE msbe, List<MSBE.Region> regions)
        {
            foreach(MSBE.Region region in regions)
            {
                msbe.Regions.Add(region);
            }
        }
    }


    public static class IListExtensions
    {
        /// <summary>
        /// Shuffles the element order of the specified list.
        /// </summary>
        public static void Shuffle<T>(this IList<T> ts)
        {
            var count = ts.Count;
            var last = count - 1;
            Random rand = new Random();
            for (var i = 0; i < last; ++i)
            {
                var r = rand.Next(i, count);
                var tmp = ts[i];
                ts[i] = ts[r];
                ts[r] = tmp;
            }
        }

        /* copy pasted from example */
        public static void Replace<T>(this IList<T> list, T oldItem, T newItem)
        {
            int index = list.IndexOf(oldItem);
            if (index != -1)
            {
                list[index] = newItem;
            }
        }
    }

    public static class BitmapUtilities
    {
        public static int GetStride(PixelFormat format) => format switch
        {
            PixelFormat.Format24bppRgb => 3,
            PixelFormat.Format32bppArgb => 4,
            PixelFormat.Format32bppRgb => 4,
            PixelFormat.Format32bppPArgb => 4,
            PixelFormat.Canonical => 4,
            _ => throw new Exception($"PixelFormat {(int)format} is unsupported!")
        };

        // The bytes are ordered in little-endian, a bit counter-intuitive
        public static byte[] GetPixelValues(ref byte[] bytes, int offset, PixelFormat format)
        {
            // Typically .NET is little-endian but weird shit happens
            int alphaOffset = BitConverter.IsLittleEndian ? 3 : 0;
            int redOffset = BitConverter.IsLittleEndian ? 2 : 1;
            int greenOffset = BitConverter.IsLittleEndian ? 1 : 2;
            int blueOffset = BitConverter.IsLittleEndian ? 0 : 3;
            byte alpha = 255, red, green, blue;
            if (format == PixelFormat.Format32bppArgb || format == PixelFormat.Format32bppPArgb || format == PixelFormat.Canonical)
                alpha = bytes[offset+alphaOffset];

            switch (format)
            {
                case PixelFormat.Format24bppRgb:
                    red = bytes[offset + redOffset];
                    green = bytes[offset + greenOffset];
                    blue = bytes[offset + blueOffset];
                    break;
                case PixelFormat.Format32bppRgb:
                    red = bytes[offset + alphaOffset];
                    green = bytes[offset + redOffset];
                    blue = bytes[offset + greenOffset];
                    break;
                case PixelFormat.Canonical:
                case PixelFormat.Format32bppArgb:
                    red = bytes[offset + redOffset];
                    green = bytes[offset + greenOffset];
                    blue = bytes[offset + blueOffset];
                    break;
                case PixelFormat.Format32bppPArgb:
                    red = (byte)(Math.Min(bytes[offset + redOffset] / (float)alpha, 1f) * 255);
                    green = (byte)(Math.Min(bytes[offset + greenOffset] / (float)alpha, 1f) * 255);
                    blue = (byte)(Math.Min(bytes[offset + blueOffset] / (float)alpha, 1f) * 255);
                    break;
                default:
                    throw new Exception($"PixelFormat {(int)format} is unsupported!");
            }

            return [ alpha, red, green, blue ];
        }
    }
}
