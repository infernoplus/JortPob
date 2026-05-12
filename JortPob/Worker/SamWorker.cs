using JortPob.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;

namespace JortPob.Worker
{
    public class SamWorker : IWorker<Unit>
    {
        private readonly List<SoundManager.SAMData> datas;

        public SamWorker(List<SoundManager.SAMData> datas)
        {
            this.datas = datas;
        }

        private Unit Run()
        {
            List<SAM.GenerateAltEntry> files = new();
            foreach (var data in datas)
            {
               files.Add(new(data.dialog, data.info, data.line, data.hashName, data.npc)); 
            }
            SAM.GenerateAltBatch(files);
            // Heavy overhead with wwise console being started for each instance so in this case limiting parallelism is needed
            /*
            Parallel.ForEach(datas, new ParallelOptions{MaxDegreeOfParallelism = 4}, data =>
            {
                SAM.GenerateAlt(data.dialog, data.info, data.line, data.hashName, data.npc);
                Lort.TaskIterate();
            });
            */
            return Unit.Default;
        }

        public Unit Go()
        {
            Lort.Log("BUILDING AUDIO FILES", Lort.Type.Main);
            return Run();
        }
    }
}
