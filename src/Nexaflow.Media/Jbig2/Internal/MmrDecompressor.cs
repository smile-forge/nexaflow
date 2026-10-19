/*
 * Ported from Apache PDFBox JBIG2 ImageIO (pdfbox-jbig2) by way of PdfPig.Filters.Jbig2.PdfboxJbig2 0.1.16.2:
 * decoder/mmr/MMRDecompressor.java and MMRConstants.java.
 * Licensed under the Apache License, Version 2.0; see the LICENSE and NOTICE files beside this folder.
 * Modified for Nexaflow: the codes are read straight from the segment's bytes, zeros past their end; the code tables
 * are built once, up front; a code the tables do not hold ends the bitmap where it is.
 */

namespace Nexaflow.Media.Jbig2.Internal;

/// <summary>
/// MMR — the two-dimensional coding of ITU-T T.6 — as a generic region may be coded, 6.2.6. Past the end of the data
/// the codes read as zeros; a line the codes run off ends the bitmap there, the rest of it white.
/// </summary>
internal sealed class MmrDecompressor
{
    private const int FirstLevelTableSize = 8;
    private const int FirstLevelTableMask = (1 << FirstLevelTableSize) - 1;
    private const int SecondLevelTableSize = 5;
    private const int SecondLevelTableMask = (1 << SecondLevelTableSize) - 1;
    private const int CodeOffset = 24;

    private static readonly Code?[] WhiteTable = CreateTable(MmrConstants.WhiteCodes);
    private static readonly Code?[] BlackTable = CreateTable(MmrConstants.BlackCodes);
    private static readonly Code?[] ModeTable = CreateTable(MmrConstants.ModeCodes);

    private readonly int _width;
    private readonly int _height;
    private readonly RunData _data;
    private readonly Jbig2DecodeContext _context;

    public MmrDecompressor(int width, int height, Jbig2Reader reader, Jbig2DecodeContext context)
    {
        _width = width;
        _height = height;
        _data = new RunData(reader);
        _context = context;
    }

    private sealed class Code
    {
        public Code(int bitLength, int codeWord, int runLength)
        {
            BitLength = bitLength;
            CodeWord = codeWord;
            RunLength = runLength;
        }

        public Code?[]? SubTable { get; set; }

        public int BitLength { get; }

        public int CodeWord { get; }

        public int RunLength { get; }
    }

    /// <summary>The coded bits, addressed by bit offset; zeros past the end.</summary>
    private sealed class RunData(Jbig2Reader reader)
    {
        private readonly byte[] _bytes = reader.Bytes;
        private readonly int _start = reader.Start;
        private readonly int _length = reader.Length;

        /// <summary>The bit the next code starts at.</summary>
        public int Offset { get; set; }

        private int Byte(int index) => (uint)index < (uint)_length ? _bytes[_start + index] : 0;

        /// <summary>The code at <see cref="Offset"/>, looked up in two levels; null where the table holds none.</summary>
        public Code? UncompressGetCode(Code?[] table)
        {
            var at = Offset >> 3;
            var code = (Byte(at) << 16 | Byte(at + 1) << 8 | Byte(at + 2)) << (Offset & 7) & 0xFFFFFF;
            var result = table[code >> (CodeOffset - FirstLevelTableSize)];
            if (result?.SubTable is { } subTable)
                result = subTable[code >> (CodeOffset - FirstLevelTableSize - SecondLevelTableSize) & SecondLevelTableMask];
            return result;
        }

        public void Align() => Offset = (Offset + 7) >> 3 << 3;
    }

    public Jbig2Bitmap Uncompress()
    {
        var result = new Jbig2Bitmap(_width, _height, _context);

        // Two rows of run offsets, swapped after every line.
        var offsetCount = _width + 5;
        _context.Charge(2L * offsetCount * sizeof(int));
        Span<int> currentOffsets = new int[offsetCount];
        Span<int> referenceOffsets = new int[offsetCount];
        referenceOffsets[0] = _width;
        var refRunLength = 1;

        for (var line = 0; line < _height; line++)
        {
            var count = Uncompress2D(_data, referenceOffsets, refRunLength, currentOffsets, _width);
            if (count == MmrConstants.Eof) break;
            if (count > 0) FillBitmap(result, line, currentOffsets, count);

            var swap = referenceOffsets;
            referenceOffsets = currentOffsets;
            currentOffsets = swap;
            refRunLength = count;
        }

        DetectAndSkipEol();
        _data.Align();
        return result;
    }

    private void DetectAndSkipEol()
    {
        while (_data.UncompressGetCode(ModeTable) is { RunLength: MmrConstants.Eol } code) _data.Offset += code.BitLength;
    }

    private static int Uncompress2D(RunData runData, Span<int> referenceOffsets, int refRunLength,
        Span<int> runOffsets, int width)
    {
        if (refRunLength < 0 || refRunLength + 3 >= referenceOffsets.Length) return MmrConstants.Eof;

        var referenceBufferOffset = 0;
        var currentBufferOffset = 0;
        var currentLineBitPosition = 0;
        var whiteRun = true;
        Code? code = null;
        referenceOffsets[refRunLength] = referenceOffsets[refRunLength + 1] = width;
        referenceOffsets[refRunLength + 2] = referenceOffsets[refRunLength + 3] = width + 1;

        // Codes that run the offsets off either row's end are a damaged line: the bitmap ends there.
        try
        {
            while (currentLineBitPosition < width)
            {
                code = runData.UncompressGetCode(ModeTable);
                if (code == null)
                {
                    runData.Offset++;
                    break;
                }
                runData.Offset += code.BitLength;

                switch (code.RunLength)
                {
                    case MmrConstants.CodeV0:
                        currentLineBitPosition = referenceOffsets[referenceBufferOffset];
                        break;
                    case MmrConstants.CodeVR1:
                        currentLineBitPosition = referenceOffsets[referenceBufferOffset] + 1;
                        break;
                    case MmrConstants.CodeVL1:
                        currentLineBitPosition = referenceOffsets[referenceBufferOffset] - 1;
                        break;
                    case MmrConstants.CodeVR2:
                        currentLineBitPosition = referenceOffsets[referenceBufferOffset] + 2;
                        break;
                    case MmrConstants.CodeVL2:
                        currentLineBitPosition = referenceOffsets[referenceBufferOffset] - 2;
                        break;
                    case MmrConstants.CodeVR3:
                        currentLineBitPosition = referenceOffsets[referenceBufferOffset] + 3;
                        break;
                    case MmrConstants.CodeVL3:
                        currentLineBitPosition = referenceOffsets[referenceBufferOffset] - 3;
                        break;

                    case MmrConstants.CodeH:
                    {
                        if (!HorizontalRun(runData, whiteRun, ref currentLineBitPosition, runOffsets, ref currentBufferOffset, out code, final: false))
                            goto EndDecodeLoop;
                        var firstHalfBitPos = currentLineBitPosition;
                        if (!HorizontalRun(runData, !whiteRun, ref currentLineBitPosition, runOffsets, ref currentBufferOffset, out code, final: true, firstHalfBitPos, width))
                            goto EndDecodeLoop;
                        while (currentLineBitPosition < width && referenceOffsets[referenceBufferOffset] <= currentLineBitPosition)
                            referenceBufferOffset += 2;
                        continue;
                    }

                    case MmrConstants.CodeP:
                        referenceBufferOffset++;
                        currentLineBitPosition = referenceOffsets[referenceBufferOffset++];
                        continue;

                    default:
                        // An EOL as the very first code: the data is MH coded, two one-dimensional lines.
                        if (runData.Offset == 12 && code.RunLength == MmrConstants.Eol)
                        {
                            runData.Offset = 0;
                            Uncompress1D(runData, referenceOffsets, width);
                            runData.Offset++;
                            Uncompress1D(runData, runOffsets, width);
                            var retCode = Uncompress1D(runData, referenceOffsets, width);
                            runData.Offset++;
                            return retCode;
                        }
                        currentLineBitPosition = width;
                        continue;
                }

                // Only the vertical modes get this far.
                if (currentLineBitPosition <= width)
                {
                    whiteRun = !whiteRun;
                    runOffsets[currentBufferOffset++] = currentLineBitPosition;
                    if (referenceBufferOffset > 0) referenceBufferOffset--;
                    else referenceBufferOffset++;
                    while (currentLineBitPosition < width && referenceOffsets[referenceBufferOffset] <= currentLineBitPosition)
                        referenceBufferOffset += 2;
                }
            }
        }
        catch (IndexOutOfRangeException)
        {
            return MmrConstants.Eof;
        }

        EndDecodeLoop:
        if (currentBufferOffset >= runOffsets.Length) return MmrConstants.Eof;
        if (runOffsets[currentBufferOffset] != width) runOffsets[currentBufferOffset] = width;
        return code is null ? MmrConstants.Eol : currentBufferOffset;
    }

    /// <summary>
    /// One run of a horizontal mode code: make-up codes until a terminating one. False when the codes end the line —
    /// a code the table does not hold, or an EOL — with <paramref name="code"/> null for the first.
    /// </summary>
    private static bool HorizontalRun(RunData runData, bool white, ref int position, Span<int> runOffsets,
        ref int currentBufferOffset, out Code? code, bool final, int firstHalfBitPos = 0, int width = 0)
    {
        while (true)
        {
            code = runData.UncompressGetCode(white ? WhiteTable : BlackTable);
            if (code is null) return false;
            runData.Offset += code.BitLength;
            if (code.RunLength < 64)
            {
                if (code.RunLength < 0)
                {
                    runOffsets[currentBufferOffset++] = position;
                    if (!final) code = null;
                    return false;
                }
                position += code.RunLength;
                // No zero-length run at the end of a line that ends in a horizontal run.
                if (!final || position < width || position != firstHalfBitPos) runOffsets[currentBufferOffset++] = position;
                return true;
            }
            position += code.RunLength;
        }
    }

    private static void FillBitmap(Jbig2Bitmap result, int line, Span<int> currentOffsets, int count)
    {
        var x = 0;
        var targetByte = result.GetByteIndex(0, line);
        var lineEnd = targetByte + result.RowStride;
        byte targetByteValue = 0;
        for (var index = 0; index < count; index++)
        {
            var offset = Math.Min(currentOffsets[index], result.Width);
            var value = index & 1;
            while (x < offset)
            {
                targetByteValue = (byte)(targetByteValue << 1 | value);
                x++;
                if ((x & 7) == 0)
                {
                    result.SetByte(targetByte++, targetByteValue);
                    targetByteValue = 0;
                }
            }
        }
        if ((x & 7) != 0 && targetByte < lineEnd)
        {
            targetByteValue <<= 8 - (x & 7);
            result.SetByte(targetByte, targetByteValue);
        }
    }

    private static int Uncompress1D(RunData runData, Span<int> runOffsets, int width)
    {
        var whiteRun = true;
        var iBitPos = 0;
        Code? code = null;
        var refOffset = 0;
        while (iBitPos < width && refOffset < runOffsets.Length - 1)
        {
            // Make-up codes, then the terminating code of the run.
            while (true)
            {
                code = runData.UncompressGetCode(whiteRun ? WhiteTable : BlackTable);
                if (code is null) goto EndLoop;
                runData.Offset += code.BitLength;
                if (code.RunLength < 0) goto EndLoop;
                iBitPos += code.RunLength;
                if (code.RunLength < 64)
                {
                    whiteRun = !whiteRun;
                    runOffsets[refOffset++] = iBitPos;
                    break;
                }
            }
        }

        EndLoop:
        runOffsets[refOffset] = width;
        return code != null && code.RunLength != MmrConstants.Eol ? refOffset : MmrConstants.Eol;
    }

    private static Code?[] CreateTable(int[][] codes)
    {
        var firstLevelTable = new Code?[FirstLevelTableMask + 1];
        foreach (var codeData in codes)
        {
            var code = new Code(codeData[0], codeData[1], codeData[2]);
            if (code.BitLength <= FirstLevelTableSize)
            {
                var variantLength = FirstLevelTableSize - code.BitLength;
                var baseWord = code.CodeWord << variantLength;
                for (var variant = (1 << variantLength) - 1; variant >= 0; variant--) firstLevelTable[baseWord | variant] = code;
            }
            else
            {
                var firstLevelIndex = code.CodeWord >>> (code.BitLength - FirstLevelTableSize);
                firstLevelTable[firstLevelIndex] ??= new Code(0, 0, 0) { SubTable = new Code?[SecondLevelTableMask + 1] };
                var secondLevelTable = firstLevelTable[firstLevelIndex]!.SubTable!;
                var variantLength = FirstLevelTableSize + SecondLevelTableSize - code.BitLength;
                var baseWord = code.CodeWord << variantLength & SecondLevelTableMask;
                for (var variant = (1 << variantLength) - 1; variant >= 0; variant--) secondLevelTable[baseWord | variant] = code;
            }
        }
        return firstLevelTable;
    }
}
