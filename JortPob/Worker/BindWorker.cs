﻿using JortPob.Common;
using SoulsFormats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace JortPob.Worker
{
    public class BindWorker : Worker
    {
        private Cache cache;
        private int start;
        private int end;

        public BindWorker(Cache cache, int start, int end)
        {
            this.cache = cache;

            this.start = start;
            this.end = end;

            _thread = new Thread(Run);
            _thread.Start();
        }

        private void Run()
        {
            ExitCode = 1;

            for (int i = start; i < Math.Min(cache.assets.Count, end); i++)
            {
                ModelInfo modelInfo = cache.assets[i];
                Bind.BindAsset(modelInfo, $"{Const.OUTPUT_PATH}asset\\aeg\\{modelInfo.AssetPath()}.geombnd.dcx");
                Lort.TaskIterate(); // Progress bar update
            }

            IsDone = true;
            ExitCode = 0;
        }
    }
}
