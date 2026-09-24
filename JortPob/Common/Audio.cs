using Microsoft.Scripting.Utils;
using NAudio.Wave;
using System;
using System.Diagnostics;
using System.IO;
using JortPob.Logging;

namespace JortPob.Common
{
    public class Audio
    {
        /* Another example copy paste */
        public static double GetDuration(string wavPath)
        {
            using (var reader = new WaveFileReader(wavPath))
            {
                return reader.TotalTime.TotalMilliseconds;
            }
        }

        /* Copy paste from example code */
        /* Doesn't seem like WWISE cares a whole lot about wav format so we are just converting it without changing anything there */
        public static void MP3toWAV(string fileMp3, string fileWav)
        {
            if (string.IsNullOrEmpty(fileMp3))
            {
                throw new ArgumentNullException(nameof(fileMp3));
            }

            if (string.IsNullOrEmpty(fileWav))
            {
                throw new ArgumentNullException(nameof(fileWav));
            }

            // NAudio uses the ACM MP3 decoder that comes with Windows to decompress the MP3 data.
            using (var mp3Reader = new Mp3FileReader(fileMp3))
            {
                // Create a PCM stream from the MP3 reader. 
                // This converts the compressed audio to uncompressed PCM data.
                using (var pcmStream = WaveFormatConversionStream.CreatePcmStream(mp3Reader))
                {
                    // Write the PCM data to the output WAV file.
                    WaveFileWriter.CreateWaveFile(fileWav, pcmStream);
                }
            }
        }

        /* Converts wav file to wem, wem file is outputted with same file name and location as wav */
        public static void WAVtoWEM(string fileWav)
        {
            string wemPath = Path.ChangeExtension(fileWav, ".wem");
            string dir = Path.GetDirectoryName(wemPath);
            string file = Path.GetFileNameWithoutExtension(wemPath);

            for (int retry = 0; retry < Const.SAM_MAX_RETRY; retry++)
            {
                if (File.Exists(wemPath))
                {
                    // Audio file already exists in cache, no need to retry
                    return;
                }

                try
                {
                    // --- 3. Convert WAV to WEM (Wwise Console) ---

                    string wwiseConsolePath = Path.Combine(Const.WWISE_PATH, "WwiseConsole.exe");
                    string xmlName = $"{file}.wsources";
                    string xmlPath = Path.Combine(dir, xmlName);
                    string projectDir = Path.Combine(Const.CACHE_PATH, "wwise");
                    string projectPath = Path.Combine(projectDir, "wwise.wproj");

                    // Create XML file
                    string xmlRaw = $"""
                        <?xml version='1.0' encoding='UTF-8'?>
                        <ExternalSourcesList SchemaVersion="1" Root="{dir}"><Source Path="{file}.wav" Conversion="Vorbis Quality High" /></ExternalSourcesList>
                        """;
                    File.WriteAllText(xmlPath, xmlRaw);

                    // Create Wwise project if it doesn't exist
                    if (!File.Exists(projectPath))
                    {
                        // Wwise requires the folder to not exist for project creation
                        if (Directory.Exists(projectDir)) { Directory.Delete(projectDir, true); }

                        ProcessStartInfo createProjectInfo = new(wwiseConsolePath)
                        {
                            WorkingDirectory = Const.CACHE_PATH,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        createProjectInfo.ArgumentList.AddRange(["create-new-project", $"\"{projectPath}\"", "--platform", "Windows"]);
                        Utility.ExecuteProcess(createProjectInfo);
                    }

                    // Convert wav to wem
                    ProcessStartInfo convertInfo = new(wwiseConsolePath)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = dir,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    string topDir = fileWav.Contains("music") ? "music" : "sound";
                    string xmlRelative = Path.Combine("..", topDir, file, xmlName);
                    convertInfo.ArgumentList.AddRange(["convert-external-source", $"\"{projectPath}\"", "--source-file", xmlRelative, "--output", "Windows", $"\"{dir}\""]);
                    Utility.ExecuteProcess(convertInfo);

                    // If we reach here, both processes completed successfully (ExitCode 0)
                    if (File.Exists(wemPath))
                    {
                        return;
                    }

                    // If processes succeeded but the file isn't there, something is wrong, we retry
                    throw new FileNotFoundException($"WEM file was not found after successful conversion: {wemPath}");
                }
                catch
                {
                    // Keep retrying. Don't spam log after every failed generation as it's bloat.
                    // If we fail up to MAX_RETRY then we throw an exception and print log.
                }
            }

            // Final check after all retries
            if (!File.Exists(wemPath))
            {
                Lort.Log($"Failed to convert wav '{fileWav}' despite {Const.SAM_MAX_RETRY} retry attempts.", Lort.Type.Debug);
                throw new($"Failed to convert wav '{fileWav}' despite {Const.SAM_MAX_RETRY} retry attempts.");
            }
        }
    }
}
