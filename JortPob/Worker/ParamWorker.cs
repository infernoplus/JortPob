using JortPob.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WitchyFormats;
using static JortPob.Paramanager;

namespace JortPob.Worker
{
    public class ParamWorker : Worker
    {
        /* Unthreads your function~ */
        /* Shit was acting up for weird reasons in paramaanger and I thijnk it was becasue of multithreading fsparasm so i changed it to single */
        public static Dictionary<ParamType, FsParam> Go(SoulsFormats.BND4 paramBnd, Dictionary<ParamDefType, WitchyFormats.PARAMDEF> paramdefs)
        {
            Lort.Log($"Loading {paramBnd.Files.Count()} PARAMs...", Lort.Type.Main);
            Lort.NewTask("Loading PARAMs", paramBnd.Files.Count());

            Dictionary<ParamType, FsParam> param = new();
            List<ParamWorker> workers = new();
            foreach (SoulsFormats.BinderFile file in paramBnd.Files)
            {
                Paramanager.ParamType t;
                FsParam p;

                FsParam fsp = FsParam.Read(file.Bytes);
                ParamDefType ty = (ParamDefType)Enum.Parse(typeof(ParamDefType), fsp.ParamType);
                ParamType ty2 = (ParamType)Enum.Parse(typeof(ParamType), Path.GetFileNameWithoutExtension(file.Name));
                fsp.ApplyParamdef(paramdefs[ty]);
                p = fsp;
                t = ty2;
                param.Add(t, p);
            }

            return param;
        }
    }
}
