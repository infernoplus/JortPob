using JortPob.Common;
using System;
using System.Collections.Generic;

namespace JortPob.Worker
{
    public class SamWorker : Worker
    {
        public static void Go(List<SoundManager.SAMData> datas)
        {
            SAM.GenerateAltBatch(datas);
        }
    }
}
