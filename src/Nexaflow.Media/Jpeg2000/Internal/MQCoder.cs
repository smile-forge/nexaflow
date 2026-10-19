// Ported without nullable annotations; they are added as the file is reworked.
#nullable disable
#region License
/*
 * Copyright (c) 2002-2007, Communications and Remote Sensing Laboratory, Universite catholique de Louvain (UCL), Belgium
 * Copyright (c) 2002-2007, Professor Benoit Macq
 * Copyright (c) 2001-2003, David Janssens
 * Copyright (c) 2002-2003, Yannick Verschueren
 * Copyright (c) 2003-2007, Francois-Olivier Devaux and Antonin Descampe
 * Copyright (c) 2005, Herve Drolon, FreeImage Team
 * All rights reserved.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions
 * are met:
 * 1. Redistributions of source code must retain the above copyright
 *    notice, this list of conditions and the following disclaimer.
 * 2. Redistributions in binary form must reproduce the above copyright
 *    notice, this list of conditions and the following disclaimer in the
 *    documentation and/or other materials provided with the distribution.
 *
 * THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS `AS IS'
 * AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
 * IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE
 * ARE DISCLAIMED.  IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE
 * LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
 * CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF
 * SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
 * INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN
 * CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE)
 * ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE
 * POSSIBILITY OF SUCH DAMAGE.
 */
#endregion
using System;
using System.Diagnostics;

namespace Nexaflow.Media.Jpeg2000.Internal
{
    /// <summary>
    /// MQ-coder.
    /// 
    /// The MQ arithmetic coding was orgiginaly developed for JBIG image compression.
    /// 
    /// Rather than coding the binary value, MQ encodies if the symbol being coded
    /// is what we expected (Most Probably, Less Probably). 
    /// </summary>
    internal sealed class MQCoder
    {
        #region Variables and properties

        /// <summary>
        /// Temporary buffer where bits are coded or decoded
        /// </summary>
        uint c;

        /// <summary>
        /// Number of bits already read or free to write
        /// </summary>
        uint ct;

        /// <summary>
        /// Maximum length to decode
        /// </summary>
        uint _end;

        /// <summary>
        /// Source data
        /// </summary>
        byte[] _data;

        /// <summary>
        /// Original value of the 2 bytes at end[0] and end[1]
        /// </summary>
        readonly byte[] _backup = new byte[Constants.COMMON_CBLK_DATA_EXTRA];

        /// <summary>
        /// aaaaaaaaaaa
        /// </summary>
        uint a;

        /// <summary>
        /// Read position in the data array
        /// </summary>
        int _bp, _start;

        /// <summary>
        /// Only used by decoder, to count the number of times a terminating 0xFF >0x8F marker is read
        /// </summary>
        internal uint _end_of_byte_stream_counter;

        /// <remarks>
        /// C# implementation note:
        /// As long as no indexing is done into the "second"
        /// dimension of this array, it will work as in the
        /// C++ code.
        /// 
        /// If it turns out this is needed, somehow store the
        /// index of the MQCState, and use that to look up in
        /// the mqc_states array.
        /// 
        /// The important bit here is anyway that one can hand
        /// out a "ptr", and if that ptr is updated said update
        /// is reflected in this array.
        /// </remarks>
        internal Ptr<MQCState>[] ctxs = new Ptr<MQCState>[Constants.MQC_NUMCTXS];

        /// <summary>Current context</summary>
        /// <remarks>
        /// C# This is a "pointer" into ctxs. However, this implementation
        ///    has it be a Ptr class. This works since it is effectivly a
        ///    pointer into the ctxs array.
        ///    
        ///    Note, whenever curctx is changed, the new Ptr must be fetched 
        ///    from ctsx.
        /// </remarks>
        internal Ptr<MQCState> curctx;

        /// <summary>
        /// lut_ctxno_zc shifted by (1 << 9) * bandno
        /// </summary>
        /// <remarks>
        /// This is a pointer into the byte array T1Luts.lut_ctxno_zc
        /// </remarks>
        internal int lut_ctxno_zc_orient;

        #endregion

        #region Init

        //C# - Creates the ctxs "struct". Might be overdesigned, as this
        //     was some of the first code I converted and I was usure whet
        //     c pointer peculiarities I needed to support.
        internal MQCoder()
        {
            for (int c = 0; c < ctxs.Length; c++)
                ctxs[c] = new Ptr<MQCState>();
            curctx = ctxs[0];
        }

        /// <remarks>
        /// 2.5 - opj_mqc_init_dec_common
        /// </remarks>
        void CommonInit(byte[] data, uint offset, uint length, uint extra_writable_bytes)
        {
            Debug.Assert(extra_writable_bytes >= Constants.COMMON_CBLK_DATA_EXTRA);
            _data = data;
            _start = (int) offset;
            _end = offset + length;

            // Insert an artificial 0xFF 0xFF marker at end of the code block
            // data so that the bytein routines stop on it. This saves us comparing
            // the bp and end pointers
            // But before inserting it, backup th bytes we will overwrite
            Array.Copy(_data, _end, _backup, 0, Constants.COMMON_CBLK_DATA_EXTRA);
            _data[_end + 0] = 0xFF;
            _data[_end + 1] = 0xFF;
            _bp = (int)offset;
        }

        /// <summary>
        /// Initialize Raw for decoder
        /// </summary>
        /// <remarks>2.5 - opj_mqc_raw_init_dec</remarks>
        /// <param name="bp">Data</param>
        /// <param name="start_pos">Offset into data</param>
        /// <param name="len">Length of data</param>
        internal void InitRawDec(byte[] bp, uint start_pos, uint len, uint extra_writable_bytes)
        {
            CommonInit(bp, start_pos, len, extra_writable_bytes);
            c = 0;
            ct = 0;
        }

        /// <summary>
        /// Initialize MQ for decoder
        /// </summary>
        /// <remarks>2.5 - opj_mqc_init_dec</remarks>
        /// <param name="bp">The buffer from which the bytes will be read</param>
        /// <param name="start_pos">Offset into the data buffer</param>
        /// <param name="len">Length of data</param>
        internal void InitDec(byte[] bp, uint start_pos, uint len, uint extra_writable_bytes)
        {
            // Implements ISO 15444-1 C.3.5 Initialization of the decoder (INITDEC)
            // Note: alternate "J.1 - Initialization of the software-conventions
            // decoder" has been tried, but does
            // not bring any improvement.
            // See https://github.com/uclouvain/openjpeg/issues/921
            CommonInit(bp, start_pos, len, extra_writable_bytes);
            Setcurctx(0);

            _end_of_byte_stream_counter = 0;
            if (len == 0)
                c = 0xFF << 16;
            else
                c = (uint)_data[start_pos] << 16;

            ByteIn();
            c <<= 7;
            ct -= 7;
            a = 0x8000;
        }

        #endregion

        /// <summary>
        /// Set the current context used for coding/decoding
        /// </summary>
        /// <param name="ctxno">Number that identifies the context</param>
        /// <remarks>
        /// 2.5 - opj_mqc_setcurctx
        /// 
        /// Org. impl. is a macro
        /// </remarks>
        internal void Setcurctx(T1_CTXNO i) { curctx = ctxs[(int) i]; }

        /// <summary>
        /// Set the current context used for coding/decoding
        /// </summary>
        /// <param name="ctxno">Number that identifies the context</param>
        /// <remarks>
        /// 2.5 - opj_mqc_setcurctx
        /// 
        /// Org. impl. is a macro
        /// </remarks>
        internal void Setcurctx(int ctxno) { curctx = ctxs[ctxno]; }

        //2.5 - opj_mqc_raw_decode
        internal bool RawDecode()
        {
            if (ct == 0)
            {
                // Given opj_mqc_raw_init_dec() we know that at some point we will
                // have a 0xFF 0xFF artificial marker
                if (c == 0xff)
                {
                    if (_data[_bp] > 0x8f)
                    {
                        c = 0xff;
                        ct = 8;
                    }
                    else
                    {
                        c = _data[_bp++];
                        ct = 7;
                    }
                }
                else
                {
                    c = _data[_bp++];
                    ct = 8;
                }
            }
            ct--;
            return ((c >> (int)ct) & 0x01) == 0x01;
        }

        //2.5 - opj_mqc_decode_macro
        internal bool Decode()
        {
            bool d;
            a -= curctx.P.qeval;
            if ((c >> 16) < curctx.P.qeval)
            {
                d = LpsExchange();
                Renormd();
            }
            else
            {
                c -= curctx.P.qeval << 16;
                if ((a & 0x8000) == 0)
                {
                    d = MpsExchange();
                    Renormd();
                }
                else
                {
                    d = curctx.P.mps;
                }
            }

            return d;
        }

        //2.5 - opj_mqc_renormd_macro
        void Renormd()
        {
            do
            {
                if (ct == 0) ByteIn();
                a <<= 1;
                c <<= 1;
                ct--;
            } while (a < 0x8000);
        }

        //2.5 - opj_mqc_mpsexchange_macro
        bool MpsExchange()
        {
            bool d;
            if (a < curctx.P.qeval)
            {
                d = !curctx.P.mps;
                curctx.P = curctx.P.nlps;
            }
            else
            {
                d = curctx.P.mps;
                curctx.P = curctx.P.nmps;
            }

            return d;
        }

        //2.5 - opj_mqc_lpsexchange_macro
        bool LpsExchange()
        {
            bool d;
            if (a < curctx.P.qeval)
            {
                a = curctx.P.qeval;
                d = curctx.P.mps;
                curctx.P = curctx.P.nmps;
            }
            else
            {
                a = curctx.P.qeval;
                d = !curctx.P.mps;
                curctx.P = curctx.P.nlps;
            }
            return d;
        }

        //C# - This method dosn't exist in 2.5, but the equivalent code it identical.
        internal void CheckPTerm(CompressionInfo cinfo)
        {
            if (this._bp + 2 < _end)
            {
                cinfo.WarnMT("\"PTERM check failure: {0} remaining bytes in code block ({1} used / {2})",
                    _end - _bp, _bp - _start, _end - _start);
            }
            else if (_end_of_byte_stream_counter > 2)
            {
                cinfo.WarnMT("PTERM check failure: {0} synthetized 0xFF markers read",
                    _end_of_byte_stream_counter);
            }
        }

        //2.5 - opq_mqc_finish_dec
        internal void FinishDec()
        {
            //Restore the bytes overwritten by opj_mqc_init_dec_common()
            Array.Copy(_backup, 0, _data, _end, Constants.COMMON_CBLK_DATA_EXTRA);
        }

        //2.5 - opj_mqc_resetstates
        internal void ResetStates()
        {
            for (int i = 0; i < Constants.MQC_NUMCTXS; i++)
                ctxs[i].P = MQCState.mqc_states[0];
        }

        //2.5 - opj_mqc_setstate
        internal void SetState(T1_CTXNO ctxno, int msb, int prob)
        {
            ctxs[(int)ctxno].P = MQCState.mqc_states[msb + (prob << 1)];
        }

        /// <summary>
        /// Reads in one byte
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_mqc_bytein_macro
        /// </remarks>
        void ByteIn()
        {
            // Given opj_mqc_init_dec() we know that at some point we will
            // have a 0xFF 0xFF artificial marker
            uint l_c = _data[_bp + 1];
            if (_data[_bp] == 0xff)
            {
                if (l_c > 0x8f)
                {
                    c += 0xff00;
                    ct = 8;
                    _end_of_byte_stream_counter++;
                }
                else
                {
                    _bp++;
                    c += l_c << 9;
                    ct = 7;
                }
            }
            else
            {
                _bp++;
                c += l_c << 8;
                ct = 8;
            }        
        }

        /// <remarks>
        /// 2.5 - opj_t1_getctxno_zc
        /// 
        /// I assume the "zc" referes to "zero coding."
        /// </remarks>
        internal byte Getctxno_zc(T1 f)
        {
            return (byte) T1Luts.lut_ctxno_zc[lut_ctxno_zc_orient + (uint)(f & T1.SIGMA_NEIGHBOURS)];
        }
    }

    /// <summary>
    /// Defines the state of a context
    /// </summary>
    internal class MQCState
    {
        /// <summary>
        /// The probability of the Least Probable Symbol 
        /// (0.75->0x8000, 1.5->0xffff)
        /// </summary>
        internal uint qeval;

        /// <summary>
        /// The Most Probable Symbol
        /// </summary>
        internal bool mps;

        /// <summary>
        /// Next state if the next encoded symbol is the MPS
        /// </summary>
        internal MQCState nmps;

        /// <summary>
        /// Next state if the next encoded symbol is the LPS
        /// </summary>
        internal MQCState nlps;

        private MQCState(int q, int m)
        { qeval = (uint)q; mps = m == 1; }

        /// <summary>
        /// This array defines all the possible states for a context.
        /// </summary>
        internal static MQCState[] mqc_states;
        
        /// <summary>
        /// Builds up the mqc_states array
        /// </summary>
        static MQCState()
        {
            mqc_states = new MQCState[47 * 2];

            for (int c = 0; c < mqc_states.Length; c++)
                mqc_states[c] = new MQCState(mcqints[c, 0], mcqints[c, 1]);

            for (int c = 0; c < mqc_states.Length; c++)
            {
                var mcq = mqc_states[c];
                mcq.nmps = mqc_states[mcqints[c, 2]];
                mcq.nlps = mqc_states[mcqints[c, 3]];
            }
            mcqints = null;
        }

        /// <summary>
        /// Information for bulding the MCQState array
        /// </summary>
        static int[,] mcqints =
        {
            {0x5601, 0, 2, 3},
            {0x5601, 1, 3, 2},
            {0x3401, 0, 4, 12},
            {0x3401, 1, 5, 13},
            {0x1801, 0, 6, 18},
            {0x1801, 1, 7, 19},
            {0x0ac1, 0, 8, 24},
            {0x0ac1, 1, 9, 25},
            {0x0521, 0, 10, 58},
            {0x0521, 1, 11, 59},
            {0x0221, 0, 76, 66},
            {0x0221, 1, 77, 67},
            {0x5601, 0, 14, 13},
            {0x5601, 1, 15, 12},
            {0x5401, 0, 16, 28},
            {0x5401, 1, 17, 29},
            {0x4801, 0, 18, 28},
            {0x4801, 1, 19, 29},
            {0x3801, 0, 20, 28},
            {0x3801, 1, 21, 29},
            {0x3001, 0, 22, 34},
            {0x3001, 1, 23, 35},
            {0x2401, 0, 24, 36},
            {0x2401, 1, 25, 37},
            {0x1c01, 0, 26, 40},
            {0x1c01, 1, 27, 41},
            {0x1601, 0, 58, 42},
            {0x1601, 1, 59, 43},
            {0x5601, 0, 30, 29},
            {0x5601, 1, 31, 28},
            {0x5401, 0, 32, 28},
            {0x5401, 1, 33, 29},
            {0x5101, 0, 34, 30},
            {0x5101, 1, 35, 31},
            {0x4801, 0, 36, 32},
            {0x4801, 1, 37, 33},
            {0x3801, 0, 38, 34},
            {0x3801, 1, 39, 35},
            {0x3401, 0, 40, 36},
            {0x3401, 1, 41, 37},
            {0x3001, 0, 42, 38},
            {0x3001, 1, 43, 39},
            {0x2801, 0, 44, 38},
            {0x2801, 1, 45, 39},
            {0x2401, 0, 46, 40},
            {0x2401, 1, 47, 41},
            {0x2201, 0, 48, 42},
            {0x2201, 1, 49, 43},
            {0x1c01, 0, 50, 44},
            {0x1c01, 1, 51, 45},
            {0x1801, 0, 52, 46},
            {0x1801, 1, 53, 47},
            {0x1601, 0, 54, 48},
            {0x1601, 1, 55, 49},
            {0x1401, 0, 56, 50},
            {0x1401, 1, 57, 51},
            {0x1201, 0, 58, 52},
            {0x1201, 1, 59, 53},
            {0x1101, 0, 60, 54},
            {0x1101, 1, 61, 55},
            {0x0ac1, 0, 62, 56},
            {0x0ac1, 1, 63, 57},
            {0x09c1, 0, 64, 58},
            {0x09c1, 1, 65, 59},
            {0x08a1, 0, 66, 60},
            {0x08a1, 1, 67, 61},
            {0x0521, 0, 68, 62},
            {0x0521, 1, 69, 63},
            {0x0441, 0, 70, 64},
            {0x0441, 1, 71, 65},
            {0x02a1, 0, 72, 66},
            {0x02a1, 1, 73, 67},
            {0x0221, 0, 74, 68},
            {0x0221, 1, 75, 69},
            {0x0141, 0, 76, 70},
            {0x0141, 1, 77, 71},
            {0x0111, 0, 78, 72},
            {0x0111, 1, 79, 73},
            {0x0085, 0, 80, 74},
            {0x0085, 1, 81, 75},
            {0x0049, 0, 82, 76},
            {0x0049, 1, 83, 77},
            {0x0025, 0, 84, 78},
            {0x0025, 1, 85, 79},
            {0x0015, 0, 86, 80},
            {0x0015, 1, 87, 81},
            {0x0009, 0, 88, 82},
            {0x0009, 1, 89, 83},
            {0x0005, 0, 90, 84},
            {0x0005, 1, 91, 85},
            {0x0001, 0, 90, 86},
            {0x0001, 1, 91, 87},
            {0x5601, 0, 92, 92},
            {0x5601, 1, 93, 93},
        };
    }
}
