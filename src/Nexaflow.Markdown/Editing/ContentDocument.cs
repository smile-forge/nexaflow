using System;
using System.Collections.Generic;
using System.Linq;

using System.Threading;

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
/// <strong>Only its parent's number, never its parent.</strong> A version holding the one before it would hold every version
/// back to the first, so it names its parent by <see cref="Version"/> and keeps only the change. A version made some other way —
/// loaded, or handed in whole — has no parent, and what changed from another is found by comparing the two once.
/// </para>
/// <para>
/// Equal to another version holding the same characters, wherever either came from: two documents are the same document when
/// they say the same thing. <c>==</c> is left as it is for any class — the same object — because comparing characters costs what
/// the document holds, and whatever only needs to know whether a version is the one it had asks that, which costs nothing.
/// </para>
/// </summary>
public sealed class ContentDocument : IEquatable<ContentDocument>
{
    private static long _made;

    private readonly string _text;

    private ContentDocument(string text, long parent, ContentChange? change, ContentChange? inverse)
    {
        _text = text;
        this.Version = Interlocked.Increment(ref _made);
        this.Parent = parent;
        this.Change = change;
        this.Inverse = inverse;
    }

    /// <summary>A version of <paramref name="text"/> made from no other: loaded, or handed in whole.</summary>
    public static ContentDocument Of(string? text) => new(text ?? string.Empty, 0, null, null);

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
    public int Length => _text.Length;

    /// <summary>The character at <paramref name="offset"/>.</summary>
    public char this[int offset] => _text[offset];

    /// <summary>The characters from <paramref name="start"/> over <paramref name="length"/>.</summary>
    public string Slice(int start, int length) => _text.Substring(start, length);

    /// <summary>Whether this was made from <paramref name="earlier"/> by one change, so <see cref="Change"/> is what changed from it.</summary>
    public bool MadeFrom(ContentDocument earlier) => this.Parent != 0 && this.Parent == earlier.Version;

    /// <summary>
    /// This version with <paramref name="change"/> made to it — or this version itself, where the change writes nothing different.
    /// The change's writes are in this version's offsets and do not overlap, as every <see cref="ContentChange"/>'s are.
    /// </summary>
    public ContentDocument Apply(ContentChange change)
    {
        var writes = change.Writes.OrderBy(write => write.Start).ToList();
        if (writes.Count == 0 || writes.All(write => write.Length == write.Text.Length && write.Start >= 0 && write.End <= _text.Length
                                                     && string.CompareOrdinal(_text, write.Start, write.Text, 0, write.Length) == 0))
            return this;

        var inverse = new List<ContentWrite>(writes.Count);
        var (from, at) = (0, 0);

        foreach (var write in writes)
        {
            if (write.Start < from || write.End > _text.Length)
                throw new ArgumentException($"a write over {write.Start}..{write.End} overlaps another or runs past the {_text.Length} characters written", nameof(change));

            at += write.Start - from;
            inverse.Add(new ContentWrite(at, write.Text.Length, _text.Substring(write.Start, write.Length)));
            at += write.Text.Length;
            from = write.End;
        }

        // Written straight into the string it makes: one copy of what is written, however long, rather than a buffer and a copy of it.
        var text = string.Create(at + _text.Length - from, (Text: _text, Writes: writes), static (into, said) =>
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

        var forward = new ContentChange([.. writes.Select(write => new ContentWrite(write.Start, write.Length, write.Text))], change.Caret);
        return new ContentDocument(text, this.Version, forward, new ContentChange(inverse, inverse[0].Start));
    }

    /// <summary>
    /// What changed from <paramref name="earlier"/> to this, both ways: the change that makes this from it, and the one that makes it
    /// again from this. Known without looking where this was made from it; otherwise found by comparing the two, once, as the one
    /// stretch where they differ.
    /// </summary>
    public (ContentChange Forward, ContentChange Backward) ChangesFrom(ContentDocument earlier)
    {
        if (this.MadeFrom(earlier)) return (this.Change!, this.Inverse!);

        var (was, now) = (earlier._text, _text);
        var shorter = Math.Min(was.Length, now.Length);

        var start = 0;
        while (start < shorter && was[start] == now[start]) start++;

        var same = 0;
        while (same < shorter - start && was[was.Length - 1 - same] == now[now.Length - 1 - same]) same++;

        var (removed, inserted) = (was.Length - start - same, now.Length - start - same);

        return (new ContentChange([new ContentWrite(start, removed, now.Substring(start, inserted))], start + inserted),
                new ContentChange([new ContentWrite(start, inserted, was.Substring(start, removed))], start + removed));
    }

    /// <summary>The characters, all of them.</summary>
    public override string ToString() => _text;

    public bool Equals(ContentDocument? other) =>
        other is not null && (ReferenceEquals(this, other) || string.Equals(_text, other._text, StringComparison.Ordinal));

    public override bool Equals(object? obj) => this.Equals(obj as ContentDocument);

    public override int GetHashCode() => _text.GetHashCode(StringComparison.Ordinal);
}
