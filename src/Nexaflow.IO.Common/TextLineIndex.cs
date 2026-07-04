using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.IO.Common;

/// <summary>
/// A sparse, encoding-correct line index over a (possibly huge) text file. A single sequential pass
/// decodes the file with a stateful <see cref="Decoder"/> — so a multibyte sequence straddling a read
/// boundary is handled correctly and newlines are counted in the decoded char stream (never by scanning
/// raw bytes for <c>0x0A</c>, which mis-fires for UTF-16/32). It records the exact total line count and
/// a checkpoint at the start of every <see cref="LinesPerPage"/>-line page, so any line can be located
/// with one seek + a bounded forward decode — without holding a per-line index in memory.
/// </summary>
/// <remarks>
/// Line numbering matches the convention the Text viewer has always used: <c>TotalLines = 1 + newline
/// count</c> (a trailing <c>\n</c> yields a final empty line). A page checkpoint's byte offset is where
/// that line's <em>decoded text</em> begins — for line 0 that is just past any BOM, so callers can both
/// seek-to-read and translate edit offsets without a separate BOM correction.
/// </remarks>
public sealed class TextLineIndex
{
    /// <summary>The encoding the index was built with (the caller's chosen encoding).</summary>
    public Encoding Encoding { get; }

    /// <summary>Length of the leading byte-order mark that was skipped, or 0 when there is none.</summary>
    public int BomByteCount { get; }

    /// <summary>Total byte length of the file, including any BOM.</summary>
    public long TotalBytes { get; }

    /// <summary>Total line count: <c>1 + newline count</c>.</summary>
    public long TotalLines { get; }

    /// <summary>Lines per checkpoint page.</summary>
    public int LinesPerPage { get; }

    // _pageByteOffsets[p] = byte offset (from file start) where line p*LinesPerPage's decoded text begins.
    // Index 0 == BomByteCount. Length == ceil(TotalLines / LinesPerPage).
    private readonly long[] _pageByteOffsets;

    private TextLineIndex(Encoding encoding, int bomByteCount, long totalBytes, long totalLines,
                          int linesPerPage, long[] pageByteOffsets)
    {
        Encoding        = encoding;
        BomByteCount    = bomByteCount;
        TotalBytes      = totalBytes;
        TotalLines      = totalLines;
        LinesPerPage    = linesPerPage;
        _pageByteOffsets = pageByteOffsets;
    }

    /// <summary>Number of checkpoint pages.</summary>
    public long PageCount => _pageByteOffsets.Length;

    /// <summary>Byte offset where the given page's first line begins.</summary>
    public long PageStartByte(long pageIndex) => _pageByteOffsets[pageIndex];

    /// <summary>
    /// The checkpoint at or before <paramref name="line"/>: the first line of that line's page and the
    /// byte offset where it begins. Seek there, then decode forward <c>line - resultLine</c> lines.
    /// </summary>
    public (long Line, long ByteOffset) PageStartForLine(long line)
    {
        if (line < 0) line = 0;
        long page = line / LinesPerPage;
        if (page >= _pageByteOffsets.Length) page = _pageByteOffsets.Length - 1;
        return (page * LinesPerPage, _pageByteOffsets[page]);
    }

    /// <summary>
    /// The checkpoint at or before byte <paramref name="byteOffset"/>: the first line of the page whose
    /// range contains it, and that page's byte offset. Decode forward from there to resolve the exact line.
    /// </summary>
    public (long Line, long ByteOffset) PageStartForByte(long byteOffset)
    {
        int lo = 0, hi = _pageByteOffsets.Length - 1, ans = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            if (_pageByteOffsets[mid] <= byteOffset) { ans = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        return ((long)ans * LinesPerPage, _pageByteOffsets[ans]);
    }

    /// <summary>
    /// Builds the index by streaming <paramref name="stream"/> once from the start. The stream is read to
    /// its end; its position is left at EOF. The BOM skipped is the one matching
    /// <paramref name="encoding"/>'s preamble (so a user-chosen encoding override is honoured).
    /// </summary>
    public static async Task<TextLineIndex> BuildAsync(Stream stream, Encoding encoding, int linesPerPage,
                                                       CancellationToken ct = default)
    {
        if (linesPerPage <= 0) throw new ArgumentOutOfRangeException(nameof(linesPerPage));

        long totalBytes = stream.CanSeek ? stream.Length : -1;
        stream.Seek(0, SeekOrigin.Begin);

        var preamble  = encoding.GetPreamble();
        int bomLen    = await MatchPreambleAsync(stream, preamble, ct);
        // Re-seek past the BOM (MatchPreamble may have read more than the preamble into its probe).
        stream.Seek(bomLen, SeekOrigin.Begin);

        var decoder = encoding.GetDecoder();
        var readBuf = new byte[1 << 16];
        var charBuf = new char[encoding.GetMaxCharCount(readBuf.Length)];

        var pages = new List<long> { bomLen };   // page 0 (line 0) begins just past the BOM

        long byteOffset          = bomLen;        // byte offset where `currentLine` begins
        long currentLine         = 0;
        long bytesSinceLineStart = 0;             // bytes of the in-progress line carried across chunks
        long consumed            = bomLen;        // total bytes consumed (for the no-seek TotalBytes path)

        int read;
        while ((read = await stream.ReadAsync(readBuf.AsMemory(0, readBuf.Length), ct)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            consumed += read;
            int chars = decoder.GetChars(readBuf, 0, read, charBuf, 0, flush: false);

            int segStart = 0;
            for (int i = 0; i < chars; i++)
            {
                if (charBuf[i] != '\n') continue;
                int segLen = i - segStart + 1;                                   // include the '\n'
                byteOffset += bytesSinceLineStart + encoding.GetByteCount(charBuf, segStart, segLen);
                bytesSinceLineStart = 0;
                currentLine++;
                if (currentLine % linesPerPage == 0) pages.Add(byteOffset);      // start of a new page
                segStart = i + 1;
            }
            if (segStart < chars)
                bytesSinceLineStart += encoding.GetByteCount(charBuf, segStart, chars - segStart);
        }

        long total = totalBytes >= 0 ? totalBytes : consumed;
        return new TextLineIndex(encoding, bomLen, total, currentLine + 1, linesPerPage, pages.ToArray());
    }

    // Returns the preamble length if the stream begins with exactly those bytes, else 0.
    private static async Task<int> MatchPreambleAsync(Stream stream, byte[] preamble, CancellationToken ct)
    {
        if (preamble.Length == 0) return 0;
        var probe = new byte[preamble.Length];
        int got = 0;
        while (got < probe.Length)
        {
            int n = await stream.ReadAsync(probe.AsMemory(got, probe.Length - got), ct);
            if (n == 0) break;
            got += n;
        }
        if (got < preamble.Length) return 0;
        for (int i = 0; i < preamble.Length; i++)
            if (probe[i] != preamble[i]) return 0;
        return preamble.Length;
    }
}
