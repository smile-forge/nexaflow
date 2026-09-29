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
    public static ContentChange Stay(EditState state) => new([], state.Caret, state.Raw);

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

        var caret = Math.Clamp(state.Caret, 0, state.Source.Length);
        return new([new ContentWrite(caret, 0, text)], caret + text.Length, shown ?? Moved(state.Raw, caret, text.Length));

        // What is shown as written stays over the same characters: pushed along by what is written in front of it, and grown by
        // what is written inside it.
        static RawZone? Moved(RawZone? zone, int at, int by) => zone switch
        {
            null => null,
            { } z when at <= z.Start => new RawZone(z.Start + by, z.End + by),
            { } z when at < z.End => z with { End = z.End + by },
            var z => z,
        };
    }

    /// <summary>The same, and <paramref name="write"/> as well — the caret and what is shown as written as they were said.</summary>
    public ContentChange And(ContentWrite write) => this with { Writes = [.. this.Writes, write] };
}
