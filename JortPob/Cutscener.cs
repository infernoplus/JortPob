using JortPob.Common;
using JortPob.Logging;
using Microsoft.WindowsAPICodePack.Shell;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using SoulsFormats;
using System;
using System.Diagnostics;
using System.IO;

namespace JortPob
{
    public class Cutscener
    {
        private static int nextId = 0060, nextBk2 = 10010020;

        public static int Create(string bikPath, int forceId = -1)
        {
            using var perf = PerformanceMonitor.TrackPerformance();
            /* Get IDs */
            int id, bk2 = nextBk2;
            if (forceId == -1) { id = nextId; }
            else { id = forceId; }

            /* Iterate */
            nextId += 10;
            nextBk2 += 10;

            /* Convert BIK to BK2 */
            string radExe = Path.Combine(Const.RAD_PATH, "radvideo64.exe");
            string bk2Dir = Path.Combine(Const.OUTPUT_PATH, "movie");
            string bk2Path = Path.Combine(bk2Dir, $"{bk2}.bk2");
            Directory.CreateDirectory(bk2Dir);
            {
                string args = $"binkc \"{bikPath}\" \"{bk2Path}\" /O /#";
                ProcessStartInfo radStartInfo = new(radExe)
                {
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true, // Added for better error capture
                    WorkingDirectory = bk2Dir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Utility.ExecuteProcess(radStartInfo, false);
            }

            /* Clone BK2 audio tracks 4 times. */  // Elden Ring for whatever reason needs this in order to playback audio on BK2 ingame.
            string tempPath = Path.Combine(bk2Dir, $"{bk2}.temp.bk2");
            for (int i = 1; i < 5; i++)
            {
                // Bruteforce approach here. Simply clones audio track 0 to 1,2,3,4 one at a time via a loop writing to a temp file each time till it's done
                string args = $"BinkMix \"{bk2Path}\" \"{bk2Path}\" \"{tempPath}\" /T{i} /#";
                ProcessStartInfo radStartInfo = new(radExe)
                {
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true, // Added for better error capture
                    WorkingDirectory = bk2Dir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Utility.ExecuteProcess(radStartInfo, false);
                File.Delete(bk2Path);
                File.Move(tempPath, bk2Path);
            }

            /* Get framecount of video by converting to AVI and reading that */
            string aviPath = Path.Combine(bk2Dir, $"{bk2}.avi");
            {
                string args = $"BinkConv \"{bikPath}\" \"{aviPath}\" /#";
                ProcessStartInfo radStartInfo = new(radExe)
                {
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true, // Added for better error capture
                    WorkingDirectory = bk2Dir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Utility.ExecuteProcess(radStartInfo, false);
            }
            int frames = GetFrameCount(aviPath);
            File.Delete(aviPath);

            /* Load some base game files */
            BND4 s1040 = BND4.Read(Path.Combine(Const.ELDEN_PATH, @"game\cutscene\s10_00_0040.cutscenebnd.dcx"));   // opening cutscene which has a BINK playback event
            BND4 s1050 = BND4.Read(Path.Combine(Const.ELDEN_PATH, @"game\cutscene\s10_00_0050.cutscenebnd.dcx"));   // blankish template

            MQB mqb1040 = MQB.Read(s1040.Files[5].Bytes);
            MQB mqb1050 = MQB.Read(s1050.Files[1].Bytes);

            /* Murder some un-needed timeline events */
            mqb1050.Cuts[0].Timelines.RemoveAt(6);  // rumble
            mqb1050.Cuts[0].Timelines.RemoveAt(5);  // fade in/out
            mqb1050.Cuts[0].Timelines.RemoveAt(4);  // SE

            /* Transplant the PlayBinkVideo event and its resources from the opening cutscene into a blankish template cutscene that does nothing */
            mqb1050.Resources.Add(mqb1040.Resources[34]);
            MQB.Timeline playMovie = mqb1040.Cuts[0].Timelines[4];
            playMovie.Events[0].ResourceIndex = mqb1050.Resources.Count - 1;
            playMovie.Events[0].Parameters[0].Value = bk2;
            mqb1050.Cuts[0].Timelines.Add(playMovie);

            /* Set cutscene length to duration of movie */
            mqb1050.Cuts[0].Duration = frames + 30;   // +30 to add some padding. idk why but it cuts off slightly early with exact frame count

            /* Create new BND */
            BND4 bnd = new();
            bnd.Compression = Compression.KRAK();
            bnd.Version = "07D7R6";

            BinderFile hkx = new();
            hkx.Bytes = s1050.Files[0].Bytes;
            hkx.ID = 100000;
            hkx.Name = @$"N:\GR\data\INTERROOT_win64\cutscene\s10_00_{id:D4}\cut0010\a0000.hkx";
            bnd.Files.Add(hkx);

            BinderFile mqb = new();
            mqb.Bytes = mqb1050.Write();
            mqb.ID = 100000000;
            mqb.Name = @$"N:\GR\data\INTERROOT_win64\cutscene\s10_00_{id:D4}\s10_00_{id:D4}.mqb";
            bnd.Files.Add(mqb);

            /* Write */
            bnd.Write(Path.Combine(Const.OUTPUT_PATH, $@"cutscene\s10_00_{id:D4}.cutscenebnd.dcx"));

            /* Return ID for playback */
            return int.Parse($"1000{id:D4}");
        }

        /* Copy paste from example */
        private static int GetFrameCount(string filePath)
        {
            using (var shell = ShellObject.FromParsingName(filePath))
            {
                // Access the System.Media.Duration property
                IShellProperty prop = shell.Properties.System.Media.Duration;

                // The value is returned as ulong in 100-nanosecond intervals (ticks)
                var durationTicks = (ulong)prop.ValueAsObject;

                // Convert to a TimeSpan
                TimeSpan timeSpan = TimeSpan.FromTicks((long)durationTicks);

                // Return frames at 30fps
                return (int)(timeSpan.Seconds * 30f);
            }
        }
    }
}