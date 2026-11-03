using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Editing;

/// <summary>
/// One stretch of the document written over, named in the document as it stood before the edit — by the node of the syntax tree
/// whose characters are given a new value, or by where it starts and how long it is where no node stands for it (text typed at a
/// caret).
/// </summary>
/// <param name="Start">Where the stretch starts.</param>
/// <param name="Length">How long it is: none, for text written between two characters.</param>
/// <param name="Text">What is written in its place.</param>
public readonly record struct ContentWrite(int Start, int Length, string Text)
{
    /// <summary><paramref name="part"/>'s characters given <paramref name="value"/> in their place.</summary>
    public ContentWrite(ContentPart part, string value) : this(part.Start, part.Length, value) => this.Part = part;

    /// <summary>
    /// <paramref name="words"/> written in <paramref name="part"/>, from <paramref name="start"/> over <paramref name="length"/>
    /// characters, as the reader means them rather than as the source has to hold them: the engine has the language's parser make
    /// them safe for the part before they are written, and writes nothing where they cannot be.
    /// </summary>
    public static ContentWrite Words(ContentPart part, int start, int length, string words) =>
        new(start, length, words) { Part = part, Meant = true };

    /// <summary>The node whose characters are written over, where the edit named one.</summary>
    public ContentPart? Part { get; init; }

    /// <summary>Whether <see cref="Text"/> is words as the reader means them, still to be made safe for <see cref="Part"/>.</summary>
    public bool Meant { get; init; }

    /// <summary>Where the stretch ends.</summary>
    public int End => this.Start + this.Length;
}

/// <summary>
/// What an edit is to be, as the language it landed in says: every stretch written over and what goes there, and where the
/// caret and whatever is shown as written stand once it is made. The engine makes it and lays the content out again; a language
/// only says what it is.
/// </summary>
/// <param name="Writes">The stretches written over, in the document as it stood, none overlapping another — none at all for an edit that only takes the key, or only shows something as written.</param>
/// <param name="Caret">Where the caret goes, in the document as it reads afterwards.</param>
/// <param name="Raw">What is shown as written afterwards, in those offsets too — or null for nothing.</param>
public sealed record ContentChange(IReadOnlyList<ContentWrite> Writes, int Caret, RawZone? Raw = null)
{
    /// <summary>The key taken and nothing changed: what a key means where there is nothing here for it to do.</summary>
    /// <summary>
    /// What the change leaves picked out as whole things drawn — a box the reader has finished writing in — rather than a caret to
    /// write at; or null, where it leaves the caret.
    /// </summary>
    public WholeRanges? Chooses { get; init; }

    /// <summary>
    /// What the change leaves picked out as stretches of text, in the document it makes — lines indented and still picked out, so the key
    /// can be pressed again — or null, where it leaves the caret, or what <see cref="Chooses"/> says.
    /// </summary>
    public IReadOnlyList<EditRange>? PicksOut { get; init; }

    public static ContentChange Stay(EditState state) => new([], state.Caret, state.Raw);

    /// <summary>
    /// Nothing of the language's own: what the engine does for <paramref name="kind"/> anyway, wherever the reader asked for
    /// it. A diagram offers Paste over a label because a label takes words, and what a paste comes to is the engine's — so it
    /// says which edit it means rather than spelling out an edit it would only get wrong.
    /// </summary>
    public static ContentChange Asks(EditKind kind) => new([], 0) { Asked = kind };

    /// <summary>The edit the language asked the engine to make of its own, where it asked for one instead of saying what to write.</summary>
    public EditKind? Asked { get; init; }

    /// <summary>Nothing written, and <paramref name="shown"/> put in front of the reader as the characters it was written with.</summary>
    public static ContentChange Showing(EditState state, RawZone? shown) => new([], state.Caret, shown);

    /// <summary><paramref name="text"/> written over one stretch, and the caret after it unless <paramref name="caret"/> says otherwise.</summary>
    public static ContentChange Write(int start, int length, string text, int? caret = null, RawZone? raw = null) =>
        new([new ContentWrite(start, length, text)], caret ?? start + text.Length, raw);

    /// <summary>
    /// <paramref name="text"/> written as typing writes it: over whatever is picked out, or at the caret, and the caret after it.
    /// What is shown as written is <paramref name="shown"/> where that is said; otherwise it stays over the characters it was over,
    /// and is gone with what was picked out.
    /// </summary>
    public static ContentChange Typed(EditState state, string text, RawZone? shown = null)
    {
        if (state.HasSelection)
        {
            var from = state.Selection[0].Start;
            return new([.. state.Selection.Select((range, at) => new ContentWrite(range.Start, range.Length, at == 0 ? text : string.Empty))],
                       from + text.Length, shown);
        }

        var caret = Math.Clamp(state.Caret, 0, state.Document.Length);
        return new([new ContentWrite(caret, 0, text)], caret + text.Length, shown ?? Moved(state.Raw, caret, text.Length));

        // What is shown as written stays over the same characters: pushed along by what is written in front of it, and grown by
        // what is written inside it.
        static RawZone? Moved(RawZone? zone, int at, int by) => zone switch
        {
            null => null,
            { } z when at <= z.Start => z with { Start = z.Start + by, End = z.End + by },
            { } z when at < z.End => z with { End = z.End + by },
            var z => z,
        };
    }

    /// <summary>The same, and <paramref name="write"/> as well — the caret and what is shown as written as they were said.</summary>
    public ContentChange And(ContentWrite write) => this with { Writes = [.. this.Writes, write] };

    /// <summary>
    /// Whether <paramref name="other"/> writes exactly what this writes, exactly where — the same edit, however each was arrived at.
    /// What two drops at neighbouring points ask the same question by: a drop that writes what the last one wrote need not be laid
    /// out again.
    /// </summary>
    public bool WritesAlike(ContentChange? other) =>
        other is not null && this.Writes.Count == other.Writes.Count
        && this.Writes.Zip(other.Writes).All(pair => pair.First.Start == pair.Second.Start && pair.First.Length == pair.Second.Length
                                                     && string.Equals(pair.First.Text, pair.Second.Text, StringComparison.Ordinal));
}

/// <summary>
/// Stretches picked out as whole things drawn — a box, a slice — rather than characters dragged over: nothing in them is being written
/// in, so no caret stands there. What a change that ends the writing leaves picked out (<see cref="ContentChange.Chooses"/>), carried
/// as the selection it makes so whatever shows it knows it was chosen whole.
/// </summary>
public sealed class WholeRanges(IList<EditRange> ranges) : System.Collections.ObjectModel.ReadOnlyCollection<EditRange>(ranges);
