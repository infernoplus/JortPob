using JortPob.Common;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static JortPob.Faction;

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
                SAM.Generate(dat.dialog, dat.info, dat.line, dat.hashName, dat.npc);
                Lort.TaskIterate(); // Progress bar update
            }

            IsDone = true;
            ExitCode = 0;
        }

        public static void Go(List<SoundManager.SAMData> datas)
        {
            Lort.Log($"Generating {datas.Count()} WEMs...", Lort.Type.Main);
            Lort.NewTask("Writing WEMs", datas.Count);

            int partition = (int)Math.Ceiling(datas.Count / (float)Const.THREAD_COUNT);
            List<SamWorker> workers = new();

            const int MAX_ATTEMPTS = 3;
            datas.AsParallel()
                .WithDegreeOfParallelism(Const.THREAD_COUNT)
                .ForAll(data =>
                {
                    for (var i = 0; i < MAX_ATTEMPTS; i++)
                    {
                        try
                        {
                            SAM.Generate(data.dialog, data.info, data.line, data.hashName, data.npc);
                            Lort.TaskIterate();
                            return;
                        }
                        catch (Exception ex)
                        {
                            Lort.Log($"Failed to generate SAM ({i + 1}/{MAX_ATTEMPTS}) hash: {data.hashName}\r\nReason: {ex.Message}", Lort.Type.Debug);
                        }
                    }
                    Lort.Log($"Failed to generate SAM after {MAX_ATTEMPTS} tries, exitting...", Lort.Type.Main);
                    throw new Exception($"Failed to generate SAM after {MAX_ATTEMPTS} tries, exitting...");
                });
        }
    }
}
