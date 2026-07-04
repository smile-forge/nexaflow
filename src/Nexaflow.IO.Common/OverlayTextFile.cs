using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.IO.Common;

/// <summary>One match found in the current (edit-overlaid) content.</summary>
public readonly record struct TextMatch(long Line, int Column, string Preview);

/// <summary>A decoded window of current content plus the byte offset where it begins.</summary>
public sealed record WindowText(string Text, long StartByteOffset, long StartLine);

/// <summary>
/// An editable overlay over a (possibly huge) text file. The current document is an ordered list of
/// <em>pieces</em>, each referencing bytes in either the original file or an append-only
/// <em>add-buffer</em> temp file — a piece table. Reads decode only the requested window (streamed in
/// bounded chunks); edits append only the changed bytes to the add-buffer, so the heap holds just the
/// piece list and one window. Saving walks the pieces, bulk-copying unchanged original spans straight
/// through and splicing the add-buffer spans — a save's footprint is its copy buffer, regardless of file
/// size.
/// </summary>
/// <remarks>
/// Coordinates are absolute byte offsets from file start (a leading BOM lives in the first original piece
/// and is never edited — line 0's text begins at <see cref="BomByteCount"/>). Line numbering matches
/// <see cref="TextLineIndex"/>: <c>TotalLines = 1 + newline count</c>. Not safe for two callers mutating
/// at once; a single owner drives it. Long streaming reads (save/find) snapshot the piece list and read
/// the original/add-buffer under a short per-read lock, so a background scan never blocks a foreground
/// edit for more than one buffer.
/// </remarks>
public sealed class OverlayTextFile : IDisposable
{
    // FromOriginal: bytes are original[Start, Start+Length); else add-buffer[Start, Start+Length).
    // Breaks = count of '\n' chars within this piece.
    private readonly record struct Piece(bool FromOriginal, long Start, long Length, long Breaks);

    private readonly TextLineIndex _index;
    private readonly Encoding _enc;
    private readonly FileStream _orig;
    private FileStream? _add;                 // the "change file" — created lazily on the first edit
    private readonly string _addPath;
    private readonly object _io = new();

    private readonly List<Piece> _pieces = [];
    private long _addEnd;
    private long _byteLength;
    private long _lineCount;
    private bool _dirty;
    private bool _disposed;

    private OverlayTextFile(TextLineIndex index, Encoding enc, FileStream orig, string addPath)
    {
        _index   = index;
        _enc     = enc;
        _orig    = orig;
        _addPath = addPath;

        _byteLength = index.TotalBytes;
        _lineCount  = index.TotalLines;
        if (index.TotalBytes > 0)
            _pieces.Add(new Piece(true, 0, index.TotalBytes, Math.Max(0, index.TotalLines - 1)));
    }

    public long TotalLines  => _lineCount;
    public long ByteLength  => _byteLength;
    public bool IsDirty     => _dirty;
    public Encoding Encoding => _enc;
    public int BomByteCount => _index.BomByteCount;

    /// <summary>Opens an overlay over <paramref name="originalPath"/>, creating (truncating) the
    /// add-buffer at <paramref name="addBufferPath"/>.</summary>
    public static async Task<OverlayTextFile> OpenAsync(string originalPath, Encoding enc, int linesPerPage,
                                                        string addBufferPath, CancellationToken ct = default)
    {
        var orig = new FileStream(originalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, useAsync: true);
        TextLineIndex index;
        try { index = await TextLineIndex.BuildAsync(orig, enc, linesPerPage, ct); }
        catch { orig.Dispose(); throw; }

        return new OverlayTextFile(index, enc, orig, addBufferPath);
    }

    // Creates the add-buffer (the change file) on first use, so a read-only session leaves no temp file.
    private FileStream EnsureAdd()
        => _add ??= new FileStream(_addPath, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 16, useAsync: false);

    // ── Reading ──────────────────────────────────────────────────────────────────

    /// <summary>Decoded text of current lines [startLine, startLine+count), exact characters (line
    /// endings preserved), plus the byte offset of the window start for translating in-window edits.</summary>
    public Task<WindowText> ReadWindowAsync(long startLine, int count, CancellationToken ct = default)
        => Task.Run(() => ReadWindowCore(startLine, count), ct);

    private WindowText ReadWindowCore(long startLine, int count)
    {
        if (startLine < 0) startLine = 0;
        long startByte;
        Piece[] snap;
        lock (_io) { startByte = LineToByte(startLine); snap = _pieces.ToArray(); }

        using var stream = new OverlayReadStream(this, snap, startByte);
        using var reader = new StreamReader(stream, _enc, detectEncodingFromByteOrderMarks: false);
        var sb   = new StringBuilder();
        var cbuf = new char[8192];
        long nl  = 0;
        int r;
        while (count > 0 && (r = reader.Read(cbuf, 0, cbuf.Length)) > 0)
        {
            for (int i = 0; i < r; i++)
            {
                sb.Append(cbuf[i]);
                if (cbuf[i] == '\n' && ++nl >= count)
                    return new WindowText(sb.ToString(), startByte, startLine);
            }
        }
        return new WindowText(sb.ToString(), startByte, startLine);
    }

    /// <summary>Streams the whole current content as lines (display text with the trailing CR stripped,
    /// plus the original terminator) for edit-aware search/replace. Line numbering matches
    /// <see cref="TotalLines"/> (a trailing newline yields a final empty line).</summary>
    public async IAsyncEnumerable<(long Line, string Text, string Terminator)> EnumerateLinesAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        Piece[] snap;
        lock (_io) snap = _pieces.ToArray();

        using var stream = new OverlayReadStream(this, snap, _index.BomByteCount);
        using var reader = new StreamReader(stream, _enc, detectEncodingFromByteOrderMarks: false);
        var cur  = new StringBuilder();
        var cbuf = new char[8192];
        long line = 0;
        int r;
        while ((r = await reader.ReadAsync(cbuf.AsMemory(0, cbuf.Length), ct)) > 0)
        {
            for (int i = 0; i < r; i++)
            {
                char ch = cbuf[i];
                if (ch == '\n')
                {
                    var raw  = cur.ToString();
                    cur.Clear();
                    bool crlf = raw.Length > 0 && raw[^1] == '\r';
                    yield return (line, crlf ? raw[..^1] : raw, crlf ? "\r\n" : "\n");
                    line++;
                }
                else cur.Append(ch);
            }
        }
        yield return (line, cur.ToString(), string.Empty); // final line (no terminator)
    }

    // ── Find / replace over current content ──────────────────────────────────────

    public async Task<IReadOnlyList<TextMatch>> FindAsync(string pattern, bool isRegex, bool caseSensitive,
        long fromLine, long toLine, int maxResults, CancellationToken ct = default)
    {
        var matches = new List<TextMatch>();
        Regex? rx = isRegex ? new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase) : null;
        var cmp   = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        await foreach (var (line, text, _) in EnumerateLinesAsync(ct))
        {
            if (line < fromLine) continue;
            if (line > toLine) break;
            if (rx is not null)
            {
                foreach (Match m in rx.Matches(text))
                {
                    matches.Add(new TextMatch(line, m.Index, Preview(text)));
                    if (matches.Count >= maxResults) return matches;
                }
            }
            else if (!string.IsNullOrEmpty(pattern))
            {
                int idx = 0;
                while ((idx = text.IndexOf(pattern, idx, cmp)) >= 0)
                {
                    matches.Add(new TextMatch(line, idx, Preview(text)));
                    if (matches.Count >= maxResults) return matches;
                    idx += Math.Max(1, pattern.Length);
                }
            }
        }
        return matches;

        static string Preview(string s) => s.Length <= 200 ? s : s[..200];
    }

    /// <summary>Replaces every match within lines [fromLine, toLine] over current content; returns the
    /// number of occurrences replaced.</summary>
    public async Task<int> ReplaceAsync(string pattern, string replacement, bool isRegex, bool caseSensitive,
        long fromLine, long toLine, CancellationToken ct = default)
    {
        Regex? rx = isRegex ? new Regex(pattern, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase) : null;
        var cmp   = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        var edits = new List<(long line, string newText)>();
        int count = 0;
        await foreach (var (line, text, term) in EnumerateLinesAsync(ct))
        {
            if (line < fromLine) continue;
            if (line > toLine) break;

            string updated; int hits;
            if (rx is not null)
            {
                hits = rx.Matches(text).Count;
                updated = hits > 0 ? rx.Replace(text, replacement) : text;
            }
            else updated = ReplaceAllPlain(text, pattern, replacement, cmp, out hits);

            if (hits > 0) { edits.Add((line, updated + term)); count += hits; }
        }

        // Bottom-up so earlier line numbers stay valid as line counts shift.
        for (int i = edits.Count - 1; i >= 0; i--)
            await ReplaceLinesAsync(edits[i].line, edits[i].line, edits[i].newText, ct);

        return count;
    }

    private static string ReplaceAllPlain(string text, string find, string repl, StringComparison cmp, out int hits)
    {
        hits = 0;
        if (string.IsNullOrEmpty(find)) return text;
        var sb = new StringBuilder();
        int idx = 0, prev = 0;
        while ((idx = text.IndexOf(find, idx, cmp)) >= 0)
        {
            sb.Append(text, prev, idx - prev).Append(repl);
            idx += find.Length; prev = idx; hits++;
        }
        if (hits == 0) return text;
        sb.Append(text, prev, text.Length - prev);
        return sb.ToString();
    }

    // ── Editing ──────────────────────────────────────────────────────────────────

    /// <summary>Applies one edit in current byte coordinates: removes <paramref name="bytesRemoved"/>
    /// bytes at <paramref name="curByteOffset"/> and inserts <paramref name="insertedText"/>.</summary>
    public void ApplyEdit(long curByteOffset, long bytesRemoved, string insertedText)
    {
        lock (_io) ApplyEditLocked(curByteOffset, bytesRemoved, insertedText ?? string.Empty);
    }

    /// <summary>Replaces current lines [startLine, endLine] (inclusive) with <paramref name="newText"/>.
    /// Empty text deletes the range. Used by the AI edit-by-line tool.</summary>
    public Task ReplaceLinesAsync(long startLine, long endLine, string newText, CancellationToken ct = default)
        => Task.Run(() =>
        {
            lock (_io)
            {
                long from = LineToByte(startLine);
                long to   = LineToByte(endLine + 1);
                ApplyEditLocked(from, to - from, newText ?? string.Empty);
            }
        }, ct);

    private void ApplyEditLocked(long curByteOffset, long bytesRemoved, string insertedText)
    {
        if (curByteOffset < 0) curByteOffset = 0;
        if (bytesRemoved < 0) bytesRemoved = 0;
        if (curByteOffset + bytesRemoved > _byteLength) bytesRemoved = _byteLength - curByteOffset;

        var insBytes  = _enc.GetBytes(insertedText);
        long insStart = _addEnd;
        if (insBytes.Length > 0)
        {
            var add = EnsureAdd();
            add.Seek(_addEnd, SeekOrigin.Begin);
            add.Write(insBytes, 0, insBytes.Length);
            add.Flush();
            _addEnd += insBytes.Length;
        }
        long insBreaks = CountNewlines(insertedText);

        int i0 = SplitAt(curByteOffset);
        int i1 = SplitAt(curByteOffset + bytesRemoved);

        long removedBreaks = 0;
        for (int i = i0; i < i1; i++) removedBreaks += _pieces[i].Breaks;

        _pieces.RemoveRange(i0, i1 - i0);
        if (insBytes.Length > 0)
            _pieces.Insert(i0, new Piece(false, insStart, insBytes.Length, insBreaks));

        _byteLength += insBytes.Length - bytesRemoved;
        _lineCount  += insBreaks - removedBreaks;
        _dirty = true;
    }

    // ── Saving (streaming bulk-copy merge) ───────────────────────────────────────

    /// <summary>Streams the merged current content (BOM included) to <paramref name="output"/>: original
    /// pieces are bulk-copied straight from the source file; add-buffer pieces from the add-buffer.</summary>
    public Task SaveAsync(Stream output, CancellationToken ct = default)
        => Task.Run(() =>
        {
            Piece[] snap;
            lock (_io) snap = _pieces.ToArray();
            using var stream = new OverlayReadStream(this, snap, 0);
            var buf = new byte[1 << 16];
            int got;
            while ((got = stream.Read(buf, 0, buf.Length)) > 0)
            {
                ct.ThrowIfCancellationRequested();
                output.Write(buf, 0, got);
            }
            output.Flush();
        }, ct);

    // ── Internal mapping helpers (call under _io) ────────────────────────────────

    // Current byte offset where current line L's text begins. Line 0 → BomByteCount (past the BOM).
    private long LineToByte(long line)
    {
        if (line <= 0) return _index.BomByteCount;
        if (line >= _lineCount) return _byteLength;

        long cum = 0, linesBefore = 0;
        foreach (var p in _pieces)
        {
            if (linesBefore + p.Breaks >= line)
                return cum + ByteAfterKthNewlineInPiece(p, line - linesBefore);
            linesBefore += p.Breaks;
            cum += p.Length;
        }
        return _byteLength;
    }

    // Offset within the piece just past its k-th '\n' (k >= 1, known to exist within the piece).
    private long ByteAfterKthNewlineInPiece(Piece p, long k)
    {
        if (p.FromOriginal)
        {
            long baseLine = OrigLineAt(p.Start);
            return OrigByteOfLine(baseLine + k) - p.Start;
        }
        var text = ReadAdd(p.Start, (int)p.Length);
        long seen = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n' && ++seen == k) return _enc.GetByteCount(text.AsSpan(0, i + 1));
        return p.Length;
    }

    // Ensures a piece boundary at current byte offset `curOffset`; returns the index of the piece that
    // starts there (== _pieces.Count when curOffset == end).
    private int SplitAt(long curOffset)
    {
        if (curOffset <= 0) return 0;
        long cum = 0;
        for (int i = 0; i < _pieces.Count; i++)
        {
            var p = _pieces[i];
            if (cum == curOffset) return i;
            if (curOffset < cum + p.Length)
            {
                long local = curOffset - cum;
                long leftBreaks = p.FromOriginal
                    ? OrigLineAt(p.Start + local) - OrigLineAt(p.Start)
                    : CountNewlines(ReadAdd(p.Start, (int)local));
                _pieces[i] = new Piece(p.FromOriginal, p.Start, local, leftBreaks);
                _pieces.Insert(i + 1, new Piece(p.FromOriginal, p.Start + local, p.Length - local, p.Breaks - leftBreaks));
                return i + 1;
            }
            cum += p.Length;
        }
        return _pieces.Count;
    }

    // Original-file line number at an original byte offset (bounded decode from the nearest page).
    private long OrigLineAt(long origByte)
    {
        var (pageLine, pageByte) = _index.PageStartForByte(origByte);
        if (origByte <= pageByte) return pageLine;
        return pageLine + CountNewlines(ReadOriginal(pageByte, (int)(origByte - pageByte)));
    }

    // Original byte offset where original line `line` begins (bounded decode within one page).
    private long OrigByteOfLine(long line)
    {
        if (line <= 0) return _index.BomByteCount;
        if (line >= _index.TotalLines) return _index.TotalBytes;
        var (pageLine, pageByte) = _index.PageStartForLine(line);
        if (pageLine == line) return pageByte;

        long page    = pageLine / _index.LinesPerPage;
        long pageEnd = page + 1 < _index.PageCount ? _index.PageStartByte(page + 1) : _index.TotalBytes;
        var text     = ReadOriginal(pageByte, (int)(pageEnd - pageByte));
        long need = line - pageLine, seen = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n' && ++seen == need) return pageByte + _enc.GetByteCount(text.AsSpan(0, i + 1));
        return pageEnd;
    }

    private string ReadOriginal(long start, int len)
    {
        if (len <= 0) return string.Empty;
        var buf = new byte[len];
        _orig.Seek(start, SeekOrigin.Begin);
        ReadFully(_orig, buf, len);
        return _enc.GetString(buf, 0, len);
    }

    private string ReadAdd(long start, int len)
    {
        if (len <= 0 || _add is null) return string.Empty;
        var buf = new byte[len];
        _add.Flush();
        _add.Seek(start, SeekOrigin.Begin);
        ReadFully(_add, buf, len);
        return _enc.GetString(buf, 0, len);
    }

    private static void ReadFully(Stream s, byte[] buf, int len)
    {
        int off = 0;
        while (off < len)
        {
            int got = s.Read(buf, off, len - off);
            if (got <= 0) break;
            off += got;
        }
    }

    private static long CountNewlines(string s)
    {
        long n = 0;
        foreach (var ch in s) if (ch == '\n') n++;
        return n;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _orig.Dispose();
        if (_add is not null)
        {
            _add.Dispose();
            try { if (File.Exists(_addPath)) File.Delete(_addPath); } catch { /* best effort */ }
        }
    }

    // Forward-only read over the merged current bytes from a start offset. Each Read seeks the right
    // source and reads from the current piece, under a short lock so it can't race a foreground edit.
    private sealed class OverlayReadStream(OverlayTextFile owner, Piece[] snapshot, long startByte) : Stream
    {
        private int  _pi;
        private long _inPiece;
        private bool _init;

        private void EnsureInit()
        {
            if (_init) return;
            _init = true;
            long cum = 0;
            for (_pi = 0; _pi < snapshot.Length; _pi++)
            {
                var p = snapshot[_pi];
                if (startByte < cum + p.Length) { _inPiece = startByte - cum; return; }
                cum += p.Length;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            EnsureInit();
            lock (owner._io)
            {
                while (_pi < snapshot.Length)
                {
                    var p = snapshot[_pi];
                    long remain = p.Length - _inPiece;
                    if (remain <= 0) { _pi++; _inPiece = 0; continue; }
                    int want = (int)Math.Min(remain, count);
                    var src = p.FromOriginal ? owner._orig : owner._add!;
                    if (!p.FromOriginal) owner._add!.Flush();
                    src.Seek(p.Start + _inPiece, SeekOrigin.Begin);
                    int got = src.Read(buffer, offset, want);
                    if (got <= 0) { _pi++; _inPiece = 0; continue; }
                    _inPiece += got;
                    return got;
                }
                return 0;
            }
        }

        public override bool CanRead  => true;
        public override bool CanSeek  => false;
        public override bool CanWrite => false;
        public override long Length   => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
