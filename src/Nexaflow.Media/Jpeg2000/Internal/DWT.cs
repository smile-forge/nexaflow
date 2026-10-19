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
//STANDARD_SLOW_VERSION Uses the OpenJpeg 2.1 codepath for DWT decoding.
//#define STANDARD_SLOW_VERSION
using System;
using System.Diagnostics;
using System.Threading;
using System.Buffers;
using System.Runtime.Intrinsics;
using System.Threading.Tasks;

namespace Nexaflow.Media.Jpeg2000.Internal
{
    /// <summary>
    /// Discrete Wavelet Transform
    /// </summary>
    /// <remarks>
    /// The DWT can be reversible, or irreversible.
    /// 
    /// Reversible uses integer math and a modified YUV colorspace,
    /// irreversible uses floating point math and the YCbCr colorspace
    /// 
    /// Irreversible is only applicable for lossy encoding.
    /// </remarks>
    internal static class DWT
    {

        #region Consts

        const float K = 1.230174105f;
        const float dwt_alpha = -1.586134342f; //  12994
        const float dwt_beta = -0.052980118f; //    434
        const float dwt_gamma = 0.882911075f; //  -7233
        const float dwt_delta = 0.443506852f; //  -3633

        /// <summary>
        /// Number of int32 values in a SSE2 register
        /// </summary>
        /// <remarks>
        /// We don't currently support SSE2, but maybe in the future
        /// </remarks>
        const uint VREG_INT_COUNT = 4;
        const uint PARALLEL_COLS_53 = 2 * VREG_INT_COUNT;
        const int NB_ELTS_V8 = 8;

        #endregion
        /// <summary>
        /// Determine maximum computed resolution level for inverse wavelet transform
        /// </summary>
        /// <param name="r">Resolutions</param>
        /// <param name="i">Number of resolutions that will be decoded</param>
        /// <remarks>2.5 - opj_dwt_max_resolution</remarks>
        static uint MaxResolution(TcdResolution[] rs, int numres)
        {
            uint mr = 0;
            uint w;
            for (int c = 1; c < numres; c++)
            {
                var r = rs[c];
                if (mr < (w = (uint)(r.x1 - r.x0)))
                    mr = w;
                if (mr < (w = (uint)(r.y1 - r.y0)))
                    mr = w;
            }
            return mr;
        }

        //2.5 - opj_dwt_decode_real
        internal static bool DecodeReal(TileCoder tcd, TcdTilecomp tilec, uint numres)
        {
            if (tcd.WholeTileDecoding)
                return decode_tile_97(tilec, numres, tcd.DisableMultiThreading);
            else
                return decode_partial_97(tilec, numres);
        }

        //2.5 - opj_dwt_decode_partial_97
        static bool decode_partial_97(TcdTilecomp tilec, uint numres)
        {
            SparseArrayInt32 sa;
            v4dwt_local h = new v4dwt_local();
            v4dwt_local v = new v4dwt_local();
            // This value matches the maximum left/right extension given in tables
            // F.2 and F.3 of the standard. Note: in opj_tcd_is_subband_area_of_interest()
            // we currently use 3.
            const uint filter_width = 4U;

            TcdResolution[] tr_ar = tilec.resolutions;
            int tr_pos = 0;
            TcdResolution tr = tr_ar[tr_pos], tr_max = tr_ar[numres - 1];

            //Width of the resolution level computed
            uint rw = (uint)(tr.x1 - tr.x0);

            //Height of the resolution level computed
            uint rh = (uint)(tr.y1 - tr.y0);

            ulong data_size;

            // Compute the intersection of the area of interest, expressed in tile coordinates
            // with the tile coordinates
            uint win_tcx0 = tilec.win_x0;
            uint win_tcy0 = tilec.win_y0;
            uint win_tcx1 = tilec.win_x1;
            uint win_tcy1 = tilec.win_y1;

            if (tr_max.x0 == tr_max.x1 || tr_max.y0 == tr_max.y1)
            {
                return true;
            }

            sa = SparseArrayInt32.Init(tilec, numres);
            if (sa == null)
                return false;

            if (numres == 1U)
            {
                bool ret = sa.read(tr_max.win_x0 - (uint)tr_max.x0,
                                   tr_max.win_y0 - (uint)tr_max.y0,
                                   tr_max.win_x1 - (uint)tr_max.x0,
                                   tr_max.win_y1 - (uint)tr_max.y0,
                                   tilec.data_win, 0,
                                   1, tr_max.win_x1 - tr_max.win_x0,
                                   true);
                Debug.Assert(ret);
                return true;
            }
            data_size = MaxResolution(tr_ar, (int)numres);
            // overflow check
            // C# 
            if (data_size > (Constants.SIZE_MAX / NB_ELTS_V8 * (sizeof(float))))
            {
                return false;
            }
            h.wavelet = new float[data_size * NB_ELTS_V8];
            v.wavelet = h.wavelet;

            for (uint resno = 1; resno < numres; resno++)
            {
                uint j;
                /* Window of interest subband-based coordinates */
                uint win_ll_x0, win_ll_y0, win_ll_x1, win_ll_y1;
                uint win_hl_x0, win_hl_x1;
                uint win_lh_y0, win_lh_y1;
                /* Window of interest tile-resolution-based coordinates */
                uint win_tr_x0, win_tr_x1, win_tr_y0, win_tr_y1;
                /* Tile-resolution subband-based coordinates */
                uint tr_ll_x0, tr_ll_y0, tr_hl_x0, tr_lh_y0;

                tr = tr_ar[++tr_pos];

                h.sn = (int)rw;
                v.sn = (int)rh;

                rw = (uint)(tr.x1 - tr.x0);
                rh = (uint)(tr.y1 - tr.y0);

                h.dn = (int)(rw - (uint)h.sn);
                h.cas = tr.x0 % 2;

                v.dn = (int)(rh - (uint)v.sn);
                v.cas = tr.y0 % 2;

                // Get the subband coordinates for the window of interest
                // LL band
                GetBandCoordinates(tilec, resno, 0,
                                   win_tcx0, win_tcy0, win_tcx1, win_tcy1,
                                   out win_ll_x0, out win_ll_y0,
                                   out win_ll_x1, out win_ll_y1);

                // HL band
                GetBandCoordinates(tilec, resno, 1,
                                   win_tcx0, win_tcy0, win_tcx1, win_tcy1,
                                   out win_hl_x0, out _, out win_hl_x1, out _);

                /* LH band */
                GetBandCoordinates(tilec, resno, 2,
                                   win_tcx0, win_tcy0, win_tcx1, win_tcy1,
                                   out _, out win_lh_y0, out _, out win_lh_y1);

                /* Beware: band index for non-LL0 resolution are 0=HL, 1=LH and 2=HH */
                tr_ll_x0 = (uint)tr.bands[1].x0;
                tr_ll_y0 = (uint)tr.bands[0].y0;
                tr_hl_x0 = (uint)tr.bands[0].x0;
                tr_lh_y0 = (uint)tr.bands[1].y0;

                /* Subtract the origin of the bands for this tile, to the subwindow */
                /* of interest band coordinates, so as to get them relative to the */
                /* tile */
                win_ll_x0 = MyMath.uint_subs(win_ll_x0, tr_ll_x0);
                win_ll_y0 = MyMath.uint_subs(win_ll_y0, tr_ll_y0);
                win_ll_x1 = MyMath.uint_subs(win_ll_x1, tr_ll_x0);
                win_ll_y1 = MyMath.uint_subs(win_ll_y1, tr_ll_y0);
                win_hl_x0 = MyMath.uint_subs(win_hl_x0, tr_hl_x0);
                win_hl_x1 = MyMath.uint_subs(win_hl_x1, tr_hl_x0);
                win_lh_y0 = MyMath.uint_subs(win_lh_y0, tr_lh_y0);
                win_lh_y1 = MyMath.uint_subs(win_lh_y1, tr_lh_y0);

                SegmentGrow(filter_width, (uint)h.sn, ref win_ll_x0, ref win_ll_x1);
                SegmentGrow(filter_width, (uint)h.dn, ref win_hl_x0, ref win_hl_x1);

                SegmentGrow(filter_width, (uint)v.sn, ref win_ll_y0, ref win_ll_y1);
                SegmentGrow(filter_width, (uint)v.dn, ref win_lh_y0, ref win_lh_y1);

                /* Compute the tile-resolution-based coordinates for the window of interest */
                if (h.cas == 0)
                {
                    win_tr_x0 = Math.Min(2 * win_ll_x0, 2 * win_hl_x0 + 1);
                    win_tr_x1 = Math.Min(Math.Max(2 * win_ll_x1, 2 * win_hl_x1 + 1), rw);
                }
                else
                {
                    win_tr_x0 = Math.Min(2 * win_hl_x0, 2 * win_ll_x0 + 1);
                    win_tr_x1 = Math.Min(Math.Max(2 * win_hl_x1, 2 * win_ll_x1 + 1), rw);
                }

                if (v.cas == 0)
                {
                    win_tr_y0 = Math.Min(2 * win_ll_y0, 2 * win_lh_y0 + 1);
                    win_tr_y1 = Math.Min(Math.Max(2 * win_ll_y1, 2 * win_lh_y1 + 1), rh);
                }
                else
                {
                    win_tr_y0 = Math.Min(2 * win_lh_y0, 2 * win_ll_y0 + 1);
                    win_tr_y1 = Math.Min(Math.Max(2 * win_lh_y1, 2 * win_ll_y1 + 1), rh);
                }

                h.win_l_x0 = win_ll_x0;
                h.win_l_x1 = win_ll_x1;
                h.win_h_x0 = win_hl_x0;
                h.win_h_x1 = win_hl_x1;
                for (j = 0; j + (NB_ELTS_V8 - 1) < rh; j += NB_ELTS_V8)
                {
                    if ((j + (NB_ELTS_V8 - 1) >= win_ll_y0 && j < win_ll_y1) ||
                            (j + (NB_ELTS_V8 - 1) >= win_lh_y0 + (uint)v.sn &&
                            j < win_lh_y1 + (uint)v.sn))
                    {
                        v8dwt_interleave_partial_h(h, sa, j, Math.Min(NB_ELTS_V8, rh - j));
                        v8dwt_decode(h);
                        if (!sa.write(win_tr_x0, j,
                                      win_tr_x1, j + NB_ELTS_V8,
                                      h.wavelet, (int)win_tr_x0 * NB_ELTS_V8, //C# Wavlet indexing, therefore mul with NB_ELTS_V8
                                      NB_ELTS_V8, 1, true))
                        {
                            return false;
                        }
                    }
                }

                if (j < rh &&
                    ((j + (NB_ELTS_V8 - 1) >= win_ll_y0 && j < win_ll_y1) ||
                     (j + (NB_ELTS_V8 - 1) >= win_lh_y0 + (uint)v.sn &&
                      j < win_lh_y1 + (uint)v.sn)))
                {
                    v8dwt_interleave_partial_h(h, sa, j, rh - j);
                    v8dwt_decode(h);
                    if (!sa.write(win_tr_x0, j,
                                  win_tr_x1, rh,
                                  h.wavelet, (int)win_tr_x0 * NB_ELTS_V8,
                                  NB_ELTS_V8, 1, true))
                    {
                        return false;
                    }
                }

                v.win_l_x0 = win_ll_y0;
                v.win_l_x1 = win_ll_y1;
                v.win_h_x0 = win_lh_y0;
                v.win_h_x1 = win_lh_y1;
                for (j = win_tr_x0; j < win_tr_x1; j += NB_ELTS_V8)
                {
                    uint nb_elts = Math.Min(NB_ELTS_V8, win_tr_x1 - j);

                    v8dwt_interleave_partial_v(v, sa, j, nb_elts);
                    v8dwt_decode(v);
                    if (!sa.write(j, win_tr_y0,
                                  j + nb_elts, win_tr_y1,
                                  v.wavelet, (int)win_tr_y0 * NB_ELTS_V8,
                                  1, NB_ELTS_V8, true))
                    {
                        return false;
                    }
                }
            }

            {
                bool ret = sa.read(
                               tr_max.win_x0 - (uint)tr_max.x0,
                               tr_max.win_y0 - (uint)tr_max.y0,
                               tr_max.win_x1 - (uint)tr_max.x0,
                               tr_max.win_y1 - (uint)tr_max.y0,
                               tilec.data_win, 0,
                               1, tr_max.win_x1 - tr_max.win_x0,
                               true);
                Debug.Assert(ret);
            }

            return true;
        }

        /// <summary>
        /// Inverse 9-7 wavelet transform in 2-D, over the whole tile.
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_dwt_decode_tile_97
        ///
        /// Each resolution is lifted along its rows eight at a time, then down its columns eight at a time, as libopenjp2
        /// does. A resolution big enough to be worth it shares its rows, then its columns, out over the processors, each
        /// share lifted through a buffer of its own from the shared pool.
        /// </remarks>
        internal static bool decode_tile_97(TcdTilecomp tilec, uint numres, bool disable_multi_threading)
        {
            TcdResolution[] res_ar = tilec.resolutions;
            int res_ar_pos = 0;
            TcdResolution res = res_ar[res_ar_pos];

            //Width of the resolution level computed
            uint rw = (uint)(res.x1 - res.x0);

            //Height of the resolution level computed
            uint rh = (uint)(res.y1 - res.y0);

            int w = tilec.resolutions[tilec.minimum_num_resolutions - 1].x1
                  - tilec.resolutions[tilec.minimum_num_resolutions - 1].x0;

            // Not entirely sure for the return code of w == 0 which is triggered per
            // https://github.com/uclouvain/openjpeg/issues/1505
            if (numres == 1U || w == 0)
            {
                return true;
            }
            var num_threads = disable_multi_threading ? 1 : Environment.ProcessorCount;

            ulong data_size = MaxResolution(res_ar, (int)numres);
            /* overflow check */
            if (data_size > (Constants.SIZE_MAX / NB_ELTS_V8 * sizeof(float)))
            {
                return false;
            }
            var length = checked((int)data_size * NB_ELTS_V8);

            var h = new v4dwt_local { wavelet = RentWavelet(length) };
            var v = new v4dwt_local { wavelet = h.wavelet };
            int[] aj_ar = tilec.data;

            try
            {
                while (--numres != 0)
                {
                    h.sn = (int)rw;
                    v.sn = (int)rh;

                    res = res_ar[++res_ar_pos];

                    rw = (uint)(res.x1 - res.x0);
                    rh = (uint)(res.y1 - res.y0);

                    h.dn = (int)(rw - (uint)h.sn);
                    h.cas = res.x0 % 2;

                    h.win_l_x0 = 0;
                    h.win_l_x1 = (uint)h.sn;
                    h.win_h_x0 = 0;
                    h.win_h_x1 = (uint)h.dn;

                    // The rows, each lifted where it lies.
                    if (num_threads <= 1 || rh < 2 * NB_ELTS_V8 || rw * (ulong)rh < MinimumSharedSamples)
                    {
                        decode_rows_97(h, aj_ar, w, 0, (int)rh);
                    }
                    else
                    {
                        var shape = h;
                        InShares(num_threads, (int)rh, 1, (first, end) => decode_rows_97(shape, aj_ar, w, first, end));
                    }

                    v.dn = (int)(rh - (uint)v.sn);
                    v.cas = res.y0 % 2;
                    v.win_l_x0 = 0;
                    v.win_l_x1 = (uint)v.sn;
                    v.win_h_x0 = 0;
                    v.win_h_x1 = (uint)v.dn;

                    // The columns eight at a time, then the few left over.
                    var columns = (int)(rw & ~(uint)(NB_ELTS_V8 - 1));
                    if (num_threads <= 1 || rw < 2 * NB_ELTS_V8 || rw * (ulong)rh < MinimumSharedSamples)
                    {
                        decode_columns_97(v, aj_ar, w, (int)rh, 0, columns);
                    }
                    else
                    {
                        /* "bench_dwt -I" shows that scaling is poor, likely due to RAM
                            transfer being the limiting factor. So limit the number of
                            threads. */
                        var shape = v;
                        var height = (int)rh;
                        InShares(Math.Max(num_threads / 2, 2), columns, NB_ELTS_V8, (first, end) =>
                        {
                            var local = shape.Clone();
                            local.wavelet = RentWavelet(length);
                            try { decode_columns_97(local, aj_ar, w, height, first, end); }
                            finally { ArrayPool<float>.Shared.Return(local.wavelet); }
                        });
                    }

                    if (columns < rw)
                    {
                        var left = (int)rw - columns;
                        v8dwt_interleave_v(v, aj_ar, columns, w, left);
                        v8dwt_decode(v);
                        store_columns_97(v.wavelet, aj_ar, columns, w, (int)rh, left);
                    }
                }
            }
            finally
            {
                ArrayPool<float>.Shared.Return(h.wavelet);
            }

            return true;
        }

        /// <summary>
        /// A wavelet buffer of at least <paramref name="length"/> floats from the shared pool, cleared as far as it is used:
        /// a strip of fewer than eight rows or columns lifts the lanes it does not fill too, and they should hold numbers.
        /// </summary>
        private static float[] RentWavelet(int length)
        {
            var wavelet = ArrayPool<float>.Shared.Rent(length);
            Array.Clear(wavelet, 0, length);
            return wavelet;
        }

        /// <summary>
        /// Rows <paramref name="first"/> to <paramref name="end"/> of a resolution, each lifted horizontally where it lies,
        /// through a row buffer of the shared pool's.
        /// </summary>
        private static void decode_rows_97(v4dwt_local h, int[] a, int w, int first, int end)
        {
            var interleaved = ArrayPool<int>.Shared.Rent(h.sn + h.dn);
            try
            {
                for (var row = first; row < end; row++)
                    decode_row_97(a.AsSpan(row * w, h.sn + h.dn), h.sn, h.dn, h.cas, interleaved);
            }
            finally
            {
                ArrayPool<int>.Shared.Return(interleaved);
            }
        }

        /// <summary>
        /// Inverse 9-7 wavelet transform in 1-D of one row, in place: the row holds its low band, then its high band, the
        /// bits of floats; the bands are lifted where they lie and then interleaved.
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_v8dwt_decode, for one row. libopenjp2 lifts eight rows at once, one to a lane, after interleaving
        /// them; lifting one row along its own length does each sample's products and sums exactly as a lane does them —
        /// in the same order, at single precision, with no fused multiply-add — so the row comes out bit for bit the same,
        /// without the transposes into the lanes and back. In the interleaved order libopenjp2 lifts, the low band is on
        /// the even samples when <paramref name="cas"/> is 0 and on the odd when 1, and a sample's neighbours in the other
        /// band are those either side of it: for the band on the even samples, the other band's previous and own; for the
        /// band on the odd, its own and next.
        /// </remarks>
        private static void decode_row_97(Span<int> row, int sn, int dn, int cas, int[] interleaved)
        {
            /* BUG_WEIRD_TWO_INVK (look for this identifier in tcd.c) */
            /* Historic value for 2 / opj_invK */
            /* Normally, we should use invK, but if we do so, we have failures in the */
            /* conformance test, due to MSE and peak errors significantly higher than */
            /* accepted value */
            /* Due to using two_invK instead of invK, we have to compensate in tcd.c */
            /* the computation of the stepsize for the non LL subbands */
            const float two_invK = 1.625732422f;

            var low = row[..sn];
            var high = row.Slice(sn, dn);
            if (cas == 0 ? dn > 0 || sn > 1 : sn > 0 || dn > 1)
            {
                scale_band(low, K);
                scale_band(high, two_invK);
                if (cas == 0)
                {
                    lift_from_previous(low, high, Math.Min(sn, dn), -dwt_delta);
                    lift_from_next(high, low, Math.Min(dn, sn - 1), -dwt_gamma);
                    lift_from_previous(low, high, Math.Min(sn, dn), -dwt_beta);
                    lift_from_next(high, low, Math.Min(dn, sn - 1), -dwt_alpha);
                }
                else
                {
                    lift_from_next(low, high, Math.Min(sn, dn - 1), -dwt_delta);
                    lift_from_previous(high, low, Math.Min(dn, sn), -dwt_gamma);
                    lift_from_next(low, high, Math.Min(sn, dn - 1), -dwt_beta);
                    lift_from_previous(high, low, Math.Min(dn, sn), -dwt_alpha);
                }
            }

            if (cas == 0)
                interleave_bands(low, high, interleaved);
            else
                interleave_bands(high, low, interleaved);
            interleaved.AsSpan(0, row.Length).CopyTo(row);
        }

        /// <summary>A band, the bits of floats, scaled by <paramref name="c"/>.</summary>
        /// <remarks>2.5 - opj_v8dwt_decode_step1, along a band</remarks>
        private static void scale_band(Span<int> band, float c)
        {
            var i = 0;
            for (; i + NB_ELTS_V8 <= band.Length; i += NB_ELTS_V8)
            {
                var lanes = band.Slice(i, NB_ELTS_V8);
                (Vector256.Create(lanes).AsSingle() * c).AsInt32().CopyTo(lanes);
            }

            for (; i < band.Length; i++)
                band[i] = BitConverter.SingleToInt32Bits(BitConverter.Int32BitsToSingle(band[i]) * c);
        }

        /// <summary>
        /// The lifting step for the band on the even samples: each of its first <paramref name="m"/> samples gains the
        /// sum of the other band's previous and own times <paramref name="c"/> — its first counts the other band's first
        /// twice — and when the band is the longer, its last gains the other band's last times twice <paramref name="c"/>.
        /// </summary>
        /// <remarks>2.5 - opj_v8dwt_decode_step2, along a band</remarks>
        private static void lift_from_previous(Span<int> target, ReadOnlySpan<int> from, int m, float c)
        {
            var end = target.Length;
            var imax = Math.Min(end, m);
            var i = 0;
            if (imax > 0)
            {
                var first = BitConverter.Int32BitsToSingle(from[0]);
                target[0] = BitConverter.SingleToInt32Bits(BitConverter.Int32BitsToSingle(target[0]) + (first + first) * c);
                i = 1;
            }

            for (; i + NB_ELTS_V8 <= imax; i += NB_ELTS_V8)
            {
                var lanes = target.Slice(i, NB_ELTS_V8);
                var neighbours = Vector256.Create(from.Slice(i - 1, NB_ELTS_V8)).AsSingle() + Vector256.Create(from.Slice(i, NB_ELTS_V8)).AsSingle();
                (Vector256.Create(lanes).AsSingle() + neighbours * c).AsInt32().CopyTo(lanes);
            }

            for (; i < imax; i++)
            {
                var neighbours = BitConverter.Int32BitsToSingle(from[i - 1]) + BitConverter.Int32BitsToSingle(from[i]);
                target[i] = BitConverter.SingleToInt32Bits(BitConverter.Int32BitsToSingle(target[i]) + neighbours * c);
            }

            if (m < end)
            {
                Debug.Assert(m + 1 == end && m >= 0);
                c += c;
                var only = BitConverter.Int32BitsToSingle(from[Math.Max(m - 1, 0)]);
                target[m] = BitConverter.SingleToInt32Bits(BitConverter.Int32BitsToSingle(target[m]) + only * c);
            }
        }

        /// <summary>
        /// The lifting step for the band on the odd samples: each of its first <paramref name="m"/> samples gains the sum
        /// of the other band's own and next times <paramref name="c"/>, and when the band is the longer, its last gains the
        /// other band's own times twice <paramref name="c"/>.
        /// </summary>
        /// <remarks>2.5 - opj_v8dwt_decode_step2, along a band</remarks>
        private static void lift_from_next(Span<int> target, ReadOnlySpan<int> from, int m, float c)
        {
            var end = target.Length;
            var imax = Math.Min(end, m);
            var i = 0;
            for (; i + NB_ELTS_V8 <= imax; i += NB_ELTS_V8)
            {
                var lanes = target.Slice(i, NB_ELTS_V8);
                var neighbours = Vector256.Create(from.Slice(i, NB_ELTS_V8)).AsSingle() + Vector256.Create(from.Slice(i + 1, NB_ELTS_V8)).AsSingle();
                (Vector256.Create(lanes).AsSingle() + neighbours * c).AsInt32().CopyTo(lanes);
            }

            for (; i < imax; i++)
            {
                var neighbours = BitConverter.Int32BitsToSingle(from[i]) + BitConverter.Int32BitsToSingle(from[i + 1]);
                target[i] = BitConverter.SingleToInt32Bits(BitConverter.Int32BitsToSingle(target[i]) + neighbours * c);
            }

            if (m < end)
            {
                Debug.Assert(m + 1 == end && m >= 0);
                c += c;
                var only = BitConverter.Int32BitsToSingle(from[m]);
                target[m] = BitConverter.SingleToInt32Bits(BitConverter.Int32BitsToSingle(target[m]) + only * c);
            }
        }

        /// <summary>
        /// Interleaves two bands into <paramref name="into"/>: <paramref name="even"/> on the even samples and
        /// <paramref name="odd"/> on the odd, eight samples of each at a time.
        /// </summary>
        private static void interleave_bands(ReadOnlySpan<int> even, ReadOnlySpan<int> odd, Span<int> into)
        {
            var firstHalf = Vector256.Create(0, 0, 1, 1, 2, 2, 3, 3);
            var secondHalf = Vector256.Create(4, 4, 5, 5, 6, 6, 7, 7);
            var evenLanes = Vector256.Create(-1, 0, -1, 0, -1, 0, -1, 0);

            var pairs = Math.Min(even.Length, odd.Length);
            var i = 0;
            for (; i + NB_ELTS_V8 <= pairs; i += NB_ELTS_V8)
            {
                var e = Vector256.Create(even.Slice(i, NB_ELTS_V8));
                var o = Vector256.Create(odd.Slice(i, NB_ELTS_V8));
                Vector256.ConditionalSelect(evenLanes, Vector256.Shuffle(e, firstHalf), Vector256.Shuffle(o, firstHalf))
                    .CopyTo(into.Slice(2 * i, NB_ELTS_V8));
                Vector256.ConditionalSelect(evenLanes, Vector256.Shuffle(e, secondHalf), Vector256.Shuffle(o, secondHalf))
                    .CopyTo(into.Slice(2 * i + NB_ELTS_V8, NB_ELTS_V8));
            }

            for (; i < pairs; i++)
            {
                into[2 * i] = even[i];
                into[2 * i + 1] = odd[i];
            }

            // The band on the even samples is longer by one when the row's length is odd.
            if (even.Length > pairs)
                into[2 * pairs] = even[pairs];
        }

        /// <summary>
        /// Columns <paramref name="first"/> to <paramref name="end"/> of a resolution, a multiple of eight, lifted
        /// vertically eight at a time.
        /// </summary>
        private static void decode_columns_97(v4dwt_local v, int[] a, int w, int rh, int first, int end)
        {
            for (var column = first; column < end; column += NB_ELTS_V8)
            {
                v8dwt_interleave_v(v, a, column, w, NB_ELTS_V8);
                v8dwt_decode(v);
                store_columns_97(v.wavelet, a, column, w, rh, NB_ELTS_V8);
            }
        }

        /// <summary>Writes a vertical lift back: the <paramref name="columns"/> lanes of each of the <paramref name="rh"/> samples to its row.</summary>
        private static void store_columns_97(float[] wavelet, int[] a, int at, int w, int rh, int columns)
        {
            for (int k = 0, kw = 0; k < rh; k++, kw += NB_ELTS_V8, at += w)
            {
                if (columns == NB_ELTS_V8)
                {
                    Vector256.Create(wavelet.AsSpan(kw, NB_ELTS_V8)).AsInt32().CopyTo(a.AsSpan(at, NB_ELTS_V8));
                    continue;
                }

                for (var l = 0; l < columns; l++)
                    a[at + l] = BitConverter.SingleToInt32Bits(wavelet[kw + l]);
            }
        }

        /// <summary>
        /// The fewest samples a resolution must have for its lifting to be shared out: below it, handing the strips to other
        /// threads costs more than lifting them.
        /// </summary>
        private const ulong MinimumSharedSamples = 64 * 1024;

        /// <summary>
        /// Shares <paramref name="end"/> rows or columns out over up to <paramref name="threads"/> jobs, each a run of whole
        /// multiples of <paramref name="granule"/> but the last, which takes what remains, and waits for them all.
        /// </summary>
        private static void InShares(int threads, int end, int granule, Action<int, int> run)
        {
            var jobs = Math.Max(1, Math.Min(threads, end / granule));
            var step = end / granule / jobs * granule;
            Parallel.For(0, jobs, new ParallelOptions { MaxDegreeOfParallelism = threads },
                job => run(job * step, job + 1 == jobs ? end : (job + 1) * step));
        }

        /// <summary>
        /// Inverse 9-7 wavelet transform in 1-D.
        /// </summary>
        /// <remarks>
        /// 2.5
        /// </remarks>
        private static void v8dwt_decode(v4dwt_local dwt)
        {
            /* BUG_WEIRD_TWO_INVK (look for this identifier in tcd.c) */
            /* Historic value for 2 / opj_invK */
            /* Normally, we should use invK, but if we do so, we have failures in the */
            /* conformance test, due to MSE and peak errors significantly higher than */
            /* accepted value */
            /* Due to using two_invK instead of invK, we have to compensate in tcd.c */
            /* the computation of the stepsize for the non LL subbands */
            const float two_invK = 1.625732422f;

            int a, b;
            if (dwt.cas == 0)
            {
                if (!((dwt.dn > 0) || (dwt.sn > 1)))
                    return;
                a = 0;
                b = 1;
            }
            else
            {
                if (!((dwt.sn > 0) || (dwt.dn > 1)))
                    return;
                a = 1;
                b = 0;
            }

            //C# Snip SSE code

            //C# Both a and b index dwt.wavelet, hench why we have to mul with NB_ELTS_V8
            v8dwt_decode_step1(dwt.wavelet, a * NB_ELTS_V8, (int)dwt.win_l_x0, (int)dwt.win_l_x1, K);
            v8dwt_decode_step1(dwt.wavelet, b * NB_ELTS_V8, (int)dwt.win_h_x0, (int)dwt.win_h_x1, two_invK);
            v8dwt_decode_step2(dwt.wavelet, b * NB_ELTS_V8, (a + 1) * NB_ELTS_V8, (int)dwt.win_l_x0, (int)dwt.win_l_x1,
                Math.Min(dwt.sn, dwt.dn - a), -dwt_delta);
            v8dwt_decode_step2(dwt.wavelet, a * NB_ELTS_V8, (b + 1) * NB_ELTS_V8, (int)dwt.win_h_x0, (int)dwt.win_h_x1,
                Math.Min(dwt.dn, dwt.sn - b), -dwt_gamma);
            v8dwt_decode_step2(dwt.wavelet, b * NB_ELTS_V8, (a + 1) * NB_ELTS_V8, (int)dwt.win_l_x0, (int)dwt.win_l_x1,
                Math.Min(dwt.sn, dwt.dn - a), -dwt_beta);
            v8dwt_decode_step2(dwt.wavelet, a * NB_ELTS_V8, (b + 1) * NB_ELTS_V8, (int)dwt.win_h_x0, (int)dwt.win_h_x1,
                Math.Min(dwt.dn, dwt.sn - b), -dwt_alpha);
        }

        /// <summary>
        /// Wavelet decode step 1: scales each of the samples from <paramref name="start"/> to <paramref name="end"/>, eight
        /// lanes at a time.
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_v8dwt_decode_step1
        /// </remarks>
        private static void v8dwt_decode_step1(float[] f, int fw, int start, int end, float c)
        {
            var k = Vector256.Create(c);
            for (int i = start, at = fw + start * 2 * NB_ELTS_V8; i < end; ++i, at += 2 * NB_ELTS_V8)
            {
                var lanes = f.AsSpan(at, NB_ELTS_V8);
                (Vector256.Create(lanes) * k).CopyTo(lanes);
            }
        }

        /// <summary>
        /// Wavelet decode step 2: adds to each sample the sum of its two neighbours times <paramref name="c"/>, eight lanes
        /// at a time.
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_v8dwt_decode_step2
        ///
        /// Each lane is the scalar sum and product libopenjp2 computes, in its order and at single precision, with no fused
        /// multiply-add: the vector only does eight of them at once, so the result is bit for bit the scalar one.
        /// </remarks>
        private static void v8dwt_decode_step2(float[] f, int fl, int fw, int start, int end, int m, float c)
        {
            int imax = Math.Min(end, m);
            if (start > 0)
            {
                fw += 2 * NB_ELTS_V8 * start;
                fl = fw - 2 * NB_ELTS_V8;
            }

            var k = Vector256.Create(c);
            for (int i = start; i < imax; ++i)
            {
                var target = f.AsSpan(fw - NB_ELTS_V8, NB_ELTS_V8);
                var neighbours = Vector256.Create(f.AsSpan(fl, NB_ELTS_V8)) + Vector256.Create(f.AsSpan(fw, NB_ELTS_V8));
                (Vector256.Create(target) + neighbours * k).CopyTo(target);
                fl = fw;
                fw += 2 * NB_ELTS_V8;
            }
            if (m < end)
            {
                Debug.Assert(m + 1 == end);
                k += k;
                var target = f.AsSpan(fw - NB_ELTS_V8, NB_ELTS_V8);
                (Vector256.Create(target) + Vector256.Create(f.AsSpan(fl, NB_ELTS_V8)) * k).CopyTo(target);
            }
        }

        /// <summary>
        /// Inverse 5-3 wavelet transform in 2-D, over the whole tile.
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_dwt_decode_tile
        ///
        /// Each resolution is lifted along its rows one at a time, then down its columns eight at a time. A resolution big
        /// enough to be worth it shares its rows, then its columns, out over the processors, each share lifted through a
        /// buffer of its own from the shared pool.
        /// </remarks>
        private static bool decode_tile(TcdTilecomp tilec, uint numres, bool disable_multi_threading)
        {
            TcdResolution[] tr_ar = tilec.resolutions;
            int tr_pos = 0;
            TcdResolution tr = tr_ar[tr_pos];

            //Width of the resolution level computed
            uint rw = (uint)(tr.x1 - tr.x0);

            //Height of the resolution level computed
            uint rh = (uint)(tr.y1 - tr.y0);

            int w = tilec.resolutions[tilec.minimum_num_resolutions - 1].x1
                  - tilec.resolutions[tilec.minimum_num_resolutions - 1].x0;

            if (numres == 1U)
            {
                return true;
            }
            var num_threads = disable_multi_threading ? 1 : Environment.ProcessorCount;

            ulong h_mem_size = MaxResolution(tr_ar, (int)numres);
            /* overflow check */
            if (h_mem_size > (Constants.SIZE_MAX / PARALLEL_COLS_53 / sizeof(int)))
            {
                return false;
            }
            // We need PARALLEL_COLS_53 times the height of the array,
            // since for the vertical pass
            // we process PARALLEL_COLS_53 columns at a time
            var length = checked((int)(h_mem_size * PARALLEL_COLS_53));

            var h = new dwt_local { mem = ArrayPool<int>.Shared.Rent(length) };
            var v = new dwt_local { mem = h.mem };
            int[] tiled = tilec.data;

            try
            {
                while (--numres != 0)
                {
                    tr = tr_ar[++tr_pos];
                    h.sn = (int)rw;
                    v.sn = (int)rh;

                    rw = (uint)(tr.x1 - tr.x0);
                    rh = (uint)(tr.y1 - tr.y0);

                    h.dn = (int)(rw - (uint)h.sn);
                    h.cas = tr.x0 % 2;

                    if (num_threads <= 1 || rh <= 1 || rw * (ulong)rh < MinimumSharedSamples)
                    {
                        decode_rows_53(h, tiled, w, 0, (int)rh);
                    }
                    else
                    {
                        var shape = h;
                        InShares(num_threads, (int)rh, 1, (first, end) =>
                        {
                            var local = shape.Clone();
                            local.mem = ArrayPool<int>.Shared.Rent(length);
                            try { decode_rows_53(local, tiled, w, first, end); }
                            finally { ArrayPool<int>.Shared.Return(local.mem); }
                        });
                    }

                    v.dn = (int)(rh - (uint)v.sn);
                    v.cas = tr.y0 % 2;

                    if (num_threads <= 1 || rw < 2 * PARALLEL_COLS_53 || rw * (ulong)rh < MinimumSharedSamples)
                    {
                        decode_columns_53(v, tiled, w, 0, (int)rw);
                    }
                    else
                    {
                        var shape = v;
                        InShares(num_threads, (int)rw, (int)PARALLEL_COLS_53, (first, end) =>
                        {
                            var local = shape.Clone();
                            local.mem = ArrayPool<int>.Shared.Rent(length);
                            try { decode_columns_53(local, tiled, w, first, end); }
                            finally { ArrayPool<int>.Shared.Return(local.mem); }
                        });
                    }
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(h.mem);
            }

            return true;
        }

        /// <summary>Rows <paramref name="first"/> to <paramref name="end"/> of a resolution, lifted horizontally one at a time.</summary>
        private static void decode_rows_53(dwt_local h, int[] tiled, int w, int first, int end)
        {
            for (var row = first; row < end; row++)
                idwt53_h(h, tiled, row * w);
        }

        /// <summary>
        /// Columns <paramref name="first"/> to <paramref name="end"/> of a resolution, lifted vertically
        /// <see cref="PARALLEL_COLS_53"/> at a time, then the few left over.
        /// </summary>
        private static void decode_columns_53(dwt_local v, int[] tiled, int w, int first, int end)
        {
            var column = first;
            for (; column + PARALLEL_COLS_53 <= end; column += (int)PARALLEL_COLS_53)
                idwt53_v(v, tiled, column, w, (int)PARALLEL_COLS_53);
            if (column < end)
                idwt53_v(v, tiled, column, w, end - column);
        }

        //2.5
        static bool DecodePartialTile(TcdTilecomp tilec, uint numres)
        {
            SparseArrayInt32 sa;
            dwt_local h = new dwt_local();
            dwt_local v = new dwt_local();
            // This value matches the maximum left/right extension given in tables
            // F.2 and F.3 of the standard.
            const uint filter_width = 2U;

            TcdResolution[] tr_ar = tilec.resolutions;
            int tr_pos = 0;
            TcdResolution tr = tr_ar[tr_pos], tr_max = tr_ar[numres - 1];

            //Width of the resolution level computed
            uint rw = (uint)(tr.x1 - tr.x0);

            //Height of the resolution level computed
            uint rh = (uint)(tr.y1 - tr.y0);

            ulong h_mem_size;

            // Compute the intersection of the area of interest, expressed in tile coordinates
            // with the tile coordinates
            uint win_tcx0 = tilec.win_x0;
            uint win_tcy0 = tilec.win_y0;
            uint win_tcx1 = tilec.win_x1;
            uint win_tcy1 = tilec.win_y1;

            if (tr_max.x0 == tr_max.x1 || tr_max.y0 == tr_max.y1)
            {
                return true;
            }

            sa = SparseArrayInt32.Init(tilec, numres);
            if (sa == null)
                return false;

            if (numres == 1U)
            {
                bool ret = sa.read(tr_max.win_x0 - (uint)tr_max.x0,
                                   tr_max.win_y0 - (uint)tr_max.y0,
                                   tr_max.win_x1 - (uint)tr_max.x0,
                                   tr_max.win_y1 - (uint)tr_max.y0,
                                   tilec.data_win, 0,
                                   1, tr_max.win_x1 - tr_max.win_x0,
                                   true);
                Debug.Assert(ret);
                return true;
            }
            h_mem_size = MaxResolution(tr_ar, (int) numres);
            // overflow check
            // in vertical pass, we process 4 columns at a time
            if (h_mem_size > (Constants.SIZE_MAX / (4 * sizeof(int))))
            {
                return false;
            }
            h_mem_size *= 4;
            h.mem = new int[h_mem_size];
            v.mem = h.mem;

            for (uint resno = 1; resno < numres; resno++)
            {
                uint i, j;
                /* Window of interest subband-based coordinates */
                uint win_ll_x0, win_ll_y0, win_ll_x1, win_ll_y1;
                uint win_hl_x0, win_hl_x1;
                uint win_lh_y0, win_lh_y1;
                /* Window of interest tile-resolution-based coordinates */
                uint win_tr_x0, win_tr_x1, win_tr_y0, win_tr_y1;
                /* Tile-resolution subband-based coordinates */
                uint tr_ll_x0, tr_ll_y0, tr_hl_x0, tr_lh_y0;

                tr = tr_ar[++tr_pos];

                h.sn = (int)rw;
                v.sn = (int)rh;

                rw = (uint)(tr.x1 - tr.x0);
                rh = (uint)(tr.y1 - tr.y0);

                h.dn = (int)(rw - (uint)h.sn);
                h.cas = tr.x0 % 2;

                v.dn = (int)(rh - (uint)v.sn);
                v.cas = tr.y0 % 2;

                // Get the subband coordinates for the window of interest
                // LL band
                GetBandCoordinates(tilec, resno, 0,
                                   win_tcx0, win_tcy0, win_tcx1, win_tcy1,
                                   out win_ll_x0, out win_ll_y0,
                                   out win_ll_x1, out win_ll_y1);

                // HL band
                GetBandCoordinates(tilec, resno, 1,
                                   win_tcx0, win_tcy0, win_tcx1, win_tcy1,
                                   out win_hl_x0, out _, out win_hl_x1, out _);

                /* LH band */
                GetBandCoordinates(tilec, resno, 2,
                                   win_tcx0, win_tcy0, win_tcx1, win_tcy1,
                                   out _, out win_lh_y0, out _, out win_lh_y1);

                /* Beware: band index for non-LL0 resolution are 0=HL, 1=LH and 2=HH */
                tr_ll_x0 = (uint)tr.bands[1].x0;
                tr_ll_y0 = (uint)tr.bands[0].y0;
                tr_hl_x0 = (uint)tr.bands[0].x0;
                tr_lh_y0 = (uint)tr.bands[1].y0;

                /* Subtract the origin of the bands for this tile, to the subwindow */
                /* of interest band coordinates, so as to get them relative to the */
                /* tile */
                win_ll_x0 = MyMath.uint_subs(win_ll_x0, tr_ll_x0);
                win_ll_y0 = MyMath.uint_subs(win_ll_y0, tr_ll_y0);
                win_ll_x1 = MyMath.uint_subs(win_ll_x1, tr_ll_x0);
                win_ll_y1 = MyMath.uint_subs(win_ll_y1, tr_ll_y0);
                win_hl_x0 = MyMath.uint_subs(win_hl_x0, tr_hl_x0);
                win_hl_x1 = MyMath.uint_subs(win_hl_x1, tr_hl_x0);
                win_lh_y0 = MyMath.uint_subs(win_lh_y0, tr_lh_y0);
                win_lh_y1 = MyMath.uint_subs(win_lh_y1, tr_lh_y0);

                SegmentGrow(filter_width, (uint)h.sn, ref win_ll_x0, ref win_ll_x1);
                SegmentGrow(filter_width, (uint)h.dn, ref win_hl_x0, ref win_hl_x1);

                SegmentGrow(filter_width, (uint)v.sn, ref win_ll_y0, ref win_ll_y1);
                SegmentGrow(filter_width, (uint)v.dn, ref win_lh_y0, ref win_lh_y1);

                /* Compute the tile-resolution-based coordinates for the window of interest */
                if (h.cas == 0)
                {
                    win_tr_x0 = Math.Min(2 * win_ll_x0, 2 * win_hl_x0 + 1);
                    win_tr_x1 = Math.Min(Math.Max(2 * win_ll_x1, 2 * win_hl_x1 + 1), rw);
                }
                else
                {
                    win_tr_x0 = Math.Min(2 * win_hl_x0, 2 * win_ll_x0 + 1);
                    win_tr_x1 = Math.Min(Math.Max(2 * win_hl_x1, 2 * win_ll_x1 + 1), rw);
                }

                if (v.cas == 0)
                {
                    win_tr_y0 = Math.Min(2 * win_ll_y0, 2 * win_lh_y0 + 1);
                    win_tr_y1 = Math.Min(Math.Max(2 * win_ll_y1, 2 * win_lh_y1 + 1), rh);
                }
                else
                {
                    win_tr_y0 = Math.Min(2 * win_lh_y0, 2 * win_ll_y0 + 1);
                    win_tr_y1 = Math.Min(Math.Max(2 * win_lh_y1, 2 * win_ll_y1 + 1), rh);
                }

                for (j = 0; j < rh; ++j)
                {
                    if ((j >= win_ll_y0 && j < win_ll_y1) ||
                            (j >= win_lh_y0 + (uint)v.sn && j < win_lh_y1 + (uint)v.sn))
                    {

                        // Avoids dwt.c:1584:44 (in opj_dwt_decode_partial_1): runtime error:
                        // signed integer overflow: -1094795586 + -1094795586 cannot be represented in type 'int'
                        // on opj_decompress -i  ../../openjpeg/MAPA.jp2 -o out.tif -d 0,0,256,256
                        // This is less extreme than memsetting the whole buffer to 0
                        // although we could potentially do better with better handling of edge conditions
                        if (win_tr_x1 >= 1 && win_tr_x1 < rw)
                        {
                            h.mem[win_tr_x1 - 1] = 0;
                        }
                        if (win_tr_x1 < rw)
                        {
                            h.mem[win_tr_x1] = 0;
                        }

                        interleave_partial_h(h.mem,
                                           h.cas,
                                           sa,
                                           j,
                                           (uint)h.sn,
                                           win_ll_x0,
                                           win_ll_x1,
                                           win_hl_x0,
                                           win_hl_x1);
                        decode_partial_1(h.mem, h.dn, h.sn, h.cas,
                                         (int)win_ll_x0,
                                         (int)win_ll_x1,
                                         (int)win_hl_x0,
                                         (int)win_hl_x1);
                        if (!sa.write(win_tr_x0, j,
                                      win_tr_x1, j + 1,
                                      h.mem, (int)win_tr_x0,
                                      1, 0, true))
                        {
                            return false;
                        }
                    }
                }

                for (i = win_tr_x0; i < win_tr_x1;)
                {
                    uint nb_cols = Math.Min(4U, win_tr_x1 - i);
                    interleave_partial_v(v.mem,
                                         v.cas,
                                         sa,
                                         i,
                                         nb_cols,
                                         (uint)v.sn,
                                         win_ll_y0,
                                         win_ll_y1,
                                         win_lh_y0,
                                         win_lh_y1);
                    decode_partial_1_parallel(v.mem, nb_cols, v.dn, v.sn, v.cas,
                                              (int)win_ll_y0,
                                              (int)win_ll_y1,
                                              (int)win_lh_y0,
                                              (int)win_lh_y1);
                    if (!sa.write(i, win_tr_y0,
                                  i + nb_cols, win_tr_y1,
                                  v.mem, 4 * (int)win_tr_y0,
                                  1, 4, true))
                    {
                        return false;
                    }

                    i += nb_cols;
                }
            }

            {
                bool ret = sa.read(
                               tr_max.win_x0 - (uint)tr_max.x0,
                               tr_max.win_y0 - (uint)tr_max.y0,
                               tr_max.win_x1 - (uint)tr_max.x0,
                               tr_max.win_y1 - (uint)tr_max.y0,
                               tilec.data_win, 0,
                               1, tr_max.win_x1 - tr_max.win_x0,
                               true);
                Debug.Assert(ret);
            }

            return true;
        }

        //2.5 - opj_dwt_interleave_partial_h
        private static void interleave_partial_h(
            int[] dest,
            int cas,
            SparseArrayInt32 sa,
            uint sa_line,
            uint sn,
            uint win_l_x0,
            uint win_l_x1,
            uint win_h_x0,
            uint win_h_x1)
        {
            bool ret;
            ret = sa.read(win_l_x0, sa_line,
                          win_l_x1, sa_line + 1,
                          dest, cas + 2 * (int)win_l_x0,
                          2, 0, true);
            Debug.Assert(ret);
            ret = sa.read(sn + win_h_x0, sa_line,
                          sn + win_h_x1, sa_line + 1,
                          dest, 1 - cas + 2 * (int)win_h_x0,
                          2, 0, true);
            Debug.Assert(ret);
        }

        //2.5 - opj_dwt_interleave_partial_v
        private static void interleave_partial_v(
            int[] dest,
            int cas,
            SparseArrayInt32 sa,
            uint sa_col,
            uint nb_cols,
            uint sn,
            uint win_l_y0,
            uint win_l_y1,
            uint win_h_y0,
            uint win_h_y1)
        {
            bool ret;
            ret = sa.read(sa_col, win_l_y0,
                          sa_col + nb_cols, win_l_y1,
                          dest, cas * 4 + 2 * 4 * (int)win_l_y0,
                          1, 2 * 4, true);
            Debug.Assert(ret);
            ret = sa.read(sa_col, sn + win_h_y0,
                          sa_col + nb_cols, sn + win_h_y1,
                          dest, (1 - cas) * 4 + 2 * 4 * (int)win_h_y0,
                          1, 2 * 4, true);
            Debug.Assert(ret);
        }

        /// <remarks>
        /// 2.5 - opj_dwt_decode_partial_1
        /// 
        /// OPJ_S(i) => a[(i)*2]
        /// OPJ_D(i) => a[(1+(i)*2)]
        /// OPJ_S_(i) => ((i)<0?OPJ_S(0):((i)>=sn?OPJ_S(sn-1):OPJ_S(i)))
        /// OPJ_D_(i) => ((i)<0?OPJ_D(0):((i)>=dn?OPJ_D(dn-1):OPJ_D(i)))
        /// OPJ_SS_(i) => ((i)<0?OPJ_S(0):((i)>=dn?OPJ_S(dn-1):OPJ_S(i)))
        /// OPJ_DD_(i) => ((i)<0?OPJ_D(0):((i)>=sn?OPJ_D(sn-1):OPJ_D(i)))
        /// 
        /// Substituted:
        /// OPJ_D_(i) => ((i) < 0 ? a[1] : ((i) >= dn ? a[(1 + (dn - 1) * 2)] : a[(1 + (i) * 2)]))
        /// OPJ_S_(i) => ((i)<0?a[0]:((i)>=sn?a[(sn-1)*2]:a[(i)*2]))
        /// OPJ_SS_(i) => ((i)<0?a[0]:((i)>=dn?a[(dn-1)*2]:a[(i)*2]))
        /// OPJ_DD_(i) => ((i)<0?a[1]:((i)>=sn?a[(1+(sn-1)*2)]:a[(1+(i)*2)]))
        /// </remarks>
        private static void decode_partial_1(int[] a, int dn, int sn,
                                             int cas,
                                             int win_l_x0,
                                             int win_l_x1,
                                             int win_h_x0,
                                             int win_h_x1)
        {
            int i;

            if (cas == 0)
            {
                if ((dn > 0) || (sn > 1))
                { 
                    i = win_l_x0;
                    if (i < win_l_x1)
                    {
                        int i_max;

                        /* Left-most case */
                        a[i * 2] -= (((i - 1) < 0 ? a[(1 + (0) * 2)] : ((i - 1) >= dn ? a[(1 + (dn - 1) * 2)] : a[(1 + (i - 1) * 2)]))
                                   + ((i) < 0 ? a[(1 + (0) * 2)] : ((i) >= dn ? a[(1 + (dn - 1) * 2)] : a[(1 + (i) * 2)]))
                                   + 2) >> 2;
                        i++;

                        i_max = win_l_x1;
                        if (i_max > dn)
                        {
                            i_max = dn;
                        }
                        for (; i < i_max; i++)
                        {
                            /* No bound checking */
                            a[i * 2] -= (a[1 + (i - 1) * 2] + a[1 + i * 2] + 2) >> 2;
                        }
                        for (; i < win_l_x1; i++)
                        {
                            /* Right-most case */
                            a[i * 2] -= (((i - 1) < 0 ? a[(1 + (0) * 2)] : ((i - 1) >= dn ? a[(1 + (dn - 1) * 2)] : a[(1 + (i - 1) * 2)]))
                                       + ((i) < 0 ? a[(1 + (0) * 2)] : ((i) >= dn ? a[(1 + (dn - 1) * 2)] : a[(1 + (i) * 2)]))
                                       + 2) >> 2;
                        }
                    }

                    i = win_h_x0;
                    if (i < win_h_x1)
                    {
                        int i_max = win_h_x1;
                        if (i_max >= sn)
                        {
                            i_max = sn - 1;
                        }
                        for (; i < i_max; i++)
                        {
                            /* No bound checking */
                            a[1 + i * 2] += (a[(i) * 2] + a[(i + 1) * 2]) >> 1;
                        }
                        for (; i < win_h_x1; i++)
                        {
                            /* Right-most case */
                            a[1 + i * 2] += (((i) < 0 ? a[0] : ((i) >= sn ? a[(sn - 1) * 2] : a[(i) * 2]))
                                           + ((i + 1) < 0 ? a[0] : ((i + 1) >= sn ? a[(sn - 1) * 2] : a[(i + 1) * 2]))
                                           ) >> 1;
                        }
                    }
                }
            }
            else
            {
                if (sn == 0 && dn == 1)
                {        /* NEW :  CASE ONE ELEMENT */
                    a[0] /= 2;
                }
                else
                {
                    for (i = win_l_x0; i < win_l_x1; i++)
                    {
                        a[1 + i * 2] = MyMath.int_sub_no_overflow(
                                              a[1 + i * 2],
                                              MyMath.int_add_no_overflow(
                                                  MyMath.int_add_no_overflow(
                                                      ((i) < 0 ? a[0] : ((i) >= dn ? a[(dn - 1) * 2] : a[(i) * 2])),
                                                      ((i + 1) < 0 ? a[0] : ((i + 1) >= dn ? a[(dn - 1) * 2] : a[(i + 1) * 2]))),
                                                  2) >> 2);
                    }
                    for (i = win_h_x0; i < win_h_x1; i++)
                    {
                        a[i * 2] = MyMath.int_add_no_overflow(
                                          a[i * 2],
                                          MyMath.int_add_no_overflow(
                                              ((i) < 0 ? a[1] : ((i) >= sn ? a[(1 + (sn - 1) * 2)] : a[(1 + (i) * 2)])),
                                              ((i - 1) < 0 ? a[1] : ((i - 1) >= sn ? a[(1 + (sn - 1) * 2)] : a[(1 + (i - 1) * 2)])))
                                          >> 1);
                    }
                }
            }
        }

        /// <remarks>
        /// 2.5
        /// 
        /// Todo: Make this more readable. Look at the OpenJpeg 2.1 C# impl. 
        ///       where the code is much more readable (Decode_1)
        /// 
        /// OPJ_S_off(i,off) => a[(uint)(i)*2*4+off]
        /// OPJ_D_off(i,off) => a[(1+(uint)(i)*2)*4+off]
        /// OPJ_S__off(i,off) => ((i)<0?OPJ_S_off(0,off):((i)>=sn?OPJ_S_off(sn-1,off):OPJ_S_off(i,off)))
        /// OPJ_D__off(i,off) => ((i)<0?OPJ_D_off(0,off):((i)>=dn?OPJ_D_off(dn-1,off):OPJ_D_off(i,off)))
        /// OPJ_SS__off(i,off) => ((i)<0?OPJ_S_off(0,off):((i)>=dn?OPJ_S_off(dn-1,off):OPJ_S_off(i,off)))
        /// OPJ_DD__off(i,off) => ((i)<0?OPJ_D_off(0,off):((i)>=sn?OPJ_D_off(sn-1,off):OPJ_D_off(i,off)))
        /// 
        /// Substituted:
        /// OPJ_S__off(i,off) => ((i)<0?a[off]:((i)>=sn?a[(uint)(sn-1)*2*4+off]:a[(uint)(i)*2*4+off]))
        /// OPJ_D__off(i,off) => ((i)<0?a[1+off]:((i)>=dn?a[(1+(uint)(dn-1)*2)*4+off]:a[(1+(uint)(i)*2)*4+off]))
        /// OPJ_SS__off(i,off) => ((i)<0?a[off]:((i)>=dn?a[(uint)(dn-1)*2*4+off]:a[(uint)(i)*2*4+off]))
        /// OPJ_DD__off(i,off) => ((i)<0?a[1+off]:((i)>=sn?a[(1+(uint)(sn-1)*2)*4+off]:a[(1+(uint)(i)*2)*4+off])
        /// </remarks>
        private static void decode_partial_1_parallel(
                                                    int[] a,
                                                    uint nb_cols,
                                                    int dn, int sn,
                                                    int cas,
                                                    int win_l_x0,
                                                    int win_l_x1,
                                                    int win_h_x0,
                                                    int win_h_x1)
        {
            int i;
            uint off;

            if (cas == 0)
            {
                if ((dn > 0) || (sn > 1))
                {
                    i = win_l_x0;
                    if (i < win_l_x1)
                    {
                        int i_max;

                        /* Left-most case */
                        for (off = 0; off < 4; off++)
                        {
                            a[(uint)(i) * 2 * 4 + off] -= (
                                ((i - 1) < 0 ? a[(1) * 4 + off] : ((i - 1) >= dn ? a[(1 + (uint)(dn - 1) * 2) * 4 + off] : a[(1 + (uint)(i - 1) * 2) * 4 + off])) 
                              + ((i) < 0 ? a[(1) * 4 + off] : ((i) >= dn ? a[(1 + (uint)(dn - 1) * 2) * 4 + off] : a[(1 + (uint)(i) * 2) * 4 + off])) 
                              + 2
                             ) >> 2;
                        }
                        i++;

                        i_max = win_l_x1;
                        if (i_max > dn)
                        {
                            i_max = dn;
                        }

                        //Snip SSE2 code. 

                        for (; i < i_max; i++)
                        {
                            /* No bound checking */
                            for (off = 0; off < 4; off++)
                            {
                                a[(uint)(i) * 2 * 4 + off] -= (
                                    a[(1 + (uint)(i - 1) * 2) * 4 + off] 
                                  + a[(1 + (uint)(i) * 2) * 4 + off] 
                                  + 2
                                ) >> 2;
                            }
                        }
                        for (; i < win_l_x1; i++)
                        {
                            /* Right-most case */
                            for (off = 0; off < 4; off++)
                            {
                                a[(uint)(i) * 2 * 4 + off] -= (
                                    ((i - 1) < 0 ? a[(1) * 4 + off] : ((i - 1) >= dn ? a[(1 + (uint)(dn - 1) * 2) * 4 + off] : a[(1 + (uint)(i - 1) * 2) * 4 + off])) 
                                  + ((i) < 0 ? a[(1) * 4 + off] : ((i) >= dn ? a[(1 + (uint)(dn - 1) * 2) * 4 + off] : a[(1 + (uint)(i) * 2) * 4 + off])) 
                                  + 2
                                ) >> 2;
                            }
                        }
                    }

                    i = win_h_x0;
                    if (i < win_h_x1)
                    {
                        int i_max = win_h_x1;
                        if (i_max >= sn)
                        {
                            i_max = sn - 1;
                        }

                        //Snip SSE2 code. 

                        for (; i < i_max; i++)
                        {
                            /* No bound checking */
                            for (off = 0; off < 4; off++)
                            {
                                a[(1 + (uint)(i) * 2) * 4 + off] += (
                                    a[(uint)(i) * 2 * 4 + off]
                                  + a[(uint)(i + 1) * 2 * 4 + off]
                                ) >> 1;
                            }
                        }
                        for (; i < win_h_x1; i++)
                        {
                            /* Right-most case */
                            for (off = 0; off < 4; off++)
                            {
                                a[(1 + (uint)(i) * 2) * 4 + off] += (
                                    ((i) < 0 ? a[off] : ((i) >= sn ? a[(uint)(sn - 1) * 2 * 4 + off] : a[(uint)(i) * 2 * 4 + off])) 
                                  + ((i + 1) < 0 ? a[off] : ((i + 1) >= sn ? a[(uint)(sn - 1) * 2 * 4 + off] : a[(uint)(i + 1) * 2 * 4 + off]))
                                ) >> 1;
                            }
                        }
                    }
                }
            }
            else
            {
                if (sn == 0 && dn == 1)
                {        /* NEW :  CASE ONE ELEMENT */
                    for (off = 0; off < 4; off++)
                        a[off] /= 2;
                }
                else
                {
                    for (i = win_l_x0; i < win_l_x1; i++)
                    {
                        for (off = 0; off < 4; off++)
                           a[(1 + (uint)(i) * 2) * 4 + off] = 
                                MyMath.int_sub_no_overflow(
                                            a[(1 + (uint)(i) * 2) * 4 + off],
                                            MyMath.int_add_no_overflow(
                                                MyMath.int_add_no_overflow(
                                                    ((i) < 0 ? a[(uint)(0) * 2 * 4 + off] : ((i) >= dn ? a[(uint)(dn - 1) * 2 * 4 + off] : a[(uint)(i) * 2 * 4 + off])), 
                                                    ((i + 1) < 0 ? a[(uint)(0) * 2 * 4 + off] : ((i + 1) >= dn ? a[(uint)(dn - 1) * 2 * 4 + off] : a[(uint)(i + 1) * 2 * 4 + off]))), 
                                                2) 
                                            >> 2);
                    }
                    for (i = win_h_x0; i < win_h_x1; i++)
                    {
                        for (off = 0; off < 4; off++)
                            a[(uint)(i) * 2 * 4 + off] = 
                                MyMath.int_add_no_overflow(
                                          a[(uint)(i) * 2 * 4 + off],
                                          MyMath.int_add_no_overflow(
                                              ((i) < 0 ? a[(1 + (uint)(0) * 2) * 4 + off] : ((i) >= sn ? a[(1 + (uint)(sn - 1) * 2) * 4 + off] : a[(1 + (uint)(i) * 2) * 4 + off])), 
                                              ((i - 1) < 0 ? a[(1 + (uint)(0) * 2) * 4 + off] : ((i - 1) >= sn ? a[(1 + (uint)(sn - 1) * 2) * 4 + off] : a[(1 + (uint)(i - 1) * 2) * 4 + off]))) 
                                          >> 1);
                    }
                }
            }
        }

        /// <summary>
        /// Gathers up to eight columns into the wavelet buffer, each sample's lanes the columns, the low band on the even
        /// samples and the high band on the odd (or the other way about).
        /// </summary>
        /// <remarks>2.5 - opj_v8dwt_interleave_v</remarks>
        static void v8dwt_interleave_v(v4dwt_local dwt, int[] a_ar, int a, int width, int n_elts_read)
        {
            float[] bi_ar = dwt.wavelet;
            for (int i = (int)dwt.win_l_x0; i < dwt.win_l_x1; ++i)
                load_lanes(a_ar, a + i * width, bi_ar, (dwt.cas + i * 2) * NB_ELTS_V8, n_elts_read);
            a += dwt.sn * width;
            for (int i = (int)dwt.win_h_x0; i < dwt.win_h_x1; ++i)
                load_lanes(a_ar, a + i * width, bi_ar, (1 - dwt.cas + i * 2) * NB_ELTS_V8, n_elts_read);
        }

        /// <summary>Reads <paramref name="count"/> samples of a row, the bits of floats, into one sample's lanes.</summary>
        static void load_lanes(int[] from, int at, float[] to, int into, int count)
        {
            if (count == NB_ELTS_V8)
            {
                Vector256.Create(from.AsSpan(at, NB_ELTS_V8)).AsSingle().CopyTo(to.AsSpan(into, NB_ELTS_V8));
                return;
            }

            for (var l = 0; l < count; l++)
                to[into + l] = BitConverter.Int32BitsToSingle(from[at + l]);
        }

        //2.5
        static void v8dwt_interleave_partial_h(v4dwt_local dwt, SparseArrayInt32 sa, uint sa_line, uint remaining_height)
        {
            for (uint i = 0; i < remaining_height; i++)
            {
                bool ret;
                ret = sa.read(dwt.win_l_x0, sa_line + i,
                              dwt.win_l_x1, sa_line + i + 1,
                              /* Nasty cast from float* to int32* */
                              dwt.wavelet, (dwt.cas + 2 * (int)dwt.win_l_x0) * NB_ELTS_V8 + (int)i, //C# dwt.wavelet index must be multiplied with NB_ELTS_V8
                              2 * NB_ELTS_V8, 0, true);
                Debug.Assert(ret);
                ret = sa.read((uint)dwt.sn + dwt.win_h_x0, sa_line + i,
                              (uint)dwt.sn + dwt.win_h_x1, sa_line + i + 1,
                              /* Nasty cast from float* to int32* */
                              dwt.wavelet, (1 - dwt.cas + 2 * (int)dwt.win_h_x0) * NB_ELTS_V8 + (int)i,
                              2 * NB_ELTS_V8, 0, true);
                Debug.Assert(ret);
            }
        }

        //2.5
        static void v8dwt_interleave_partial_v(v4dwt_local dwt, SparseArrayInt32 sa, uint sa_col, uint nb_elts_read)
        {
            bool ret;
            ret = sa.read(sa_col, dwt.win_l_x0,
                          sa_col + nb_elts_read, dwt.win_l_x1,
                          /* Nasty cast from float* to int32* */
                          dwt.wavelet, (dwt.cas + 2 * (int)dwt.win_l_x0) * NB_ELTS_V8,
                          1, 2 * NB_ELTS_V8, true);
            Debug.Assert(ret);
            ret = sa.read(sa_col, (uint)dwt.sn + dwt.win_h_x0,
                          sa_col + nb_elts_read, (uint)dwt.sn + dwt.win_h_x1,
                          /* Nasty cast from float* to int32* */
                          dwt.wavelet, (1 - dwt.cas + 2 * (int)dwt.win_h_x0) * NB_ELTS_V8,
                          1, 2 * NB_ELTS_V8, true);
            Debug.Assert(ret);
        }

        /// <summary>
        /// Inverse 5-3 wavelet transform in 2-D
        /// </summary>
        /// <param name="tilec">Tile component information (current tile)</param>
        /// <param name="numres">Number of resolution levels to decode</param>
        /// <remarks>
        /// 2.5 - opj_dwt_decode
        /// </remarks>
        internal static bool Decode(TileCoder tcd, TcdTilecomp tilec, uint numres)
        {
            if (tcd.WholeTileDecoding)
                return decode_tile(tilec, numres, tcd.DisableMultiThreading);
            else
                return DecodePartialTile(tilec, numres);
        }

        /// <summary>
        /// Inverse 5-3 wavelet transform in 1-D for one row.
        /// </summary>
        /// <remarks>
        /// 2.5
        /// Performs interleave, inverse wavelet transform and copy back to buffer
        /// </remarks>
        private static void idwt53_h(dwt_local dwt, int[] tiled, int tiledp)
        {
#if STANDARD_SLOW_VERSION
            Interleave_h(dwt, tiledp, tiled);
            Decode_1(dwt);
            Buffer.BlockCopy(dwt.mem, 0, tiled, tiledp * sizeof(int), (dwt.sn + dwt.dn) * sizeof(int));
#else
            int sn = dwt.sn;
            int len = sn + dwt.dn;
            if (dwt.cas == 0)
            { /* Left-most sample is on even coordinate */
                if (len > 1)
                {
                    idwt53_h_cas0(dwt.mem, sn, len, tiled, tiledp);
                }
                else
                {
                    /* Unmodified value */
                }
            }
            else
            { /* Left-most sample is on odd coordinate */
                if (len == 1)
                {
                    tiled[tiledp] /= 2;
                }
                else if (len == 2)
                {
                    var o = dwt.mem;
                    int in_even = tiledp + sn;
                    int in_odd = tiledp;
                    o[1] = tiled[in_odd] - ((tiled[in_even] + 1) >> 1);
                    o[0] = tiled[in_even] + o[1];
                    Buffer.BlockCopy(dwt.mem, 0, tiled, tiledp * sizeof(int), len * sizeof(int));
                }
                else if (len > 2)
                {
                    opj_idwt53_h_cas1(dwt.mem, sn, len, tiled, tiledp);
                }
            }
#endif
        }

#if STANDARD_SLOW_VERSION
        /// <summary>
        /// Inverse 5-3 wavelet transform in 1-D.
        /// </summary>
        //2.1
        static void Decode_1(dwt_local v)
        {
            Decode_1_(v.mem, v.dn, v.sn, v.cas);
        }

        /// <summary>
        /// Inverse 5-3 wavelet transform in 1-D.
        /// </summary>
        //2.1
        static void Decode_1_(int[] a, int dn, int sn, int cas)
        {
            if (cas == 0)
            {
                if (dn > 0 || sn > 1)
                {
                    for (int i = 0; i < sn; i++)
                    {
                        //C# impl. of the macro: D_(i - 1) + D_(i)
                        int D1 = (i - 1) < 0 ? 0 : (i - 1) >= dn ? (dn - 1) : (i - 1);
                        int D2 = i < 0 ? 0 : i >= dn ? (dn - 1) : i;

                        //S(i) -= (D_(i - 1) + D_(i) + 2) >> 2;
                        a[i * 2] -= (a[1 + D1 * 2] + a[1 + D2 * 2] + 2) >> 2;
                    }
                    for (int i = 0; i < dn; i++)
                    {
                        //C# impl. of the macro: S_(i) + S_(i + 1)
                        int S1 = i < 0 ? 0 : i >= sn ? (sn - 1) : i;
                        int S2 = (i + 1) < 0 ? 0 : (i + 1) >= sn ? (sn - 1) : (i + 1);

                        //D(i) += (S_(i) + S_(i + 1)) >> 1;
                        a[1 + i * 2] += (a[S1 * 2] + a[S2 * 2]) >> 1;
                    }
                    //#define S(i) a[(i)*2]
                    //#define D(i) a[(1+(i)*2)]
                     //#define S_(i) ((i)<0?S(0):((i)>=sn?S(sn-1):S(i)))
                    //#define SS_(i) ((i)<0?S(0):((i)>=dn?S(dn-1):S(i)))
                     //#define D_(i) ((i)<0?D(0):((i)>=dn?D(dn-1):D(i)))
                    //#define DD_(i) ((i)<0?D(0):((i)>=sn?D(sn-1):D(i)))
                }
            }
            else
            {
                if (sn == 0 && dn == 1)
                    a[0] /= 2;
                else
                {
                    for (int i = 0; i < sn; i++)
                    {
                        //C# impl. of the macro: SS_(i) + SS_(i + 1)
                        int SS1 = i < 0 ? 0 : i >= dn ? (dn - 1) : i;
                        int SS2 = (i + 1) < 0 ? 0 : (i + 1) >= dn ? (dn - 1) : (i + 1);

                        //D(i) -= (SS_(i) + SS_(i + 1) + 2) >> 2;
                        a[1 + i * 2] -= (a[SS1 * 2] + a[SS2 * 2] + 2) >> 2;
                    }
                    for (int i = 0; i < dn; i++)
                    {
                        //C# impl. of the macro: DD_(i) + DD_(i - 1)
                        int DD1 = i < 0 ? 0 : i >= sn ? (sn - 1) : i;
                        int DD2 = (i - 1) < 0 ? 0 : (i - 1) >= sn ? (sn - 1) : (i - 1);

                        //S(i) += (DD_(i) + DD_(i - 1)) >> 1;
                        a[i * 2] += (a[1 + DD1 * 2] + a[1 + DD2 * 2]) >> 1;
                    }
                }
            }
        }

        
        /// <summary>
        /// Inverse lazy transform (vertical).
        /// </summary>
        //2.1
        static void Interleave_v(dwt_local v, int a, int[] a_ar, int x)
        {
            int ai = a;
            int bi = v.cas;
            int[] b_ar = v.mem;
            int i = v.sn;
            while (i-- != 0)
            {
                b_ar[bi] = a_ar[ai];
                bi += 2;
                ai += x;
            }
            ai = a + (v.sn * x);
            bi = 1 - v.cas;
            i = v.dn;
            while (i-- != 0)
            {
                b_ar[bi] = a_ar[ai];
                bi += 2;
                ai += x;
            }
        }
#else
        //2.5
        private static void idwt53_h_cas0(int[] tmp, int sn, int len, int[] tiled, int tiledp)
        {
            Debug.Assert(len > 1);

            int i, j;
            int in_even = tiledp;
            int in_odd = tiledp + sn;

#if TWO_PASS_VERSION
            /* For documentation purpose: performs lifting in two iterations, */
            /* but without explicit interleaving */

            /* Even */
            tmp[0] = tiled[in_even] - ((tiled[in_odd] + 1) >> 1);
            for (i = 2, j = 0; i <= len - 2; i += 2, j++)
            {
                tmp[i] = tiled[in_even + j + 1] - ((tiled[in_odd + j] + tiled[in_odd + j + 1] + 2) >> 2);
            }
            if ((len & 1) != 0)
            { /* if len is odd */
                tmp[len - 1] = tiled[in_even + (len - 1) / 2] - ((tiled[in_odd + (len - 2) / 2] + 1) >> 1);
            }

            /* Odd */
            for (i = 1, j = 0; i < len - 1; i += 2, j++)
            {
                tmp[i] = tiled[in_odd + j] + ((tmp[i - 1] + tmp[i + 1]) >> 1);
            }
            if ((len & 1) == 0)
            { /* if len is even */
                tmp[len - 1] = tiled[in_odd + (len - 1) / 2] + tmp[len - 2];
            }
#else
            // Improved version of the TWO_PASS_VERSION:
            // Performs lifting in one single iteration. Saves memory
            // accesses and explicit interleaving.
            int d1c, d1n, s1n, s0c, s0n;

            s1n = tiled[in_even];
            d1n = tiled[in_odd];
            s0n = s1n - ((d1n + 1) >> 1);

            for (i = 0, j = 1; i < (len - 3); i += 2, j++)
            {
                d1c = d1n;
                s0c = s0n;

                s1n = tiled[in_even + j];
                d1n = tiled[in_odd + j];

                s0n = s1n - ((d1c + d1n + 2) >> 2);

                tmp[i] = s0c;
                tmp[i + 1] = MyMath.int_add_no_overflow(d1c, MyMath.int_add_no_overflow(s0c,
                                                     s0n) >> 1);
            }

            tmp[i] = s0n;

            if ((len & 1) != 0)
            {
                tmp[len - 1] = tiled[in_even + (len - 1) / 2] - ((d1n + 1) >> 1);
                tmp[len - 2] = d1n + ((s0n + tmp[len - 1]) >> 1);
            }
            else
            {
                tmp[len - 1] = d1n + s0n;
            }
#endif
            tmp.AsSpan(0, len).CopyTo(tiled.AsSpan(tiledp, len));
        }

        //2.5
        private static void opj_idwt53_h_cas1(int[] tmp, int sn, int len, int[] tiled, int tiledp)
        {
            Debug.Assert(len > 2);

            int i, j;
            int in_even = tiledp + sn;
            int in_odd = tiledp;

#if TWO_PASS_VERSION
            /* For documentation purpose: performs lifting in two iterations, */
            /* but without explicit interleaving */

            /* Odd */
            for (i = 1, j = 0; i < len - 1; i += 2, j++)
            {
                tmp[i] = tiled[in_odd + j] - ((tiled[in_even + j] + tiled[in_even + j + 1] + 2) >> 2);
            }
            if ((len & 1) == 0)
            {
                tmp[len - 1] = tiled[in_odd + len / 2 - 1] - ((tiled[in_even + len / 2 - 1] + 1) >> 1);
            }

            /* Even */
            tmp[0] = tiled[in_even] + tmp[1];
            for (i = 2, j = 1; i < len - 1; i += 2, j++)
            {
                tmp[i] = tiled[in_even + j] + ((tmp[i + 1] + tmp[i - 1]) >> 1);
            }
            if ((len & 1) != 0)
            {
                tmp[len - 1] = tiled[in_even + len / 2] + tmp[len - 2];
            }
#else
            int s1, s2, dc, dn;

            /* Improved version of the TWO_PASS_VERSION: */
            /* Performs lifting in one single iteration. Saves memory */
            /* accesses and explicit interleaving. */

            s1 = tiled[in_even + 1];
            dc = tiled[in_odd] - ((tiled[in_even] + s1 + 2) >> 2);
            tmp[0] = tiled[in_even] + dc;

            int end = len - 2 - ((len & 1) == 0 ? 1 : 0);
            for (i = 1, j = 1; i < end; i += 2, j++)
            {

                s2 = tiled[in_even + j + 1];

                dn = tiled[in_odd + j] - ((s1 + s2 + 2) >> 2);
                tmp[i] = dc;
                tmp[i + 1] = MyMath.int_add_no_overflow(s1, MyMath.int_add_no_overflow(dn, dc) >> 1);

                dc = dn;
                s1 = s2;
            }

            tmp[i] = dc;

            if ((len & 1) == 0)
            {
                dn = tiled[in_odd + len / 2 - 1] - ((s1 + 1) >> 1);
                tmp[len - 2] = s1 + ((dn + dc) >> 1);
                tmp[len - 1] = dn;
            }
            else
            {
                tmp[len - 1] = s1 + dc;
            }
#endif
            tmp.AsSpan(0, len).CopyTo(tiled.AsSpan(tiledp, len));
        }

        //2.5
        private static void idwt3_v_cas0(int[] tmp, int sn, int len, int[] tiled, int tiledp_col, int stride)
        {
            int i, j;
            int d1c, d1n, s1n, s0c, s0n;

            Debug.Assert(len > 1);

            /* Performs lifting in one single iteration. Saves memory */
            /* accesses and explicit interleaving. */

            s1n = tiled[tiledp_col];
            d1n = tiled[tiledp_col + sn * stride];
            s0n = s1n - ((d1n + 1) >> 1);

            for (i = 0, j = 0; i < (len - 3); i += 2, j++)
            {
                d1c = d1n;
                s0c = s0n;

                s1n = tiled[tiledp_col + (j + 1) * stride];
                d1n = tiled[tiledp_col + (sn + j + 1) * stride];

                s0n = MyMath.int_sub_no_overflow(s1n,
                                  MyMath.int_add_no_overflow(MyMath.int_add_no_overflow(d1c, d1n), 2) >> 2);

                tmp[i] = s0c;
                tmp[i + 1] = MyMath.int_add_no_overflow(d1c, MyMath.int_add_no_overflow(s0c,
                                                     s0n) >> 1);
            }

            tmp[i] = s0n;

            if ((len & 1) != 0)
            {
                tmp[len - 1] =
                    tiled[tiledp_col + ((len - 1) / 2) * stride] -
                    ((d1n + 1) >> 1);
                tmp[len - 2] = d1n + ((s0n + tmp[len - 1]) >> 1);
            }
            else
            {
                tmp[len - 1] = d1n + s0n;
            }

            for (i = 0; i < len; ++i)
            {
                tiled[tiledp_col + i * stride] = tmp[i];
                //if (214928 == tiledp_col + i * stride)
                //{
                //    Debug.Write("Hello");
                //}
            }
        }

        //2.5
        private static void idwt3_v_cas1(int[] tmp, int sn, int len, int[] tiled, int tiledp_col, int stride)
        {
            int i, j;
            int s1, s2, dc, dn;
            int in_even = tiledp_col + sn * stride;
            int in_odd = tiledp_col;

            Debug.Assert(len > 2);

            // Performs lifting in one single iteration. Saves memory
            // accesses and explicit interleaving.

            s1 = tiled[in_even + stride];
            dc = tiled[in_odd] - ((tiled[in_even] + s1 + 2) >> 2);
            tmp[0] = tiled[in_even] + dc;
            int end = len - 2 - ((len & 1) == 0 ? 1 : 0);
            for (i = 1, j = 1; i < end; i += 2, j++)
            {

                s2 = tiled[in_even + (j + 1) * stride];

                dn = tiled[in_odd + j * stride] - ((s1 + s2 + 2) >> 2);
                tmp[i] = dc;
                tmp[i + 1] = s1 + ((dn + dc) >> 1);

                dc = dn;
                s1 = s2;
            }
            tmp[i] = dc;
            if ((len & 1) == 0)
            {
                dn = tiled[in_odd + (len / 2 - 1) * stride] - ((s1 + 1) >> 1);
                tmp[len - 2] = s1 + ((dn + dc) >> 1);
                tmp[len - 1] = dn;
            }
            else
            {
                tmp[len - 1] = s1 + dc;
            }

            for (i = 0; i < len; ++i)
            {
                tiled[tiledp_col + i * stride] = tmp[i];
            }
        }
#endif

        //2.5
        private static void idwt53_v(dwt_local dwt, int[] tiled, int tiledp_col, int stride, int nb_cols)
        {
#if STANDARD_SLOW_VERSION
            for (int c = 0; c < nb_cols; c ++) {
                Interleave_v(dwt, tiledp_col + c, tiled, stride);
                Decode_1(dwt);
                for (int k = 0; k < dwt.sn + dwt.dn; ++k) {
                    tiled[tiledp_col + c + k * stride] = dwt.mem[k];
                }
            }
#else
            //var mem_copy = new int[dwt.mem.Length];
            //Array.Copy(dwt.mem, mem_copy, dwt.mem.Length);
            //var tiled_copy = new int[tiled.Length];
            //Array.Copy(tiled, tiled_copy, tiled_copy.Length);

            //for (int c = 0; c < nb_cols; c++)
            //{
            //    Interleave_v(dwt, tiledp_col + c, tiled, stride);
            //    Decode_1(dwt);
            //    for (int k = 0; k < dwt.sn + dwt.dn; ++k)
            //    {
            //        tiled[tiledp_col + c + k * stride] = dwt.mem[k];
            //    }
            //}

            //var correct_tiled = tiled;
            //tiled = tiled_copy;
            //dwt.mem = mem_copy;

            int sn = dwt.sn;
            int len = sn + dwt.dn;
            if (dwt.cas == 0)
            {
                if (len > 1)
                {
                    if (nb_cols == PARALLEL_COLS_53)
                    {
                        idwt53_v_cas0_mcols(dwt.mem, sn, len, tiled, tiledp_col, stride);
                        return;
                    }

                    for (int c = 0; c < nb_cols; c++, tiledp_col++)
                    {
                        idwt3_v_cas0(dwt.mem, sn, len, tiled, tiledp_col, stride);
                    }

                    ////Check for correctnes
                    //for (int c = 0; c < correct_tiled.Length; c++)
                    //{
                    //    if (correct_tiled[c] != tiled[c])
                    //    {
                    //        Debug.Write("Nah");
                    //    }
                    //}
                    return;
                }
            }
            else
            {
                if (len == 1)
                {
                    for (int c = 0; c < nb_cols; c++, tiledp_col++)
                    {
                        tiled[tiledp_col] /= 2;
                    }
                    return;
                }

                if (len == 2)
                {
                    int[] o = dwt.mem;
                    for (int c = 0; c < nb_cols; c++, tiledp_col++)
                    {
                        int in_even = tiledp_col + sn * stride;
                        int in_odd = tiledp_col;

                        o[1] = tiled[in_odd] - ((tiled[in_even] + 1) >> 1);
                        o[0] = tiled[in_even] + o[1];

                        for (int i = 0; i < len; ++i)
                        {
                            tiled[tiledp_col + i * stride] = o[i] ;
                        }
                    }

                    return;
                }

                if (len > 2)
                {
                    if (nb_cols == PARALLEL_COLS_53)
                    {
                        idwt53_v_cas1_mcols(dwt.mem, sn, len, tiled, tiledp_col, stride);
                        return;
                    }

                    for (int c = 0; c < nb_cols; c++, tiledp_col++)
                    {
                        idwt3_v_cas1(dwt.mem, sn, len, tiled, tiledp_col, stride);
                    }
                    return;
                }
            }
#endif
        }

        /// <summary>
        /// The lifting of <see cref="idwt3_v_cas0"/> down eight columns at once, a row of eight samples a vector, the
        /// intermediate column kept in <paramref name="tmp"/> eight to a row.
        /// </summary>
        /// <remarks>
        /// 2.5 - opj_idwt53_v_cas0_mcols_SSE2. Integer lifting wraps and shifts exactly as the scalar does, so the
        /// columns come out the same.
        /// </remarks>
        private static void idwt53_v_cas0_mcols(int[] tmp, int sn, int len, int[] tiled, int tiledp_col, int stride)
        {
            const int Lanes = (int)PARALLEL_COLS_53;
            var one = Vector256<int>.One;
            var two = Vector256.Create(2);
            int i, j;

            Debug.Assert(len > 1);

            var s1n = Vector256.Create(tiled.AsSpan(tiledp_col, Lanes));
            var d1n = Vector256.Create(tiled.AsSpan(tiledp_col + sn * stride, Lanes));
            var s0n = s1n - Vector256.ShiftRightArithmetic(d1n + one, 1);

            for (i = 0, j = 0; i < (len - 3); i += 2, j++)
            {
                var d1c = d1n;
                var s0c = s0n;

                s1n = Vector256.Create(tiled.AsSpan(tiledp_col + (j + 1) * stride, Lanes));
                d1n = Vector256.Create(tiled.AsSpan(tiledp_col + (sn + j + 1) * stride, Lanes));

                s0n = s1n - Vector256.ShiftRightArithmetic(d1c + d1n + two, 2);

                s0c.CopyTo(tmp.AsSpan(i * Lanes, Lanes));
                (d1c + Vector256.ShiftRightArithmetic(s0c + s0n, 1)).CopyTo(tmp.AsSpan((i + 1) * Lanes, Lanes));
            }

            s0n.CopyTo(tmp.AsSpan(i * Lanes, Lanes));

            if ((len & 1) != 0)
            {
                var last = Vector256.Create(tiled.AsSpan(tiledp_col + ((len - 1) / 2) * stride, Lanes))
                           - Vector256.ShiftRightArithmetic(d1n + one, 1);
                last.CopyTo(tmp.AsSpan((len - 1) * Lanes, Lanes));
                (d1n + Vector256.ShiftRightArithmetic(s0n + last, 1)).CopyTo(tmp.AsSpan((len - 2) * Lanes, Lanes));
            }
            else
            {
                (d1n + s0n).CopyTo(tmp.AsSpan((len - 1) * Lanes, Lanes));
            }

            for (i = 0; i < len; ++i)
                tmp.AsSpan(i * Lanes, Lanes).CopyTo(tiled.AsSpan(tiledp_col + i * stride, Lanes));
        }

        /// <summary>
        /// The lifting of <see cref="idwt3_v_cas1"/> down eight columns at once, a row of eight samples a vector.
        /// </summary>
        /// <remarks>2.5 - opj_idwt53_v_cas1_mcols_SSE2</remarks>
        private static void idwt53_v_cas1_mcols(int[] tmp, int sn, int len, int[] tiled, int tiledp_col, int stride)
        {
            const int Lanes = (int)PARALLEL_COLS_53;
            var one = Vector256<int>.One;
            var two = Vector256.Create(2);
            int i, j;
            int in_even = tiledp_col + sn * stride;
            int in_odd = tiledp_col;

            Debug.Assert(len > 2);

            var even = Vector256.Create(tiled.AsSpan(in_even, Lanes));
            var s1 = Vector256.Create(tiled.AsSpan(in_even + stride, Lanes));
            var dc = Vector256.Create(tiled.AsSpan(in_odd, Lanes)) - Vector256.ShiftRightArithmetic(even + s1 + two, 2);
            (even + dc).CopyTo(tmp.AsSpan(0, Lanes));

            int end = len - 2 - ((len & 1) == 0 ? 1 : 0);
            for (i = 1, j = 1; i < end; i += 2, j++)
            {
                var s2 = Vector256.Create(tiled.AsSpan(in_even + (j + 1) * stride, Lanes));

                var dn = Vector256.Create(tiled.AsSpan(in_odd + j * stride, Lanes)) - Vector256.ShiftRightArithmetic(s1 + s2 + two, 2);
                dc.CopyTo(tmp.AsSpan(i * Lanes, Lanes));
                (s1 + Vector256.ShiftRightArithmetic(dn + dc, 1)).CopyTo(tmp.AsSpan((i + 1) * Lanes, Lanes));

                dc = dn;
                s1 = s2;
            }
            dc.CopyTo(tmp.AsSpan(i * Lanes, Lanes));

            if ((len & 1) == 0)
            {
                var dn = Vector256.Create(tiled.AsSpan(in_odd + (len / 2 - 1) * stride, Lanes)) - Vector256.ShiftRightArithmetic(s1 + one, 1);
                (s1 + Vector256.ShiftRightArithmetic(dn + dc, 1)).CopyTo(tmp.AsSpan((len - 2) * Lanes, Lanes));
                dn.CopyTo(tmp.AsSpan((len - 1) * Lanes, Lanes));
            }
            else
            {
                (s1 + dc).CopyTo(tmp.AsSpan((len - 1) * Lanes, Lanes));
            }

            for (i = 0; i < len; ++i)
                tmp.AsSpan(i * Lanes, Lanes).CopyTo(tiled.AsSpan(tiledp_col + i * stride, Lanes));
        }

        //2.5
        private static void GetBandCoordinates(TcdTilecomp tilec,
                uint resno,
                uint bandno,
                uint tcx0,
                uint tcy0,
                uint tcx1,
                uint tcy1,
                out uint tbx0,
                out uint tby0,
                out uint tbx1,
                out uint tby1)
        {
            // Compute number of decomposition for this band. See table F-1
            int nb = (resno == 0) ?
                      (int)tilec.numresolutions - 1 :
                      (int)(tilec.numresolutions - resno);
            /* Map above tile-based coordinates to sub-band-based coordinates per */
            /* equation B-15 of the standard */
            uint x0b = bandno & 1;
            uint y0b = bandno >> 1;
            //if (tbx0)
            {
                tbx0 = (nb == 0) ? tcx0 :
                       (tcx0 <= (1U << (int)(nb - 1)) * x0b) ? 0 :
                       MyMath.uint_ceildivpow2(tcx0 - (1U << (nb - 1)) * x0b, nb);
            }
            //if (tby0)
            {
                tby0 = (nb == 0) ? tcy0 :
                       (tcy0 <= (1U << (nb - 1)) * y0b) ? 0 :
                       MyMath.uint_ceildivpow2(tcy0 - (1U << (nb - 1)) * y0b, nb);
            }
            //if (tbx1)
            {
                tbx1 = (nb == 0) ? tcx1 :
                       (tcx1 <= (1U << (nb - 1)) * x0b) ? 0 :
                       MyMath.uint_ceildivpow2(tcx1 - (1U << (nb - 1)) * x0b, nb);
            }
            //if (tby1)
            {
                tby1 = (nb == 0) ? tcy1 :
                       (tcy1 <= (1U << (nb - 1)) * y0b) ? 0 :
                       MyMath.uint_ceildivpow2(tcy1 - (1U << (nb - 1)) * y0b, nb);
            }
        }

        //2.5 - opj_dwt_segment_grow
        private static void SegmentGrow(uint filter_width, uint max_size, ref uint start, ref uint end)
        {
            start = MyMath.uint_subs(start, filter_width);
            end = MyMath.uint_adds(end, filter_width);
            end = Math.Min(end, max_size);
        }

        class dwt_local
        {
            internal int[] mem;

            /// <summary>
            /// Number of elements in high pass band
            /// </summary>
            internal int dn;

            /// <summary>
            /// Number of elements in low pass band
            /// </summary>
            internal int sn;

            /// <summary>
            /// 0 = start on even coord, 1 = start on odd coord
            /// </summary>
            internal int cas;

            public dwt_local Clone()
            {
                return (dwt_local)MemberwiseClone();
            }
        }

        class v4dwt_local
        {
            /// <summary>
            /// Each wavelet is 4 floating points.
            /// </summary>
            /// <remarks>
            ///C# note: Org. impl stores the wavlet as a struct with eight
            ///floating points. Here it's stored as a continious array. 
            ///
            /// This so to make it possible to use Buffer.BlockCopy to
            /// copy the values. 
            ///</remarks>
            internal float[] wavelet;

            /// <summary>
            /// Number of elements in high pass band
            /// </summary>
            internal int dn;

            /// <summary>
            /// Number of elements in low pass band
            /// </summary>
            internal int sn;

            /// <summary>
            ///  0 = start on even coord, 1 = start on odd coord
            /// </summary>
            internal int cas;

            /// <summary>
            /// Start coord in low pass band
            /// </summary>
            internal uint win_l_x0;

            /// <summary>
            /// End coord in low pass band
            /// </summary>
            internal uint win_l_x1;

            /// <summary>
            /// Start coord in high pass band
            /// </summary>
            internal uint win_h_x0;

            /// <summary>
            /// End coord in high pass band
            /// </summary>
            internal uint win_h_x1;

            public v4dwt_local Clone() { return (v4dwt_local)MemberwiseClone(); }
        }
    }
}
