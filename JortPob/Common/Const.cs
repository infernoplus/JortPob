﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace JortPob.Common
{
    public static class Const
    {
        #region Paths
        public static string MORROWIND_PATH = Settable.Get("MORROWIND_PATH");
        public static string OUTPUT_PATH = Settable.Get("OUTPUT_PATH");
        public static string CACHE_PATH = $"{OUTPUT_PATH}cache\\";
        #endregion

        #region Optimization
        public static readonly int THREAD_COUNT = int.Parse(Settable.Get("THREAD_COUNT"));
        #endregion

        #region General
        public static readonly float GLOBAL_SCALE = 0.01f;
        public static readonly int CELL_EXTERIOR_BOUNDS = 30;
        public static readonly float CELL_SIZE = 8192f * GLOBAL_SCALE;
        public static readonly float TILE_SIZE = 256f;
        public static readonly int CELL_GRID_SIZE = 64;    // terrain vertices

        /* Calculated... ESM lowest cell is [-20,-20]~ on the grid. MSB lowest value is [+33,+40]~. Offset so they overlap */
        public static readonly Vector3 LAYOUT_COORDINATE_OFFSET = new((20*CELL_SIZE)+(35*TILE_SIZE), 0, (20*CELL_SIZE)+(38*TILE_SIZE));

        public static int CHUNK_PARTITION_SIZE = 6;

        public static readonly float CONTENT_SIZE_BIG = 7f;
        public static readonly float CONTENT_SIZE_HUGE = 20f;

        public static readonly int ASSET_BAKE_SCALE_CUTOFF = 5;  // how many assets need a scale before we bake it into a prescaled asset. otherwise dynamic scale is used
        public static readonly int DYNAMIC_ASSET = int.MaxValue;
        #endregion

        #region Debug
        /* when building for release everything in this group should be FALSE or NULL */
        public static readonly string DEBUG_EXCLUSIVE_CELL_BUILD_BY_NAME = null; // set to "null" to build entire map.
        public static readonly bool DEBUG_SKIP_INTERIOR = true;
        public static readonly string DEBUG_PRINT_LOCATION_INFO = null; // set to null if you don't need it. prints msb name of a named location at build done
        public static readonly bool DEBUG_HKX_FORCE_BINARY = true;   // if true we build hkx to binary instead of xml. binary is worse inengine but smithbox cant read xml so guuh
        #endregion
    }
}
