﻿using JortPob.Common;
using SharpAssimp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace JortPob.Worker
{
    public class CellWorker : Worker
    {
        private ESM esm;
        private List<JsonNode> json;
        private int start;
        private int end;

        public List<Cell> cells;

        public CellWorker(ESM esm, List<JsonNode> json, int start, int end)
        {
            this.esm = esm;
            this.json = json;
            this.start = start;
            this.end = end;

            cells = new();

            _thread = new Thread(Run);
            _thread.Start();
        }

        private void Run()
        {
            ExitCode = 1;

            for (int i = start; i < Math.Min(json.Count, end); i++)
            {
                JsonNode node = json[i];
                if (Const.DEBUG_EXCLUSIVE_CELL_BUILD_BY_NAME != null && !(node["name"] != null && node["name"].ToString() == Const.DEBUG_EXCLUSIVE_CELL_BUILD_BY_NAME)) { continue; }

                Cell cell = new(esm, node);
                cells.Add(cell);

                Lort.TaskIterate(); // Progress bar update
            }

            IsDone = true;
            ExitCode = 0;
        }

        public static List<List<Cell>> Go(ESM esm)
        {
            Lort.Log($"Parsing {esm.records[ESM.Type.Cell].Count} cells...", Lort.Type.Main);
            Lort.NewTask("Parsing Cells", esm.records[ESM.Type.Cell].Count);

            int partition = (int)Math.Ceiling(esm.records[ESM.Type.Cell].Count / (float)Const.THREAD_COUNT);
            List<CellWorker> workers = new();

            for (int i = 0; i < Const.THREAD_COUNT; i++)
            {
                int start = i * partition;
                int end = start + partition;
                CellWorker worker = new(esm, esm.records[ESM.Type.Cell], start, end);
                workers.Add(worker);
            }

            /* Wait for threads to finish */
            while (true)
            {
                bool done = true;
                foreach (CellWorker worker in workers)
                {
                    done &= worker.IsDone;
                }

                if (done)
                    break;
            }

            /* Grab all parsed cells from threads and put em in lists */
            List<Cell> interior = new();
            List<Cell> exterior = new();
            foreach (CellWorker worker in workers)
            {
                foreach (Cell cell in worker.cells)
                {
                    if (Math.Abs(cell.coordinate.x) > Const.CELL_EXTERIOR_BOUNDS || Math.Abs(cell.coordinate.y) > Const.CELL_EXTERIOR_BOUNDS)
                    {
                        interior.Add(cell);
                    }
                    else
                    {
                        exterior.Add(cell);
                    }
                }

            }

            return new List<List<Cell>>() { exterior, interior };
        }
    }
}
