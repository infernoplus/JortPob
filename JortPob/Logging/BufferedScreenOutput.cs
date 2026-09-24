using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JortPob.Logging
{
    public class BufferedScreenOutput : ILogOutput
    {
        public ConcurrentQueue<string> LogLines { get; } = new();

        public void SubmitLog(string message)
        {
            LogLines.Enqueue(message);
        }
    }
}
