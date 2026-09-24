using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Runtime.CompilerServices;
using JortPob.Common;
using System.IO;

#nullable enable

namespace JortPob.Logging
{
    public static class PerformanceMonitor
    {
        // Get the most 
        private static ConcurrentDictionary<int, IPerformanceSection> ThreadSections { get; } = new();

        private static int ThreadId => Thread.CurrentThread.ManagedThreadId;

        public static IPerformanceSection? CurrentSection
        {
            get
            {
                return ThreadSections.GetValueOrDefault(ThreadId);
            }
        }

        public static IPerformanceSection TrackPerformance([CallerMemberName] string callingMethod = "", [CallerFilePath] string callingFile = "")
        {
            if (!Const.DEBUG_LOG_PERFORMANCE)
                return new PerformanceSection.Dummy();

            if (!string.IsNullOrWhiteSpace(callingFile))
                callingFile = Path.GetFileNameWithoutExtension(callingFile);
            else
                callingFile = "NONE";
            // may be null which means this is the top-most performance section of this thread
            var parent = CurrentSection;
            var newSection = new PerformanceSection($"{callingFile}.{callingMethod}", parent);
            // override the current thread section
            return ThreadSections.AddOrUpdate(ThreadId, newSection, (_, old) =>
            {
                // Pause the active timer until this returns to being the current section for the thread
                old.Pause();
                return newSection;
            });
        }

        public static void ReportResults(PerformanceSection section)
        {
            var currentSection = CurrentSection;
            if (!ReferenceEquals(currentSection, section))
            {
                Lort.Log("WARNING: PerformanceSection reporting for non-current section", Lort.Type.Debug);
            }
            else if (section.ParentSection is PerformanceSection parentSection && parentSection is not null && !parentSection.DisposedValue) // Parent should never be disposed of before child...
            {
                if (!ThreadSections.TryUpdate(ThreadId, parentSection, section))
                {
                    Lort.Log("WARNING: PerformanceSection reporting failed to set new current section for thread", Lort.Type.Debug);
                }
                else
                    // Make sure to resume the parent section's active timer
                    section.ParentSection.Resume();
            }
            else
            {
                if (!ThreadSections.TryRemove(ThreadId, out _))
                {
                    Lort.Log("WARNING: PerformanceSection reporting failed to clear final section for thread", Lort.Type.Debug);
                }
            }

            // Technically inefficient to concat like this but there aren't heaps of these operations and otherwise terrible to read
            Lort.Log($"[{DateTime.UtcNow:G}] {section.Name}:".PadRight(80) +
                $"ElapsedTime={section.TotalElapsedTimeMilliseconds}ms".PadRight(30) +
                $"ActiveTime={section.ActiveElapsedTimeMilliseconds}ms".PadRight(30) +
                $"StartingMemory={section.StartingMemoryBytes / 1000000}MB".PadRight(30) +
                $"EndingMemory={section.EndingMemoryBytes / 1000000}MB".PadRight(30) +
                $"StartTime={section.StartTime:u}".PadRight(34) +
                $"EndTime={section.EndTime:u}".PadRight(34),
                Lort.Type.Performance);
        }
    }

    public interface IPerformanceSection : IDisposable
    {
        // Resume this section as the currently active section for the current thread
        void Resume();

        // Pause this section as it is not longer the currently active section for the current thread
        void Pause();
    }

    public class PerformanceSection : IPerformanceSection
    {
        // Measures the total time since this performance section was created
        private Stopwatch _stopwatch = new();

        // Measures the total time that this performance section was 'current'
        private Stopwatch _activeStopwatch = new();

        public bool DisposedValue { get; private set; }

        public IPerformanceSection? ParentSection { get; init; } = null;

        public DateTime StartTime { get; init; }

        public DateTime EndTime { get; private set; }

        /// <summary>
        /// Gets the elapsed time on the timer in milliseconds
        /// </summary>
        public float TotalElapsedTimeMilliseconds => _stopwatch.ElapsedTicks * (1000f) / Stopwatch.Frequency;

        public float ActiveElapsedTimeMilliseconds => _activeStopwatch.ElapsedTicks * (1000f) / Stopwatch.Frequency;

        public long StartingMemoryBytes { get; init; }

        public long EndingMemoryBytes { get; private set; }

        /// <summary>
        /// Performance section name, may be either the caller method name or a specified name
        /// </summary>
        public string Name { get; init; }

        public PerformanceSection(string name, IPerformanceSection? parentSection = null)
        {
            Name = name;
            ParentSection = parentSection;
            StartingMemoryBytes = Process.GetCurrentProcess().PrivateMemorySize64;
            StartTime = DateTime.Now;
            _stopwatch.Start();
            _activeStopwatch.Start();
        }

        public void Resume()
        {
            _activeStopwatch.Start();
        }

        public void Pause()
        {
            _activeStopwatch.Stop();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!DisposedValue)
            {
                if (disposing)
                {
                    _activeStopwatch.Stop();
                    _stopwatch.Stop();
                    EndTime = DateTime.Now;
                    EndingMemoryBytes = Process.GetCurrentProcess().PrivateMemorySize64;
                    PerformanceMonitor.ReportResults(this);
                }

                DisposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        // Dummy class for when performance logging is disabled
        internal class Dummy : IPerformanceSection
        {
            public void Dispose()
            { }

            // Do nothing
            public void Resume()
            { }

            public void Pause()
            { }
        }
    }
}
