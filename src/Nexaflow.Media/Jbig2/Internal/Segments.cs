/*
 * Ported from Apache PDFBox JBIG2 ImageIO (pdfbox-jbig2) by way of PdfPig.Filters.Jbig2.PdfboxJbig2 0.1.16.2:
 * SegmentHeader.java, SegmentData.java, Region.java, Dictionary.java, segments/PageInformation.java,
 * segments/RegionSegmentInformation.java, segments/EndOfStripe.java, segments/Table.java and
 * decoder/huffman/EncodedTable.java.
 * Licensed under the Apache License, Version 2.0; see the LICENSE and NOTICE files beside this folder.
 * Modified for Nexaflow: a segment's data is decoded once and kept for the decode, or the reason it could not be;
 * data cut short is read as far as it goes; a referred-to segment that is not there is a damaged segment.
 */

namespace Nexaflow.Media.Jbig2.Internal;

/// <summary>A segment's data part, decoded from its bytes, 7.3.</summary>
internal interface ISegmentData
{
    void Init(SegmentHeader header, Jbig2Reader reader);
}

/// <summary>A region segment: a bitmap and where on the page it goes.</summary>
internal interface IRegion : ISegmentData
{
    RegionSegmentInformation RegionInfo { get; }

    Jbig2Bitmap GetRegionBitmap();
}

/// <summary>A symbol or pattern dictionary: the bitmaps it exports.</summary>
internal interface IJbig2Dictionary : ISegmentData
{
    List<Jbig2Bitmap> GetDictionary();
}

/// <summary>A segment header, 7.2, and the data it introduces.</summary>
internal sealed class SegmentHeader
{
    private readonly Jbig2Reader _reader;
    private readonly Lock _gate = new();
    private ISegmentData? _segmentData;
    private Jbig2DataException? _failure;

    public SegmentHeader(Jbig2Document document, Jbig2Reader reader, long offset, int organisationType)
    {
        Document = document;
        _reader = reader;
        reader.Seek(offset);

        // 7.2.2 - 7.2.3
        SegmentNumber = (int)reader.ReadBits(32);
        IsRetained = reader.ReadBit() == 1;
        var pageAssociationFieldSize = reader.ReadBit();
        SegmentType = (int)reader.ReadBits(6);

        // 7.2.4 - 7.2.5
        var countOfReferredToSegments = ReadAmountOfReferredToSegments(reader);
        var referredToSegmentNumbers = new int[countOfReferredToSegments];
        if (countOfReferredToSegments > 0)
        {
            var rtsSize = (uint)SegmentNumber > 65536 ? 4 : (uint)SegmentNumber > 256 ? 2 : 1;
            for (var i = 0; i < countOfReferredToSegments; i++) referredToSegmentNumbers[i] = (int)reader.ReadBits(rtsSize << 3);
        }

        // 7.2.6
        PageAssociation = pageAssociationFieldSize == 0 ? (int)reader.ReadBits(8) : (int)reader.ReadBits(32);
        if (countOfReferredToSegments > 0)
        {
            var page = document.GetPage(PageAssociation);
            RtSegments = new SegmentHeader?[countOfReferredToSegments];
            for (var i = 0; i < countOfReferredToSegments; i++)
            {
                RtSegments[i] = page != null ? page.GetSegment(referredToSegmentNumbers[i])
                    : document.GetGlobalSegment(referredToSegmentNumbers[i]);
            }
            ReferredToSegmentNumbers = referredToSegmentNumbers;
        }

        // 7.2.7
        SegmentDataLength = reader.ReadBits(32);
        if (organisationType == Jbig2Document.Sequential) SegmentDataStartOffset = reader.Position;
    }

    public Jbig2Document Document { get; }

    public Jbig2DecodeContext Context => Document.Context;

    /// <summary>The segments this one refers to, in order; null for one not in the stream.</summary>
    public SegmentHeader?[]? RtSegments { get; }

    private int[] ReferredToSegmentNumbers { get; } = [];

    public int SegmentNumber { get; }

    public int SegmentType { get; }

    public int PageAssociation { get; }

    public bool IsRetained { get; }

    /// <summary>The data's length, or 0xFFFFFFFF for an immediate generic region of unknown length.</summary>
    public long SegmentDataLength { get; }

    public long SegmentDataStartOffset { get; set; }

    /// <summary>The segments referred to, each of them there, 7.2.5; a missing one is a damaged segment.</summary>
    public IEnumerable<SegmentHeader> ReferredTo()
    {
        if (RtSegments is null) yield break;
        for (var i = 0; i < RtSegments.Length; i++)
            yield return RtSegments[i] ?? throw new Jbig2DataException($"segment {SegmentNumber} refers to segment {ReferredToSegmentNumbers[i]}, which is not there");
    }

    private static int ReadAmountOfReferredToSegments(Jbig2Reader reader)
    {
        var count = (int)reader.ReadBits(3);
        if (count <= 4)
        {
            // Short form: five retain bits.
            reader.ReadBits(5);
            return count;
        }

        // Long form: a 29-bit count, then a retain bit for each segment and one for this.
        count = (int)reader.ReadBits(29);
        var retainBytes = (count + 8) >> 3;
        for (var i = 0; i < retainBytes; i++) reader.ReadByte();
        return count;
    }

    /// <summary>The segment's data, or as much of it as the stream holds.</summary>
    public Jbig2Reader GetDataReader() => _reader.Slice(SegmentDataStartOffset, SegmentDataLength);

    /// <summary>Whether the stream ends before this segment's data does.</summary>
    public bool IsCutShort => SegmentDataLength != 0xFFFFFFFF && SegmentDataStartOffset + SegmentDataLength > _reader.Length;

    /// <summary>
    /// The segment's data part, read once; a segment that cannot be read fails the same way each time it is asked for.
    /// A globals segment is asked for by every page decoded with it, from any thread.
    /// </summary>
    public ISegmentData GetSegmentData()
    {
        lock (_gate)
        {
            if (_failure != null) throw _failure;
            if (_segmentData != null) return _segmentData;
            try
            {
                var data = CreateSegmentData(SegmentType);
                data.Init(this, GetDataReader());
                return _segmentData = data;
            }
            catch (Jbig2DataException e)
            {
                _failure = e;
                throw;
            }
        }
    }

    private ISegmentData CreateSegmentData(int segmentType) => segmentType switch
    {
        0 => new SymbolDictionary(),
        4 or 6 or 7 => new TextRegion(),
        16 => new PatternDictionary(),
        20 or 22 or 23 => new HalftoneRegion(),
        36 or 38 or 39 => new GenericRegion(),
        40 or 42 or 43 => new GenericRefinementRegion(),
        48 => new PageInformation(),
        50 => new EndOfStripe(),
        53 => new Table(),
        _ => throw new Jbig2DataException($"segment {SegmentNumber} is of type {segmentType}, which holds nothing to decode"),
    };
}

/// <summary>The page information segment, 7.4.8.</summary>
internal sealed class PageInformation : ISegmentData
{
    /// <summary>7.4.8.1</summary>
    public int BitmapWidth { get; private set; }

    /// <summary>7.4.8.2: -1 while a striped page's height is unknown.</summary>
    public int BitmapHeight { get; private set; }

    public int ResolutionX { get; private set; }

    public int ResolutionY { get; private set; }

    // 7.4.8.5
    public bool IsCombinationOperatorOverrideAllowed { get; private set; }

    public CombinationOperator CombinationOperator { get; private set; }

    public bool RequiresAuxiliaryBuffer { get; private set; }

    public int DefaultPixelValue { get; private set; }

    public bool MightContainRefinements { get; private set; }

    public bool IsLossless { get; private set; }

    // 7.4.8.6
    public bool IsStriped { get; private set; }

    public int MaxStripeSize { get; private set; }

    public void Init(SegmentHeader header, Jbig2Reader reader)
    {
        BitmapWidth = (int)reader.ReadBits(32);
        BitmapHeight = (int)reader.ReadBits(32);
        ResolutionX = (int)reader.ReadBits(32);
        ResolutionY = (int)reader.ReadBits(32);

        reader.ReadBit(); // Bit 7: reserved
        IsCombinationOperatorOverrideAllowed = reader.ReadBit() == 1;
        RequiresAuxiliaryBuffer = reader.ReadBit() == 1;
        CombinationOperator = CombinationOperators.FromCode((int)reader.ReadBits(2));
        DefaultPixelValue = reader.ReadBit();
        MightContainRefinements = reader.ReadBit() == 1;
        IsLossless = reader.ReadBit() == 1;

        IsStriped = reader.ReadBit() == 1;
        MaxStripeSize = (int)reader.ReadBits(15);
    }
}

/// <summary>The region segment information field, 7.4.1.</summary>
internal sealed class RegionSegmentInformation
{
    public RegionSegmentInformation()
    {
    }

    public RegionSegmentInformation(int bitmapWidth, int bitmapHeight)
    {
        BitmapWidth = bitmapWidth;
        BitmapHeight = bitmapHeight;
    }

    public int BitmapWidth { get; set; }

    public int BitmapHeight { get; set; }

    public int X { get; private set; }

    public int Y { get; private set; }

    public CombinationOperator CombinationOperator { get; private set; }

    public void ParseHeader(Jbig2Reader reader)
    {
        BitmapWidth = (int)reader.ReadBits(32);
        BitmapHeight = (int)reader.ReadBits(32);
        X = (int)reader.ReadBits(32);
        Y = (int)reader.ReadBits(32);
        reader.ReadBits(5); // Bits 3-7: reserved
        CombinationOperator = CombinationOperators.FromCode((int)reader.ReadBits(3));
    }
}

/// <summary>The end of stripe segment, 7.4.10.</summary>
internal sealed class EndOfStripe : ISegmentData
{
    public int LineNumber { get; private set; }

    public void Init(SegmentHeader header, Jbig2Reader reader) => LineNumber = (int)reader.ReadBits(32);
}

/// <summary>A tables segment, 7.4.13: a Huffman table coded as B.2 codes it.</summary>
internal sealed class Table : ISegmentData
{
    private readonly Lock _gate = new();
    private HuffmanTable? _huffmanTable;
    private Jbig2Reader _reader = null!;
    private Jbig2DecodeContext _context = null!;
    private int _htOutOfBand;
    private int _htPs;
    private int _htRs;
    private int _htLow;
    private int _htHigh;

    public void Init(SegmentHeader header, Jbig2Reader reader)
    {
        _reader = reader;
        _context = header.Context;

        // B.2.1 - B.2.3
        if (reader.ReadBit() == 1) throw new Jbig2DataException("a code table's flags have bit 7 set");
        _htRs = (int)reader.ReadBits(3) + 1;
        _htPs = (int)reader.ReadBits(3) + 1;
        _htOutOfBand = reader.ReadBit();
        _htLow = (int)reader.ReadBits(32);
        _htHigh = (int)reader.ReadBits(32);
    }

    /// <summary>The table its lines code, B.2 5) - 10), read once — by whichever page of a globals stream first needs it.</summary>
    public HuffmanTable HuffmanTable
    {
        get
        {
            lock (_gate) return _huffmanTable ??= Parse();
        }
    }

    private HuffmanTable Parse()
    {
        var reader = _reader;
        var codeTable = new List<HuffmanTable.Code>();
        long curRangeLow = _htLow;
        while (curRangeLow < _htHigh)
        {
            var prefLen = (int)reader.ReadBits(_htPs);
            var rangeLen = (int)reader.ReadBits(_htRs);
            _context.Charge(64);
            codeTable.Add(new HuffmanTable.Code(prefLen, rangeLen, (int)curRangeLow, false));
            curRangeLow += 1L << rangeLen;
        }

        // The lower range line, the upper range line and the out-of-band line.
        codeTable.Add(new HuffmanTable.Code((int)reader.ReadBits(_htPs), 32, _htLow - 1, true));
        codeTable.Add(new HuffmanTable.Code((int)reader.ReadBits(_htPs), 32, _htHigh, false));
        if (_htOutOfBand == 1) codeTable.Add(new HuffmanTable.Code((int)reader.ReadBits(_htPs), -1, -1, false));
        return new HuffmanTable(codeTable);
    }
}
