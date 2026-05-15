using static SoulsFormats.DCX;

namespace JortPob.Common
{
    public class Compression
    {
        public static CompressionInfo DFLT()
        {
            return new DcxDfltCompressionInfo(DfltCompressionPreset.DCX_DFLT_11000_44_9);
        }

        public static CompressionInfo KRAK()
        {
            if(Const.DEBUG_USE_DFLT_COMPRESSION) { return DFLT(); }
            return new DcxKrakCompressionInfo(KrakCompressionPreset.EldenRing);
        }

        public static CompressionInfo ZSTD()
        {
            return new DcxZstdCompressionInfo(21);
        }

        public static CompressionInfo NONE()
        {
            return new NoCompressionInfo();
        }
    }
}
