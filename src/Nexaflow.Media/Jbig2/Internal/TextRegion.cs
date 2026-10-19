/*
 * Ported from Apache PDFBox JBIG2 ImageIO (pdfbox-jbig2) by way of PdfPig.Filters.Jbig2.PdfboxJbig2 0.1.16.2:
 * segments/TextRegion.java.
 * Licensed under the Apache License, Version 2.0; see the LICENSE and NOTICE files beside this folder.
 * Modified for Nexaflow: a Huffman-coded refinement is decoded by an arithmetic decoder of its own over its own
 * bytes, as 6.4.11 has it; a symbol identifier past the symbols is a damaged segment; data that runs out leaves the
 * symbols placed so far on the region.
 */

namespace Nexaflow.Media.Jbig2.Internal;

/// <summary>
/// A text region, 6.4 and 7.4.3: symbols from the dictionaries it refers to, placed in strips, Huffman or
/// arithmetically coded, each symbol instance possibly refined. A symbol dictionary decodes its aggregates through
/// one as well.
/// </summary>
internal sealed class TextRegion : IRegion
{
    private Jbig2Reader _reader = null!;
    private Jbig2DecodeContext _context = null!;
    private SegmentHeader? _segmentHeader;

    // Text region segment flags, 7.4.3.1.1
    private int _sbrTemplate;
    private int _sbdsOffset;
    private int _defaultPixel;
    private CombinationOperator _combinationOperator;
    private int _isTransposed;
    private int _referenceCorner;
    private int _logSbStrips;
    private bool _useRefinement;
    private bool _isHuffmanEncoded;

    // Text region segment Huffman flags, 7.4.3.1.2
    private int _sbHuffRSize;
    private int _sbHuffRdy;
    private int _sbHuffRdx;
    private int _sbHuffRdHeight;
    private int _sbHuffRdWidth;
    private int _sbHuffDt;
    private int _sbHuffDs;
    private int _sbHuffFs;

    // Text region refinement AT flags, 7.4.3.1.3
    private short[]? _sbrAtX;
    private short[]? _sbrAtY;

    // Number of symbol instances, 7.4.3.1.4
    private long _amountOfSymbolInstances;

    private long _currentS;
    private int _sbStrips;
    private int _amountOfSymbols;
    private Jbig2Bitmap? _regionBitmap;
    private List<Jbig2Bitmap> _symbols = [];

    private ArithmeticDecoder? _arithmeticDecoder;
    private ArithmeticIntegerDecoder? _integerDecoder;
    private GenericRefinementRegion? _genericRefinementRegion;

    private CX? _cxIadt;
    private CX? _cxIafs;
    private CX? _cxIads;
    private CX? _cxIait;
    private CX? _cxIari;
    private CX? _cxIardw;
    private CX? _cxIardh;
    private CX? _cxIaid;
    private CX? _cxIardx;
    private CX? _cxIardy;
    private CX? _cx;

    // The code for each symbol a Huffman-coded region uses, 7.4.3.1.7.
    private int _symbolCodeLength;
    private HuffmanTable? _symbolCodeTable;

    // Tables of the region's own, from the tables segments it refers to.
    private HuffmanTable? _fsTable;
    private HuffmanTable? _dsTable;
    private HuffmanTable? _table;
    private HuffmanTable? _rdwTable;
    private HuffmanTable? _rdhTable;
    private HuffmanTable? _rdxTable;
    private HuffmanTable? _rdyTable;
    private HuffmanTable? _rSizeTable;

    public TextRegion()
    {
    }

    /// <summary>A region a symbol dictionary decodes an aggregate through, 6.5.8.2.</summary>
    public TextRegion(Jbig2Reader reader, Jbig2DecodeContext context)
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
        ParseHeader();
    }

    private void ParseHeader()
    {
        RegionInfo.ParseHeader(_reader);
        ReadRegionFlags();
        if (_isHuffmanEncoded) ReadHuffmanFlags();
        ReadUseRefinement();
        ReadAmountOfSymbolInstances();

        // 7.4.3.1.7
        if (_segmentHeader?.RtSegments != null) InitSymbols();
        if (_isHuffmanEncoded) SymbolIdCodeLengths();
        else _symbolCodeLength = CeilingLog2(_amountOfSymbols);
        CheckInput();
    }

    private static int CeilingLog2(long value)
    {
        var bits = 0;
        while (1L << bits < value) bits++;
        return bits;
    }

    private void ReadRegionFlags()
    {
        _sbrTemplate = _reader.ReadBit();
        _sbdsOffset = (int)_reader.ReadBits(5);
        if (_sbdsOffset > 0x0f) _sbdsOffset -= 0x20;
        _defaultPixel = _reader.ReadBit();
        _combinationOperator = CombinationOperators.FromCode((int)_reader.ReadBits(2));
        _isTransposed = _reader.ReadBit();
        _referenceCorner = (int)_reader.ReadBits(2);
        _logSbStrips = (int)_reader.ReadBits(2);
        _sbStrips = 1 << _logSbStrips;
        _useRefinement = _reader.ReadBit() == 1;
        _isHuffmanEncoded = _reader.ReadBit() == 1;
    }

    private void ReadHuffmanFlags()
    {
        _reader.ReadBit(); // Bit 15: reserved
        _sbHuffRSize = _reader.ReadBit();
        _sbHuffRdy = (int)_reader.ReadBits(2);
        _sbHuffRdx = (int)_reader.ReadBits(2);
        _sbHuffRdHeight = (int)_reader.ReadBits(2);
        _sbHuffRdWidth = (int)_reader.ReadBits(2);
        _sbHuffDt = (int)_reader.ReadBits(2);
        _sbHuffDs = (int)_reader.ReadBits(2);
        _sbHuffFs = (int)_reader.ReadBits(2);
    }

    private void ReadUseRefinement()
    {
        if (!_useRefinement || _sbrTemplate != 0) return;
        _sbrAtX = new short[2];
        _sbrAtY = new short[2];
        _sbrAtX[0] = (sbyte)_reader.ReadByte();
        _sbrAtY[0] = (sbyte)_reader.ReadByte();
        _sbrAtX[1] = (sbyte)_reader.ReadByte();
        _sbrAtY[1] = (sbyte)_reader.ReadByte();
    }

    private void ReadAmountOfSymbolInstances()
    {
        _amountOfSymbolInstances = _reader.ReadBits(32);

        // Never more symbols than pixels.
        var pixels = RegionInfo.BitmapWidth * (long)RegionInfo.BitmapHeight;
        if (pixels < _amountOfSymbolInstances) _amountOfSymbolInstances = pixels;
    }

    private void CheckInput()
    {
        if (!_useRefinement) _sbrTemplate = 0;
        if (_sbHuffFs == 2 || _sbHuffRdWidth == 2 || _sbHuffRdHeight == 2 || _sbHuffRdx == 2 || _sbHuffRdy == 2)
            throw new Jbig2DataException("a text region's Huffman flags select a table the standard does not allow");
        if (_useRefinement) return;
        _sbHuffRSize = 0;
        _sbHuffRdy = 0;
        _sbHuffRdx = 0;
        _sbHuffRdWidth = 0;
        _sbHuffRdHeight = 0;
    }

    public Jbig2Bitmap GetRegionBitmap()
    {
        if (!_isHuffmanEncoded) SetCodingStatistics();

        // 6.4.5 1)
        _regionBitmap = new Jbig2Bitmap(RegionInfo.BitmapWidth, RegionInfo.BitmapHeight, _context);
        if (_defaultPixel != 0) _regionBitmap.ByteArray.AsSpan().Fill(0xff);

        try
        {
            DecodeSymbolInstances();
        }
        catch (Jbig2DataException e) when (_segmentHeader != null)
        {
            // The instances placed so far stand.
            _context.Warn($"Text region segment {_segmentHeader.SegmentNumber} is decoded in part: {e.Message}");
        }
        return _regionBitmap;
    }

    private void SetCodingStatistics()
    {
        _cxIadt ??= _context.NewCx(512);
        _cxIafs ??= _context.NewCx(512);
        _cxIads ??= _context.NewCx(512);
        _cxIait ??= _context.NewCx(512);
        _cxIari ??= _context.NewCx(512);
        _cxIardw ??= _context.NewCx(512);
        _cxIardh ??= _context.NewCx(512);
        if (_cxIaid is null)
        {
            if (_symbolCodeLength > 30) throw new Jbig2DataException($"symbol codes of {_symbolCodeLength} bits");
            _cxIaid = _context.NewCx(1 << _symbolCodeLength);
        }
        _cxIardx ??= _context.NewCx(512);
        _cxIardy ??= _context.NewCx(512);
        _arithmeticDecoder ??= new ArithmeticDecoder(_reader);
        _integerDecoder ??= new ArithmeticIntegerDecoder(_arithmeticDecoder);
    }

    private ArithmeticIntegerDecoder IntegerDecoder => _integerDecoder!;

    private long DecodeStripT()
    {
        // 6.4.5 2)
        long stripT;
        if (_isHuffmanEncoded)
        {
            // 6.4.6
            if (_sbHuffDt == 3)
            {
                _table ??= GetUserTable((_sbHuffFs == 3 ? 1 : 0) + (_sbHuffDs == 3 ? 1 : 0));
                stripT = _table.Decode(_reader);
            }
            else
            {
                stripT = StandardTables.GetTable(11 + _sbHuffDt).Decode(_reader);
            }
        }
        else
        {
            stripT = IntegerDecoder.Decode(_cxIadt!);
        }
        return stripT * -_sbStrips;
    }

    private void DecodeSymbolInstances()
    {
        var stripT = DecodeStripT();

        // Last two sentences of 6.4.5 2)
        long firstS = 0;
        long instanceCounter = 0;

        // 6.4.5 3 a)
        while (instanceCounter < _amountOfSymbolInstances)
        {
            var dT = DecodeDt();
            if (dT == HuffmanTable.OutOfBand) throw new Jbig2DataException("a strip's DT is out of band");
            stripT += dT;

            // 3 c): the symbol instances of the strip, until OOB.
            var first = true;
            _currentS = 0;
            while (true)
            {
                if (first)
                {
                    // 6.4.7
                    var dfS = DecodeDfS();
                    if (dfS == HuffmanTable.OutOfBand) throw new Jbig2DataException("a strip's first S is out of band");
                    firstS += dfS;
                    _currentS = firstS;
                    first = false;
                }
                else
                {
                    // 6.4.8: OOB ends the strip; so does reaching the number of instances, should OOB never come.
                    var idS = DecodeIdS();
                    if (idS == HuffmanTable.OutOfBand || instanceCounter >= _amountOfSymbolInstances) break;
                    _currentS += idS + _sbdsOffset;
                }

                // 3 c) iii) - v)
                var currentT = DecodeCurrentT();
                var t = stripT + currentT;
                var id = DecodeId();
                var r = DecodeRi();

                // 6.4.11
                var ib = DecodeIb(r, id);

                // vi)
                Blit(ib, t);
                instanceCounter++;
            }
        }
    }

    private long DecodeDt()
    {
        long dT;
        if (_isHuffmanEncoded)
        {
            dT = _sbHuffDt == 3 ? _table!.Decode(_reader) : StandardTables.GetTable(11 + _sbHuffDt).Decode(_reader);
        }
        else
        {
            dT = IntegerDecoder.Decode(_cxIadt!);
        }
        return dT == HuffmanTable.OutOfBand ? dT : dT * _sbStrips;
    }

    private long DecodeDfS()
    {
        if (!_isHuffmanEncoded) return IntegerDecoder.Decode(_cxIafs!);
        if (_sbHuffFs != 3) return StandardTables.GetTable(6 + _sbHuffFs).Decode(_reader);
        _fsTable ??= GetUserTable(0);
        return _fsTable.Decode(_reader);
    }

    private long DecodeIdS()
    {
        if (!_isHuffmanEncoded) return IntegerDecoder.Decode(_cxIads!);
        if (_sbHuffDs != 3) return StandardTables.GetTable(8 + _sbHuffDs).Decode(_reader);
        _dsTable ??= GetUserTable(_sbHuffFs == 3 ? 1 : 0);
        return _dsTable.Decode(_reader);
    }

    private long DecodeCurrentT()
    {
        if (_sbStrips == 1) return 0;
        return _isHuffmanEncoded ? _reader.ReadBits(_logSbStrips) : IntegerDecoder.Decode(_cxIait!);
    }

    private long DecodeId()
    {
        if (!_isHuffmanEncoded) return IntegerDecoder.DecodeIaid(_cxIaid!, _symbolCodeLength);
        return _symbolCodeTable is null ? _reader.ReadBits(_symbolCodeLength) : _symbolCodeTable.Decode(_reader);
    }

    private long DecodeRi()
    {
        if (!_useRefinement) return 0;
        return _isHuffmanEncoded ? _reader.ReadBit() : IntegerDecoder.Decode(_cxIari!);
    }

    private Jbig2Bitmap DecodeIb(long r, long id)
    {
        if (id < 0 || id >= _symbols.Count)
            throw new Jbig2DataException($"symbol {id} of a text region that has {_symbols.Count}");
        var ibo = _symbols[(int)id];
        if (r == 0) return ibo;

        // 6.4.11 1) - 4)
        var rdw = DecodeRdw();
        var rdh = DecodeRdh();
        var rdx = DecodeRdx();
        var rdy = DecodeRdy();

        // 5): a Huffman-coded refinement takes the next SYMINREFSIZE bytes, its own arithmetic decoder at their start.
        long symInRefSize = 0;
        var decoder = _arithmeticDecoder;
        var start = 0L;
        if (_isHuffmanEncoded)
        {
            symInRefSize = DecodeSymInRefSize();
            _reader.SkipBits();
            start = _reader.Position;
            decoder = new ArithmeticDecoder(_reader.Slice(start, symInRefSize));
        }

        // 6)
        var width = ibo.Width + rdw;
        var height = ibo.Height + rdh;
        if (width is < 0 or > int.MaxValue || height is < 0 or > int.MaxValue)
            throw new Jbig2DataException($"a refined symbol of {width} x {height} pixels");
        var referenceDx = (int)((rdw >> 1) + rdx);
        var referenceDy = (int)((rdh >> 1) + rdy);
        _cx ??= _context.NewCx(65536);
        _genericRefinementRegion ??= new GenericRefinementRegion(_reader, _context);
        _genericRefinementRegion.SetParameters(_cx, decoder!, _sbrTemplate, (int)width, (int)height, ibo,
            referenceDx, referenceDy, false, _sbrAtX, _sbrAtY);
        var ib = _genericRefinementRegion.GetRegionBitmap();

        // 7)
        if (_isHuffmanEncoded) _reader.Seek(start + symInRefSize);
        return ib;
    }

    /// <summary>The position among the tables segments referred to of the table for a field coded with a table of the region's own.</summary>
    private int UserTableIndex(params int[] earlierSelections) => earlierSelections.Count(s => s == 3);

    private long DecodeRdw()
    {
        if (!_isHuffmanEncoded) return IntegerDecoder.Decode(_cxIardw!);
        if (_sbHuffRdWidth != 3) return StandardTables.GetTable(14 + _sbHuffRdWidth).Decode(_reader);
        _rdwTable ??= GetUserTable(UserTableIndex(_sbHuffFs, _sbHuffDs, _sbHuffDt));
        return _rdwTable.Decode(_reader);
    }

    private long DecodeRdh()
    {
        if (!_isHuffmanEncoded) return IntegerDecoder.Decode(_cxIardh!);
        if (_sbHuffRdHeight != 3) return StandardTables.GetTable(14 + _sbHuffRdHeight).Decode(_reader);
        _rdhTable ??= GetUserTable(UserTableIndex(_sbHuffFs, _sbHuffDs, _sbHuffDt, _sbHuffRdWidth));
        return _rdhTable.Decode(_reader);
    }

    private long DecodeRdx()
    {
        if (!_isHuffmanEncoded) return IntegerDecoder.Decode(_cxIardx!);
        if (_sbHuffRdx != 3) return StandardTables.GetTable(14 + _sbHuffRdx).Decode(_reader);
        _rdxTable ??= GetUserTable(UserTableIndex(_sbHuffFs, _sbHuffDs, _sbHuffDt, _sbHuffRdWidth, _sbHuffRdHeight));
        return _rdxTable.Decode(_reader);
    }

    private long DecodeRdy()
    {
        if (!_isHuffmanEncoded) return IntegerDecoder.Decode(_cxIardy!);
        if (_sbHuffRdy != 3) return StandardTables.GetTable(14 + _sbHuffRdy).Decode(_reader);
        _rdyTable ??= GetUserTable(UserTableIndex(_sbHuffFs, _sbHuffDs, _sbHuffDt, _sbHuffRdWidth, _sbHuffRdHeight, _sbHuffRdx));
        return _rdyTable.Decode(_reader);
    }

    private long DecodeSymInRefSize()
    {
        if (_sbHuffRSize == 0) return StandardTables.GetTable(1).Decode(_reader);
        _rSizeTable ??= GetUserTable(UserTableIndex(_sbHuffFs, _sbHuffDs, _sbHuffDt, _sbHuffRdWidth, _sbHuffRdHeight, _sbHuffRdx, _sbHuffRdy));
        return _rSizeTable.Decode(_reader);
    }

    private void Blit(Jbig2Bitmap ib, long t)
    {
        if (_isTransposed == 0 && _referenceCorner is 2 or 3) _currentS += ib.Width - 1;
        else if (_isTransposed == 1 && _referenceCorner is 0 or 2) _currentS += ib.Height - 1;

        // vii) - viii)
        var s = _currentS;
        if (_isTransposed == 1) (t, s) = (s, t);

        switch (_referenceCorner)
        {
            case 0: // bottom left
                t -= ib.Height - 1;
                break;
            case 2: // bottom right
                t -= ib.Height - 1;
                s -= ib.Width - 1;
                break;
            case 3: // top right
                s -= ib.Width - 1;
                break;
        }

        Jbig2Bitmaps.Blit(ib, _regionBitmap!, (int)Math.Clamp(s, int.MinValue, int.MaxValue), (int)Math.Clamp(t, int.MinValue, int.MaxValue), _combinationOperator, _context);

        // x)
        if (_isTransposed == 0 && _referenceCorner is 0 or 1) _currentS += ib.Width - 1;
        if (_isTransposed == 1 && _referenceCorner is 1 or 3) _currentS += ib.Height - 1;
    }

    private void InitSymbols()
    {
        foreach (var segment in _segmentHeader!.ReferredTo())
        {
            if (segment.SegmentType != 0) continue;
            var sd = (SymbolDictionary)segment.GetSegmentData();
            _symbols.AddRange(sd.GetDictionary());
        }
        _amountOfSymbols = _symbols.Count;
    }

    private HuffmanTable GetUserTable(int tablePosition)
    {
        var tableCounter = 0;
        if (_segmentHeader != null)
        {
            foreach (var referred in _segmentHeader.ReferredTo())
            {
                if (referred.SegmentType != 53) continue;
                if (tableCounter++ == tablePosition) return ((Table)referred.GetSegmentData()).HuffmanTable;
            }
        }
        throw new Jbig2DataException($"a text region uses table {tablePosition + 1} of its own, and refers to {tableCounter}");
    }

    /// <summary>7.4.3.1.7: the symbol ID Huffman table, its code lengths themselves run-length coded.</summary>
    private void SymbolIdCodeLengths()
    {
        // 1) - 2)
        var runCodeTable = new List<HuffmanTable.Code>();
        for (var i = 0; i < 35; i++)
        {
            var prefLen = (int)_reader.ReadBits(4);
            if (prefLen > 0) runCodeTable.Add(new HuffmanTable.Code(prefLen, 0, i, false));
        }
        var ht = new HuffmanTable(runCodeTable);

        // 3) - 5)
        long previousCodeLength = 0;
        var counter = 0;
        var sbSymCodes = new List<HuffmanTable.Code>();
        _context.Charge(64L * _amountOfSymbols);
        while (counter < _amountOfSymbols)
        {
            var code = ht.Decode(_reader);
            if (code < 32)
            {
                if (code > 0) sbSymCodes.Add(new HuffmanTable.Code((int)code, 0, counter, false));
                previousCodeLength = code;
                counter++;
                continue;
            }

            long runLength = 0;
            long currCodeLength = 0;
            if (code == 32)
            {
                runLength = 3 + _reader.ReadBits(2);
                if (counter > 0) currCodeLength = previousCodeLength;
            }
            else if (code == 33)
            {
                runLength = 3 + _reader.ReadBits(3);
            }
            else if (code == 34)
            {
                runLength = 11 + _reader.ReadBits(7);
            }

            for (var j = 0; j < runLength && counter < _amountOfSymbols; j++)
            {
                if (currCodeLength > 0) sbSymCodes.Add(new HuffmanTable.Code((int)currCodeLength, 0, counter, false));
                counter++;
            }
        }

        // 6)
        _reader.SkipBits();

        // 7)
        _symbolCodeTable = new HuffmanTable(sbSymCodes);
    }

    /// <summary>The contexts a symbol dictionary shares with the text region it decodes an aggregate through.</summary>
    internal void SetContexts(CX cx, CX cxIadt, CX cxIafs, CX cxIads, CX cxIait, CX? cxIaid, CX cxIardw, CX cxIardh,
        CX cxIardx, CX cxIardy)
    {
        _cx = cx;
        _cxIadt = cxIadt;
        _cxIafs = cxIafs;
        _cxIads = cxIads;
        _cxIait = cxIait;
        _cxIaid = cxIaid;
        _cxIardw = cxIardw;
        _cxIardh = cxIardh;
        _cxIardx = cxIardx;
        _cxIardy = cxIardy;
    }

    /// <summary>The parameters of an aggregate, Table 17.</summary>
    internal void SetParameters(ArithmeticDecoder? arithmeticDecoder, ArithmeticIntegerDecoder? iDecoder,
        bool isHuffmanEncoded, bool sbRefine, int sbw, int sbh, long sbNumInstances, int sbStrips, int sbNumSyms,
        int sbDefaultPixel, int sbCombinationOperator, int transposed, int refCorner, int sbdsOffset, int sbHuffFs,
        int sbHuffDs, int sbHuffDt, int sbHuffRdWidth, int sbHuffRdHeight, int sbHuffRdx, int sbHuffRdy,
        int sbHuffRSize, int sbrTemplate, short[]? sbrAtX, short[]? sbrAtY, List<Jbig2Bitmap> sbSyms,
        int sbSymCodeLen)
    {
        _arithmeticDecoder = arithmeticDecoder;
        _integerDecoder = iDecoder;
        _isHuffmanEncoded = isHuffmanEncoded;
        _useRefinement = sbRefine;
        RegionInfo.BitmapWidth = sbw;
        RegionInfo.BitmapHeight = sbh;
        _amountOfSymbolInstances = Math.Min(sbNumInstances, (long)sbw * sbh);
        _sbStrips = sbStrips;
        _amountOfSymbols = sbNumSyms;
        _defaultPixel = sbDefaultPixel;
        _combinationOperator = CombinationOperators.FromCode(sbCombinationOperator);
        _isTransposed = transposed;
        _referenceCorner = refCorner;
        _sbdsOffset = sbdsOffset;
        _sbHuffFs = sbHuffFs;
        _sbHuffDs = sbHuffDs;
        _sbHuffDt = sbHuffDt;
        _sbHuffRdWidth = sbHuffRdWidth;
        _sbHuffRdHeight = sbHuffRdHeight;
        _sbHuffRdx = sbHuffRdx;
        _sbHuffRdy = sbHuffRdy;
        _sbHuffRSize = sbHuffRSize;
        _sbrTemplate = sbrTemplate;
        _sbrAtX = sbrAtX;
        _sbrAtY = sbrAtY;
        _symbols = sbSyms;
        _symbolCodeLength = sbSymCodeLen;
    }
}
