/*
 * Ported from Apache PDFBox JBIG2 ImageIO (pdfbox-jbig2) by way of PdfPig.Filters.Jbig2.PdfboxJbig2 0.1.16.2:
 * JBIG2Document.java, JBIG2Globals.java and JBIG2Page.java.
 * Licensed under the Apache License, Version 2.0; see the LICENSE and NOTICE files beside this folder.
 * Modified for Nexaflow: segments are read up to where the data ends, a header cut short ends the stream there, and
 * a region that cannot be decoded is left off the page with a warning rather than failing it; a page bitmap is the
 * one region on it only when that region covers it exactly and its operator leaves it as it is.
 */

namespace Nexaflow.Media.Jbig2.Internal;

/// <summary>
/// A JBIG2 stream's segments, Annex D: a file, with its header, or a stream embedded in a PDF without one, read
/// sequentially; a file may also be organised for random access, its headers first.
/// </summary>
internal sealed class Jbig2Document
{
    public const int RandomAccess = 0;
    public const int Sequential = 1;

    // D.4.1
    private static readonly byte[] FileHeaderId = [0x97, 0x4A, 0x42, 0x32, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly SortedDictionary<int, Jbig2Page> _pages = [];
    // The segments of no page: the globals stream's, or any of this stream's own, which it looks in first.
    private readonly Dictionary<int, SegmentHeader> _globalSegments = [];
    private readonly Jbig2Document? _globals;
    private readonly Jbig2Reader _reader;
    private int _organisationType = Sequential;

    /// <param name="globals">The segments of a PDF's JBIG2Globals stream, which this stream's segments may refer to.</param>
    public Jbig2Document(Jbig2Reader reader, Jbig2Document? globals, Jbig2DecodeContext context)
    {
        _reader = reader;
        _globals = globals;
        Context = context;
        MapStream();
    }

    public Jbig2DecodeContext Context { get; }

    /// <summary>The globals stream's segments this stream's may refer to, if it has one.</summary>
    public Jbig2Document? Globals => _globals;

    public Jbig2Page? GetPage(int pageNumber) => _pages.GetValueOrDefault(pageNumber);

    /// <summary>The page to decode: page 1, or failing that the first page the stream holds.</summary>
    public Jbig2Page? FirstPage => GetPage(1) ?? _pages.Values.FirstOrDefault();

    public SegmentHeader? GetGlobalSegment(int segmentNumber) =>
        _globalSegments.GetValueOrDefault(segmentNumber) ?? _globals?.GetGlobalSegment(segmentNumber);

    private void MapStream()
    {
        var segments = new List<SegmentHeader>();
        long offset = 0;
        var segmentType = 0;

        if (IsFileHeaderPresent()) offset = ParseFileHeader();

        // 51 is the end of file segment. A sequential stream's segments follow one another, each header then its data.
        while (segmentType != 51 && offset + 4 <= _reader.Length)
        {
            SegmentHeader segment;
            try
            {
                segment = new SegmentHeader(this, _reader, offset, _organisationType);
            }
            catch (Jbig2DataException)
            {
                Context.Warn("The data ends inside a segment header; the segments before it are decoded");
                break;
            }

            segmentType = segment.SegmentType;
            if (segment.PageAssociation != 0)
            {
                if (!_pages.TryGetValue(segment.PageAssociation, out var page))
                    _pages[segment.PageAssociation] = page = new Jbig2Page(this, segment.PageAssociation);
                page.Add(segment);
            }
            else
            {
                _globalSegments[segment.SegmentNumber] = segment;
            }
            segments.Add(segment);

            offset = _reader.Position;
            if (_organisationType == Sequential)
            {
                if (segment.IsCutShort)
                    Context.Warn($"Segment {segment.SegmentNumber} is cut short: the data ends {segment.SegmentDataStartOffset + segment.SegmentDataLength - _reader.Length} bytes early");
                offset += segment.SegmentDataLength;
            }
        }

        // Random access: the headers are all read, and the data parts follow in the same order.
        if (_organisationType == RandomAccess)
        {
            foreach (var s in segments)
            {
                s.SegmentDataStartOffset = offset;
                offset += s.SegmentDataLength;
            }
        }
    }

    private bool IsFileHeaderPresent() =>
        _reader.Length >= FileHeaderId.Length && _reader.Bytes.AsSpan(_reader.Start, FileHeaderId.Length).SequenceEqual(FileHeaderId);

    /// <summary>D.4: the file header, returning its length.</summary>
    private long ParseFileHeader()
    {
        _reader.Seek(FileHeaderId.Length);
        _reader.ReadBits(5); // Bits 3-7: reserved
        _reader.ReadBit(); // Bit 2: extended templates are used
        var numberOfPagesUnknown = _reader.ReadBit() == 1;
        _organisationType = _reader.ReadBit();
        if (numberOfPagesUnknown) return 9;
        _reader.ReadUnsignedInt();
        return 13;
    }
}

/// <summary>A page and its segments, composed into its bitmap as 8.2 composes it.</summary>
internal sealed class Jbig2Page(Jbig2Document document, int pageNumber)
{
    // The segments of this page, in order of segment number.
    private readonly SortedDictionary<int, SegmentHeader> _segments = [];

    private Jbig2Bitmap? _pageBitmap;

    public int PageNumber { get; } = pageNumber;

    private Jbig2DecodeContext Context => document.Context;

    public SegmentHeader? GetSegment(int number) => _segments.GetValueOrDefault(number) ?? document.GetGlobalSegment(number);

    public void Add(SegmentHeader segment) => _segments[segment.SegmentNumber] = segment;

    public Jbig2Bitmap GetBitmap()
    {
        if (_pageBitmap != null) return _pageBitmap;

        // 8.2 1): the page information segment.
        var pageInformationSegment = _segments.Values.FirstOrDefault(s => s.SegmentType == 48)
            ?? throw new Jbig2Exception($"Page {PageNumber} has no page information segment");
        PageInformation pageInformation;
        try
        {
            pageInformation = (PageInformation)pageInformationSegment.GetSegmentData();
        }
        catch (Jbig2DataException e)
        {
            throw new Jbig2Exception($"Page {PageNumber}'s page information cannot be read: {e.Message}");
        }

        if (!pageInformation.IsStriped || pageInformation.BitmapHeight != -1) CreateNormalPage(pageInformation);
        else CreateStripedPage(pageInformation);
        return _pageBitmap!;
    }

    private static bool IsImmediateRegion(int segmentType) => segmentType is 6 or 7 or 22 or 23 or 38 or 39 or 42 or 43;

    private void CreateNormalPage(PageInformation pageInformation)
    {
        _pageBitmap = new Jbig2Bitmap(pageInformation.BitmapWidth, pageInformation.BitmapHeight, Context);
        AllowWorkFor(_pageBitmap);

        // 8.2 3): the page starts as its default pixel value.
        if (pageInformation.DefaultPixelValue != 0) _pageBitmap.ByteArray.AsSpan().Fill(0xff);

        var regionCount = _segments.Values.Count(s => IsImmediateRegion(s.SegmentType));
        foreach (var s in _segments.Values)
        {
            // 8.2 5)
            if (!IsImmediateRegion(s.SegmentType)) continue;
            if (DecodeRegion(s) is not var (region, regionBitmap)) continue;

            var regionInfo = region.RegionInfo;
            var op = GetCombinationOperator(pageInformation, regionInfo.CombinationOperator);
            if (regionCount == 1 && FitsPage(pageInformation, regionInfo, regionBitmap, op)) _pageBitmap = regionBitmap;
            else Jbig2Bitmaps.Blit(regionBitmap, _pageBitmap, regionInfo.X, regionInfo.Y, op, Context);
        }
    }

    /// <summary>
    /// Whether the one region on the page is the page as it will be: it covers the page exactly, the page starts white,
    /// and its operator over white gives the region itself.
    /// </summary>
    private static bool FitsPage(PageInformation pageInformation, RegionSegmentInformation regionInfo, Jbig2Bitmap regionBitmap, CombinationOperator op) =>
        pageInformation.DefaultPixelValue == 0
        && op is CombinationOperator.Or or CombinationOperator.Xor or CombinationOperator.Replace
        && regionInfo.X == 0 && regionInfo.Y == 0
        && pageInformation.BitmapWidth == regionBitmap.Width
        && pageInformation.BitmapHeight == regionBitmap.Height;

    private void CreateStripedPage(PageInformation pageInformation)
    {
        // The page's height is the end of its last stripe.
        var finalHeight = 0;
        var stripes = new List<SegmentHeader>();
        foreach (var s in _segments.Values)
        {
            if (IsImmediateRegion(s.SegmentType)) stripes.Add(s);
            else if (s.SegmentType == 50 && EndOfStripe(s) is { } eos)
            {
                stripes.Add(s);
                finalHeight = eos.LineNumber + 1;
            }
        }

        _pageBitmap = new Jbig2Bitmap(pageInformation.BitmapWidth, finalHeight, Context);
        AllowWorkFor(_pageBitmap);
        if (pageInformation.DefaultPixelValue != 0) _pageBitmap.ByteArray.AsSpan().Fill(0xff);

        var startLine = 0;
        foreach (var s in stripes)
        {
            if (s.SegmentType == 50)
            {
                startLine = EndOfStripe(s)!.LineNumber + 1;
                continue;
            }
            if (DecodeRegion(s) is not var (region, regionBitmap)) continue;
            var op = GetCombinationOperator(pageInformation, region.RegionInfo.CombinationOperator);
            Jbig2Bitmaps.Blit(regionBitmap, _pageBitmap, region.RegionInfo.X, startLine, op, Context);
        }
    }

    /// <summary>The work this page may do, allowed to its decode and to the globals it decodes with.</summary>
    private void AllowWorkFor(Jbig2Bitmap page)
    {
        var pixels = (long)page.Width * page.Height;
        Context.AllowWorkFor(pixels);
        document.Globals?.Context.AllowWorkFor(pixels);
    }

    private EndOfStripe? EndOfStripe(SegmentHeader s)
    {
        try
        {
            return (EndOfStripe)s.GetSegmentData();
        }
        catch (Jbig2DataException e)
        {
            Context.Warn($"End of stripe segment {s.SegmentNumber} cannot be read: {e.Message}");
            return null;
        }
    }

    /// <summary>A region and its bitmap, or null — with a warning — for one that cannot be decoded, left off the page.</summary>
    private (IRegion, Jbig2Bitmap)? DecodeRegion(SegmentHeader s)
    {
        try
        {
            var region = (IRegion)s.GetSegmentData();
            if (region is GenericRefinementRegion refinement) refinement.SetPageBitmap(_pageBitmap!);
            return (region, region.GetRegionBitmap());
        }
        catch (Jbig2DataException e)
        {
            Context.Warn($"Region segment {s.SegmentNumber} is left out: {e.Message}");
            return null;
        }
    }

    private static CombinationOperator GetCombinationOperator(PageInformation pi, CombinationOperator newOperator) =>
        pi.IsCombinationOperatorOverrideAllowed ? newOperator : pi.CombinationOperator;
}
