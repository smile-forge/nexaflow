using System.Runtime.CompilerServices;

namespace Nexaflow.Media.Jpeg2000.Internal;

/// <summary>
/// The MQ-coded passes over a code block's bit-planes — significance propagation, magnitude refinement and cleanup —
/// as libopenjp2 2.5 runs them.
/// </summary>
/// <remarks>
/// Each pass is one generic method the JIT compiles once for each block shape and stripe-causal setting, so a 64×64
/// block's width, height and stride are constants, as in libopenjp2's specialised passes. The MQ decoder's registers
/// are copied into a local for the pass and back after it, and every step is inlined into the pass, so they stay in
/// registers throughout, as libopenjp2's decode macros keep them.
/// </remarks>
internal sealed partial class Tier1Coding
{
    /// <summary>The dimensions a pass runs over: the block's own, or constants the JIT folds in.</summary>
    private interface IBlockShape
    {
        static abstract int Width(uint w);

        static abstract int Height(uint h);
    }

    /// <summary>A block of any size: its own width and height.</summary>
    private readonly struct AnyBlock : IBlockShape
    {
        public static int Width(uint w) => (int)w;

        public static int Height(uint h) => (int)h;
    }

    /// <summary>The usual 64×64 block.</summary>
    private readonly struct Block64 : IBlockShape
    {
        public static int Width(uint w) => 64;

        public static int Height(uint h) => 64;
    }

    /// <summary>Whether the code-block style is vertically stripe-causal (VSC).</summary>
    private interface IStripeCausal
    {
        static abstract bool On { get; }
    }

    private readonly struct StripeCausal : IStripeCausal
    {
        public static bool On => true;
    }

    private readonly struct NotStripeCausal : IStripeCausal
    {
        public static bool On => false;
    }

    /// <summary>
    /// Decode significant pass
    /// </summary>
    /// <remarks>2.5 - opj_t1_dec_sigpass_mqc</remarks>
    void DecSigpassMqc(int bpno, CCP_CBLKSTY cblksty)
    {
        var vsc = (cblksty & CCP_CBLKSTY.VSC) != 0;
        if (_w == 64 && _h == 64)
        {
            if (vsc) SignificancePass<Block64, StripeCausal>(bpno);
            else SignificancePass<Block64, NotStripeCausal>(bpno);
        }
        else if (vsc) SignificancePass<AnyBlock, StripeCausal>(bpno);
        else SignificancePass<AnyBlock, NotStripeCausal>(bpno);
    }

    /// <summary>
    /// Decode refinement pass
    /// </summary>
    /// <remarks>2.5 - opj_t1_dec_refpass_mqc</remarks>
    void DecRefpassMqc(int bpno)
    {
        if (_w == 64 && _h == 64) RefinementPass<Block64>(bpno);
        else RefinementPass<AnyBlock>(bpno);
    }

    /// <summary>
    /// Decode clean pass
    /// </summary>
    /// <remarks>2.5 - opj_t1_dec_clnpass</remarks>
    void DecClnpass(int bpno, CCP_CBLKSTY cblksty)
    {
        var vsc = (cblksty & CCP_CBLKSTY.VSC) != 0;
        if (_w == 64 && _h == 64)
        {
            if (vsc) CleanupPass<Block64, StripeCausal>(bpno);
            else CleanupPass<Block64, NotStripeCausal>(bpno);
        }
        else if (vsc) CleanupPass<AnyBlock, StripeCausal>(bpno);
        else CleanupPass<AnyBlock, NotStripeCausal>(bpno);
        DecClnpassCheckSegsym(cblksty);
    }

    /// <summary>
    /// The significance propagation pass: each insignificant sample with a significant neighbour is decoded, and
    /// its sign when it turns significant.
    /// </summary>
    /// <remarks>2.5 - opj_t1_dec_sigpass_mqc_internal</remarks>
    void SignificancePass<TShape, TVsc>(int bpno)
        where TShape : struct, IBlockShape
        where TVsc : struct, IStripeCausal
    {
        int w = TShape.Width(_w), h = TShape.Height(_h), stride = w + 2;
        int one = 1 << bpno, oneplushalf = one | (one >> 1);
        var flags = this.flags;
        var data = _data;
        var zc = _mqc.lut_ctxno_zc_orient;
        var mqc = _mqc.Registers;

        int k, d = 0, f = stride + 1;
        for (k = 0; k < (h & ~3); k += 4, d += 3 * w, f += 2)
        {
            for (var i = 0; i < w; ++i, ++d, ++f)
            {
                var flag = flags[f];
                if (flag == 0) continue;

                SignificanceStep(ref mqc, ref flag, flags, f, stride, data, d, oneplushalf, zc, 0, TVsc.On);
                SignificanceStep(ref mqc, ref flag, flags, f, stride, data, d + w, oneplushalf, zc, 1, false);
                SignificanceStep(ref mqc, ref flag, flags, f, stride, data, d + 2 * w, oneplushalf, zc, 2, false);
                SignificanceStep(ref mqc, ref flag, flags, f, stride, data, d + 3 * w, oneplushalf, zc, 3, false);
                flags[f] = flag;
            }
        }

        // The last stripe, shorter than four rows.
        if (k < h)
        {
            for (var i = 0; i < w; ++i, ++d, ++f)
            {
                for (var j = 0; j < h - k; ++j)
                    SignificanceStep(ref mqc, ref flags[f], flags, f, stride, data, d + j * w, oneplushalf, zc, j, TVsc.On);
            }
        }

        _mqc.Registers = mqc;
    }

    /// <summary>
    /// The magnitude refinement pass: each sample significant before this bit-plane gets its next bit.
    /// </summary>
    /// <remarks>2.5 - opj_t1_dec_refpass_mqc_internal</remarks>
    void RefinementPass<TShape>(int bpno)
        where TShape : struct, IBlockShape
    {
        int w = TShape.Width(_w), h = TShape.Height(_h), stride = w + 2;
        var poshalf = (1 << bpno) >> 1;
        var flags = this.flags;
        var data = _data;
        var mqc = _mqc.Registers;

        int k, d = 0, f = stride + 1;
        for (k = 0; k < (h & ~3); k += 4, d += 3 * w, f += 2)
        {
            for (var i = 0; i < w; ++i, ++d, ++f)
            {
                var flag = flags[f];
                if (flag == 0) continue;

                RefinementStep(ref mqc, ref flag, data, d, poshalf, 0);
                RefinementStep(ref mqc, ref flag, data, d + w, poshalf, 1);
                RefinementStep(ref mqc, ref flag, data, d + 2 * w, poshalf, 2);
                RefinementStep(ref mqc, ref flag, data, d + 3 * w, poshalf, 3);
                flags[f] = flag;
            }
        }

        if (k < h)
        {
            for (var i = 0; i < w; ++i, ++d, ++f)
            {
                for (var j = 0; j < h - k; ++j)
                    RefinementStep(ref mqc, ref flags[f], data, d + j * w, poshalf, j);
            }
        }

        _mqc.Registers = mqc;
    }

    /// <summary>
    /// The cleanup pass: every sample neither pass reached. A stripe column with no significant sample and no
    /// significant neighbour is decoded as a run — one decision says whether any of its four turns significant,
    /// two more which is first, and that one's zero-coding decision is the run's.
    /// </summary>
    /// <remarks>2.5 - opj_t1_dec_clnpass_internal</remarks>
    void CleanupPass<TShape, TVsc>(int bpno)
        where TShape : struct, IBlockShape
        where TVsc : struct, IStripeCausal
    {
        const T1 NotVisited = ~(T1.PI_0 | T1.PI_1 | T1.PI_2 | T1.PI_3);
        int w = TShape.Width(_w), h = TShape.Height(_h), stride = w + 2;
        int one = 1 << bpno, oneplushalf = one | (one >> 1);
        var flags = this.flags;
        var data = _data;
        var zc = _mqc.lut_ctxno_zc_orient;
        var mqc = _mqc.Registers;

        int k, d = 0, f = stride + 1;
        for (k = 0; k < (h & ~3); k += 4, d += 3 * w, f += 2)
        {
            for (var i = 0; i < w; ++i, ++d, ++f)
            {
                var flag = flags[f];
                if (flag == 0)
                {
                                        if (!mqc.Decode((int)T1_CTXNO.AGG)) continue;

                                        var runlen = mqc.Decode((int)T1_CTXNO.UNI) ? 2 : 0;
                    runlen |= mqc.Decode((int)T1_CTXNO.UNI) ? 1 : 0;

                    var partial = true;
                    switch (runlen)
                    {
                        case 0:
                            CleanupStep(ref mqc, ref flag, flags, f, stride, data, d, oneplushalf, zc, 0, TVsc.On, false, true);
                            partial = false;
                            goto case 1;
                        case 1:
                            CleanupStep(ref mqc, ref flag, flags, f, stride, data, d + w, oneplushalf, zc, 1, false, false, partial);
                            partial = false;
                            goto case 2;
                        case 2:
                            CleanupStep(ref mqc, ref flag, flags, f, stride, data, d + 2 * w, oneplushalf, zc, 2, false, false, partial);
                            partial = false;
                            goto case 3;
                        case 3:
                            CleanupStep(ref mqc, ref flag, flags, f, stride, data, d + 3 * w, oneplushalf, zc, 3, false, false, partial);
                            break;
                    }
                }
                else
                {
                    CleanupStep(ref mqc, ref flag, flags, f, stride, data, d, oneplushalf, zc, 0, TVsc.On, true, false);
                    CleanupStep(ref mqc, ref flag, flags, f, stride, data, d + w, oneplushalf, zc, 1, false, true, false);
                    CleanupStep(ref mqc, ref flag, flags, f, stride, data, d + 2 * w, oneplushalf, zc, 2, false, true, false);
                    CleanupStep(ref mqc, ref flag, flags, f, stride, data, d + 3 * w, oneplushalf, zc, 3, false, true, false);
                }
                flags[f] = flag & NotVisited;
            }
        }

        if (k < h)
        {
            for (var i = 0; i < w; ++i, ++d, ++f)
            {
                for (var j = 0; j < h - k; ++j)
                    CleanupStep(ref mqc, ref flags[f], flags, f, stride, data, d + j * w, oneplushalf, zc, j, TVsc.On, true, false);
                flags[f] &= NotVisited;
            }
        }

        _mqc.Registers = mqc;
    }

    /// <summary>One sample of the significance pass.</summary>
    /// <remarks>2.5 - opj_t1_dec_sigpass_step_mqc_macro</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void SignificanceStep(ref MQRegisters mqc, ref T1 flag, T1[] flags, int f, int stride, int[] data, int at,
        int oneplushalf, int zc, int ci, bool vsc)
    {
        if ((flag & (T1)((uint)(T1.SIGMA_THIS | T1.PI_THIS) << (ci * 3))) != 0
            || (flag & (T1)((uint)T1.SIGMA_NEIGHBOURS << (ci * 3))) == 0)
            return;

                if (mqc.Decode(T1Luts.lut_ctxno_zc[zc + (int)(((uint)flag >> (ci * 3)) & (uint)T1.SIGMA_NEIGHBOURS)]))
        {
            var lu = T1Luts.Getctxtno_sc_or_spb_index(flag, flags[f - 1], flags[f + 1], ci);
                        var v = mqc.Decode(T1Luts.Getctxno_sc(lu)) ^ T1Luts.Getspb(lu);
            data[at] = v ? -oneplushalf : oneplushalf;
            UpdateNeighbours(ref flag, flags, f, ci, v ? 1u : 0u, stride, vsc);
        }
        flag |= (T1)((uint)T1.PI_THIS << (ci * 3));
    }

    /// <summary>One sample of the refinement pass.</summary>
    /// <remarks>2.5 - opj_t1_dec_refpass_step_mqc_macro</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void RefinementStep(ref MQRegisters mqc, ref T1 flag, int[] data, int at, int poshalf, int ci)
    {
        if ((flag & (T1)((uint)(T1.SIGMA_THIS | T1.PI_THIS) << (ci * 3))) != (T1)((uint)T1.SIGMA_THIS << (ci * 3)))
            return;

                var v = mqc.Decode((int)T1Luts.Getctxno_mag((T1)((uint)flag >> (ci * 3))));
        data[at] += (v ^ (data[at] < 0)) ? poshalf : -poshalf;
        flag |= (T1)((uint)T1.MU_THIS << (ci * 3));
    }

    /// <summary>
    /// One sample of the cleanup pass: unless <paramref name="partial"/>, which skips the zero-coding decision a
    /// run has already made.
    /// </summary>
    /// <remarks>2.5 - opj_t1_dec_clnpass_step_macro</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void CleanupStep(ref MQRegisters mqc, ref T1 flag, T1[] flags, int f, int stride, int[] data, int at,
        int oneplushalf, int zc, int ci, bool vsc, bool checkFlags, bool partial)
    {
        if (checkFlags && (flag & (T1)((uint)(T1.SIGMA_THIS | T1.PI_THIS) << (ci * 3))) != 0)
            return;

        if (!partial)
        {
                        if (!mqc.Decode(T1Luts.lut_ctxno_zc[zc + (int)(((uint)flag >> (ci * 3)) & (uint)T1.SIGMA_NEIGHBOURS)]))
                return;
        }

        var lu = T1Luts.Getctxtno_sc_or_spb_index(flag, flags[f - 1], flags[f + 1], ci);
                var v = mqc.Decode(T1Luts.Getctxno_sc(lu)) ^ T1Luts.Getspb(lu);
        data[at] = v ? -oneplushalf : oneplushalf;
        UpdateNeighbours(ref flag, flags, f, ci, v ? 1u : 0u, stride, vsc);
    }

    /// <summary>
    /// Marks a sample turned significant with sign <paramref name="s"/>: in its own flags, and in its neighbours'
    /// — east and west always, the row of stripes above for the first row unless stripe-causal, the row below for
    /// the last.
    /// </summary>
    /// <remarks>2.5 - opj_t1_update_flags_macro</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static void UpdateNeighbours(ref T1 flag, T1[] flags, int f, int ci, uint s, int stride, bool vsc)
    {
        // East
        flags[f - 1] |= (T1)((uint)T1.SIGMA_5 << (3 * ci));

        // Mark target as significant
        flag |= (T1)(((s << (int)T1.CHI_1_I) | (uint)T1.SIGMA_4) << (3 * ci));

        // West
        flags[f + 1] |= (T1)((uint)T1.SIGMA_3 << (3 * ci));

        // North-west, north, north-east
        if (ci == 0 && !vsc)
        {
            var north = f - stride;
            flags[north] |= (T1)(s << (int)T1.CHI_5_I) | T1.SIGMA_16;
            flags[north - 1] |= T1.SIGMA_17;
            flags[north + 1] |= T1.SIGMA_15;
        }

        // South-west, south, south-east
        if (ci == 3)
        {
            var south = f + stride;
            flags[south] |= (T1)(s << (int)T1.CHI_0_I) | T1.SIGMA_1;
            flags[south - 1] |= T1.SIGMA_2;
            flags[south + 1] |= T1.SIGMA_0;
        }
    }
}
