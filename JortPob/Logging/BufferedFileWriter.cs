using JortPob.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace JortPob.Logging
{
    public class BufferedFileWriter : ILogOutput, IDisposable
    {
        private const int TickBufferSize = 200;

        private bool Disposed { get; set; } = false;
        
        private string FilePath { get; init; }

        private ConcurrentQueue<string> LogLines { get; } = new();

        private Thread WorkerThread;

        public BufferedFileWriter(string filePath)
        {
            FilePath = filePath;
            WorkerThread = new(ThreadLoop);
            WorkerThread.Start();
        }

        private async void ThreadLoop()
        {
            while (!Disposed)
            {
                ThreadTick();
                if (LogLines.IsEmpty)
                    await Task.Delay(100);
            }
        }

        private void ThreadTick()
        {
            List<string> lines = new(TickBufferSize);
            for (int i = 0; i < TickBufferSize && !LogLines.IsEmpty; i++)
            {
                if (LogLines.TryDequeue(out var line))
                    lines.Add(line);
                else
                    break;
            }

            if (!lines.Empty())
                File.AppendAllLines(FilePath, lines);
        }

        public void SubmitLog(string message)
        {
            if (!Disposed)
                LogLines.Enqueue(message);
        }

        public void Dispose()
        {
            Disposed = true;
            LogLines.Clear();
            try
            {
                WorkerThread.Join(5000);
            }
            catch
            { 
                // Do nothing as logging has been terminated and likely the app is shutting down
            }
        }
    }
}
