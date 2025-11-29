using JortPob.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace JortPob.Worker
{
    public class CellWorker
    {
        private static Cell? MakeCell (JsonNode node, ESM esm)
        {
            bool is_interior = node["data"]!["flags"]!.GetValue<string>().ToLower().Contains("is_interior");
            int x = int.Parse(node["data"]!["grid"]![0]!.ToString());
            int y = int.Parse(node["data"]!["grid"]![1]!.ToString());
            if (Const.DEBUG_EXCLUSIVE_CELL_BUILD_BY_NAME != null && !(node["name"] != null && node["name"]!.ToString() == Const.DEBUG_EXCLUSIVE_CELL_BUILD_BY_NAME)) { return null; }
            if (Math.Abs(x) > Const.CELL_EXTERIOR_BOUNDS || Math.Abs(y) > Const.CELL_EXTERIOR_BOUNDS || is_interior)
            {
                if (!Const.DEBUG_EXCLUSIVE_INTERIOR_BUILD_NAME(node["name"]!.ToString()) || Const.DEBUG_SKIP_INTERIOR)
                {
                    return null;
                }
            }
            else
            {
                if (Const.DEBUG_EXCLUSIVE_BUILD_BY_BOX != null)
                {
                    if (
                        x < Const.DEBUG_EXCLUSIVE_BUILD_BY_BOX[0] ||
                        y < Const.DEBUG_EXCLUSIVE_BUILD_BY_BOX[1] ||
                        x > Const.DEBUG_EXCLUSIVE_BUILD_BY_BOX[2] ||
                        y > Const.DEBUG_EXCLUSIVE_BUILD_BY_BOX[3]
                    )
                    { return null; }
                }
            }
            /* ================== =================================== ================== */

            Cell cell = new(esm, node);

            // If the cell is basically empty, we just go ahead and discard it.
            if (cell.contents.Count() <= 0) { return null; }

            return cell;
        }

        public static (List<Cell>, List<Cell>) Go(ESM esm)
        {
            var cellRecords = esm.GetAllRecordsByType(ESM.Type.Cell).ToList();
            Lort.Log($"Parsing {cellRecords.Count} cells...", Lort.Type.Main);
            Lort.NewTask("Parsing Cells", cellRecords.Count);

            var cells = cellRecords.AsParallel()
                .WithDegreeOfParallelism(Const.THREAD_COUNT)
                .Select(node => {
                        var cell = MakeCell(node, esm);
                        Lort.TaskIterate(); // Progress bar update
                        return cell;
                    })
                .Where(cell => cell != null);

            /* Grab all parsed cells from threads and put em in lists */
            List<Cell> interior = new();
            List<Cell> exterior = new();
            foreach (var cell in cells)
            {
                if (Math.Abs(cell!.coordinate.x) > Const.CELL_EXTERIOR_BOUNDS || Math.Abs(cell.coordinate.y) > Const.CELL_EXTERIOR_BOUNDS || cell.HasFlag(Cell.Flag.IsInterior))
                {
                    interior.Add(cell);
                }
                else
                {
                    exterior.Add(cell);
                }
            }

            return (exterior, interior);
        }
    }
}
