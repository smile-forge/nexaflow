/*
 * Ported from Apache PDFBox JBIG2 ImageIO (pdfbox-jbig2) by way of PdfPig.Filters.Jbig2.PdfboxJbig2 0.1.16.2:
 * decoder/huffman/HuffmanTable.java, Node.java, InternalNode.java, ValueNode.java, OutOfBandNode.java,
 * FixedSizeTable.java, EncodedTable.java and StandardTables.java.
 * Licensed under the Apache License, Version 2.0; see the LICENSE and NOTICE files beside this folder.
 * Modified for Nexaflow: the standard tables are built once, up front; a code no table holds is a damaged segment.
 */

namespace Nexaflow.Media.Jbig2.Internal;

/// <summary>A Huffman table of Annex B, as a binary tree walked a bit at a time.</summary>
internal class HuffmanTable
{
    /// <summary>OOB, as a table with an out-of-band line decodes it.</summary>
    public const long OutOfBand = long.MaxValue;

    private readonly InternalNode _root = new(0);

    /// <summary>One table line: its prefix length, range length and range low, B.2.</summary>
    internal sealed class Code(int prefixLength, int rangeLength, int rangeLow, bool isLowerRange)
    {
        public int PrefixLength { get; } = prefixLength;

        /// <summary>The bits that follow the prefix, or -1 for the out-of-band line.</summary>
        public int RangeLength { get; } = rangeLength;

        public int RangeLow { get; } = rangeLow;

        public bool IsLowerRange { get; } = isLowerRange;

        /// <summary>The prefix code, assigned by B.3.</summary>
        public int Value { get; set; } = -1;
    }

    public HuffmanTable(List<Code> codeTable)
    {
        AssignPrefixCodes(codeTable);
        foreach (var code in codeTable) _root.Append(code);
    }

    public long Decode(Jbig2Reader reader) => _root.Decode(reader);

    /// <summary>B.3: prefix codes in order of length, then of position in the table.</summary>
    private static void AssignPrefixCodes(List<Code> codeTable)
    {
        var maxPrefixLength = 0;
        foreach (var c in codeTable) maxPrefixLength = Math.Max(maxPrefixLength, c.PrefixLength);

        var lenCount = new int[maxPrefixLength + 1];
        foreach (var c in codeTable) lenCount[c.PrefixLength]++;
        lenCount[0] = 0;

        var firstCode = new int[lenCount.Length + 1];
        for (var curLen = 1; curLen <= lenCount.Length; curLen++)
        {
            firstCode[curLen] = firstCode[curLen - 1] + lenCount[curLen - 1] << 1;
            var curCode = firstCode[curLen];
            foreach (var code in codeTable)
            {
                if (code.PrefixLength == curLen) code.Value = curCode++;
            }
        }
    }

    private abstract class Node
    {
        public abstract long Decode(Jbig2Reader reader);
    }

    private sealed class InternalNode(int depth) : Node
    {
        private Node? _zero;
        private Node? _one;

        public void Append(Code c)
        {
            // A line with no prefix is unused.
            if (c.PrefixLength == 0) return;

            var shift = c.PrefixLength - 1 - depth;
            if (shift < 0) throw new Jbig2DataException("a Huffman table's codes overlap");
            var bit = c.Value >> shift & 1;
            if (shift == 0)
            {
                Node leaf = c.RangeLength == -1 ? new OutOfBandNode() : new ValueNode(c);
                if (bit == 1)
                {
                    if (_one != null) throw new Jbig2DataException("a Huffman table gives two lines one code");
                    _one = leaf;
                }
                else
                {
                    if (_zero != null) throw new Jbig2DataException("a Huffman table gives two lines one code");
                    _zero = leaf;
                }
                return;
            }

            ref var child = ref bit == 1 ? ref _one : ref _zero;
            child ??= new InternalNode(depth + 1);
            if (child is not InternalNode inner) throw new Jbig2DataException("a Huffman table's codes overlap");
            inner.Append(c);
        }

        public override long Decode(Jbig2Reader reader)
        {
            var next = reader.ReadBit() == 0 ? _zero : _one;
            if (next is null) throw new Jbig2DataException("a Huffman code no table line holds");
            return next.Decode(reader);
        }
    }

    private sealed class ValueNode(Code c) : Node
    {
        private readonly int _rangeLength = c.RangeLength;
        private readonly int _rangeLow = c.RangeLow;
        private readonly bool _isLowerRange = c.IsLowerRange;

        // B.4 4) and 5).
        public override long Decode(Jbig2Reader reader) =>
            _isLowerRange ? _rangeLow - reader.ReadBits(_rangeLength) : _rangeLow + reader.ReadBits(_rangeLength);
    }

    private sealed class OutOfBandNode : Node
    {
        public override long Decode(Jbig2Reader reader) => OutOfBand;
    }
}

/// <summary>The standard Huffman tables B.1 to B.15, Annex B.5.</summary>
internal static class StandardTables
{
    // Prefix length, range length and range low of each line; a fourth value marks the lower range line.
    private static readonly int[][][] Lines =
    [
        // B.1
        [[1, 4, 0], [2, 8, 16], [3, 16, 272], [3, 32, 65808]],
        // B.2
        [[1, 0, 0], [2, 0, 1], [3, 0, 2], [4, 3, 3], [5, 6, 11], [6, 32, 75], [6, -1, 0]],
        // B.3
        [[8, 8, -256], [1, 0, 0], [2, 0, 1], [3, 0, 2], [4, 3, 3], [5, 6, 11], [8, 32, -257, 999], [7, 32, 75], [6, -1, 0]],
        // B.4
        [[1, 0, 1], [2, 0, 2], [3, 0, 3], [4, 3, 4], [5, 6, 12], [5, 32, 76]],
        // B.5
        [[7, 8, -255], [1, 0, 1], [2, 0, 2], [3, 0, 3], [4, 3, 4], [5, 6, 12], [7, 32, -256, 999], [6, 32, 76]],
        // B.6
        [
            [5, 10, -2048], [4, 9, -1024], [4, 8, -512], [4, 7, -256], [5, 6, -128], [5, 5, -64], [4, 5, -32], [2, 7, 0],
            [3, 7, 128], [3, 8, 256], [4, 9, 512], [4, 10, 1024], [6, 32, -2049, 999], [6, 32, 2048],
        ],
        // B.7
        [
            [4, 9, -1024], [3, 8, -512], [4, 7, -256], [5, 6, -128], [5, 5, -64], [4, 5, -32], [4, 5, 0], [5, 5, 32],
            [5, 6, 64], [4, 7, 128], [3, 8, 256], [3, 9, 512], [3, 10, 1024], [5, 32, -1025, 999], [5, 32, 2048],
        ],
        // B.8
        [
            [8, 3, -15], [9, 1, -7], [8, 1, -5], [9, 0, -3], [7, 0, -2], [4, 0, -1], [2, 1, 0], [5, 0, 2], [6, 0, 3],
            [3, 4, 4], [6, 1, 20], [4, 4, 22], [4, 5, 38], [5, 6, 70], [5, 7, 134], [6, 7, 262], [7, 8, 390],
            [6, 10, 646], [9, 32, -16, 999], [9, 32, 1670], [2, -1, 0],
        ],
        // B.9
        [
            [8, 4, -31], [9, 2, -15], [8, 2, -11], [9, 1, -7], [7, 1, -5], [4, 1, -3], [3, 1, -1], [3, 1, 1], [5, 1, 3],
            [6, 1, 5], [3, 5, 7], [6, 2, 39], [4, 5, 43], [4, 6, 75], [5, 7, 139], [5, 8, 267], [6, 8, 523], [7, 9, 779],
            [6, 11, 1291], [9, 32, -32, 999], [9, 32, 3339], [2, -1, 0],
        ],
        // B.10
        [
            [7, 4, -21], [8, 0, -5], [7, 0, -4], [5, 0, -3], [2, 2, -2], [5, 0, 2], [6, 0, 3], [7, 0, 4], [8, 0, 5],
            [2, 6, 6], [5, 5, 70], [6, 5, 102], [6, 6, 134], [6, 7, 198], [6, 8, 326], [6, 9, 582], [6, 10, 1094],
            [7, 11, 2118], [8, 32, -22, 999], [8, 32, 4166], [2, -1, 0],
        ],
        // B.11
        [
            [1, 0, 1], [2, 1, 2], [4, 0, 4], [4, 1, 5], [5, 1, 7], [5, 2, 9], [6, 2, 13], [7, 2, 17], [7, 3, 21],
            [7, 4, 29], [7, 5, 45], [7, 6, 77], [7, 32, 141],
        ],
        // B.12
        [
            [1, 0, 1], [2, 0, 2], [3, 1, 3], [5, 0, 5], [5, 1, 6], [6, 1, 8], [7, 0, 10], [7, 1, 11], [7, 2, 13],
            [7, 3, 17], [7, 4, 25], [8, 5, 41], [8, 32, 73],
        ],
        // B.13
        [
            [1, 0, 1], [3, 0, 2], [4, 0, 3], [5, 0, 4], [4, 1, 5], [3, 3, 7], [6, 1, 15], [6, 2, 17], [6, 3, 21],
            [6, 4, 29], [6, 5, 45], [7, 6, 77], [7, 32, 141],
        ],
        // B.14
        [[3, 0, -2], [3, 0, -1], [1, 0, 0], [3, 0, 1], [3, 0, 2]],
        // B.15
        [
            [7, 4, -24], [6, 2, -8], [5, 1, -4], [4, 0, -2], [3, 0, -1], [1, 0, 0], [3, 0, 1], [4, 0, 2], [5, 1, 3],
            [6, 2, 5], [7, 4, 9], [7, 32, -25, 999], [7, 32, 25],
        ],
    ];

    private static readonly HuffmanTable[] Tables = [.. Lines.Select(lines =>
        new HuffmanTable([.. lines.Select(l => new HuffmanTable.Code(l[0], l[1], l[2], l.Length > 3))]))];

    /// <summary>Table B.<paramref name="number"/>.</summary>
    public static HuffmanTable GetTable(int number) => Tables[number - 1];
}
