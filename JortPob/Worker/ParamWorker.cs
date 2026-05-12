using JortPob.Common;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SoulsFormats;
using WitchyFormats;
using static JortPob.Paramanager;

namespace JortPob.Worker
{
    public class ParamWorker(BND4 paramBnd, Dictionary<ParamDefType, WitchyFormats.PARAMDEF> paramdefs) : IWorker<Dictionary<ParamType, FsParam>>
    {
        private Dictionary<ParamType, FsParam> Run()
        {
            ConcurrentDictionary<ParamType, FsParam> param = new();
            
            Parallel.ForEach(paramBnd.Files, (file) =>
            {
                ParamType t;
                FsParam p;

                FsParam fsp = FsParam.Read(file.Bytes);
                ParamDefType ty = (ParamDefType)Enum.Parse(typeof(ParamDefType), fsp.ParamType);
                ParamType ty2 =
                    (ParamType)Enum.Parse(typeof(ParamType), Path.GetFileNameWithoutExtension(file.Name));
                fsp.ApplyParamdef(paramdefs[ty]);
                p = fsp;
                t = ty2;
                param.TryAdd(t, p);
            });
            return param.ToDictionary();
        }
        
        /* Unthreads your function~ */
        /* Shit was acting up for weird reasons in paramaanger and I thijnk it was becasue of multithreading fsparasm so i changed it to single */
        public Dictionary<ParamType, FsParam> Go()
        {
            Lort.Log($"Loading {paramBnd.Files.Count()} PARAMs...", Lort.Type.Main);
            Lort.NewTask("Loading PARAMs", paramBnd.Files.Count());

            return Run();
        }
    }
}
