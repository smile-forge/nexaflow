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
using System.Runtime.CompilerServices;

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
    /// <remarks>
    /// What decoding changes is in <see cref="Registers"/>, which a coding pass copies into a local for the pass and
    /// back after it; what stays put across a code block — its bounds and the bytes its end marker overwrote — is here.
    /// </remarks>
    internal sealed class MQCoder
    {
        /// <summary>The decoder's working state between passes.</summary>
        internal MQRegisters Registers;

        /// <summary>
        /// Maximum length to decode
        /// </summary>
        uint _end;

        /// <summary>
        /// Original value of the 2 bytes at end[0] and end[1]
        /// </summary>
        readonly byte[] _backup = new byte[Constants.COMMON_CBLK_DATA_EXTRA];

        /// <summary>
        /// Where the code block's data starts in <see cref="MQRegisters.data"/>
        /// </summary>
        int _start;

        /// <summary>
        /// lut_ctxno_zc shifted by (1 << 9) * bandno
        /// </summary>
        /// <remarks>
        /// This is an offset into the byte array T1Luts.lut_ctxno_zc
        /// </remarks>
        internal int lut_ctxno_zc_orient;

        /// <summary>The context <see cref="Decode"/> decodes in, as its index in <see cref="MQRegisters.ctxs"/></summary>
        int _curctx;

        internal MQCoder()
        {
            Registers.ctxs = new uint[Constants.MQC_NUMCTXS];
        }

        /// <remarks>
        /// 2.5 - opj_mqc_init_dec_common
        /// </remarks>
        void CommonInit(byte[] data, uint offset, uint length, uint extra_writable_bytes)
        {
            Debug.Assert(extra_writable_bytes >= Constants.COMMON_CBLK_DATA_EXTRA);
            Registers.data = data;
            _start = (int)offset;
            _end = offset + length;

            // Insert an artificial 0xFF 0xFF marker at end of the code block
            // data so that the bytein routines stop on it. This saves us comparing
            // the bp and end pointers
            // But before inserting it, backup th bytes we will overwrite
            Array.Copy(data, _end, _backup, 0, Constants.COMMON_CBLK_DATA_EXTRA);
            data[_end + 0] = 0xFF;
            data[_end + 1] = 0xFF;
            Registers.bp = (int)offset;
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
            Registers.c = 0;
            Registers.ct = 0;
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

            Registers.end_of_byte_stream_counter = 0;
            Registers.c = len == 0 ? 0xFFu << 16 : (uint)bp[start_pos] << 16;

            Registers.ByteIn();
            Registers.c <<= 7;
            Registers.ct -= 7;
            Registers.a = 0x8000;
        }

        /// <summary>
        /// Set the current context used for coding/decoding
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_mqc_setcurctx
        /// </remarks>
        internal void Setcurctx(T1_CTXNO i) { _curctx = (int)i; }

        /// <summary>
        /// Set the current context used for coding/decoding
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_mqc_setcurctx
        /// </remarks>
        internal void Setcurctx(int ctxno) { _curctx = ctxno; }

        //2.5 - opj_mqc_raw_decode
        internal bool RawDecode()
        {
            ref var r = ref Registers;
            if (r.ct == 0)
            {
                // Given opj_mqc_raw_init_dec() we know that at some point we will
                // have a 0xFF 0xFF artificial marker
                if (r.c == 0xff)
                {
                    if (r.data[r.bp] > 0x8f)
                    {
                        r.c = 0xff;
                        r.ct = 8;
                    }
                    else
                    {
                        r.c = r.data[r.bp++];
                        r.ct = 7;
                    }
                }
                else
                {
                    r.c = r.data[r.bp++];
                    r.ct = 8;
                }
            }
            r.ct--;
            return ((r.c >> (int)r.ct) & 0x01) == 0x01;
        }

        /// <summary>Decodes one decision in the current context.</summary>
        internal bool Decode() => Registers.Decode(_curctx);

        //C# - This method dosn't exist in 2.5, but the equivalent code it identical.
        internal void CheckPTerm(CompressionInfo cinfo)
        {
            if (Registers.bp + 2 < _end)
            {
                cinfo.WarnMT("\"PTERM check failure: {0} remaining bytes in code block ({1} used / {2})",
                    _end - Registers.bp, Registers.bp - _start, _end - _start);
            }
            else if (Registers.end_of_byte_stream_counter > 2)
            {
                cinfo.WarnMT("PTERM check failure: {0} synthetized 0xFF markers read",
                    Registers.end_of_byte_stream_counter);
            }
        }

        //2.5 - opq_mqc_finish_dec
        internal void FinishDec()
        {
            //Restore the bytes overwritten by opj_mqc_init_dec_common()
            Array.Copy(_backup, 0, Registers.data, _end, Constants.COMMON_CBLK_DATA_EXTRA);
        }

        //2.5 - opj_mqc_resetstates
        internal void ResetStates()
        {
            Array.Fill(Registers.ctxs, MQCState.mqc_states[0]);
        }

        //2.5 - opj_mqc_setstate
        internal void SetState(T1_CTXNO ctxno, int msb, int prob)
        {
            Registers.ctxs[(int)ctxno] = MQCState.mqc_states[msb + (prob << 1)];
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
    /// What the MQ decoder changes as it decodes: the interval, the code register and how many of its bits are left,
    /// the read position, and the context and the state of each.
    /// </summary>
    /// <remarks>
    /// A struct, so a coding pass can copy it into a local that the JIT keeps in registers for the whole pass — with
    /// <see cref="Decode"/> and what it calls inlined into the pass — and copy it back after, as libopenjp2's
    /// DOWNLOAD_MQC_VARIABLES and UPLOAD_MQC_VARIABLES do around its decode macros.
    /// </remarks>
    internal struct MQRegisters
    {
        /// <summary>The interval</summary>
        internal uint a;

        /// <summary>The code register</summary>
        internal uint c;

        /// <summary>Number of bits left in <see cref="c"/> before the next byte is read</summary>
        internal uint ct;

        /// <summary>Read position in <see cref="data"/></summary>
        internal int bp;

        /// <summary>Only used by decoder, to count the number of times a terminating 0xFF >0x8F marker is read</summary>
        internal uint end_of_byte_stream_counter;

        /// <summary>Source data</summary>
        internal byte[] data;

        /// <summary>The state each context is in, as its entry in <see cref="MQCState.mqc_states"/></summary>
        internal uint[] ctxs;

        /// <summary>Decodes one decision in context <paramref name="ctx"/>, moving the context to its next state.</summary>
        /// <remarks>
        /// 2.5 - opj_mqc_decode_macro, with opj_mqc_lpsexchange_macro and opj_mqc_mpsexchange_macro
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool Decode(int ctx)
        {
            ref uint context = ref ctxs[ctx];
            var state = context;
            var qeval = state >> 16;
            var mps = (state & 1) != 0;
            bool d;

            a -= qeval;
            if ((c >> 16) < qeval)
            {
                // The LPS interval: when it is the larger, the MPS was coded in it.
                if (a < qeval)
                {
                    d = mps;
                    context = MQCState.mqc_states[(state >> 1) & 0x7F];
                }
                else
                {
                    d = !mps;
                    context = MQCState.mqc_states[(state >> 8) & 0x7F];
                }
                a = qeval;
                Renormd();
                return d;
            }

            c -= qeval << 16;
            if ((a & 0x8000) != 0)
                return mps;

            // The MPS interval, too small to stay: when it is the smaller, the LPS was coded in it.
            if (a < qeval)
            {
                d = !mps;
                context = MQCState.mqc_states[(state >> 8) & 0x7F];
            }
            else
            {
                d = mps;
                context = MQCState.mqc_states[(state >> 1) & 0x7F];
            }
            Renormd();
            return d;
        }

        //2.5 - opj_mqc_renormd_macro
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

        /// <summary>
        /// Reads in one byte
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_mqc_bytein_macro
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ByteIn()
        {
            // Given opj_mqc_init_dec() we know that at some point we will
            // have a 0xFF 0xFF artificial marker
            uint l_c = data[bp + 1];
            if (data[bp] == 0xff)
            {
                if (l_c > 0x8f)
                {
                    c += 0xff00;
                    ct = 8;
                    end_of_byte_stream_counter++;
                }
                else
                {
                    bp++;
                    c += l_c << 9;
                    ct = 7;
                }
            }
            else
            {
                bp++;
                c += l_c << 8;
                ct = 8;
            }
        }
    }

    /// <summary>
    /// Defines the state of a context
    /// </summary>
    internal static class MQCState
    {
        /// <summary>
        /// Every state a context can be in, packed as <see cref="MQCState()"/> describes; a context holds its state's entry
        /// itself, so a decision that leaves the state as it is reads nothing else.
        /// </summary>
        internal static readonly uint[] mqc_states;
        
        /// <summary>
        /// Packs the states from <see cref="mcqints"/>, each into one entry: its MPS in bit 0, the index of the state after
        /// an MPS in bits 1 to 7 and after an LPS in bits 8 to 14, and the LPS probability in the top 16 bits.
        /// </summary>
        static MQCState()
        {
            mqc_states = new uint[128];

            for (int c = 0; c < mcqints.GetLength(0); c++)
                mqc_states[c] = (uint)mcqints[c, 1] | (uint)mcqints[c, 2] << 1 | (uint)mcqints[c, 3] << 8 | (uint)mcqints[c, 0] << 16;
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
