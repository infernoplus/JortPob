using System;
using System.IO;
using System.Collections.Generic;
using JortPob.Common;

namespace JortPob.Logging
{
    public class Lort
    {
        private static readonly Dictionary<Type, List<ILogOutput>> LogOutputs = new()
        {
            { Type.Main, new() },
            { Type.Debug, new() },
            { Type.Performance, new() },
        };

        // Screen rendering ordered output for Lort.Type.Main
        public static readonly BufferedScreenOutput MainScreenOutput = new();
        // Screen rendering ordered output for non-Lort.Type.Main
        public static readonly BufferedScreenOutput ScreenOutput = new();

        public static string progressOutput { get; private set; }
        public static int total { get; private set; }
        public static int current { get; private set; }
        public static bool update { get; set; }
        public static string logFilePath { get; private set; }
        public static string performanceLogFilePath { get; private set; }

        public static void Initialize()
        {
            progressOutput = string.Empty;
            total = 0;
            current = 0;
            update = false;

            Directory.CreateDirectory(Path.Combine(Const.OUTPUT_PATH, "logs"));

            var timestamp = DateTime.UtcNow.ToLongTimeString().Replace(":", "").Replace(" PM", "");
            logFilePath = Path.Combine(Const.OUTPUT_PATH, @$"logs\jortpob-log-{timestamp}.txt");
            performanceLogFilePath = Path.Combine(Const.OUTPUT_PATH, @$"logs\jortpob-performance-{timestamp}.txt");
            var nonPerfLogWriter = new BufferedFileWriter(logFilePath);
            var perfLogWriter = new BufferedFileWriter(performanceLogFilePath);

            LogOutputs[Type.Main].Add(MainScreenOutput);
            LogOutputs[Type.Debug].Add(ScreenOutput);
            LogOutputs[Type.Performance].Add(ScreenOutput);
            LogOutputs[Type.Main].Add(nonPerfLogWriter);
            LogOutputs[Type.Debug].Add(nonPerfLogWriter);
            LogOutputs[Type.Performance].Add(perfLogWriter);
        }

        public enum Type
        {
            Main,
            Debug,
            Performance
        }

        public static void Log(string message, Lort.Type type)
        {
            LogOutputs[type].ForEach(output => output.SubmitLog(message));
            update = true;
        }

        public static void NewTask(string task, int max)
        {
            progressOutput = $"{task}";
            current = 0;
            total = max;
            update = true;
        }

        public static void TaskIterate()
        {
            current = Math.Min(current+1, total);
            update = true;
        }
    }
}
