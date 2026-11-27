using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Nexaflow.Markdown.Editing;

/// <summary>What a <see cref="ContentFile"/> is opened to do: be read, or be read and later written.</summary>
public enum ContentFileAccess
{
    /// <summary>Read only, for as long as it is open: never held against other writers, never written — what a log is.</summary>
    ReadOnly,

    /// <summary>Read, while other programs may go on writing it, until it is held for an edit (<see cref="ContentFile.HoldForEdit"/>).</summary>
    Editable,
}

/// <summary>What came of asking to hold a file for an edit (<see cref="ContentFile.HoldForEdit"/>).</summary>
public enum ContentFileHold
{
    /// <summary>Held: no other program can write it until it is let go.</summary>
    Held,

    /// <summary>It changed on disk since it was read, so it has to be read again before anything is written over what it said.</summary>
    Changed,

    /// <summary>Another program has it open to write, so it cannot be held.</summary>
    WrittenElsewhere,

    /// <summary>It was opened read only, so it is never held.</summary>
    ReadOnly,
}

/// <summary>
/// A file read a block at a time, for a document as long as the file (<see cref="ContentDocument.Of(ContentFile, int, int?)"/>) that costs
/// what is on screen rather than what is on disk.
///
/// <para>
/// <strong>A table of blocks, not the text.</strong> Scanning (<see cref="Scan"/>) cuts the file into blocks of about
/// <see cref="BlockBytes"/> bytes, each cut where a character starts, and keeps for each only where it lies, how many characters and line
/// breaks it decodes to and its first and last character — some forty bytes a block. A document over the file is a rope whose leaves name
/// blocks, so nothing is read to make one, and reading it decodes only the blocks read, keeping the last <see cref="CachedBlocks"/>.
/// </para>
/// <para>
/// <strong>Encodings a block can be cut out of.</strong> A block is decoded on its own, so the encoding has to start every character
/// afresh wherever a block is cut: UTF-8, UTF-16 and UTF-32, cut where a character starts, and any single-byte encoding
/// (<see cref="Reads"/>). A file in any other — one whose decoder carries state from byte to byte — is read whole instead, by whoever opens it.
/// </para>
/// <para>
/// <strong>Opened to be read, shared with writers.</strong> Another program may go on writing the file while it is shown; a log grows
/// (<see cref="Scan"/> takes up what was added), and a file changed under an unedited view is read again by whoever shows it. Only an
/// <see cref="ContentFileAccess.Editable"/> file, at its first edit, is held against other writers (<see cref="HoldForEdit"/>).
/// </para>
/// </summary>
public sealed class ContentFile : IDisposable
{
    /// <summary>How many bytes a block is cut at, give or take the few that finish a character.</summary>
    public const int BlockBytes = 64 * 1024;

    /// <summary>How many decoded blocks are kept, the last read: a few screens either side of what is shown.</summary>
    public const int CachedBlocks = 64;

    private readonly object _gate = new();
    private readonly List<ContentFileBlock> _blocks = [];
    private readonly int _blockBytes;

    /// <summary>The blocks decoded, by block, with where each stands in <see cref="_recent"/>.</summary>
    private readonly Dictionary<int, LinkedListNode<(int Block, string Text)>> _cached = [];

    /// <summary>The blocks decoded, the last read first.</summary>
    private readonly LinkedList<(int Block, string Text)> _recent = new();

    private SafeFileHandle _handle;
    private long _scanned;
    private long _characters;
    private DateTime _written;
    private bool _held;
    private bool _disposed;

    private ContentFile(string path, SafeFileHandle handle, Encoding encoding, int preamble, long from, ContentFileAccess access, int blockBytes)
    {
        this.Path = path;
        this.Encoding = encoding;
        this.Preamble = preamble;
        this.From = from;
        this.Access = access;
        _handle = handle;
        _blockBytes = blockBytes;
        _scanned = from;
        _written = File.GetLastWriteTimeUtc(path);
    }

    /// <summary>Where it is.</summary>
    public string Path { get; }

    /// <summary>What its bytes are written in.</summary>
    public Encoding Encoding { get; }

    /// <summary>How many bytes the encoding's mark at its start takes, which no block holds.</summary>
    public int Preamble { get; }

    /// <summary>
    /// Where its first block starts, in bytes: just past the mark at the start of the file, or — for a stretch of it (<see cref="OpenStretch"/>)
    /// — where the stretch starts. Its characters are counted from there.
    /// </summary>
    public long From { get; }

    /// <summary>What it was opened to do.</summary>
    public ContentFileAccess Access { get; }

    /// <summary>Whether it is held against other writers, after <see cref="HoldForEdit"/>.</summary>
    public bool IsHeld => _held;

    /// <summary>How many blocks have been scanned.</summary>
    public int BlockCount { get { lock (_gate) return _blocks.Count; } }

    /// <summary>How many characters the blocks scanned hold.</summary>
    public long Characters { get { lock (_gate) return _characters; } }

    /// <summary>How far into the file has been scanned, in bytes, its mark included.</summary>
    public long Scanned { get { lock (_gate) return _scanned; } }

    /// <summary>
    /// Whether a file in <paramref name="encoding"/> can be read a block at a time: whether every character it writes can be found the start of
    /// from its bytes alone, so a block cut where one starts decodes the same on its own as in the whole.
    /// </summary>
    public static bool Reads(Encoding encoding) =>
        encoding.IsSingleByte || encoding is UTF8Encoding or UnicodeEncoding or UTF32Encoding;

    /// <summary>
    /// Opens the file at <paramref name="path"/>, written in <paramref name="encoding"/> after a mark of <paramref name="preamble"/> bytes, to be
    /// read — shared with other readers and writers — and scans none of it yet. Blocks are cut at <paramref name="blockBytes"/>, which only the
    /// tests of where blocks are cut say.
    /// </summary>
    public static ContentFile Open(string path, Encoding encoding, int preamble, ContentFileAccess access, int blockBytes = BlockBytes)
    {
        if (!Reads(encoding)) throw new ArgumentException($"{encoding.WebName} cannot be read a block at a time", nameof(encoding));

        return new ContentFile(path, Shared(path), encoding, preamble, preamble, access, Math.Max(blockBytes, 16));
    }

    /// <summary>
    /// Opens the file at <paramref name="path"/>, written in <paramref name="encoding"/>, as the stretch of it from byte <paramref name="from"/> to
    /// its end, however far that grows — where a line starts near the end of a long log (<see cref="LineStartByte"/>), say, so its last lines
    /// are shown before the rest is read. Shared with other readers and writers as <see cref="Open"/> is, and scanning none of it yet; a document
    /// over it is that stretch alone, its characters counted from <paramref name="from"/>.
    /// </summary>
    public static ContentFile OpenStretch(string path, Encoding encoding, long from, ContentFileAccess access, int blockBytes = BlockBytes)
    {
        if (!Reads(encoding)) throw new ArgumentException($"{encoding.WebName} cannot be read a block at a time", nameof(encoding));

        return new ContentFile(path, Shared(path), encoding, 0, Math.Max(0, from), access, Math.Max(blockBytes, 16));
    }

    /// <summary>
    /// Scans up to <paramref name="most"/> more blocks from where scanning last stopped — the file as it was opened, or what another program
    /// has written to its end since — and no further than byte <paramref name="until"/>, where a stretch of the file shown already starts
    /// (<see cref="OpenStretch"/>); and says how many it added. Nothing past the last character finished is taken, so a character being
    /// written is taken up once it is whole.
    /// </summary>
    public int Scan(int most = int.MaxValue, CancellationToken cancel = default, long until = long.MaxValue)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var end = Math.Min(RandomAccess.GetLength(_handle), until);
        var bytes = new byte[_blockBytes + 4];
        var chars = new char[this.Encoding.GetMaxCharCount(bytes.Length)];
        var added = 0;
        long from;

        lock (_gate) from = _scanned;

        while (added < most && from < end)
        {
            cancel.ThrowIfCancellationRequested();

            var wanted = (int)Math.Min(bytes.Length, end - from);
            var read = RandomAccess.Read(_handle, bytes.AsSpan(0, wanted), from);
            if (read <= 0) break;

            // The last block of what is there takes all of it; any other ends where a character starts, so it decodes alone.
            var cut = from + read >= end ? this.Whole(bytes.AsSpan(0, read)) : this.Cut(bytes.AsSpan(0, read), Math.Min(read, _blockBytes));
            if (cut == 0) break;

            var length = this.Encoding.GetChars(bytes, 0, cut, chars, 0);
            var (breaks, crLfs) = ContentRope.Counted(chars.AsSpan(0, length));
            var block = new ContentFileBlock(from, cut, length, breaks, crLfs, length > 0 ? chars[0] : '\0', length > 0 ? chars[length - 1] : '\0');

            lock (_gate)
            {
                if (length > 0)
                {
                    _blocks.Add(block);
                    _characters += length;
                }

                _scanned = from += cut;
            }

            added++;
        }

        return added;
    }

    /// <summary>How far along a line start is looked for, in bytes, before one is stood in the middle of.</summary>
    private const int LineSearchBytes = 1 << 20;

    /// <summary>
    /// Where the first line that starts at or after byte <paramref name="at"/> starts, in bytes — just after a line feed, or a return no line
    /// feed follows, read in the file's encoding — looked for no further than a megabyte along; a line longer than that is stood in the middle
    /// of, where a character starts. Never before <see cref="From"/>, which a line is taken to start at, nor past the end of the file.
    /// </summary>
    public long LineStartByte(long at)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var end = RandomAccess.GetLength(_handle);
        var (feed, back) = (this.Encoding.GetBytes("\n"), this.Encoding.GetBytes("\r"));
        var unit = feed.Length;

        // Characters of the encodings a block can be cut out of are found only where their units start, counted from where the first does.
        at = Math.Clamp(at, this.From, end);
        at = Math.Min(end, this.From + ((at - this.From + unit - 1) / unit * unit));
        if (at <= this.From || at >= end) return at;

        // From the unit before it, which says whether a line starts at it.
        var bytes = new byte[(int)Math.Min(LineSearchBytes + (2 * unit), end - (at - unit))];
        var read = RandomAccess.Read(_handle, bytes, at - unit);

        for (var index = 0; index + unit <= read; index += unit)
        {
            var unitAt = bytes.AsSpan(index, unit);
            if (unitAt.SequenceEqual(feed)) return at - unit + index + unit;

            // A return ends a line unless a line feed follows it, which then does.
            if (unitAt.SequenceEqual(back) && (index + (2 * unit) > read || !bytes.AsSpan(index + unit, unit).SequenceEqual(feed)))
                return at - unit + index + unit;
        }

        return at + this.CharacterStart(bytes.AsSpan(unit, Math.Max(0, read - unit)));
    }

    /// <summary>
    /// The characters of blocks <paramref name="first"/> up to <paramref name="last"/> (one past the last), counted from the start of the file —
    /// where a document over those blocks starts and how long it is.
    /// </summary>
    public (long Start, long Length) CharactersOf(int first, int last)
    {
        lock (_gate)
        {
            var (start, length) = (0L, 0L);
            for (var at = 0; at < last; at++) (start, length) = at < first ? (start + _blocks[at].Chars, 0) : (start, length + _blocks[at].Chars);

            return (start, length);
        }
    }

    /// <summary>
    /// How many line breaks blocks <paramref name="first"/> up to <paramref name="last"/> (one past the last) hold — a return and a line feed
    /// together being one, wherever a block cuts between them: how many lines a document over them has, less one.
    /// </summary>
    public long LineBreaksOf(int first, int last)
    {
        lock (_gate)
        {
            var (breaks, before) = (0L, '\0');

            for (var at = Math.Max(0, first); at < Math.Min(last, _blocks.Count); at++)
            {
                var block = _blocks[at];
                breaks += block.Breaks - (before == '\r' && block.First == '\n' ? 1 : 0);
                before = block.Last;
            }

            return breaks;
        }
    }

    /// <summary>
    /// Holds it against other writers for an edit to be written to it — what an <see cref="ContentFileAccess.Editable"/> file does on its first
    /// edit — after making sure it is still what was scanned: no longer and no newer. Holding a held file again holds it.
    /// </summary>
    public ContentFileHold HoldForEdit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (this.Access == ContentFileAccess.ReadOnly) return ContentFileHold.ReadOnly;
        if (_held) return ContentFileHold.Held;

        lock (_gate)
        {
            if (RandomAccess.GetLength(_handle) != _scanned || File.GetLastWriteTimeUtc(this.Path) != _written) return ContentFileHold.Changed;

            SafeFileHandle held;
            try
            {
                // Read access, denying writers — so no other program writes it, and this one only reads it: a save writes another file.
                held = File.OpenHandle(this.Path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            }
            catch (IOException)
            {
                return ContentFileHold.WrittenElsewhere;
            }

            _handle.Dispose();
            _handle = held;
            _held = true;
        }

        return ContentFileHold.Held;
    }

    /// <summary>
    /// Saves <paramref name="document"/> to <paramref name="path"/> in <paramref name="encoding"/>, with its mark where <paramref name="preamble"/>
    /// says: written whole to a file beside it and put in its place, so a save that fails leaves the file as it was. The document is then held
    /// over the file it was saved to (<see cref="EditedDocument.Rebase"/>), with everything that could be taken back kept, and that file —
    /// opened with <paramref name="access"/>, and held against other writers where <paramref name="hold"/> says — is what comes back.
    /// Whatever stood over the file before keeps reading the version it had until nothing holds it.
    /// </summary>
    public static ContentFile Save(EditedDocument document, string path, Encoding encoding, bool preamble,
                                   ContentFileAccess access = ContentFileAccess.Editable, bool hold = true)
    {
        var saved = WriteOut(document.Current, path, encoding, preamble, access, hold);

        document.Rebase(ContentDocument.Of(saved));
        document.MarkSaved();

        return saved;
    }

    /// <summary>
    /// The writing half of <see cref="Save"/>: <paramref name="version"/> written to <paramref name="path"/> beside it and put in its place, and the
    /// file it now is, opened, scanned and held as <see cref="Save"/> says — touching no document, so it can be done away from whatever shows
    /// one, which then stands its document over the file this returns (<see cref="EditedDocument.Rebase"/>, <see cref="EditedDocument.MarkSaved"/>).
    /// </summary>
    public static ContentFile WriteOut(ContentDocument version, string path, Encoding encoding, bool preamble,
                                       ContentFileAccess access = ContentFileAccess.Editable, bool hold = true)
    {
        var full = Replace(path, encoding, preamble, version.WriteTo);

        var saved = Open(full, encoding, preamble ? encoding.GetPreamble().Length : 0, access);
        saved.Scan();
        if (hold) saved.HoldForEdit();

        return saved;
    }

    /// <summary>
    /// Writes, through <paramref name="write"/>, a file in <paramref name="encoding"/> — with its mark where <paramref name="preamble"/> says —
    /// beside <paramref name="path"/>, then puts it in its place, so a save that fails leaves the file as it was; and says where it is.
    /// </summary>
    internal static string Replace(string path, Encoding encoding, bool preamble, Action<TextWriter> write)
    {
        var full = System.IO.Path.GetFullPath(path);
        var beside = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(full)!, $".{System.IO.Path.GetFileName(full)}.{Guid.NewGuid():N}.saving");

        try
        {
            using (var stream = new FileStream(beside, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
            using (var writer = new StreamWriter(stream, preamble ? encoding : Unmarked(encoding), 1 << 16))
                write(writer);

            if (File.Exists(full)) File.Replace(beside, full, destinationBackupFileName: null, ignoreMetadataErrors: true);
            else File.Move(beside, full);
        }
        catch
        {
            try { File.Delete(beside); }
            catch (IOException) { }

            throw;
        }

        return full;
    }

    /// <summary>
    /// The blocks holding the characters from <paramref name="start"/> over <paramref name="length"/> — counted from the file's first — the first and
    /// one past the last, and how far into the first they start.
    /// </summary>
    internal (int First, int Last, int Skip) BlocksOf(long start, long length)
    {
        var starts = this.Starts;
        var found = Array.BinarySearch(starts, start);
        var first = Math.Max(0, found >= 0 ? Math.Min(found, starts.Length - 2) : ~found - 1);
        var end = start + length;
        var last = first;

        while (last < starts.Length - 1 && starts[last] < end) last++;

        return (first, Math.Max(last, first + 1), (int)(start - starts[first]));
    }

    /// <summary>Where each block's characters start, counted from the file's first — and, one past the last, where the last ends.</summary>
    private long[] Starts
    {
        get
        {
            lock (_gate)
            {
                if (_starts is { } kept && kept.Length == _blocks.Count + 1) return kept;

                var starts = new long[_blocks.Count + 1];
                for (var block = 0; block < _blocks.Count; block++) starts[block + 1] = starts[block] + _blocks[block].Chars;

                return _starts = starts;
            }
        }
    }

    /// <summary>What <see cref="Starts"/> came to, for the blocks scanned when it was asked.</summary>
    private long[]? _starts;

    /// <summary><paramref name="encoding"/> without the mark at the start of a file it would otherwise write — for a file saved as it was found, without one.</summary>
    public static Encoding Unmarked(Encoding encoding) => encoding switch
    {
        UTF8Encoding => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        UnicodeEncoding => new UnicodeEncoding(bigEndian: encoding.CodePage == 1201, byteOrderMark: false),
        UTF32Encoding => new UTF32Encoding(bigEndian: encoding.CodePage == 12001, byteOrderMark: false),
        _ => encoding,
    };

    /// <summary>The characters of block <paramref name="block"/>: decoded once, then kept while it is among the last read.</summary>
    internal string Block(int block)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ContentFileBlock said;

        lock (_gate)
        {
            if (_cached.TryGetValue(block, out var kept))
            {
                _recent.Remove(kept);
                _recent.AddFirst(kept);
                return kept.Value.Text;
            }

            said = _blocks[block];
        }

        var bytes = new byte[said.ByteLength];
        var read = 0;
        while (read < bytes.Length)
        {
            var got = RandomAccess.Read(_handle, bytes.AsSpan(read), said.ByteStart + read);
            if (got <= 0) throw new IOException($"{this.Path} is shorter than when it was read: block {block} is gone");
            read += got;
        }

        var text = this.Encoding.GetString(bytes);
        if (text.Length != said.Chars) throw new IOException($"{this.Path} changed under what was read: block {block} decodes differently");

        lock (_gate)
        {
            if (_cached.TryGetValue(block, out var raced)) return raced.Value.Text;

            _cached[block] = _recent.AddFirst((block, text));

            while (_recent.Count > CachedBlocks)
            {
                _cached.Remove(_recent.Last!.Value.Block);
                _recent.RemoveLast();
            }
        }

        return text;
    }

    /// <summary>What the scan said of block <paramref name="block"/>.</summary>
    internal ContentFileBlock Said(int block)
    {
        lock (_gate) return _blocks[block];
    }

    /// <summary>How many decoded blocks are kept now, which only the tests of the bound ask.</summary>
    internal int CachedCount
    {
        get { lock (_gate) return _recent.Count; }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _handle.Dispose();

        lock (_gate)
        {
            _cached.Clear();
            _recent.Clear();
        }
    }

    /// <summary>The file opened to be read, sharing it with other readers, other writers, and a save that replaces it.</summary>
    private static SafeFileHandle Shared(string path) =>
        File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// Where a block of <paramref name="bytes"/> — read a few past where it is to end — is cut: at <paramref name="at"/>, or as little before it
    /// as puts it where a character starts.
    /// </summary>
    private int Cut(ReadOnlySpan<byte> bytes, int at)
    {
        switch (this.Encoding)
        {
            case UTF8Encoding:
                // A continuation byte only finishes a character, so the cut moves back off any that follows it.
                while (at > 0 && at < bytes.Length && (bytes[at] & 0xC0) == 0x80) at--;
                return at;

            case UnicodeEncoding:
                at &= ~1;
                // A high surrogate needs the low one after it.
                if (at >= 2 && this.IsHighSurrogate(bytes, at - 2)) at -= 2;
                return at;

            case UTF32Encoding:
                return at & ~3;

            default:
                return at;
        }
    }
    /// <summary>How far into <paramref name="bytes"/> the first character that starts in them starts: past the bytes that only finish one.</summary>
    private int CharacterStart(ReadOnlySpan<byte> bytes)
    {
        switch (this.Encoding)
        {
            case UTF8Encoding:
                var at = 0;
                while (at < bytes.Length && at < 4 && (bytes[at] & 0xC0) == 0x80) at++;
                return at;

            case UnicodeEncoding:
                if (bytes.Length < 2) return 0;
                var unit = this.Encoding.CodePage == 1201 ? (bytes[0] << 8) | bytes[1] : bytes[0] | (bytes[1] << 8);
                return unit is >= 0xDC00 and <= 0xDFFF ? 2 : 0;

            default:
                return 0;
        }
    }

    /// <summary>How many of <paramref name="bytes"/>, the last there is, end on a whole character.</summary>
    private int Whole(ReadOnlySpan<byte> bytes) => this.Encoding switch
    {
        UTF8Encoding => Utf8Whole(bytes),
        UnicodeEncoding => bytes.Length >= 2 && this.IsHighSurrogate(bytes, (bytes.Length & ~1) - 2) ? (bytes.Length & ~1) - 2 : bytes.Length & ~1,
        UTF32Encoding => bytes.Length & ~3,
        _ => bytes.Length,
    };


    /// <summary>How many of <paramref name="bytes"/> end on a whole UTF-8 character: all, less a character begun and not finished.</summary>
    private static int Utf8Whole(ReadOnlySpan<byte> bytes)
    {
        var start = bytes.Length;
        while (start > 0 && bytes.Length - start < 4 && (bytes[start - 1] & 0xC0) == 0x80) start--;
        if (start == 0) return bytes.Length;

        var lead = bytes[start - 1];
        var needs = lead >= 0xF0 ? 4 : lead >= 0xE0 ? 3 : lead >= 0xC0 ? 2 : 1;

        return bytes.Length - (start - 1) >= needs ? bytes.Length : start - 1;
    }

    /// <summary>Whether the UTF-16 code unit at <paramref name="at"/> is the first half of a pair.</summary>
    private bool IsHighSurrogate(ReadOnlySpan<byte> bytes, int at)
    {
        // Code page 1201 is UTF-16 written high byte first.
        var unit = this.Encoding.CodePage == 1201 ? (bytes[at] << 8) | bytes[at + 1] : bytes[at] | (bytes[at + 1] << 8);

        return unit is >= 0xD800 and <= 0xDBFF;
    }
}

/// <summary>Where a block of a <see cref="ContentFile"/> lies, and what the scan found it decodes to — all a document needs of it unread.</summary>
internal readonly record struct ContentFileBlock(long ByteStart, int ByteLength, int Chars, int Breaks, int CrLfs, char First, char Last);
