/*
 * Ported from Apache PDFBox JBIG2 ImageIO (pdfbox-jbig2) by way of PdfPig.Filters.Jbig2.PdfboxJbig2 0.1.16.2:
 * segments/GenericRegion.java.
 * Licensed under the Apache License, Version 2.0; see the LICENSE and NOTICE files beside this folder.
 * Modified for Nexaflow: read from a span reader, its bitmaps charged to the decode's memory limit.
 */

namespace Nexaflow.Media.Jbig2.Internal;

/// <summary>
/// A generic region, 6.2 and 7.4.6: arithmetically coded with one of four templates and its adaptive pixels, or MMR
/// coded. Symbol dictionaries, pattern dictionaries and halftone regions decode their bitmaps through one as well.
/// </summary>
internal sealed class GenericRegion : IRegion
{
    private Jbig2Reader _reader = null!;
    private Jbig2DecodeContext _context = null!;
    private long _dataOffset;
    private long _dataLength;

    // Generic region segment flags, 7.4.6.2
    public bool UseExtTemplates { get; private set; }

    public bool IsTPGDon { get; private set; }

    public byte GbTemplate { get; private set; }

    public bool IsMMREncoded { get; private set; }

    // Generic region segment AT flags, 7.4.6.3
    public short[]? GbAtX { get; private set; }

    public short[]? GbAtY { get; private set; }

    private bool[] _gbAtOverride = [];

    // Whether AT pixels are away from their nominal places and have to be read where they are.
    private bool _isOverride;

    private Jbig2Bitmap _regionBitmap = null!;
    private bool _decoded;
    private ArithmeticDecoder? _arithDecoder;
    private MmrDecompressor? _mmrDecompressor;
    private CX _cx = null!;
    private bool _useSkip;
    private Jbig2Bitmap? _hSkip;

    // Region segment information field, 7.4.1
    public RegionSegmentInformation RegionInfo { get; private set; } = new();

    public GenericRegion()
    {
    }

    public GenericRegion(Jbig2Reader reader, Jbig2DecodeContext context)
    {
        _reader = reader;
        _context = context;
    }

    public void Init(SegmentHeader header, Jbig2Reader reader)
    {
        _reader = reader;
        _context = header.Context;
        RegionInfo = new RegionSegmentInformation();
        ParseHeader();
    }

    private void ParseHeader()
    {
        RegionInfo.ParseHeader(_reader);

        _reader.ReadBits(3); // Bits 5-7: reserved
        UseExtTemplates = _reader.ReadBit() == 1;
        IsTPGDon = _reader.ReadBit() == 1;
        GbTemplate = (byte)_reader.ReadBits(2);
        IsMMREncoded = _reader.ReadBit() == 1;

        if (!IsMMREncoded) ReadGbAtPixels(GbTemplate == 0 ? UseExtTemplates ? 12 : 4 : 1);

        _dataOffset = _reader.Position;
        _dataLength = _reader.Length - _dataOffset;
    }

    private void ReadGbAtPixels(int amountOfGbAt)
    {
        GbAtX = new short[amountOfGbAt];
        GbAtY = new short[amountOfGbAt];
        for (var i = 0; i < amountOfGbAt; i++)
        {
            GbAtX[i] = (sbyte)_reader.ReadByte();
            GbAtY[i] = (sbyte)_reader.ReadByte();
        }
    }

    public Jbig2Bitmap GetRegionBitmap()
    {
        if (_decoded) return _regionBitmap;
        _context.Work(Jbig2Bitmaps.Work(RegionInfo.BitmapWidth, RegionInfo.BitmapHeight));

        if (IsMMREncoded)
        {
            // 6.2.6: a halftone region's gray-scale planes follow one another in one MMR stream.
            _mmrDecompressor ??= new MmrDecompressor(RegionInfo.BitmapWidth, RegionInfo.BitmapHeight,
                _reader.Slice(_dataOffset, _dataLength), _context);
            _regionBitmap = _mmrDecompressor.Uncompress();
        }
        else
        {
            // 6.2.5.7
            UpdateOverrideFlags();
            var ltp = 0;
            _arithDecoder ??= new ArithmeticDecoder(_reader);
            _cx ??= _context.NewCx(65536);
            _regionBitmap = new Jbig2Bitmap(RegionInfo.BitmapWidth, RegionInfo.BitmapHeight, _context);
            var paddedWidth = _regionBitmap.Width + 7 & -8;

            for (var line = 0; line < _regionBitmap.Height; line++)
            {
                // 6.2.5.7 3 b)
                if (IsTPGDon) ltp ^= DecodeSLTP();

                // 6.2.5.7 3 c) - d)
                if (ltp == 1)
                {
                    if (line > 0) CopyLineAbove(line);
                }
                else
                {
                    DecodeLine(line, _regionBitmap.Width, _regionBitmap.RowStride, paddedWidth);
                }
            }
        }

        _decoded = true;
        return _regionBitmap;
    }

    private int DecodeSLTP()
    {
        _cx.Index = GbTemplate switch
        {
            0 => 0x9b25,
            1 => 0x795,
            2 => 0xe5,
            _ => 0x195,
        };
        return _arithDecoder!.Decode(_cx);
    }

    private void DecodeLine(int lineNumber, int width, int rowStride, int paddedWidth)
    {
        // A symbol may be no pixels wide; its rows hold nothing to decode or to read.
        if (width == 0) return;

        var byteIndex = _regionBitmap.GetByteIndex(0, lineNumber);
        var idx = byteIndex - rowStride;
        switch (GbTemplate)
        {
            case 0:
                if (!UseExtTemplates) DecodeTemplate0a(lineNumber, width, rowStride, paddedWidth, byteIndex, idx);
                else DecodeTemplate0b(lineNumber, width, rowStride, paddedWidth, byteIndex, idx);
                break;
            case 1:
                DecodeTemplate1(lineNumber, width, rowStride, paddedWidth, byteIndex, idx);
                break;
            case 2:
                DecodeTemplate2(lineNumber, width, rowStride, paddedWidth, byteIndex, idx);
                break;
            case 3:
                DecodeTemplate3(lineNumber, width, rowStride, paddedWidth, byteIndex, idx);
                break;
        }
    }

    private void CopyLineAbove(int lineNumber)
    {
        var rowStride = _regionBitmap.RowStride;
        var rows = _regionBitmap.ByteArray.AsSpan();
        var target = lineNumber * rowStride;
        rows.Slice(target - rowStride, rowStride).CopyTo(rows.Slice(target, rowStride));
    }
    private void DecodeTemplate0a(int lineNumber, int width, int rowStride,
            int paddedWidth, int byteIndex, int idx)
    {
        int context;
        int overriddenContext;

        int line1 = 0;
        int line2 = 0;

        if (lineNumber >= 1)
        {
            line1 = _regionBitmap.GetByteAsInteger(idx);
        }

        if (lineNumber >= 2)
        {
            line2 = _regionBitmap.GetByteAsInteger(idx - rowStride) << 6;
        }

        context = line1 & 0xf0 | line2 & 0x3800;

        int nextByte;
        for (int x = 0; x < paddedWidth; x = nextByte)
        {
            // 6.2.5.7 3d
            byte result = 0;
            nextByte = x + 8;
            int minorWidth = width - x > 8 ? 8 : width - x;

            if (lineNumber > 0)
            {
                line1 = line1 << 8
                        | (nextByte < width ? _regionBitmap.GetByteAsInteger(idx + 1) : 0);
            }

            if (lineNumber > 1)
            {
                line2 = line2 << 8 | (nextByte < width
                        ? _regionBitmap.GetByteAsInteger(idx - rowStride + 1) << 6 : 0);
            }

            for (int minorX = 0; minorX < minorWidth; minorX++)
            {
                int toShift = 7 - minorX;
                if (_isOverride)
                {
                    overriddenContext = OverrideAtTemplate0a(context, x + minorX, lineNumber,
                            result, minorX, toShift);
                    _cx.Index = overriddenContext;
                }
                else
                {
                    _cx.Index = context;
                }

                int bit;
                if (_useSkip && _hSkip!.GetPixel(x + minorX, lineNumber) == 1)
                {
                    bit = 0;
                }
                else
                {
                    bit = _arithDecoder!.Decode(_cx);
                }

                result = (byte)(result | bit << toShift);

                context = (context & 0x7bf7) << 1 | bit | line1 >> toShift & 0x10
                        | line2 >> toShift & 0x800;
            }

            _regionBitmap.SetByte(byteIndex++, result);
            idx++;
        }
    }

    private void DecodeTemplate0b(int lineNumber, int width, int rowStride,
            int paddedWidth, int byteIndex, int idx)
    {
        int context;
        int overriddenContext;

        int line1 = 0;
        int line2 = 0;

        if (lineNumber >= 1)
        {
            line1 = _regionBitmap.GetByteAsInteger(idx);
        }

        if (lineNumber >= 2)
        {
            line2 = _regionBitmap.GetByteAsInteger(idx - rowStride) << 6;
        }

        context = line1 & 0xf0 | line2 & 0x3800;

        int nextByte;
        for (int x = 0; x < paddedWidth; x = nextByte)
        {
            // 6.2.5.7 3d
            byte result = 0;
            nextByte = x + 8;
            int minorWidth = width - x > 8 ? 8 : width - x;

            if (lineNumber > 0)
            {
                line1 = line1 << 8
                        | (nextByte < width ? _regionBitmap.GetByteAsInteger(idx + 1) : 0);
            }

            if (lineNumber > 1)
            {
                line2 = line2 << 8 | (nextByte < width
                        ? _regionBitmap.GetByteAsInteger(idx - rowStride + 1) << 6 : 0);
            }

            for (int minorX = 0; minorX < minorWidth; minorX++)
            {
                int toShift = 7 - minorX;
                if (_isOverride)
                {
                    overriddenContext = OverrideAtTemplate0b(context, x + minorX, lineNumber,
                            result, minorX, toShift);
                    _cx.Index = overriddenContext;
                }
                else
                {
                    _cx.Index = context;
                }

                int bit;
                if (_useSkip && _hSkip!.GetPixel(x + minorX, lineNumber) == 1)
                {
                    bit = 0;
                }
                else
                {
                    bit = _arithDecoder!.Decode(_cx);
                }

                result = (byte)(result | bit << toShift);

                context = (context & 0x7bf7) << 1 | bit | line1 >> toShift & 0x10
                        | line2 >> toShift & 0x800;
            }

            _regionBitmap.SetByte(byteIndex++, result);
            idx++;
        }
    }

    private void DecodeTemplate1(int lineNumber, int width, int rowStride,
            int paddedWidth, int byteIndex, int idx)
    {
        int context;
        int overriddenContext;

        int line1 = 0;
        int line2 = 0;

        if (lineNumber >= 1)
        {
            line1 = _regionBitmap.GetByteAsInteger(idx);
        }

        if (lineNumber >= 2)
        {
            line2 = _regionBitmap.GetByteAsInteger(idx - rowStride) << 5;
        }

        context = line1 >> 1 & 0x1f8 | line2 >> 1 & 0x1e00;

        int nextByte;
        for (int x = 0; x < paddedWidth; x = nextByte)
        {
            // 6.2.5.7 3d
            byte result = 0;
            nextByte = x + 8;
            int minorWidth = width - x > 8 ? 8 : width - x;

            if (lineNumber >= 1)
            {
                line1 = line1 << 8
                        | (nextByte < width ? _regionBitmap.GetByteAsInteger(idx + 1) : 0);
            }

            if (lineNumber >= 2)
            {
                line2 = line2 << 8 | (nextByte < width
                        ? _regionBitmap.GetByteAsInteger(idx - rowStride + 1) << 5 : 0);
            }

            for (int minorX = 0; minorX < minorWidth; minorX++)
            {
                if (_isOverride)
                {
                    overriddenContext = OverrideAtTemplate1(context, x + minorX, lineNumber, result,
                            minorX);
                    _cx.Index = overriddenContext;
                }
                else
                {
                    _cx.Index = context;
                }

                int bit;
                if (_useSkip && _hSkip!.GetPixel(x + minorX, lineNumber) == 1)
                {
                    bit = 0;
                }
                else
                {
                    bit = _arithDecoder!.Decode(_cx);
                }

                result = (byte)(result | bit << 7 - minorX);

                int toShift = 8 - minorX;
                context = (context & 0xefb) << 1 | bit | line1 >> toShift & 0x8
                        | line2 >> toShift & 0x200;
            }

            _regionBitmap.SetByte(byteIndex++, result);
            idx++;
        }
    }

    private void DecodeTemplate2(int lineNumber, int width, int rowStride,
            int paddedWidth, int byteIndex, int idx)
    {
        int context;
        int overriddenContext;

        int line1 = 0;
        int line2 = 0;

        if (lineNumber >= 1)
        {
            line1 = _regionBitmap.GetByteAsInteger(idx);
        }

        if (lineNumber >= 2)
        {
            line2 = _regionBitmap.GetByteAsInteger(idx - rowStride) << 4;
        }

        context = line1 >> 3 & 0x7c | line2 >> 3 & 0x380;

        int nextByte;
        for (int x = 0; x < paddedWidth; x = nextByte)
        {
            // 6.2.5.7 3d
            byte result = 0;
            nextByte = x + 8;
            int minorWidth = width - x > 8 ? 8 : width - x;

            if (lineNumber >= 1)
            {
                line1 = line1 << 8
                        | (nextByte < width ? _regionBitmap.GetByteAsInteger(idx + 1) : 0);
            }

            if (lineNumber >= 2)
            {
                line2 = line2 << 8 | (nextByte < width
                        ? _regionBitmap.GetByteAsInteger(idx - rowStride + 1) << 4 : 0);
            }

            for (int minorX = 0; minorX < minorWidth; minorX++)
            {
                if (_isOverride)
                {
                    overriddenContext = OverrideAtTemplate2(context, x + minorX, lineNumber, result,
                            minorX);
                    _cx.Index = overriddenContext;
                }
                else
                {
                    _cx.Index = context;
                }

                int bit;
                if (_useSkip && _hSkip!.GetPixel(x + minorX, lineNumber) == 1)
                {
                    bit = 0;
                }
                else
                {
                    bit = _arithDecoder!.Decode(_cx);
                }

                result = (byte)(result | bit << 7 - minorX);

                int toShift = 10 - minorX;
                context = (context & 0x1bd) << 1 | bit | line1 >> toShift & 0x4
                        | line2 >> toShift & 0x80;
            }

            _regionBitmap.SetByte(byteIndex++, result);
            idx++;
        }
    }

    private void DecodeTemplate3(int lineNumber, int width, int rowStride,
            int paddedWidth, int byteIndex, int idx)
    {
        int context;
        int overriddenContext;

        int line1 = 0;

        if (lineNumber >= 1)
        {
            line1 = _regionBitmap.GetByteAsInteger(idx);
        }

        context = line1 >> 1 & 0x70;

        int nextByte;
        for (int x = 0; x < paddedWidth; x = nextByte)
        {
            // 6.2.5.7 3d
            byte result = 0;
            nextByte = x + 8;
            int minorWidth = width - x > 8 ? 8 : width - x;

            if (lineNumber >= 1)
            {
                line1 = line1 << 8
                        | (nextByte < width ? _regionBitmap.GetByteAsInteger(idx + 1) : 0);
            }

            for (int minorX = 0; minorX < minorWidth; minorX++)
            {
                if (_isOverride)
                {
                    overriddenContext = OverrideAtTemplate3(context, x + minorX, lineNumber, result,
                            minorX);
                    _cx.Index = overriddenContext;
                }
                else
                {
                    _cx.Index = context;
                }

                int bit;
                if (_useSkip && _hSkip!.GetPixel(x + minorX, lineNumber) == 1)
                {
                    bit = 0;
                }
                else
                {
                    bit = _arithDecoder!.Decode(_cx);
                }

                result = (byte)(result | bit << 7 - minorX);
                context = (context & 0x1f7) << 1 | bit | line1 >> 8 - minorX & 0x010;
            }

            _regionBitmap.SetByte(byteIndex++, result);
            idx++;
        }
    }

    private void UpdateOverrideFlags()
    {
        if (GbAtX is null || GbAtY is null)
        {
            return;
        }

        if (GbAtX.Length != GbAtY.Length)
        {
            return;
        }

        _gbAtOverride = new bool[GbAtX.Length];

        switch (GbTemplate)
        {
            case 0:
                if (!UseExtTemplates)
                {
                    if (GbAtX![0] != 3 || GbAtY![0] != -1)
                    {
                        SetOverrideFlag(0);
                    }

                    if (GbAtX![1] != -3 || GbAtY![1] != -1)
                    {
                        SetOverrideFlag(1);
                    }

                    if (GbAtX![2] != 2 || GbAtY![2] != -2)
                    {
                        SetOverrideFlag(2);
                    }

                    if (GbAtX![3] != -2 || GbAtY![3] != -2)
                    {
                        SetOverrideFlag(3);
                    }
                }
                else
                {
                    if (GbAtX![0] != -2 || GbAtY![0] != 0)
                    {
                        SetOverrideFlag(0);
                    }

                    if (GbAtX![1] != 0 || GbAtY![1] != -2)
                    {
                        SetOverrideFlag(1);
                    }

                    if (GbAtX![2] != -2 || GbAtY![2] != -1)
                    {
                        SetOverrideFlag(2);
                    }

                    if (GbAtX![3] != -1 || GbAtY![3] != -2)
                    {
                        SetOverrideFlag(3);
                    }

                    if (GbAtX![4] != 1 || GbAtY![4] != -2)
                    {
                        SetOverrideFlag(4);
                    }

                    if (GbAtX![5] != 2 || GbAtY![5] != -1)
                    {
                        SetOverrideFlag(5);
                    }

                    if (GbAtX![6] != -3 || GbAtY![6] != 0)
                    {
                        SetOverrideFlag(6);
                    }

                    if (GbAtX![7] != -4 || GbAtY![7] != 0)
                    {
                        SetOverrideFlag(7);
                    }

                    if (GbAtX![8] != 2 || GbAtY![8] != -2)
                    {
                        SetOverrideFlag(8);
                    }

                    if (GbAtX![9] != 3 || GbAtY![9] != -1)
                    {
                        SetOverrideFlag(9);
                    }

                    if (GbAtX![10] != -2 || GbAtY![10] != -2)
                    {
                        SetOverrideFlag(10);
                    }

                    if (GbAtX![11] != -3 || GbAtY![11] != -1)
                    {
                        SetOverrideFlag(11);
                    }
                }
                break;

            case 1:
                if (GbAtX![0] != 3 || GbAtY![0] != -1)
                {
                    SetOverrideFlag(0);
                }

                break;

            case 2:
                if (GbAtX![0] != 2 || GbAtY![0] != -1)
                {
                    SetOverrideFlag(0);
                }

                break;

            case 3:
                if (GbAtX![0] != 2 || GbAtY![0] != -1)
                {
                    SetOverrideFlag(0);
                }

                break;
        }
    }

    private void SetOverrideFlag(int index)
    {
        _gbAtOverride[index] = true;
        _isOverride = true;
    }

    private int OverrideAtTemplate0a(int context, int x, int y, int result,
            int minorX, int toShift)
    {
        if (_gbAtOverride[0])
        {
            context &= 0xffef;
            if (GbAtY![0] == 0 && GbAtX![0] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![0] & 0x1) << 4;
            }
            else
            {
                context |= GetPixel(x + GbAtX![0], y + GbAtY![0]) << 4;
            }
        }

        if (_gbAtOverride[1])
        {
            context &= 0xfbff;
            if (GbAtY![1] == 0 && GbAtX![1] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![1] & 0x1) << 10;
            }
            else
            {
                context |= GetPixel(x + GbAtX![1], y + GbAtY![1]) << 10;
            }
        }

        if (_gbAtOverride[2])
        {
            context &= 0xf7ff;
            if (GbAtY![2] == 0 && GbAtX![2] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![2] & 0x1) << 11;
            }
            else
            {
                context |= GetPixel(x + GbAtX![2], y + GbAtY![2]) << 11;
            }
        }

        if (_gbAtOverride[3])
        {
            context &= 0x7fff;
            if (GbAtY![3] == 0 && GbAtX![3] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![3] & 0x1) << 15;
            }
            else
            {
                context |= GetPixel(x + GbAtX![3], y + GbAtY![3]) << 15;
            }
        }
        return context;
    }

    private int OverrideAtTemplate0b(int context, int x, int y, int result,
            int minorX, int toShift)
    {
        if (_gbAtOverride[0])
        {
            context &= 0xfffd;
            if (GbAtY![0] == 0 && GbAtX![0] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![0] & 0x1) << 1;
            }
            else
            {
                context |= GetPixel(x + GbAtX![0], y + GbAtY![0]) << 1;
            }
        }

        if (_gbAtOverride[1])
        {
            context &= 0xdfff;
            if (GbAtY![1] == 0 && GbAtX![1] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![1] & 0x1) << 13;
            }
            else
            {
                context |= GetPixel(x + GbAtX![1], y + GbAtY![1]) << 13;
            }
        }

        if (_gbAtOverride[2])
        {
            context &= 0xfdff;
            if (GbAtY![2] == 0 && GbAtX![2] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![2] & 0x1) << 9;
            }
            else
            {
                context |= GetPixel(x + GbAtX![2], y + GbAtY![2]) << 9;
            }
        }

        if (_gbAtOverride[3])
        {
            context &= 0xbfff;
            if (GbAtY![3] == 0 && GbAtX![3] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![3] & 0x1) << 14;
            }
            else
            {
                context |= GetPixel(x + GbAtX![3], y + GbAtY![3]) << 14;
            }
        }

        if (_gbAtOverride[4])
        {
            context &= 0xefff;
            if (GbAtY![4] == 0 && GbAtX![4] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![4] & 0x1) << 12;
            }
            else
            {
                context |= GetPixel(x + GbAtX![4], y + GbAtY![4]) << 12;
            }
        }

        if (_gbAtOverride[5])
        {
            context &= 0xffdf;
            if (GbAtY![5] == 0 && GbAtX![5] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![5] & 0x1) << 5;
            }
            else
            {
                context |= GetPixel(x + GbAtX![5], y + GbAtY![5]) << 5;
            }
        }

        if (_gbAtOverride[6])
        {
            context &= 0xfffb;
            if (GbAtY![6] == 0 && GbAtX![6] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![6] & 0x1) << 2;
            }
            else
            {
                context |= GetPixel(x + GbAtX![6], y + GbAtY![6]) << 2;
            }
        }

        if (_gbAtOverride[7])
        {
            context &= 0xfff7;
            if (GbAtY![7] == 0 && GbAtX![7] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![7] & 0x1) << 3;
            }
            else
            {
                context |= GetPixel(x + GbAtX![7], y + GbAtY![7]) << 3;
            }
        }

        if (_gbAtOverride[8])
        {
            context &= 0xf7ff;
            if (GbAtY![8] == 0 && GbAtX![8] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![8] & 0x1) << 11;
            }
            else
            {
                context |= GetPixel(x + GbAtX![8], y + GbAtY![8]) << 11;
            }
        }

        if (_gbAtOverride[9])
        {
            context &= 0xffef;
            if (GbAtY![9] == 0 && GbAtX![9] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![9] & 0x1) << 4;
            }
            else
            {
                context |= GetPixel(x + GbAtX![9], y + GbAtY![9]) << 4;
            }
        }

        if (_gbAtOverride[10])
        {
            context &= 0x7fff;
            if (GbAtY![10] == 0 && GbAtX![10] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![10] & 0x1) << 15;
            }
            else
            {
                context |= GetPixel(x + GbAtX![10], y + GbAtY![10]) << 15;
            }
        }

        if (_gbAtOverride[11])
        {
            context &= 0xfdff;
            if (GbAtY![11] == 0 && GbAtX![11] >= -minorX)
            {
                context |= (result >> toShift - GbAtX![11] & 0x1) << 10;
            }
            else
            {
                context |= GetPixel(x + GbAtX![11], y + GbAtY![11]) << 10;
            }
        }

        return context;
    }

    private int OverrideAtTemplate1(int context, int x, int y, int result, int minorX)
    {
        context &= 0x1ff7;
        if (GbAtY![0] == 0 && GbAtX![0] >= -minorX)
        {
            return context | (result >> 7 - (minorX + GbAtX![0]) & 0x1) << 3;
        }

        return context | GetPixel(x + GbAtX![0], y + GbAtY![0]) << 3;
    }

    private int OverrideAtTemplate2(int context, int x, int y, int result, int minorX)
    {
        context &= 0x3fb;
        if (GbAtY![0] == 0 && GbAtX![0] >= -minorX)
        {
            return context | (result >> 7 - (minorX + GbAtX![0]) & 0x1) << 2;
        }

        return context | GetPixel(x + GbAtX![0], y + GbAtY![0]) << 2;
    }

    private int OverrideAtTemplate3(int context, int x, int y, int result, int minorX)
    {
        context &= 0x3ef;
        if (GbAtY![0] == 0 && GbAtX![0] >= -minorX)
        {
            return context | (result >> 7 - (minorX + GbAtX![0]) & 0x1) << 4;
        }

        return context | GetPixel(x + GbAtX![0], y + GbAtY![0]) << 4;
    }

    private byte GetPixel(int x, int y) => _regionBitmap.GetPixelOrZero(x, y);

    /// <summary>An MMR coded bitmap of a symbol dictionary or a pattern dictionary: <paramref name="dataLength"/> bytes from <paramref name="dataOffset"/>.</summary>
    internal void SetParameters(bool isMMREncoded, long dataOffset, long dataLength, int gbh, int gbw)
    {
        IsMMREncoded = isMMREncoded;
        _dataOffset = dataOffset;
        _dataLength = dataLength;
        RegionInfo.BitmapHeight = gbh;
        RegionInfo.BitmapWidth = gbw;
        _mmrDecompressor = null;
        ResetBitmap();
    }

    /// <summary>A symbol coded directly, Table 16: the dictionary's own contexts and arithmetic decoder.</summary>
    internal void SetParameters(bool isMMREncoded, byte sdTemplate, bool isTPGDon, bool useSkip, short[]? sdATX,
        short[]? sdATY, int symWidth, int hcHeight, CX? cx, ArithmeticDecoder? arithmeticDecoder)
    {
        IsMMREncoded = isMMREncoded;
        GbTemplate = sdTemplate;
        IsTPGDon = isTPGDon;
        GbAtX = sdATX;
        GbAtY = sdATY;
        RegionInfo.BitmapWidth = symWidth;
        RegionInfo.BitmapHeight = hcHeight;
        if (cx != null) _cx = cx;
        if (arithmeticDecoder != null) _arithDecoder = arithmeticDecoder;
        _useSkip = useSkip;
        _mmrDecompressor = null;
        ResetBitmap();
    }

    /// <summary>The collective bitmap of a pattern dictionary, or a gray-scale plane of a halftone region.</summary>
    internal void SetParameters(bool isMMREncoded, long dataOffset, long dataLength, int gbh, int gbw, byte gbTemplate,
        bool isTPGDon, bool useSkip, Jbig2Bitmap? hSkip, short[]? gbAtX, short[]? gbAtY)
    {
        _dataOffset = dataOffset;
        _dataLength = dataLength;
        RegionInfo = new RegionSegmentInformation(gbw, gbh);
        GbTemplate = gbTemplate;
        IsMMREncoded = isMMREncoded;
        IsTPGDon = isTPGDon;
        GbAtX = gbAtX;
        GbAtY = gbAtY;
        _useSkip = useSkip;
        _hSkip = hSkip;
    }

    internal void ResetBitmap() => _decoded = false;
}
