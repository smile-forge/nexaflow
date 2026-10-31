using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// What is written, as one version of it: the characters, and how this version came from the one before it.
///
/// <para>
/// <strong>Immutable, one version per edit.</strong> An edit is a <see cref="ContentChange"/> applied to a version
/// (<see cref="Apply"/>), and what comes back is a new version that knows the version it was made from and the change that made
/// it, both ways (<see cref="Change"/>, <see cref="Inverse"/>). So whatever has two versions in hand — what is kept so it can be
/// taken back, whatever is told the source changed — knows what changed between them without comparing them
/// (<see cref="ChangesFrom"/>), and a version kept to go back to costs a reference rather than a copy.
/// </para>
/// <para>
/// <strong>One string while it is short, a rope once it is long.</strong> Under <see cref="RopeFrom"/> characters — a note, a
/// reply, most documents — the characters are one string, and an edit makes another: a copy of what is short costs microseconds.
/// From there they are a tree of short strings (<see cref="ContentRope"/>), and an edit makes new nodes only where it writes, so a
/// keystroke costs the same in fifty megabytes as in two hundred kilobytes and a version kept costs only what its edit made. It
/// goes back to one string under <see cref="FlatBelow"/>, so writing back and forth across one length does not switch it on
/// every keystroke. Nothing outside can tell which it is, except by how long things take: read it a stretch at a time
/// (<see cref="Slice"/>, <see cref="CopyTo"/>, <see cref="Chunks"/>), and all of it only where all of it is wanted
/// (<see cref="ToString"/>, <see cref="WriteTo"/>).
/// </para>
/// <para>
/// <strong>Lines are counted, never searched for.</strong> Which line a place is on and where a line starts (<see cref="LineOf"/>,
/// <see cref="OffsetOf"/>) and the line ending it is written with (<see cref="LineEnding"/>) come from counts the rope keeps in
/// every node — or, for one string, from where its lines start, found once for the version the first time it is asked.
/// </para>
/// <para>
/// <strong>Only its parent's number, never its parent.</strong> A version holding the one before it would hold every version
/// back to the first, so it names its parent by <see cref="Version"/> and keeps only the change. A version made some other way —
/// loaded, or handed in whole — has no parent, and what changed from another is found by comparing the two once.
/// </para>
/// <para>
/// Equal to another version holding the same characters, wherever either came from and whichever way either holds them: two
/// documents are the same document when they say the same thing. <c>==</c> is left as it is for any class — the same object —
/// because comparing characters costs what the document holds, and whatever only needs to know whether a version is the one it
/// had asks that, which costs nothing.
/// </para>
/// </summary>
public sealed class ContentDocument : IEquatable<ContentDocument>, IContentCharacters
{
    /// <summary>How long a document has to be before it is held as a rope rather than one string.</summary>
    public const int RopeFrom = 128 * 1024;

    /// <summary>How short a document held as a rope has to become before it is one string again.</summary>
    public const int FlatBelow = 64 * 1024;

    private static long _made;

    /// <summary>The characters, where they are one string; null where they are a rope.</summary>
    private readonly string? _flat;

    /// <summary>The characters, where they are a rope; null where they are one string.</summary>
    private readonly ContentRope? _rope;

    /// <summary>Where each line of one string starts, found the first time a line is asked about.</summary>
    private int[]? _lineStarts;

    private ContentDocument(string? flat, ContentRope? rope, long parent, ContentChange? change, ContentChange? inverse)
    {
        _flat = flat;
        _rope = rope;
        this.Version = Interlocked.Increment(ref _made);
        this.Parent = parent;
        this.Change = change;
        this.Inverse = inverse;
    }

    /// <summary>A version of <paramref name="text"/> made from no other: loaded, or handed in whole.</summary>
    public static ContentDocument Of(string? text) => Holding(text ?? string.Empty, 0, null, null);

    /// <summary>Nothing written.</summary>
    public static ContentDocument Empty { get; } = Of(string.Empty);

    /// <summary>Which version this is: a number no other version in the process has.</summary>
    public long Version { get; }

    /// <summary>The <see cref="Version"/> this was made from by <see cref="Change"/>, or nought for one made from no other.</summary>
    public long Parent { get; }

    /// <summary>The change that made this from its parent, in the parent's offsets — or null for one made from no other.</summary>
    public ContentChange? Change { get; }

    /// <summary>The change that makes the parent again from this, in this version's offsets — or null for one made from no other.</summary>
    public ContentChange? Inverse { get; }

    /// <summary>How many characters it holds.</summary>
    public int Length => _flat?.Length ?? _rope!.Length;

    /// <summary>How many lines it holds: one more than its line breaks, so nothing written is one line.</summary>
    public int LineCount => _rope is { } rope ? rope.Breaks + 1 : this.LineStarts.Length;

    /// <summary>
    /// The line ending it is written with: <c>\r\n</c> where any line ends with one, and <c>\n</c> otherwise — what a line written
    /// into it is to end with so it reads as the lines round it.
    /// </summary>
    public string LineEnding => (_rope?.CrLfs > 0 || (_flat?.Contains("\r\n", StringComparison.Ordinal) ?? false)) ? "\r\n" : "\n";

    /// <summary>Whether its characters are held as a rope, which only the tests of how they are held ask.</summary>
    internal bool Roped => _rope is not null;

    /// <summary>The rope its characters are held in, for the tests of how they are held — or null where they are one string.</summary>
    internal ContentRope? Rope => _rope;

    /// <summary>The character at <paramref name="offset"/>.</summary>
    public char this[int offset] => _flat is { } flat ? flat[offset] : _rope![offset];

    /// <summary>The characters from <paramref name="start"/> over <paramref name="length"/>.</summary>
    public string Slice(int start, int length)
    {
        if (_flat is { } flat) return flat.Substring(start, length);

        Checked(start, length);

        return length == 0 ? string.Empty : string.Create(length, (Rope: _rope!, Start: start), static (into, said) => said.Rope.CopyTo(said.Start, into));
    }

    /// <summary>The characters from <paramref name="start"/> on, as many as <paramref name="into"/> holds.</summary>
    public void CopyTo(int start, Span<char> into)
    {
        Checked(start, into.Length);

        if (_flat is { } flat) flat.AsSpan(start, into.Length).CopyTo(into);
        else _rope!.CopyTo(start, into);
    }

    /// <summary>
    /// The characters from <paramref name="start"/> to <paramref name="end"/> — to the end where it says nothing — as the stretches
    /// they are held in, in order, none of them copied: one for a document held as one string, a few thousand characters each for a rope.
    /// </summary>
    public IEnumerable<ReadOnlyMemory<char>> Chunks(int start = 0, int? end = null)
    {
        var to = end ?? this.Length;
        Checked(start, to - start);

        if (to == start) return [];

        return _flat is { } flat ? [flat.AsMemory(start, to - start)] : _rope!.Chunks(start, to);
    }

    /// <summary>Where <paramref name="value"/> is first found from <paramref name="start"/> on, before <paramref name="end"/> — or -1.</summary>
    public int IndexOf(char value, int start = 0, int? end = null)
    {
        var at = start;

        foreach (var chunk in this.Chunks(start, end))
        {
            var found = chunk.Span.IndexOf(value);
            if (found >= 0) return at + found;

            at += chunk.Length;
        }

        return -1;
    }

    /// <summary>
    /// Where <paramref name="value"/> is first found, ordinally, starting from <paramref name="start"/> on and ending before
    /// <paramref name="end"/> — or -1. Found across where one stretch the characters are held in meets the next, as well as inside one.
    /// </summary>
    public int IndexOf(string value, int start = 0, int? end = null)
    {
        var to = end ?? this.Length;
        if (value.Length == 0) return start <= to ? start : -1;

        if (_flat is { } flat)
        {
            Checked(start, to - start);
            return flat.IndexOf(value, start, to - start, StringComparison.Ordinal);
        }

        // What a match straddling two stretches needs from the end of the one before: all but one of its characters.
        var carried = string.Empty;
        var at = start;

        foreach (var chunk in this.Chunks(start, to))
        {
            if (carried.Length > 0)
            {
                var seam = string.Concat(carried.AsSpan(), chunk.Span[..Math.Min(value.Length - 1, chunk.Length)]);
                var across = seam.IndexOf(value, StringComparison.Ordinal);
                if (across >= 0) return at - carried.Length + across;
            }

            var found = chunk.Span.IndexOf(value.AsSpan(), StringComparison.Ordinal);
            if (found >= 0) return at + found;

            var wanted = value.Length - 1;
            carried = chunk.Length >= wanted ? chunk.Span[^wanted..].ToString()
                    : string.Concat(carried.AsSpan(Math.Max(carried.Length + chunk.Length - wanted, 0)), chunk.Span);
            at += chunk.Length;
        }

        return -1;
    }

    /// <summary>Which line <paramref name="offset"/> is on, counting from nought: how many line breaks end before it.</summary>
    public int LineOf(int offset)
    {
        offset = Math.Clamp(offset, 0, this.Length);

        if (_rope is not { } rope)
        {
            var starts = this.LineStarts;
            var found = Array.BinarySearch(starts, offset);

            return found >= 0 ? found : ~found - 1;
        }

        // Between the two characters of a \r\n, the break has not ended yet.
        return rope.BreaksIn(offset) - (this.Splits(offset) ? 1 : 0);
    }

    /// <summary>Where line <paramref name="line"/> starts, counting from nought — the last line's start for a line past it.</summary>
    public int OffsetOf(int line)
    {
        line = Math.Clamp(line, 0, this.LineCount - 1);

        if (_rope is not { } rope) return this.LineStarts[line];
        if (line == 0) return 0;

        var after = rope.AfterBreak(line);

        return this.Splits(after) ? after + 1 : after;
    }

    /// <summary>
    /// The line <paramref name="offset"/> is on: where it starts, and where it ends — before its line break, which is not the line's.
    /// Found from the counts the rope keeps, or for one string by looking either side of the offset, never by reading the document.
    /// </summary>
    public (int Start, int End) LineAt(int offset)
    {
        // Between the two characters of a \r\n is still the line the \r ends.
        offset = Math.Clamp(offset, 0, this.Length);
        if (this.Splits(offset)) offset--;

        int start, next;

        if (_flat is { } flat)
        {
            start = offset == 0 ? 0 : flat.LastIndexOfAny(['\n', '\r'], offset - 1) + 1;

            next = flat.IndexOfAny(['\n', '\r'], offset);
            next = next < 0 ? flat.Length : next;

            return (start, next);
        }

        var line = this.LineOf(offset);
        start = this.OffsetOf(line);
        next = line + 1 < this.LineCount ? this.OffsetOf(line + 1) : this.Length;

        if (next > start && this[next - 1] == '\n') next--;
        if (next > start && this[next - 1] == '\r') next--;

        return (start, next);
    }

    /// <summary>Whether it holds exactly <paramref name="text"/> — known from the lengths where they differ, and read a stretch at a time where they do not.</summary>
    public bool HoldsExactly(string text) => this.Length == text.Length && this.Holds(0, text);

    /// <summary>Whether this was made from <paramref name="earlier"/> by one change, so <see cref="Change"/> is what changed from it.</summary>
    public bool MadeFrom(ContentDocument earlier) => this.Parent != 0 && this.Parent == earlier.Version;

    /// <summary>
    /// This version with <paramref name="change"/> made to it — or this version itself, where the change writes nothing different.
    /// The change's writes are in this version's offsets and do not overlap, as every <see cref="ContentChange"/>'s are.
    /// </summary>
    public ContentDocument Apply(ContentChange change)
    {
        var writes = change.Writes.OrderBy(write => write.Start).ToList();
        var length = this.Length;

        if (writes.Count == 0 || writes.All(write => write.Length == write.Text.Length && write.Start >= 0 && write.End <= length
                                                     && this.Holds(write.Start, write.Text)))
            return this;

        var inverse = new List<ContentWrite>(writes.Count);
        var (from, at) = (0, 0);

        foreach (var write in writes)
        {
            if (write.Start < from || write.End > length)
                throw new ArgumentException($"a write over {write.Start}..{write.End} overlaps another or runs past the {length} characters written", nameof(change));

            at += write.Start - from;
            inverse.Add(new ContentWrite(at, write.Text.Length, this.Slice(write.Start, write.Length)));
            at += write.Text.Length;
            from = write.End;
        }

        var made = at + length - from;
        var forward = new ContentChange([.. writes.Select(write => new ContentWrite(write.Start, write.Length, write.Text))], change.Caret);
        var backward = new ContentChange(inverse, inverse[0].Start);

        // Held as it was where it stays on its side of the line it would switch at; a rope is made, or let go of, once, on the edit
        // that crosses it.
        if (_rope is { } rope && made >= FlatBelow)
        {
            // Written from the last back, so every write's offsets still mean what they did.
            for (var write = writes.Count - 1; write >= 0; write--)
                rope = ContentRope.Spliced(rope, writes[write].Start, writes[write].End, writes[write].Text)!;

            return new ContentDocument(null, rope, this.Version, forward, backward);
        }

        if (_flat is { } flat && made < RopeFrom) return new ContentDocument(Written(flat, writes, made), null, this.Version, forward, backward);

        return Holding(Written(this.ToString(), writes, made), this.Version, forward, backward);
    }

    /// <summary>
    /// What changed from <paramref name="earlier"/> to this, both ways: the change that makes this from it, and the one that makes it
    /// again from this. Known without looking where this was made from it; otherwise found by comparing the two, once, as the one
    /// stretch where they differ.
    /// </summary>
    public (ContentChange Forward, ContentChange Backward) ChangesFrom(ContentDocument earlier)
    {
        if (this.MadeFrom(earlier)) return (this.Change!, this.Inverse!);

        var start = SameFromStart(earlier, this);
        var same = SameFromEnd(earlier, this, Math.Min(earlier.Length, this.Length) - start);
        var (removed, inserted) = (earlier.Length - start - same, this.Length - start - same);

        return (new ContentChange([new ContentWrite(start, removed, this.Slice(start, inserted))], start + inserted),
                new ContentChange([new ContentWrite(start, inserted, earlier.Slice(start, removed))], start + removed));
    }

    /// <summary>Writes the characters to <paramref name="writer"/> a stretch at a time, without a copy of all of them being made.</summary>
    public void WriteTo(TextWriter writer)
    {
        foreach (var chunk in this.Chunks()) writer.Write(chunk.Span);
    }

    /// <summary>The characters, all of them — one string made of a rope, which costs what the document holds.</summary>
    public override string ToString() =>
        _flat ?? string.Create(_rope!.Length, _rope, static (into, rope) => rope.CopyTo(0, into));

    public bool Equals(ContentDocument? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_flat is { } flat && other._flat is { } theirs) return string.Equals(flat, theirs, StringComparison.Ordinal);

        return this.Length == other.Length && SameFromStart(this, other) == this.Length;
    }

    public override bool Equals(object? obj) => this.Equals(obj as ContentDocument);

    /// <summary>Hashed over the characters, a stretch at a time, so a version held one way hashes as the same characters held the other.</summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var chunk in this.Chunks()) hash.AddBytes(MemoryMarshal.AsBytes(chunk.Span));

        return hash.ToHashCode();
    }

    /// <summary><paramref name="text"/> held the way its length says, made from <paramref name="parent"/> by <paramref name="change"/>.</summary>
    private static ContentDocument Holding(string text, long parent, ContentChange? change, ContentChange? inverse) =>
        text.Length >= RopeFrom ? new(null, ContentRope.Of(text), parent, change, inverse) : new(text, null, parent, change, inverse);

    /// <summary><paramref name="text"/> with <paramref name="writes"/> made to it, in order, coming to <paramref name="length"/> characters.</summary>
    private static string Written(string text, List<ContentWrite> writes, int length) =>
        // Written straight into the string it makes: one copy of what is written, however long, rather than a buffer and a copy of it.
        string.Create(length, (Text: text, Writes: writes), static (into, said) =>
        {
            var (from, at) = (0, 0);

            foreach (var write in said.Writes)
            {
                said.Text.AsSpan(from, write.Start - from).CopyTo(into[at..]);
                at += write.Start - from;
                write.Text.AsSpan().CopyTo(into[at..]);
                at += write.Text.Length;
                from = write.End;
            }

            said.Text.AsSpan(from).CopyTo(into[at..]);
        });

    /// <summary>Where each line of one string starts.</summary>
    private int[] LineStarts
    {
        get
        {
            if (_lineStarts is { } starts) return starts;

            var text = _flat!;
            var found = new List<int> { 0 };

            for (var at = 0; at < text.Length; at++)
            {
                if (text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n') continue;
                if (text[at] is '\n' or '\r') found.Add(at + 1);
            }

            return _lineStarts = [.. found];
        }
    }

    /// <summary>Whether <paramref name="offset"/> stands between the <c>\r</c> and the <c>\n</c> of one line break.</summary>
    private bool Splits(int offset) => offset > 0 && offset < this.Length && this[offset - 1] == '\r' && this[offset] == '\n';

    /// <summary>Whether the characters from <paramref name="start"/> are <paramref name="text"/>.</summary>
    private bool Holds(int start, string text)
    {
        if (_flat is { } flat) return string.CompareOrdinal(flat, start, text, 0, text.Length) == 0;

        var at = 0;

        foreach (var chunk in _rope!.Chunks(start, start + text.Length))
        {
            if (!chunk.Span.SequenceEqual(text.AsSpan(at, chunk.Length))) return false;
            at += chunk.Length;
        }

        return true;
    }

    /// <summary>Throws where <paramref name="start"/> over <paramref name="length"/> is not inside the document.</summary>
    private void Checked(int start, int length)
    {
        if (start < 0 || length < 0 || start + length > this.Length)
            throw new ArgumentOutOfRangeException(nameof(start), $"{start} over {length} is not inside the {this.Length} characters written");
    }

    /// <summary>How many characters <paramref name="left"/> and <paramref name="right"/> start with alike.</summary>
    private static int SameFromStart(ContentDocument left, ContentDocument right)
    {
        Span<char> ours = stackalloc char[1024];
        Span<char> theirs = stackalloc char[1024];
        var (shortest, at) = (Math.Min(left.Length, right.Length), 0);

        while (at < shortest)
        {
            var taken = Math.Min(ours.Length, shortest - at);
            left.CopyTo(at, ours[..taken]);
            right.CopyTo(at, theirs[..taken]);

            var differs = ours[..taken].CommonPrefixLength(theirs[..taken]);
            if (differs < taken) return at + differs;

            at += taken;
        }

        return shortest;
    }

    /// <summary>How many characters <paramref name="left"/> and <paramref name="right"/> end with alike, looking back no further than <paramref name="most"/>.</summary>
    private static int SameFromEnd(ContentDocument left, ContentDocument right, int most)
    {
        Span<char> ours = stackalloc char[1024];
        Span<char> theirs = stackalloc char[1024];
        var same = 0;

        while (same < most)
        {
            var taken = Math.Min(ours.Length, most - same);
            left.CopyTo(left.Length - same - taken, ours[..taken]);
            right.CopyTo(right.Length - same - taken, theirs[..taken]);

            for (var back = taken - 1; back >= 0; back--)
                if (ours[back] != theirs[back]) return same + (taken - 1 - back);

            same += taken;
        }

        return most;
    }
}
