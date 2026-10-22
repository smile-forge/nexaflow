/*
 * Ported from Apache PDFBox JBIG2 ImageIO (pdfbox-jbig2) by way of PdfPig.Filters.Jbig2.PdfboxJbig2 0.1.16.2:
 * segments/GenericRefinementRegion.java.
 * Licensed under the Apache License, Version 2.0; see the LICENSE and NOTICE files beside this folder.
 * Modified for Nexaflow: the decoding procedure is rewritten from 6.3.5 — each pixel's context formed as Figures 12
 * and 13 number its pixels, the second adaptive pixel read from the reference where the region places it — in place
 * of the byte-packed paths, which numbered the contexts otherwise.
 */

namespace Nexaflow.Media.Jbig2.Internal;

/// <summary>
/// A generic refinement region, 6.3 and 7.4.7: a bitmap coded as a refinement of a reference bitmap — another region,
/// or the page as it stands under it — or, inside a text region or a symbol dictionary, of a symbol.
/// </summary>
internal sealed class GenericRefinementRegion : IRegion
{
    private SegmentHeader? _segmentHeader;
    private Jbig2Reader _reader = null!;
    private Jbig2DecodeContext _context = null!;

    // Generic refinement region segment flags, 7.4.7.2
    private bool _isTpgrOn;
    private int _templateId;

    // Generic refinement region segment AT flags, 7.4.7.3: the first in this region, the second in the reference.
    private short[]? _grAtX;
    private short[]? _grAtY;

    private Jbig2Bitmap? _regionBitmap;
    private Jbig2Bitmap? _referenceBitmap;
    private int _referenceDx;
    private int _referenceDy;
    private ArithmeticDecoder? _arithDecoder;
    private CX? _cx;

    // GRREFERENCE when this segment refers to no other, 7.4.7.4.
    private Jbig2Bitmap? _pageBitmap;

    public GenericRefinementRegion()
    {
    }

    public GenericRefinementRegion(Jbig2Reader reader, Jbig2DecodeContext context)
    {
        _reader = reader;
        _context = context;
    }

    // Region segment information field, 7.4.1
    public RegionSegmentInformation RegionInfo { get; private set; } = new();

    public void Init(SegmentHeader header, Jbig2Reader reader)
    {
        _segmentHeader = header;
        _reader = reader;
        _context = header.Context;
        RegionInfo = new RegionSegmentInformation();
        RegionInfo.ParseHeader(reader);

        reader.ReadBits(6); // Bits 2-7: reserved
        _isTpgrOn = reader.ReadBit() == 1;
        _templateId = reader.ReadBit();
        if (_templateId == 0)
        {
            _grAtX = new short[2];
            _grAtY = new short[2];
            _grAtX[0] = (sbyte)reader.ReadByte();
            _grAtY[0] = (sbyte)reader.ReadByte();
            _grAtX[1] = (sbyte)reader.ReadByte();
            _grAtY[1] = (sbyte)reader.ReadByte();
        }
    }

    public void SetPageBitmap(Jbig2Bitmap pageBitmap) => _pageBitmap = pageBitmap;

    /// <summary>A refinement inside a text region or symbol dictionary: Tables 12 and 18.</summary>
    internal void SetParameters(CX cx, ArithmeticDecoder arithmeticDecoder, int grTemplate, int regionWidth,
        int regionHeight, Jbig2Bitmap grReference, int grReferenceDx, int grReferenceDy, bool isTpgrOn,
        short[]? grAtX, short[]? grAtY)
    {
        _cx = cx;
        _arithDecoder = arithmeticDecoder;
        _templateId = grTemplate;
        RegionInfo.BitmapWidth = regionWidth;
        RegionInfo.BitmapHeight = regionHeight;
        _referenceBitmap = grReference;
        _referenceDx = grReferenceDx;
        _referenceDy = grReferenceDy;
        _isTpgrOn = isTpgrOn;
        _grAtX = grAtX;
        _grAtY = grAtY;
        _regionBitmap = null;
    }

    public Jbig2Bitmap GetRegionBitmap()
    {
        if (_regionBitmap != null) return _regionBitmap;

        _referenceBitmap ??= GetGrReference();
        _arithDecoder ??= new ArithmeticDecoder(_reader);
        _cx ??= _context.NewCx(8192);

        if (_templateId == 0)
        {
            // The first adaptive pixel must be one already decoded.
            var (x, y) = (_grAtX![0], _grAtY![0]);
            if (y > 0 || (y == 0 && x >= 0)) throw new Jbig2DataException($"a refinement's adaptive pixel ({x}, {y}) is not yet decoded");
        }

        var region = new Jbig2Bitmap(RegionInfo.BitmapWidth, RegionInfo.BitmapHeight, _context);
        _context.Work(Jbig2Bitmaps.Work(region.Width, region.Height));
        var reference = _referenceBitmap;
        var cx = _cx;
        var decoder = _arithDecoder;

        // 6.3.5.6
        var ltp = 0;
        for (var y = 0; y < region.Height; y++)
        {
            // 3 b): SLTP's context, Figures 14 and 15, is the template with only the reference pixel under this one set.
            if (_isTpgrOn)
            {
                cx.Index = _templateId == 0 ? 0x100 : 0x080;
                ltp ^= decoder.Decode(cx);
            }

            for (var x = 0; x < region.Width; x++)
            {
                // 3 d): where the reference is all one value around the pixel, the pixel is that value.
                if (ltp == 1 && TypicalPixel(reference, x - _referenceDx, y - _referenceDy) is var typical and >= 0)
                {
                    if (typical == 1) region.SetPixel(x, y, 1);
                    continue;
                }

                cx.Index = _templateId == 0 ? Context0(region, reference, x, y) : Context1(region, reference, x, y);
                if (decoder.Decode(cx) == 1) region.SetPixel(x, y, 1);
            }
        }

        return _regionBitmap = region;
    }

    /// <summary>TPGRPIX, 6.3.5.6 3 d) i): the value of the 3 x 3 pixels of the reference about (rx, ry), or -1 when they differ.</summary>
    private static int TypicalPixel(Jbig2Bitmap reference, int rx, int ry)
    {
        var value = reference.GetPixelOrZero(rx, ry);
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
                if (reference.GetPixelOrZero(rx + dx, ry + dy) != value) return -1;
        return value;
    }

    /// <summary>The context of template 0, Figure 12, with its two adaptive pixels.</summary>
    private int Context0(Jbig2Bitmap region, Jbig2Bitmap reference, int x, int y)
    {
        var rx = x - _referenceDx;
        var ry = y - _referenceDy;
        return region.GetPixelOrZero(x - 1, y)
            | region.GetPixelOrZero(x + 1, y - 1) << 1
            | region.GetPixelOrZero(x, y - 1) << 2
            | region.GetPixelOrZero(x + _grAtX![0], y + _grAtY![0]) << 3
            | reference.GetPixelOrZero(rx + 1, ry + 1) << 4
            | reference.GetPixelOrZero(rx, ry + 1) << 5
            | reference.GetPixelOrZero(rx - 1, ry + 1) << 6
            | reference.GetPixelOrZero(rx + 1, ry) << 7
            | reference.GetPixelOrZero(rx, ry) << 8
            | reference.GetPixelOrZero(rx - 1, ry) << 9
            | reference.GetPixelOrZero(rx + 1, ry - 1) << 10
            | reference.GetPixelOrZero(rx, ry - 1) << 11
            | reference.GetPixelOrZero(rx + _grAtX[1], ry + _grAtY[1]) << 12;
    }

    /// <summary>The context of template 1, Figure 13.</summary>
    private int Context1(Jbig2Bitmap region, Jbig2Bitmap reference, int x, int y)
    {
        var rx = x - _referenceDx;
        var ry = y - _referenceDy;
        return region.GetPixelOrZero(x - 1, y)
            | region.GetPixelOrZero(x + 1, y - 1) << 1
            | region.GetPixelOrZero(x, y - 1) << 2
            | region.GetPixelOrZero(x - 1, y - 1) << 3
            | reference.GetPixelOrZero(rx + 1, ry + 1) << 4
            | reference.GetPixelOrZero(rx, ry + 1) << 5
            | reference.GetPixelOrZero(rx + 1, ry) << 6
            | reference.GetPixelOrZero(rx, ry) << 7
            | reference.GetPixelOrZero(rx - 1, ry) << 8
            | reference.GetPixelOrZero(rx, ry - 1) << 9;
    }

    /// <summary>7.4.7.4: the region this one refers to, or the page under it.</summary>
    private Jbig2Bitmap GetGrReference()
    {
        if (_segmentHeader?.RtSegments is null)
        {
            // 7.4.7.5 1): a refinement of the page itself replaces what it refines.
            if (RegionInfo.CombinationOperator != CombinationOperator.Replace)
                throw new Jbig2DataException("a refinement of the page combines other than by REPLACE");
            if (_pageBitmap is null) throw new Jbig2DataException("a refinement of the page comes before the page");
            var roi = new Jbig2Rectangle(RegionInfo.X, RegionInfo.Y, RegionInfo.BitmapWidth, RegionInfo.BitmapHeight);
            return Jbig2Bitmaps.Extract(roi, _pageBitmap, _context);
        }

        var referred = _segmentHeader.ReferredTo().First();
        if (referred.GetSegmentData() is not IRegion region) throw new Jbig2DataException("a refinement refers to a segment that is no region");
        return region.GetRegionBitmap();
    }
}
