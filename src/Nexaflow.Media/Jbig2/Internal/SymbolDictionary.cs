/*
 * Ported from Apache PDFBox JBIG2 ImageIO (pdfbox-jbig2) by way of PdfPig.Filters.Jbig2.PdfboxJbig2 0.1.16.2:
 * segments/SymbolDictionary.java.
 * Licensed under the Apache License, Version 2.0; see the LICENSE and NOTICE files beside this folder.
 * Modified for Nexaflow: an MMR coded height class takes exactly its BMSIZE bytes; counts and identifiers the data
 * gives are checked before they size or index anything; symbols are charged to the decode's memory limit.
 */

namespace Nexaflow.Media.Jbig2.Internal;

/// <summary>
/// A symbol dictionary, 6.5 and 7.4.2: symbols by height class, each coded directly, as an aggregate or refinement
/// of others, or — Huffman coded — together in one collective bitmap; and which of them, with those it imports, it
/// exports.
/// </summary>
internal sealed class SymbolDictionary : IJbig2Dictionary
{
    private readonly Lock _gate = new();
    private Jbig2Reader _reader = null!;
    private Jbig2DecodeContext _context = null!;
    private SegmentHeader _segmentHeader = null!;

    // Symbol dictionary flags, 7.4.2.1.1
    private int _sdrTemplate;
    private byte _sdTemplate;
    private bool _isCodingContextRetained;
    private bool _isCodingContextUsed;
    private int _sdHuffAggInstanceSelection;
    private int _sdHuffBmSizeSelection;
    private int _sdHuffDecodeWidthSelection;
    private int _sdHuffDecodeHeightSelection;
    private bool _useRefinementAggregation;
    private bool _isHuffmanEncoded;

    // Symbol dictionary AT flags, 7.4.2.1.2, and refinement AT flags, 7.4.2.1.3
    private short[]? _sdAtX;
    private short[]? _sdAtY;
    private short[]? _sdrAtX;
    private short[]? _sdrAtY;

    // 7.4.2.1.4 - 7.4.2.1.5
    private int _amountOfExportSymbols;
    private int _amountOfNewSymbols;

    private int _amountOfImportedSymbols;
    private List<Jbig2Bitmap>? _importSymbols;
    private int _amountOfDecodedSymbols;
    private Jbig2Bitmap[] _newSymbols = [];

    // Tables of the dictionary's own, from the tables segments it refers to.
    private HuffmanTable? _dhTable;
    private HuffmanTable? _dwTable;
    private HuffmanTable? _bmSizeTable;
    private HuffmanTable? _aggInstTable;

    private List<Jbig2Bitmap>? _exportSymbols;
    private List<Jbig2Bitmap>? _sbSymbols;

    private ArithmeticDecoder? _arithmeticDecoder;
    private ArithmeticIntegerDecoder? _iDecoder;
    private TextRegion? _textRegion;
    private GenericRegion? _genericRegion;
    private GenericRefinementRegion? _genericRefinementRegion;
    private CX? _cx;
    private SymbolDictionary? _lastSymbolDictionary;
    private CX? _cxIadh;
    private CX? _cxIadw;
    private CX? _cxIaai;
    private CX? _cxIaex;
    private CX? _cxIardx;
    private CX? _cxIardy;
    private CX? _cxIadt;
    private CX? _cxIaid;
    private int _sbSymCodeLen;

    public void Init(SegmentHeader header, Jbig2Reader reader)
    {
        _reader = reader;
        _context = header.Context;
        _segmentHeader = header;
        ParseHeader();
    }

    private void ParseHeader()
    {
        ReadRegionFlags();
        if (!_isHuffmanEncoded) ReadAtPixels(_sdTemplate == 0 ? 4 : 1);
        if (_useRefinementAggregation && _sdrTemplate == 0) ReadRefinementAtPixels();
        _amountOfExportSymbols = (int)_reader.ReadBits(32);
        _amountOfNewSymbols = (int)_reader.ReadBits(32);
        if (_amountOfExportSymbols < 0 || _amountOfNewSymbols < 0)
            throw new Jbig2DataException($"a symbol dictionary of {(uint)_amountOfNewSymbols} new symbols exporting {(uint)_amountOfExportSymbols}");

        SetInSyms();

        // 7.4.2.2 3): the contexts of the last dictionary referred to, when it kept them and this one uses them.
        var isContextAdopted = false;
        var referred = _segmentHeader.ReferredTo().ToList();
        for (var i = referred.Count - 1; i >= 0; i--)
        {
            if (referred[i].SegmentType != 0) continue;
            _lastSymbolDictionary = (SymbolDictionary)referred[i].GetSegmentData();
            if (_isCodingContextUsed && _lastSymbolDictionary._isCodingContextRetained)
            {
                ValidateContextValues(_lastSymbolDictionary);
                isContextAdopted = true;
            }
            break;
        }
        if (_isCodingContextUsed && !isContextAdopted)
            throw new Jbig2DataException(_lastSymbolDictionary is null
                ? "a symbol dictionary reuses coding contexts, and refers to no dictionary"
                : "a symbol dictionary reuses coding contexts the dictionary it refers to did not keep");
        CheckInput();
    }

    private void ReadRegionFlags()
    {
        _reader.ReadBits(3); // Bits 13-15: reserved
        _sdrTemplate = _reader.ReadBit();
        _sdTemplate = (byte)_reader.ReadBits(2);
        _isCodingContextRetained = _reader.ReadBit() == 1;
        _isCodingContextUsed = _reader.ReadBit() == 1;
        _sdHuffAggInstanceSelection = _reader.ReadBit();
        _sdHuffBmSizeSelection = _reader.ReadBit();
        _sdHuffDecodeWidthSelection = (int)_reader.ReadBits(2);
        _sdHuffDecodeHeightSelection = (int)_reader.ReadBits(2);
        _useRefinementAggregation = _reader.ReadBit() == 1;
        _isHuffmanEncoded = _reader.ReadBit() == 1;
    }

    private void ReadAtPixels(int amountOfPixels)
    {
        _sdAtX = new short[amountOfPixels];
        _sdAtY = new short[amountOfPixels];
        for (var i = 0; i < amountOfPixels; i++)
        {
            _sdAtX[i] = (sbyte)_reader.ReadByte();
            _sdAtY[i] = (sbyte)_reader.ReadByte();
        }
    }

    private void ReadRefinementAtPixels()
    {
        _sdrAtX = new short[2];
        _sdrAtY = new short[2];
        for (var i = 0; i < 2; i++)
        {
            _sdrAtX[i] = (sbyte)_reader.ReadByte();
            _sdrAtY[i] = (sbyte)_reader.ReadByte();
        }
    }

    private void SetInSyms()
    {
        if (_segmentHeader.RtSegments != null) RetrieveImportSymbols();
        else _importSymbols = [];
    }

    private void ValidateContextValues(SymbolDictionary sd)
    {
        if (_isHuffmanEncoded != sd._isHuffmanEncoded
            || _useRefinementAggregation != sd._useRefinementAggregation
            || _sdTemplate != sd._sdTemplate
            || _sdrTemplate != sd._sdrTemplate
            || !SameValues(_sdAtX, sd._sdAtX)
            || !SameValues(_sdAtY, sd._sdAtY)
            || !SameValues(_sdrAtX, sd._sdrAtX)
            || !SameValues(_sdrAtY, sd._sdrAtY))
            throw new Jbig2DataException("a symbol dictionary reuses coding contexts made for other parameters");
    }

    private static bool SameValues(short[]? a, short[]? b) =>
        a is null || b is null ? a == b : a.AsSpan().SequenceEqual(b);

    private void CheckInput()
    {
        if (_isHuffmanEncoded)
        {
            _sdTemplate = 0;
            if (!_useRefinementAggregation)
            {
                _isCodingContextRetained = false;
                _isCodingContextUsed = false;
            }
        }
        else
        {
            _sdHuffBmSizeSelection = 0;
            _sdHuffDecodeWidthSelection = 0;
            _sdHuffDecodeHeightSelection = 0;
        }
        if (!_useRefinementAggregation) _sdrTemplate = 0;
        if (!_isHuffmanEncoded || !_useRefinementAggregation) _sdHuffAggInstanceSelection = 0;
    }

    /// <summary>The symbols exported, decoded once — by whichever page of a globals stream first needs them.</summary>
    public List<Jbig2Bitmap> GetDictionary()
    {
        lock (_gate) return _exportSymbols ?? Decode();
    }

    private List<Jbig2Bitmap> Decode()
    {

        if (_useRefinementAggregation) _sbSymCodeLen = GetSbSymCodeLen();

        // Decodes all referred segments, including the last symbol dictionary.
        SetSymbolsArray();

        // The bitmap contexts serve the arithmetic path and the Huffman path with refinement both.
        if (!_isHuffmanEncoded || _useRefinementAggregation)
        {
            if (_isCodingContextUsed)
                _cx = _lastSymbolDictionary!._cx?.Copy() ?? throw new Jbig2DataException("a symbol dictionary reuses bitmap coding contexts the dictionary it refers to never made");
            else _cx = _context.NewCx(65536);
        }

        // The integer decoders serve the arithmetic path only.
        if (!_isHuffmanEncoded) ResetIntegerCoderStatistics();

        // 6.5.5 1) - 2)
        _context.Charge(8L * _amountOfNewSymbols);
        _newSymbols = new Jbig2Bitmap[_amountOfNewSymbols];
        int[]? newSymbolsWidths = null;
        if (_isHuffmanEncoded && !_useRefinementAggregation)
        {
            _context.Charge(4L * _amountOfNewSymbols);
            newSymbolsWidths = new int[_amountOfNewSymbols];
        }

        // 6.5.5 3)
        long heightClassHeight = 0;
        _amountOfDecodedSymbols = 0;
        var emptyClasses = 0;

        // 6.5.5 4 a)
        while (_amountOfDecodedSymbols < _amountOfNewSymbols)
        {
            // 4 b)
            var deltaHeight = DecodeHeightClassDeltaHeight();
            if (deltaHeight == HuffmanTable.OutOfBand) throw new Jbig2DataException("a height class's height is out of band");
            heightClassHeight += deltaHeight;
            if (heightClassHeight is < 0 or > int.MaxValue) throw new Jbig2DataException($"a height class {heightClassHeight} pixels high");

            long symbolWidth = 0;
            long totalWidth = 0;
            var heightClassFirstSymbolIndex = _amountOfDecodedSymbols;

            // 4 c): until OOB, or until every symbol is decoded, should OOB never come.
            while (true)
            {
                var differenceWidth = DecodeDifferenceWidth();
                if (differenceWidth == HuffmanTable.OutOfBand || _amountOfDecodedSymbols >= _amountOfNewSymbols) break;

                symbolWidth += differenceWidth;
                totalWidth += symbolWidth;
                if (symbolWidth is < 0 or > int.MaxValue || totalWidth > int.MaxValue)
                    throw new Jbig2DataException($"a symbol {symbolWidth} pixels wide");

                // 4 c) ii) - iii)
                if (!_isHuffmanEncoded || _useRefinementAggregation)
                {
                    if (!_useRefinementAggregation) DecodeDirectlyThroughGenericRegion((int)symbolWidth, (int)heightClassHeight);
                    else DecodeAggregate((int)symbolWidth, (int)heightClassHeight);
                }
                else
                {
                    newSymbolsWidths![_amountOfDecodedSymbols] = (int)symbolWidth;
                }
                _amountOfDecodedSymbols++;
            }

            // A height class of no symbols decodes nothing; two in a row are data that has run out, which gives them forever.
            emptyClasses = _amountOfDecodedSymbols == heightClassFirstSymbolIndex ? emptyClasses + 1 : 0;
            if (emptyClasses == 2) throw new Jbig2DataException("two height classes in a row hold no symbols");

            // 4 d): a Huffman-coded height class's symbols are in one collective bitmap.
            if (_isHuffmanEncoded && !_useRefinementAggregation)
            {
                // 6.5.9
                var bmSize = _sdHuffBmSizeSelection == 0 ? StandardTables.GetTable(1).Decode(_reader) : HuffDecodeBmSize();
                _reader.SkipBits();
                var collective = DecodeHeightClassCollectiveBitmap(bmSize, (int)heightClassHeight, (int)totalWidth);
                _reader.SkipBits();
                DecodeHeightClassBitmap(collective, heightClassFirstSymbolIndex, (int)heightClassHeight, newSymbolsWidths!);
            }
        }

        // 6.5.10
        SetExportedSymbols(GetToExportFlags());
        return _exportSymbols!;
    }

    private void ResetIntegerCoderStatistics()
    {
        _cxIadt = _context.NewCx(512);
        _cxIadh = _context.NewCx(512);
        _cxIadw = _context.NewCx(512);
        _cxIaai = _context.NewCx(512);
        _cxIaex = _context.NewCx(512);
        if (_useRefinementAggregation)
        {
            if (_sbSymCodeLen > 30) throw new Jbig2DataException($"symbol codes of {_sbSymCodeLen} bits");
            _cxIaid = _context.NewCx(1 << _sbSymCodeLen);
            _cxIardx = _context.NewCx(512);
            _cxIardy = _context.NewCx(512);
        }
        _arithmeticDecoder = new ArithmeticDecoder(_reader);
        _iDecoder = new ArithmeticIntegerDecoder(_arithmeticDecoder);
    }

    private void DecodeHeightClassBitmap(Jbig2Bitmap heightClassCollectiveBitmap, int heightClassFirstSymbol,
        int heightClassHeight, int[] newSymbolsWidths)
    {
        var startColumn = 0;
        for (var i = heightClassFirstSymbol; i < _amountOfDecodedSymbols; i++)
        {
            var roi = new Jbig2Rectangle(startColumn, 0, newSymbolsWidths[i], heightClassHeight);
            var symbolBitmap = Jbig2Bitmaps.Extract(roi, heightClassCollectiveBitmap, _context);
            _newSymbols[i] = symbolBitmap;
            _sbSymbols!.Add(symbolBitmap);
            startColumn += newSymbolsWidths[i];
        }
    }

    private void DecodeAggregate(int symbolWidth, int heightClassHeight)
    {
        // 6.5.8.2.1
        var instances = _isHuffmanEncoded ? HuffDecodeRefAggNInst() : _iDecoder!.Decode(_cxIaai!);
        if (instances == HuffmanTable.OutOfBand) throw new Jbig2DataException("a symbol's number of aggregated instances is out of band");

        if (instances > 1) DecodeThroughTextRegion(symbolWidth, heightClassHeight, instances); // 6.5.8.2 2)
        else if (instances == 1) DecodeRefinedSymbol(symbolWidth, heightClassHeight); // 6.5.8.2 3)
        else throw new Jbig2DataException($"a symbol aggregated from {instances} instances");
    }

    private long HuffDecodeRefAggNInst()
    {
        if (_sdHuffAggInstanceSelection == 0) return StandardTables.GetTable(1).Decode(_reader);
        _aggInstTable ??= GetUserTable(UserTableIndex(_sdHuffDecodeHeightSelection, _sdHuffDecodeWidthSelection, _sdHuffBmSizeSelection));
        return _aggInstTable.Decode(_reader);
    }

    private void DecodeThroughTextRegion(int symbolWidth, int heightClassHeight, long instances)
    {
        if (_textRegion is null)
        {
            _textRegion = new TextRegion(_reader, _context);
            _textRegion.SetContexts(_cx!, _context.NewCx(512), _context.NewCx(512), _context.NewCx(512), _context.NewCx(512), _cxIaid, _context.NewCx(512),
                _context.NewCx(512), _context.NewCx(512), _context.NewCx(512));
        }

        // 6.5.8.2.4: the symbols so far, imported and new.
        SetSymbolsArray();

        // 6.5.8.2 2): Table 17.
        _textRegion.SetParameters(_arithmeticDecoder, _iDecoder, _isHuffmanEncoded, true, symbolWidth, heightClassHeight,
            instances, 1, _amountOfImportedSymbols + _amountOfDecodedSymbols, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            _sdrTemplate, _sdrAtX, _sdrAtY, _sbSymbols!, _sbSymCodeLen);
        AddSymbol(_textRegion);
    }

    private void DecodeRefinedSymbol(int symbolWidth, int heightClassHeight)
    {
        int id, rdx, rdy;
        long symInRefSize = 0;
        long start = 0;
        var decoder = _arithmeticDecoder;
        if (_isHuffmanEncoded)
        {
            // 6.5.8.2.2 2) - 5): the refinement takes the next SYMINREFSIZE bytes, its own arithmetic decoder at their start.
            id = (int)_reader.ReadBits(_sbSymCodeLen);
            rdx = (int)StandardTables.GetTable(15).Decode(_reader);
            rdy = (int)StandardTables.GetTable(15).Decode(_reader);
            symInRefSize = StandardTables.GetTable(1).Decode(_reader);
            _reader.SkipBits();
            start = _reader.Position;
            decoder = new ArithmeticDecoder(_reader.Slice(start, symInRefSize));
        }
        else
        {
            id = _iDecoder!.DecodeIaid(_cxIaid!, _sbSymCodeLen);
            rdx = (int)_iDecoder.Decode(_cxIardx!);
            rdy = (int)_iDecoder.Decode(_cxIardy!);
        }

        // 6)
        SetSymbolsArray();
        if ((uint)id >= (uint)_sbSymbols!.Count) throw new Jbig2DataException($"a refinement of symbol {id} of {_sbSymbols.Count}");
        var ibo = _sbSymbols[id];

        // Table 18
        _genericRefinementRegion ??= new GenericRefinementRegion(_reader, _context);
        _genericRefinementRegion.SetParameters(_cx!, decoder!, _sdrTemplate, symbolWidth, heightClassHeight, ibo, rdx, rdy,
            false, _sdrAtX, _sdrAtY);
        AddSymbol(_genericRefinementRegion);

        // 7)
        if (_isHuffmanEncoded) _reader.Seek(start + symInRefSize);
    }

    private void DecodeDirectlyThroughGenericRegion(int symWidth, int hcHeight)
    {
        _genericRegion ??= new GenericRegion(_reader, _context);

        // Table 16
        _genericRegion.SetParameters(false, _sdTemplate, false, false, _sdAtX, _sdAtY, symWidth, hcHeight, _cx,
            _arithmeticDecoder);
        AddSymbol(_genericRegion);
    }

    private void AddSymbol(IRegion region)
    {
        var symbol = region.GetRegionBitmap();
        _newSymbols[_amountOfDecodedSymbols] = symbol;
        _sbSymbols!.Add(symbol);
    }

    private long DecodeDifferenceWidth()
    {
        if (!_isHuffmanEncoded) return _iDecoder!.Decode(_cxIadw!);
        switch (_sdHuffDecodeWidthSelection)
        {
            case 0:
                return StandardTables.GetTable(2).Decode(_reader);
            case 1:
                return StandardTables.GetTable(3).Decode(_reader);
            case 3:
                _dwTable ??= GetUserTable(UserTableIndex(_sdHuffDecodeHeightSelection));
                return _dwTable.Decode(_reader);
            default:
                throw new Jbig2DataException("a symbol dictionary selects a width table the standard does not allow");
        }
    }

    private long DecodeHeightClassDeltaHeight()
    {
        if (!_isHuffmanEncoded) return _iDecoder!.Decode(_cxIadh!);
        switch (_sdHuffDecodeHeightSelection)
        {
            case 0:
                return StandardTables.GetTable(4).Decode(_reader);
            case 1:
                return StandardTables.GetTable(5).Decode(_reader);
            case 3:
                _dhTable ??= GetUserTable(0);
                return _dhTable.Decode(_reader);
            default:
                throw new Jbig2DataException("a symbol dictionary selects a height table the standard does not allow");
        }
    }

    /// <summary>6.5.9: a height class's collective bitmap, uncompressed or MMR coded in exactly <paramref name="bmSize"/> bytes.</summary>
    private Jbig2Bitmap DecodeHeightClassCollectiveBitmap(long bmSize, int heightClassHeight, int totalWidth)
    {
        if (bmSize == 0)
        {
            var collective = new Jbig2Bitmap(totalWidth, heightClassHeight, _context);
            for (var i = 0; i < collective.ByteArray.Length; i++) collective.SetByte(i, _reader.ReadByte());
            return collective;
        }

        if (bmSize < 0) throw new Jbig2DataException($"a collective bitmap of {bmSize} bytes");
        var start = _reader.Position;
        _genericRegion ??= new GenericRegion(_reader, _context);
        _genericRegion.SetParameters(true, start, bmSize, heightClassHeight, totalWidth);
        var bitmap = _genericRegion.GetRegionBitmap();
        _reader.Seek(start + bmSize);
        return bitmap;
    }

    private void SetExportedSymbols(int[] toExportFlags)
    {
        _exportSymbols = new List<Jbig2Bitmap>(Math.Min(_amountOfExportSymbols, toExportFlags.Length));
        for (var i = 0; i < toExportFlags.Length; i++)
        {
            if (toExportFlags[i] != 1) continue;
            _exportSymbols.Add(i < _amountOfImportedSymbols ? _importSymbols![i] : _newSymbols[i - _amountOfImportedSymbols]);
        }
    }

    /// <summary>6.5.10 1) - 5): the export flags, run-length coded.</summary>
    private int[] GetToExportFlags()
    {
        var total = (long)_amountOfImportedSymbols + _amountOfNewSymbols;
        if (total > int.MaxValue) throw new Jbig2DataException($"a symbol dictionary of {total} symbols");
        _context.Charge(4 * total);
        var exportFlags = new int[total];
        var exIndex = 0;
        var curExFlag = 0;
        var emptyRuns = 0;
        while (exIndex < total)
        {
            var exRunLength = _isHuffmanEncoded ? StandardTables.GetTable(1).Decode(_reader) : _iDecoder!.Decode(_cxIaex!);
            if (exRunLength < 0 || exRunLength > total - exIndex) throw new Jbig2DataException($"an export run of {exRunLength}");

            // A run of none only switches the flag; two in a row decode nothing, and data that runs out gives them forever.
            emptyRuns = exRunLength == 0 ? emptyRuns + 1 : 0;
            if (emptyRuns == 2) throw new Jbig2DataException("two export runs in a row are empty");
            exportFlags.AsSpan(exIndex, (int)exRunLength).Fill(curExFlag);
            exIndex += (int)exRunLength;
            curExFlag ^= 1;
        }
        return exportFlags;
    }

    private long HuffDecodeBmSize()
    {
        _bmSizeTable ??= GetUserTable(UserTableIndex(_sdHuffDecodeHeightSelection, _sdHuffDecodeWidthSelection));
        return _bmSizeTable.Decode(_reader);
    }

    private int GetSbSymCodeLen()
    {
        var symbols = (long)_amountOfImportedSymbols + _amountOfNewSymbols;
        var bits = 0;
        while (1L << bits < symbols) bits++;
        return _isHuffmanEncoded ? Math.Max(bits, 1) : bits;
    }

    private void SetSymbolsArray()
    {
        if (_importSymbols is null) RetrieveImportSymbols();
        _sbSymbols ??= [.. _importSymbols!];
    }

    private void RetrieveImportSymbols()
    {
        _importSymbols = [];
        foreach (var referred in _segmentHeader.ReferredTo())
        {
            if (referred.SegmentType != 0) continue;
            var sd = (SymbolDictionary)referred.GetSegmentData();
            var exported = sd.GetDictionary();
            _importSymbols.AddRange(exported);
            _amountOfImportedSymbols += exported.Count;
        }
    }

    /// <summary>The position among the tables segments referred to of the table for a field coded with a table of the dictionary's own.</summary>
    private static int UserTableIndex(params int[] earlierSelections) => earlierSelections.Count(s => s == 3);

    private HuffmanTable GetUserTable(int tablePosition)
    {
        var tableCounter = 0;
        foreach (var referred in _segmentHeader.ReferredTo())
        {
            if (referred.SegmentType != 53) continue;
            if (tableCounter++ == tablePosition) return ((Table)referred.GetSegmentData()).HuffmanTable;
        }
        throw new Jbig2DataException($"a symbol dictionary uses table {tablePosition + 1} of its own, and refers to {tableCounter}");
    }
}
