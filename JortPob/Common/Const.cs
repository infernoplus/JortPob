using Mutagen.Bethesda;
using SoulsFormats;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace JortPob.Common
{
    /// <summary>
    /// Const class that contains settings.json values.
    /// Properties marked with [Setting] are required in the json.
    /// Properties marked with [Setting(value)] have a default value and thus not required in the json.
    /// Properties marked with either `Setting` attribute MUST have { get; private set; }. Even if your IDE tells you
    /// that it's never used, it's lying.
    /// </summary>
    public static class Const
    {
        static Const()
        {
            Settable.PopulateStaticClass(typeof(Const));
        }

        #region Paths

        [Setting(IsRequired = true)]
        public static string MORROWIND_PATH { get; private set; }

        [Setting(IsRequired = true)]
        public static string ELDEN_PATH { get; private set; }

        [Setting]
        public static string SKYRIM_PATH { get; private set; }

        [Setting]
        private static string SKYRIM_EDITION { get; set; }

        public static GameRelease SKYRIM_EDITION_ENUM
        {
            get
            {
                switch (SKYRIM_EDITION)
                {
                    case "SE": 
                        return GameRelease.SkyrimSE;
                    case "SEGOG": 
                        return GameRelease.SkyrimSEGog;
                    case "VR": 
                        return GameRelease.SkyrimVR;
                    case "LE":
                    default:
                        return GameRelease.SkyrimLE;
                }
            }
        }

        [Setting(IsRequired = true)]
        public static string OUTPUT_PATH { get; private set; }

        [Setting(IsRequired = true)]
        public static string WWISE_PATH { get; private set; }

        // Not required if DEBUG_SKIP_CUTSCENES is true
        [Setting]
        public static string RAD_PATH { get; private set; }

        public static string CACHE_PATH => Path.Combine(OUTPUT_PATH, @"cache\");

        [Setting(DefaultValue = new string[] { "Morrowind.esm" })]
        public static string[] LOAD_ORDER { get; private set; }

        #endregion

        #region Optimization

        [Setting(DefaultValue = 16)]
        public static int THREAD_COUNT { get; private set; }

        [Setting(DefaultValue = 15000)]
        /// when running a subprocess this is the default timeout time in millis.
        public static int DEFAULT_PROCESS_TIMEOUT { get; private set; }

        #endregion

        #region General

        /// When doing anything random we use this as our seed so results are consistent between builds
        [Setting(DefaultValue = 42)]
        public static int RANDOM_SEED { get; private set; }

        /// new global scale calculated from approx measurements of player height in both games
        public static readonly float GLOBAL_SCALE = 0.0129f;

        public static readonly int CELL_EXTERIOR_BOUNDS = 30;
        public static readonly float CELL_SIZE = 8192f * GLOBAL_SCALE;
        public static readonly float TILE_SIZE = 256f;

        /// terrain vertices
        public static readonly int CELL_GRID_SIZE = 64;

        /// generic value added to all positions in an MSB. just shifting vertical position a bit so the morrowind map isn't super far down
        public static readonly Vector3 MSB_OFFSET = new(0, 185, 0);

        /// how far the morrownid npc root (pelvis) is from it's feet. this is to fix the offset of spawn points since elden ring uses feet for characters position and mw uses pelvis
        public static readonly float NPC_ROOT_OFFSET = 75f * GLOBAL_SCALE;

        /// If a "door" is really short we make an assumption that it's a trapdoor or something and resize the interaction area to compensate
        public static readonly float DOOR_MINIMUM_HEIGHT = 115f * GLOBAL_SCALE;
        public static readonly float TRAPDOOR_HEIGHT_CORRECTION = 175f * GLOBAL_SCALE;

        /// uv scale for terrain textures
        public static readonly float TERRAIN_UV_SCALE = 20f;

        /// size of texture generated for vertex color overlay on terrain meshes
        public static readonly int TERRAIN_COLOR_OVERLAY_SIZE = 256;

        /// morrowind has a default terrain texture that is hardcoded to the exe. id = 65535
        public static readonly string TERRAIN_DEFAULT_TEXTURE = @"Data Files\textures\_land_default.dds";

        public static readonly LOD_VALUE[] TERRAIN_LOD_VALUES = new LOD_VALUE[]
        {
            new LOD_VALUE(0, FLVER2.FaceSet.FSFlags.None, 1, 512),
            new LOD_VALUE(1, FLVER2.FaceSet.FSFlags.LodLevel1, 4, 1024),
            new LOD_VALUE(2, FLVER2.FaceSet.FSFlags.LodLevel2, 16, 99999),
        };

        /* values are [0] = size of the asset model (calculated by radius and stored in modelinfo) [1] = distance it's visible via partsdrawparam. [2] = fade out range */
        public static readonly List<float[]> ASSET_LOD_VALUES = new()
        {
            new float[] { 1f, 32f, 4f },
            new float[] { 3f, 64f, 8f },
            new float[] { 7f, 128f, 16f },
            new float[] { 14f, 256f, 32f },
            new float[] { 22f, 512f, 64f },
            new float[] { 99999f, 99999f, 0f },
        };

        /* Calculated... ESM lowest cell is [-20,-20]~ on the grid. MSB lowest value is [+33,+40]~. Offset so they overlap */
        /* Updated for bloodmoon: bloodmoons furthest cell to the left is -28, 28 so we need to shift a bit more to make that fit */
        public static readonly Vector3 LAYOUT_COORDINATE_OFFSET = new((21 * CELL_SIZE) + (35 * TILE_SIZE), 0, (12 * CELL_SIZE) + (38 * TILE_SIZE));

        public static readonly int CHUNK_PARTITION_SIZE = 6;

        public static readonly float CONTENT_SIZE_BIG = 7f;
        public static readonly float CONTENT_SIZE_HUGE = 20f;

        /// how many assets need a scale before we bake it into a prescaled asset. otherwise dynamic scale is used
        public static readonly int ASSET_BAKE_SCALE_CUTOFF = 5;

        public static readonly int DYNAMIC_ASSET = -1;

        public static readonly short FLVER_DMY_ROOT = 90;
        public static readonly short FLVER_DMY_CENTER = 100;
        public static readonly short FLVER_DMY_BOTTOM = 101;
        public static readonly short FLVER_DMY_TOP = 102;

        /// Random magic number used in NVA.Navmesh 
        public static readonly int NVA_NV_UNK00_MAGIC = 1726789910;

        /// when creating regions for patrol routes or travel points or whatever, use this size sphere
        public static readonly float PATH_REGION_SIZE = 2f;

        /// asset folder starting id for generated assets EX: "aeg900_xxx"
        public static readonly int ASSET_GROUP = 900;

        /// asset folder id for stuff generated by the watermanager
        public static readonly int WATER_ASSET_GROUP = 910;

        /// asset folder id for pickable assets
        public static readonly int PICKABLE_ASSET_GROUP = 911;

        /// param row starting id for this type of param
        public static readonly short PART_DRAW_PARAM = 9000;

        /// used to build water. primarily, if a collision triangle is under this value it becomes the water material. i have no idea why morrowind water is at -3f
        public static readonly float WATER_HEIGHT = -3 * GLOBAL_SCALE;

        /// when we generate water squares, we generate a circle of cells this size. 30 means 30 CELLS of water not 30 units
        public static readonly int WATER_RADIUS = 30;

        /// the ""center"" cell of morrowind. the actual 0,0 of the morrowind map is very off center. this correction is just for the water plane to look nicer.
        public static readonly Vector2 WATER_CENTER = new Vector2(3f, 5f);

        /// number of times to subdivide squares for water
        public static readonly int WATER_TESSELATION = 2;

        /// size of a quad for lava/swamp visual mesh. affects tesselation and edge accuracy. smaller than 2f gets really slow! ideal value is like 1.28 though
        public static readonly float LIQUID_QUAD_GENERATE_SIZE = 2.56f;

        /// add a bottom to lava pools so they dont get super deep
        public static readonly float LAVA_FLOOR_DEPTH = -0.3f;

        /// lowers visual mesh  of lava. this offset is to fix the issue of the lava waves going way higher than the edge of pools sometimes
        public static readonly float LAVA_VISUAL_OFFSET = -0.125f;

        /// add a bottom to swamp pools so they dont get super deep
        public static readonly float SWAMP_FLOOR_DEPTH = -0.875f;

        /// increase size of cutouts slightly so we dont get any clipping at the edges, only used by water visual mesh subtraction
        public static readonly float WATER_CUTOUT_SIZE_TWEAK = 1.25f;

        /// an MSB can only support 19 "bonfires" due to entity ids being hardcoded to the range of 0950 to 0999 in the msb block
        public static readonly int MAX_BEDS_PER_MSB = 19;

        #endregion

        #region SFX

        public static readonly int FXR_START_ID = 900000000;

        #endregion

        #region Gameplay

        public static readonly float MERCANTILE_BUY_SCALE = 1.1f;
        public static readonly float MERCANTILE_SELL_SCALE = 0.45f;

        /// multiplier of spells mana cost to it's purchase value
        public static readonly float MERCANTILE_SPELL_VALUE_SCALE = 10f;

        /// instead of 100 being "master" its 100 * X
        public static readonly float ALCHEMY_TIER_REQUIREMENT_SCALE = 0.45f;

        /// various distances for crime detection
        public static readonly float CRIME_CITIZEN_REACT_RADIUS = 15f;
        public static readonly float CRIME_GUARD_REACT_RADIUS = 50f;

        #endregion

        #region Papyrus

        public static readonly int ESD_STATE_HARDCODE_MODDISPOSITION = 45;
        public static readonly int ESD_STATE_HARDCODE_MODFACREP = 46;
        public static readonly int ESD_STATE_HARDCODE_PERSUADEMENU = 47;
        public static readonly int ESD_STATE_HARDCODE_HANDLECRIME = 48;
        public static readonly int ESD_STATE_HARDCODE_COMBATDIALOGSELECT = 49;
        public static readonly int ESD_STATE_HARDCODE_COMBATTALK = 50;
        public static readonly int ESD_STATE_HARDCODE_DOATTACKTALK = 51;
        public static readonly int ESD_STATE_HARDCODE_DOHITTALK = 52;
        public static readonly int ESD_STATE_HARDCODE_DOTHIEFTALK = 53;
        public static readonly int ESD_STATE_HARDCODE_IDLETALK = 54;
        public static readonly int ESD_STATE_HARDCODE_PICKPOCKET = 55;
        public static readonly int ESD_STATE_HARDCODE_TRAVELMENU = 56;
        public static readonly int ESD_STATE_HARDCODE_RANKREQUIREMENT = 57;
        public static readonly int ESD_STATE_HARDCODE_REACTIONCALC = 58;

        /// this one must be last as it can generate multiple ones after this number
        public static readonly int ESD_STATE_HARDCODE_CHOICE = 59;

        public static readonly int CRIME_GOLD_PICKPOCKET = 50;
        public static readonly int CRIME_GOLD_ASSAULT = 250;
        public static readonly int CRIME_GOLD_RESIST = 100;
        public static readonly int CRIME_GOLD_MURDER = 1000;

        /// interior travel like mage guild teleporters just use a set value
        public static readonly int TRAVEL_DEFAULT_COST = 100;

        /// distance in cells multiplied by this value
        public static readonly int TRAVEL_DISTANCE_COST = 10;

        /// distance for hello trigger and hello untrigger
        public static readonly float NPC_HELLO_DIST_IN = 3f;
        public static readonly float NPC_HELLO_DIST_OUT = 4f;
        public static readonly float NPC_IDLE_DIST_OUT = 12f;

        #endregion

        #region Dialog

        /// very ultra mega hyper slow, only for stress testing dialog
        [Setting(DefaultValue = true)]
        public static bool USE_SAM { get; private set; }

        /// generating voice synth is slightly inconsistent. it occasionally fails for no real reason.
        [Setting(DefaultValue = 10)]
        public static int SAM_MAX_RETRY { get; private set; }

        public static readonly string DEFAULT_DIALOG_WEM = Utility.ResourcePath(@"sound\page_turn.wem");

        /// character limit in a line of dialog. prevents subtitle cutting off
        [Setting(DefaultValue = 160)]
        public static int MAX_CHAR_PER_TALK { get; private set; }

        [Setting(DefaultValue = 10)]
        public static int MAX_ESD_PER_VCBNK { get; private set; }

        #endregion

        #region Music
        /// dont want or dont have set false
        [Setting]
        public static bool INCLUDE_SKYRIM_MUSIC { get; private set; }

        /// dont want set false. you can have both and it will combine the OSTs from both games if desired
        [Setting(DefaultValue = true)]
        public static bool INCLUDE_MORROWIND_MUSIC { get; private set; }
        #endregion

        #region Debug

        /* when building for release everything in this group should be FALSE or NULL */

        [Setting]
        public static bool DEBUG_REUSE_FILES { get; private set; }

        [Setting]
        public static bool DEBUG_SKIP_CUSTOM_MAP { get; private set; }

        /// can make dialog unuseable
        [Setting]
        public static bool DEBUG_SKIP_SOUND { get; private set; }

        /// skips navmeshes, will make enmey ai act really stupid
        [Setting]
        public static bool DEBUG_SKIP_NAVMESH { get; private set; }

        /// if true we only generate items that referenced in script files directly, or have overrides. minor speedup
        [Setting]
        public static bool DEBUG_SKIP_NON_ESSENTIAL_ITEMS { get; private set; }

        /// skip generating icons and previews for items. All icons will show default fallback icon (saves 1~ minute on builds)
        [Setting]
        public static bool DEBUG_SKIP_MENU_TEXTURES { get; private set; }

        /// skip generating cutscene binds and BK2 videos.
        [Setting]
        public static bool DEBUG_SKIP_CUTSCENES { get; private set; }

        /// if true we don't overwrite base game overworld tiles with blanks. probably no reason to set this to true but it's here
        [Setting]
        public static bool DEBUG_DONT_WRITE_BLANK_MSBS { get; private set; }

        /// disables all doors that are NOT load doors
        [Setting(DefaultValue = true)]
        public static bool DEBUG_DISCARD_ANIMATED_DOORS { get; private set; }

        [Setting] 
        public static bool DEBUG_SKIP_FMG_PARAM_SORTING { get; private set; }

        /// skip building dialog AND scripts
        [Setting]
        public static bool DEBUG_SKIP_SCRIPTS { get; private set; }

        /// slow as shit, skipping this saves about a minute per build
        [Setting(DefaultValue = true)]
        public static bool DEBUG_SKIP_NICE_WATER_CIRCLIFICATION { get; private set; }

        /// big speedup on builds, allows multithreading of landscape processing, but makes terrain borders very ugly
        [Setting(DefaultValue = true)]
        public static bool DEBUG_SKIP_TERRAIN_BORDER_BLENDING { get; private set; }

        /// if true we use DFLT in place of KRAK to save time on compression. use KRAK in production!
        [Setting(DefaultValue = true)]
        public static bool DEBUG_USE_DFLT_COMPRESSION { get; private set; }

        /// if true we build hkx to binary instead of xml. binary is worse inengine but smithbox cant read xml so guuh
        [Setting(DefaultValue = true)]
        public static bool DEBUG_HKX_FORCE_BINARY { get; private set; }

        /// also set to null or remove from settings.json to build entire map. format x1, y1, x2, y2. smaller values first, 1 = 1 cell, use cell coordinates
        /// seyda neen area (small) = new int[] {-3, -10, -1, -8 }
        /// seyda neen area (large) = new int[] { -5, -15, 5, -5 }
        /// balmora area (small) = new int[] {-4, -3, -2, -1}
        /// caldera area (small) = new int[] {-3, 1, 0, 3}
        /// balmora + caldera combo = new int[] {-4, -3, -1, 3}
        /// lava area near Marandus and Ashunartes = new int[] {1, -5, 5, -1}
        /// all lava areas (big) = new int[] {0, -5, 15, 10}
        /// lava area near Galom Daeus = new int[] {8, -2, 12, 2}
        /// lava and swamp areas combined = new int[] {-10, -10, 15, 5};
        /// half the map = new int[] {-10, -15, 20, 0};
        [Setting(DefaultValue = new int[] { })]
        public static int[] DEBUG_EXCLUSIVE_BUILD_BY_BOX { get; private set; }

        /// set to "null" or remove from settings.json to build entire map.
        [Setting(DefaultValue = new string[] { })]
        public static string[] DEBUG_EXCLUSIVE_INTERIOR_BUILD_NAME_MATCHES { get; private set; }

        public static bool DEBUG_EXCLUSIVE_INTERIOR_BUILD_NAME(string name)
        {
            // if a cell name contains any of the strings in this list (even partial matches) we build it, otherwise skip.
            // set MATCHES to null if for proper normal building
            string[]
                MATCHES =
                    DEBUG_EXCLUSIVE_INTERIOR_BUILD_NAME_MATCHES;

            if (MATCHES == null)
            {
                return true;
            }

            foreach (string m in MATCHES)
            {
                if (name.ToLower().Contains(m.ToLower()))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        /* Some CBT */
        public struct LOD_VALUE
        {
            public readonly FLVER2.FaceSet.FSFlags FLAG;
            public readonly int INDEX, DIVISOR;
            public readonly float DISTANCE;

            public LOD_VALUE(int index, FLVER2.FaceSet.FSFlags flag, int divisor, float distance)
            {
                INDEX = index;
                FLAG = flag;
                DIVISOR = divisor;
                DISTANCE = distance;
            }
        }
    }
}