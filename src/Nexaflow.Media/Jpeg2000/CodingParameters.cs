// Ported without nullable annotations; they are added as the file is reworked.
#nullable disable
using System;
using Nexaflow.Media.Jpeg2000.Internal;

namespace Nexaflow.Media.Jpeg2000
{
    internal class CodingParameters
    {
        #region C# impl
        #region JPEG 2000 profile macros

        #endregion

        #endregion

        public J2K_PROFILE rsiz;

        public uint tx0;

        public uint ty0;

        public uint tdx;

        public uint tdy;

        /// <summary>
        /// Number of tiles in width
        /// </summary>
        public uint tw;

        /// <summary>
        /// Number of tiles in heigth
        /// </summary>
        public uint th;

        /// <summary>
        /// Number of ppm markers
        /// </summary>
        public uint ppm_markers_count
        {
            get { return ppm_markers != null ? (uint)ppm_markers.Length : 0u; }
            set
            {
                if (value != ppm_markers_count)
                    throw new NotImplementedException("ppm_markers_count != ppm_markers.Length");
            }
        }

        /// <summary>
        /// PPM markers data (table indexed by Zppm) 
        /// </summary>
        public PPX[] ppm_markers;

        /// <summary>
        /// Packet header store there for futur use in t2_decode_packet
        /// </summary>
        /// <remarks>For length, remeber to substract ppm_data_start</remarks>
        public byte[] ppm_data;
        //private byte[] _ppm_buffer;
        /// <summary>
        /// Size of ppm data.
        /// </summary>
        /// <remarks>
        /// Use of this is a little odd, so don't assume ppm_len == ppm_data.Length
        /// </remarks>
        public uint ppm_len;

        /// <summary>
        /// C# impl. From where one must start reading in the ppm_data array
        /// </summary>
        /// <remarks>To avoid having to shrink the array when there's
        /// a need to move the start position</remarks>
        public int ppm_data_start, ppm_buffer_pt;

        /// <summary>
        /// Tile Coding Parameters
        /// </summary>
        internal TileCodingParams[] tcps;

        internal EncOrDec specific_param;

        /// <summary>
        /// True: entire bit stream must be decoded
        /// False: partial bitstream decoding allowed
        /// </summary>
        public bool strict;

        uint bitvector1;

        public bool ppm
        {
            get
            {
                return (this.bitvector1 & 1u) != 0;
            }
            set
            {
                this.bitvector1 = value ? 1u | bitvector1 : ~1u & bitvector1;
            }
        }

        public bool AllowDifferentBitDepthSign
        {
            get
            {
                return (bitvector1 & 4u) == 4u;
            }
            set
            {
                bitvector1 = value ? 4u | bitvector1 : ~4u & bitvector1;
            }
        }

        //[System.Runtime.InteropServices.StructLayoutAttribute(System.Runtime.InteropServices.LayoutKind.Explicit)]
        internal struct EncOrDec
        {
            //[System.Runtime.InteropServices.FieldOffsetAttribute(0)]
            public DecodeParms dec;
        }
    }

    internal struct PPX
    {
        public uint DataSize { get { return (uint) Data.Length; } }

        /// <summary>
        /// If null => Zppx not read yet 
        /// </summary>
        public byte[] Data;
    }
}
