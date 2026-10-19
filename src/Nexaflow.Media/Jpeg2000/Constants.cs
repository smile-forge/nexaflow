// Ported without nullable annotations; they are added as the file is reworked.
#nullable disable
namespace Nexaflow.Media.Jpeg2000
{
    internal static class Constants
    {
        /// <summary>
        /// Number of maximum resolution level authorized
        /// </summary>
        internal const int J2K_MAXRLVLS = 33;

        /// <summary>
        /// Number of maximum sub-band linked to number of resolution level
        /// </summary>
        internal const int J2K_MAXBANDS = (3 * J2K_MAXRLVLS - 2);
        internal const int J2K_DEFAULT_NB_SEGS = 10;
        internal const int MQC_NUMCTXS = 19;
        internal const int MCT_DEFAULT_NB_RECORDS = 10;
        internal const int MCC_DEFAULT_NB_RECORDS = 10;

        /// <summary>
        /// Org impl have this as ulong.MaxValue, but C# has a 2GB limit.
        /// </summary>
        internal const int SIZE_MAX = int.MaxValue;

        /// <summary>
        /// Default size of the buffer for header bytes
        /// </summary>
        internal const int J2K_DEFAULT_HEADER_SIZE = 1000;

        /// <summary>
        /// Margin for a fake FFFF marker
        /// </summary>
        internal const int COMMON_CBLK_DATA_EXTRA = 2;
    }
}
