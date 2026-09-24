using JortPob.Common;
using JortPob.Logging;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static JortPob.FactionInfo;

namespace JortPob.Worker
{
    public class SamWorker : Worker
    {
        private readonly List<SoundManager.SAMData> datas;
        private readonly int start;
        private readonly int end;

        public SamWorker(List<SoundManager.SAMData> datas, int start, int end)
        {
            this.datas = datas;
            this.start = start;
            this.end = end;

            _thread = new Thread(Run);
            _thread.Start();
        }

        private void Run()
        {
            ExitCode = 1;

            for(int i = start;i<Math.Min(datas.Count(), end);i++)
            {
                SoundManager.SAMData dat = datas[i];
                SAM.GenerateAlt(dat.dialog, dat.info, dat.line, dat.hashName, dat.npc);
                Lort.TaskIterate(); // Progress bar update
            }

            IsDone = true;
            ExitCode = 0;
        }

        public static void Go(List<SoundManager.SAMData> datas)
        {
            using var perf = PerformanceMonitor.TrackPerformance();
            Lort.Log($"Generating {datas.Count()} WEMs...", Lort.Type.Main);
            Lort.NewTask("Writing WEMs", datas.Count);

            int partition = (int)Math.Ceiling(datas.Count / (float)Const.THREAD_COUNT);
            List<SamWorker> workers = new();

            datas.AsParallel()
                .WithDegreeOfParallelism(Const.THREAD_COUNT)
                .ForAll(data =>
                {
                    SAM.GenerateAlt(data.dialog, data.info, data.line, data.hashName, data.npc);
                    Lort.TaskIterate();
                    return;
                });
        }
    }
}
