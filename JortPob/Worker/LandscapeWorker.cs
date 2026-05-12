using JortPob.Common;
using JortPob.Model;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static JortPob.ESM;

namespace JortPob.Worker
{
    public class LandscapeWorker : IWorker<List<TerrainInfo>>
    {
        private readonly MaterialContext _materialContext;
        private readonly ESM _esm;
        
        public LandscapeWorker(MaterialContext materialContext, ESM esm)
        {
            _materialContext = materialContext;
            _esm = esm;
        }

        private List<TerrainInfo> Run()
        {
            ConcurrentBag<TerrainInfo> terrains = new();
            Parallel.ForEach(_esm.exterior, cell =>
            {
                Landscape landscape = _esm.GetLandscape(cell.coordinate);
                if (landscape == null)
                {
                    return;
                }

                TerrainInfo terrainInfo = new(landscape.coordinate,
                    $"terrain\\ext{landscape.coordinate.x},{landscape.coordinate.y}.flver");
                terrainInfo = ModelConverter.LANDSCAPEtoFLVER(_materialContext, terrainInfo, landscape,
                    Path.Combine(Const.CACHE_PATH, "terrain",
                        $"ext{landscape.coordinate.x},{landscape.coordinate.y}.flver"));

                // Set some stuff
                terrainInfo.hasWater = landscape.hasWater;
                terrainInfo.hasSwamp = landscape.hasSwamp;
                terrainInfo.hasLava = landscape.hasLava;

                terrains.Add(terrainInfo);

                Lort.TaskIterate(); // Progress bar update
            });
            return terrains.ToList();
        }

        public List<TerrainInfo> Go()
        {
            /* We can no longer multi-thread landscape loading. This is due to border blending requiring things be loaded 1 at a time. */
            /* We can still multithread the model conversions though so that is still a thing */
            if (!Const.DEBUG_SKIP_TERRAIN_BORDER_BLENDING) { _esm.LoadLandscapes(); }

            Lort.Log($"Converting {_esm.exterior.Count} landscapes...", Lort.Type.Main); // Not that slow but multithreading good
            Lort.NewTask("Converting Landscape", _esm.exterior.Count);
            
            return Run();
        }
    }
}
