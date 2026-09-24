using System.IO;
using JortPob.Common;
using JortPob.Logging;

namespace JortPob
{
    public class MenuTextureManager
    {
        public IconManager icon;

        public MenuTextureManager(ESM esm)
        {
            using var perf = PerformanceMonitor.TrackPerformance();
            icon = new(esm);
        }

        public void Write()
        {
            if (Const.DEBUG_SKIP_MENU_TEXTURES) { return; }

            (var hiBxf, var lowBxf) = icon.Write();
            (var newHiBxf, var newLowBxf) = LoadingImagesManager.Write(hiBxf, lowBxf);

            newHiBxf.Write(Path.Combine(Const.OUTPUT_PATH, @"menu\hi\00_solo.tpfbhd"), Path.Combine(Const.OUTPUT_PATH, @"menu\hi\00_solo.tpfbdt"));
            newLowBxf.Write(Path.Combine(Const.OUTPUT_PATH, @"menu\low\00_solo.tpfbhd"), Path.Combine(Const.OUTPUT_PATH, @"menu\low\00_solo.tpfbdt"));
        }
    }
}
