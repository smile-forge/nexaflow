/*
 * Ported from Apache PDFBox JBIG2 ImageIO (pdfbox-jbig2) by way of PdfPig.Filters.Jbig2.PdfboxJbig2 0.1.16.2:
 * segments/HalftoneRegion.java and segments/PatternDictionary.java.
 * Licensed under the Apache License, Version 2.0; see the LICENSE and NOTICE files beside this folder.
 * Modified for Nexaflow: grids, planes and patterns are sized from checked values and charged to the decode's memory
 * limit; a gray-scale value past the last pattern draws the last pattern, with a warning.
 */

namespace Nexaflow.Media.Jbig2.Internal;

/// <summary>A halftone region, 6.6 and 7.4.5: a grid of gray-scale values, each drawn as a pattern from a pattern dictionary.</summary>
internal sealed class HalftoneRegion : IRegion
{
    private Jbig2Reader _reader = null!;
    private Jbig2DecodeContext _context = null!;
    private SegmentHeader _segmentHeader = null!;
    private long _dataOffset;
    private long _dataLength;
    private Jbig2Bitmap? _halftoneRegionBitmap;
    private List<Jbig2Bitmap>? _patterns;

    // Region segment information field, 7.4.1
    public RegionSegmentInformation RegionInfo { get; private set; } = new();

    // Halftone region segment flags, 7.4.5.1.1
    private int _hDefaultPixel;
    private CombinationOperator _hCombinationOperator;
    private bool _hSkipEnabled;
    private byte _hTemplate;
    private bool _isMmrEncoded;

    // Halftone grid position and size, 7.4.5.1.2, and vector, 7.4.5.1.3
    private int _hGridWidth;
    private int _hGridHeight;
    private int _hGridX;
    private int _hGridY;
    private int _hRegionX;
    private int _hRegionY;

    public void Init(SegmentHeader header, Jbig2Reader reader)
    {
        _segmentHeader = header;
        _reader = reader;
        _context = header.Context;
        RegionInfo = new RegionSegmentInformation();
        RegionInfo.ParseHeader(reader);

        _hDefaultPixel = reader.ReadBit();
        _hCombinationOperator = CombinationOperators.FromCode((int)reader.ReadBits(3));
        _hSkipEnabled = reader.ReadBit() == 1;
        _hTemplate = (byte)reader.ReadBits(2);
        _isMmrEncoded = reader.ReadBit() == 1;

        _hGridWidth = (int)reader.ReadBits(32);
        _hGridHeight = (int)reader.ReadBits(32);
        _hGridX = (int)reader.ReadBits(32);
        _hGridY = (int)reader.ReadBits(32);
        _hRegionX = (int)reader.ReadBits(16);
        _hRegionY = (int)reader.ReadBits(16);
        if (_hGridWidth < 0 || _hGridHeight < 0) throw new Jbig2DataException($"a halftone grid of {(uint)_hGridWidth} x {(uint)_hGridHeight}");

        _dataOffset = reader.Position;
        _dataLength = reader.Length - _dataOffset;
    }

    public Jbig2Bitmap GetRegionBitmap()
    {
        if (_halftoneRegionBitmap != null) return _halftoneRegionBitmap;

        // 6.6.5 1)
        _halftoneRegionBitmap = new Jbig2Bitmap(RegionInfo.BitmapWidth, RegionInfo.BitmapHeight, _context);
        _patterns ??= GetPatterns();
        if (_patterns.Count == 0) throw new Jbig2DataException("a halftone region has no patterns");
        if (_hDefaultPixel == 1) _halftoneRegionBitmap.ByteArray.AsSpan().Fill(0xff);

        // 2): 6.6.5.1, HSKIP
        Jbig2Bitmap? hSkip = null;
        if (_hSkipEnabled) hSkip = ComputeHSkip(_patterns[0].Width, _patterns[0].Height);

        // 3) - 5)
        var bitsPerValue = 0;
        while (1L << bitsPerValue < _patterns.Count) bitsPerValue++;
        var grayScaleValues = GrayScaleDecoding(bitsPerValue, hSkip);
        RenderPattern(grayScaleValues);

        // 6)
        return _halftoneRegionBitmap;
    }

    /// <summary>6.6.5.2: each grid cell's pattern drawn where the grid places it.</summary>
    private void RenderPattern(int[] grayScaleValues)
    {
        var last = _patterns!.Count - 1;
        var clamped = false;
        for (var m = 0; m < _hGridHeight; m++)
        {
            var rowOffset = m * _hGridWidth;
            for (var n = 0; n < _hGridWidth; n++)
            {
                var gray = grayScaleValues[rowOffset + n];
                if (gray > last)
                {
                    gray = last;
                    clamped = true;
                }
                Jbig2Bitmaps.Blit(_patterns[gray], _halftoneRegionBitmap!, ComputeX(m, n), ComputeY(m, n), _hCombinationOperator, _context);
            }
        }
        if (clamped) _context.Warn($"Halftone region segment {_segmentHeader.SegmentNumber} has gray-scale values past its last pattern; the last pattern is drawn for them");
    }

    private List<Jbig2Bitmap> GetPatterns()
    {
        var patterns = new List<Jbig2Bitmap>();
        foreach (var s in _segmentHeader.ReferredTo())
        {
            if (s.GetSegmentData() is not PatternDictionary patternDictionary)
                throw new Jbig2DataException("a halftone region refers to a segment that is no pattern dictionary");
            patterns.AddRange(patternDictionary.GetDictionary());
        }
        return patterns;
    }

    /// <summary>Annex C.5: the gray-scale image, its bit planes Gray coded, most significant first.</summary>
    private int[] GrayScaleDecoding(int bitsPerValue, Jbig2Bitmap? hSkip)
    {
        var cells = (long)_hGridWidth * _hGridHeight;
        _context.Charge(4 * cells);
        if (cells > Array.MaxLength) throw new Jbig2Exception($"A halftone grid of {cells} cells is larger than one buffer holds");
        var grayScaleValues = new int[cells];
        if (bitsPerValue == 0) return grayScaleValues;

        short[]? gbAtX = null;
        short[]? gbAtY = null;
        if (!_isMmrEncoded)
        {
            gbAtX = [(short)(_hTemplate <= 1 ? 3 : 2), -3, 2, -2];
            gbAtY = [-1, -1, -2, -2];
        }

        // 1) - 3)
        var planes = new Jbig2Bitmap[bitsPerValue];
        var genericRegion = new GenericRegion(_reader, _context);
        genericRegion.SetParameters(_isMmrEncoded, _dataOffset, _dataLength, _hGridHeight, _hGridWidth, _hTemplate, false,
            _hSkipEnabled, hSkip, gbAtX, gbAtY);

        var j = bitsPerValue - 1;
        planes[j] = genericRegion.GetRegionBitmap();
        while (j > 0)
        {
            j--;
            genericRegion.ResetBitmap();
            planes[j] = genericRegion.GetRegionBitmap();

            // 3) b): each plane is XORed with the one above it.
            var lower = planes[j].ByteArray;
            var upper = planes[j + 1].ByteArray;
            for (var i = 0; i < lower.Length; i++) lower[i] ^= upper[i];
        }

        // 4)
        for (var y = 0; y < _hGridHeight; y++)
        {
            var rowOffset = y * _hGridWidth;
            for (var x = 0; x < _hGridWidth; x++)
            {
                var value = 0;
                for (var plane = 0; plane < bitsPerValue; plane++) value |= planes[plane].GetPixel(x, y) << plane;
                grayScaleValues[rowOffset + x] = value;
            }
        }
        return grayScaleValues;
    }

    private int ComputeX(int m, int n) => (int)((_hGridX + (long)m * _hRegionY + (long)n * _hRegionX) >> 8);

    private int ComputeY(int m, int n) => (int)((_hGridY + (long)m * _hRegionX - (long)n * _hRegionY) >> 8);

    /// <summary>6.6.5.1: the grid cells whose pattern would fall wholly outside the region, skipped when decoding.</summary>
    private Jbig2Bitmap ComputeHSkip(int hPatternWidth, int hPatternHeight)
    {
        var bitmap = new Jbig2Bitmap(_hGridWidth, _hGridHeight, _context);
        _context.Work((long)_hGridWidth * _hGridHeight);
        for (var m = 0; m < _hGridHeight; m++)
        {
            for (var n = 0; n < _hGridWidth; n++)
            {
                var x = ComputeX(m, n);
                var y = ComputeY(m, n);
                if (x + hPatternWidth <= 0 || x >= _halftoneRegionBitmap!.Width
                    || y + hPatternHeight <= 0 || y >= _halftoneRegionBitmap.Height)
                    bitmap.SetPixel(n, m, 1);
            }
        }
        return bitmap;
    }
}

/// <summary>A pattern dictionary, 6.7 and 7.4.4: patterns of one size, side by side in one collective bitmap.</summary>
internal sealed class PatternDictionary : IJbig2Dictionary
{
    private readonly Lock _gate = new();
    private Jbig2Reader _reader = null!;
    private Jbig2DecodeContext _context = null!;
    private long _dataOffset;
    private long _dataLength;
    private List<Jbig2Bitmap>? _patterns;

    // Pattern dictionary flags, 7.4.4.1.1 - 7.4.4.1.4
    private bool _isMmrEncoded;
    private byte _hdTemplate;
    private int _hdpWidth;
    private int _hdpHeight;
    private long _grayMax;

    public void Init(SegmentHeader header, Jbig2Reader reader)
    {
        _reader = reader;
        _context = header.Context;

        reader.ReadBits(5); // Bits 3-7: reserved
        _hdTemplate = (byte)reader.ReadBits(2);
        _isMmrEncoded = reader.ReadBit() == 1;
        _hdpWidth = reader.ReadByte();
        _hdpHeight = reader.ReadByte();
        _grayMax = reader.ReadBits(32);

        _dataOffset = reader.Position;
        _dataLength = reader.Length - _dataOffset;
        if (_hdpHeight < 1 || _hdpWidth < 1) throw new Jbig2DataException("a pattern dictionary's patterns have no width or no height");
    }

    /// <summary>The patterns, decoded once — by whichever page of a globals stream first needs them.</summary>
    public List<Jbig2Bitmap> GetDictionary()
    {
        lock (_gate) return _patterns ?? Decode();
    }

    private List<Jbig2Bitmap> Decode()
    {

        short[]? gbAtX = null;
        short[]? gbAtY = null;
        if (!_isMmrEncoded)
        {
            // 6.7.5 1): the first adaptive pixel is one pattern to the left.
            if (_hdTemplate == 0)
            {
                gbAtX = [(short)-_hdpWidth, -3, 2, -2];
                gbAtY = [0, -1, -2, -2];
            }
            else
            {
                gbAtX = [(short)-_hdpWidth];
                gbAtY = [0];
            }
        }

        // 2)
        var collectiveWidth = (_grayMax + 1) * _hdpWidth;
        if (collectiveWidth > int.MaxValue) throw new Jbig2DataException($"{_grayMax + 1} patterns side by side");
        _context.Charge(64 * (_grayMax + 1));
        var genericRegion = new GenericRegion(_reader, _context);
        genericRegion.SetParameters(_isMmrEncoded, _dataOffset, _dataLength, _hdpHeight, (int)collectiveWidth, _hdTemplate,
            false, false, null, gbAtX, gbAtY);
        var collectiveBitmap = genericRegion.GetRegionBitmap();

        // 3) - 4)
        _patterns = new List<Jbig2Bitmap>((int)(_grayMax + 1));
        for (long gray = 0; gray <= _grayMax; gray++)
        {
            var roi = new Jbig2Rectangle((int)(_hdpWidth * gray), 0, _hdpWidth, _hdpHeight);
            _patterns.Add(Jbig2Bitmaps.Extract(roi, collectiveBitmap, _context));
        }
        return _patterns;
    }
}
