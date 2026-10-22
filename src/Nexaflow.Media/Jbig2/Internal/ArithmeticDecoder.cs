/*
 * Ported from Apache PDFBox JBIG2 ImageIO (pdfbox-jbig2) by way of PdfPig.Filters.Jbig2.PdfboxJbig2 0.1.16.2:
 * decoder/arithmetic/ArithmeticDecoder.java, ArithmeticIntegerDecoder.java and CX.java.
 * Licensed under the Apache License, Version 2.0; see the LICENSE and NOTICE files beside this folder.
 * Modified for Nexaflow: the decoder reads its bytes by index rather than seeking a stream, and past the end of its
 * data reads 0xFF, so data cut short decodes on to the end of its region.
 */

namespace Nexaflow.Media.Jbig2.Internal;

/// <summary>
/// The probability estimate and the more probable symbol of each context, Annex E: one byte a context, the state's
/// index in Table E.1 above the more probable symbol.
/// </summary>
internal sealed class CX(int size, int index)
{
    public byte[] States { get; } = new byte[size];

    public int Index { get; set; } = index;

    public int Size => States.Length;

    public CX Copy()
    {
        var result = new CX(States.Length, Index);
        States.CopyTo(result.States, 0);
        return result;
    }
}
/// <summary>
/// The MQ decoder of Annex E, reading the data from where it is made. Past the end of its data it reads 0xFF — the
/// marker that ends arithmetically coded data — so a segment cut short decodes on to the end of its region, the rest
/// of it whatever those bytes decode to.
/// </summary>
internal sealed class ArithmeticDecoder
{
    // Qe, NMPS, NLPS and SWITCH of each state, Table E.1.
    private static readonly int[] Qe =
    [
        0x5601, 0x3401, 0x1801, 0x0AC1, 0x0521, 0x0221, 0x5601, 0x5401, 0x4801, 0x3801, 0x3001, 0x2401, 0x1C01, 0x1601,
        0x5601, 0x5401, 0x5101, 0x4801, 0x3801, 0x3401, 0x3001, 0x2801, 0x2401, 0x2201, 0x1C01, 0x1801, 0x1601, 0x1401,
        0x1201, 0x1101, 0x0AC1, 0x09C1, 0x08A1, 0x0521, 0x0441, 0x02A1, 0x0221, 0x0141, 0x0111, 0x0085, 0x0049, 0x0025,
        0x0015, 0x0009, 0x0005, 0x0001, 0x5601,
    ];
    private static readonly byte[] Nmps =
    [
        1, 2, 3, 4, 5, 38, 7, 8, 9, 10, 11, 12, 13, 29, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31,
        32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 45, 46,
    ];
    private static readonly byte[] Nlps =
    [
        1, 6, 9, 12, 29, 33, 6, 14, 14, 14, 17, 18, 20, 21, 14, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 23, 24, 25, 26, 27,
        28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 46,
    ];
    private static readonly byte[] Switch =
    [
        1, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0,
    ];

    private readonly byte[] _bytes;
    private readonly int _end;
    private int _bp;
    private uint _c;
    private int _a;
    private int _ct;

    /// <summary>Starts decoding at the reader's place, reading to the end of the reader's bytes.</summary>
    public ArithmeticDecoder(Jbig2Reader reader)
    {
        _bytes = reader.Bytes;
        _end = reader.Start + reader.Length;
        _bp = reader.Start + (int)Math.Min(reader.Position, reader.Length);

        // INITDEC, E.3.5.
        _c = (uint)Byte(_bp) << 16;
        ByteIn();
        _c <<= 7;
        _ct -= 7;
        _a = 0x8000;
    }

    private int Byte(int at) => at < _end ? _bytes[at] : 0xFF;

    // The Qe of each context state, and the state that follows a more and a less probable symbol, the SWITCH of
    // Table E.1 applied to the second: indexed by the state's index above its more probable symbol.
    private static readonly int[] QeOfState = [.. Enumerable.Range(0, 94).Select(s => Qe[s >> 1])];
    private static readonly byte[] AfterMps = [.. Enumerable.Range(0, 94).Select(s => (byte)(Nmps[s >> 1] << 1 | s & 1))];
    private static readonly byte[] AfterLps = [.. Enumerable.Range(0, 94).Select(s => (byte)(Nlps[s >> 1] << 1 | (s & 1 ^ Switch[s >> 1])))];

    /// <summary>DECODE, E.3.2, with MPS_EXCHANGE and LPS_EXCHANGE: the next decision in the context <paramref name="cx"/> points at.</summary>
    public int Decode(CX cx)
    {
        ref var state = ref cx.States[cx.Index];
        int s = state;
        var mps = s & 1;
        var qe = QeOfState[s];
        _a -= qe;

        int d;
        if (_c >> 16 < qe)
        {
            // LPS_EXCHANGE: the interval left is the one the less probable symbol had, whichever it now is.
            if (_a < qe)
            {
                d = mps;
                state = AfterMps[s];
            }
            else
            {
                d = mps ^ 1;
                state = AfterLps[s];
            }
            _a = qe;
        }
        else
        {
            _c -= (uint)qe << 16;
            if ((_a & 0x8000) != 0) return mps;

            // MPS_EXCHANGE
            if (_a < qe)
            {
                d = mps ^ 1;
                state = AfterLps[s];
            }
            else
            {
                d = mps;
                state = AfterMps[s];
            }
        }
        Renormalize();
        return d;
    }
    /// <summary>BYTEIN, E.3.4: a marker — 0xFF and a byte above 0x8F — is never read past.</summary>
    private void ByteIn()
    {
        if (Byte(_bp) == 0xFF)
        {
            var b1 = Byte(_bp + 1);
            if (b1 > 0x8F)
            {
                _c += 0xFF00;
                _ct = 8;
            }
            else
            {
                _bp++;
                _c += (uint)b1 << 9;
                _ct = 7;
            }
        }
        else
        {
            _bp++;
            _c += (uint)Byte(_bp) << 8;
            _ct = 8;
        }
    }

    private void Renormalize()
    {
        do
        {
            if (_ct == 0) ByteIn();
            _a <<= 1;
            _c <<= 1;
            _ct--;
        }
        while ((_a & 0x8000) == 0);
    }

}

/// <summary>The integer and symbol-identifier decoding procedures of Annex A, over an <see cref="ArithmeticDecoder"/>.</summary>
internal sealed class ArithmeticIntegerDecoder(ArithmeticDecoder decoder)
{
    /// <summary>OOB, as the integer decoder returns it.</summary>
    public const long OutOfBand = long.MaxValue;

    /// <summary>A.2: a signed integer, or <see cref="OutOfBand"/>.</summary>
    public long Decode(CX cxIAx)
    {
        // PREV holds the eight most recently decoded bits after a leading 1, A.2.
        var prev = 1;
        int bitsToRead, offset;

        cxIAx.Index = prev & 0x1FF;
        var s = decoder.Decode(cxIAx);
        prev = SetPrev(prev, s);

        if (DecodeBit(cxIAx, ref prev) == 0) (bitsToRead, offset) = (2, 0);
        else if (DecodeBit(cxIAx, ref prev) == 0) (bitsToRead, offset) = (4, 4);
        else if (DecodeBit(cxIAx, ref prev) == 0) (bitsToRead, offset) = (6, 20);
        else if (DecodeBit(cxIAx, ref prev) == 0) (bitsToRead, offset) = (8, 84);
        else if (DecodeBit(cxIAx, ref prev) == 0) (bitsToRead, offset) = (12, 340);
        else (bitsToRead, offset) = (32, 4436);

        long v = 0;
        for (var i = 0; i < bitsToRead; i++) v = v << 1 | (long)DecodeBit(cxIAx, ref prev);
        v += offset;

        if (s == 0) return v;
        return v > 0 ? -v : OutOfBand;
    }

    /// <summary>A.3: a symbol identifier of <paramref name="symCodeLen"/> bits.</summary>
    public int DecodeIaid(CX cxIaid, int symCodeLen)
    {
        // Only the low SBSYMCODELEN bits of PREV choose the context; the leading 1 counts the bits read.
        long prev = 1;
        var mask = (1L << symCodeLen) - 1;
        for (var i = 0; i < symCodeLen; i++)
        {
            cxIaid.Index = (int)(prev & mask);
            prev = prev << 1 | (long)decoder.Decode(cxIaid);
        }
        return (int)(prev - (1L << symCodeLen));
    }

    private int DecodeBit(CX cx, ref int prev)
    {
        cx.Index = prev & 0x1FF;
        var d = decoder.Decode(cx);
        prev = SetPrev(prev, d);
        return d;
    }

    private static int SetPrev(int prev, int bit) =>
        prev < 256 ? (prev << 1 | bit) & 0x1FF : ((prev << 1 | bit) & 511 | 256) & 0x1FF;
}
