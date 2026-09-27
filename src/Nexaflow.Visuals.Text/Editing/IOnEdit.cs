using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What a key did: wrote something, ended what was half-written, broke a line, took back the character on one side of the caret,
/// moved on to the next place to write in, asked for something new where the caret is, or chose from what was offered.
/// </summary>
public enum EditKind
{
    /// <summary>Text written at the caret — a key, a palette's symbol.</summary>
    Typing,

    /// <summary>Words pasted at the caret, or over what is picked out: the clipboard's, as plain words.</summary>
    Pasting,

    /// <summary>Space or Enter: what ends whatever is half-written.</summary>
    Settling,

    /// <summary>Shift+Enter: a line broken inside what is being written, rather than what comes after it.</summary>
    Breaking,

    /// <summary>Backspace: the character before the caret.</summary>
    Erasing,

    /// <summary>Delete: the character after it.</summary>
    Deleting,

    /// <summary>Tab: on to the next place to write in.</summary>
    Tabbing,

    /// <summary>Shift+Tab: back to the one before.</summary>
    TabbingBack,

    /// <summary>Insert: something new, where the caret is or before what is picked out.</summary>
    Inserting,

    /// <summary>One of what the language offered was chosen; <see cref="ContentEdit.Text"/> is its verb.</summary>
    Choosing,

    /// <summary>What is picked out, carried and let go over the piece the edit applied to.</summary>
    Dropping,
}

/// <summary>
/// An edit, told to the language it landed in: what it is, the piece of layout it applied to, and the part of that language's
/// syntax tree the piece was drawn from.
///
/// <para>
/// <strong>Found by walking two trees.</strong> From the piece the caret stands against, up the layout to the first piece drawn
/// from a part; from that part, up its own syntax tree to the root, which names the language (<see cref="BlockNode"/>). Every
/// tree a language reads is its own, and only the layout is one tree, so the layout is the only way from one language to
/// another — and nothing has to be tracked to know which language an edit is in.
/// </para>
/// <para>
/// <strong>Offsets are the document's.</strong> A language laid inside another is laid at the offset its source starts at, so
/// every part it drew names the characters a reader is typing between: the caret, a hole and a run of words all agree without
/// anything being moved. <see cref="Start"/> and <see cref="End"/> say where the language's own source is.
/// </para>
/// </summary>
/// <param name="Kind">What the key did.</param>
/// <param name="Text">What it wrote: the text typed, or the separator settling — nothing, for a key taking a character back.</param>
/// <param name="Landing">The document as it stands, what was drawn of it, and where the caret is.</param>
/// <param name="Piece">The piece of layout the edit applied to.</param>
/// <param name="Part">The part of the syntax tree that piece was drawn from — or null, where nothing on the way up names one.</param>
/// <param name="Root">The root of that part's tree: the language's own source, all of it.</param>
public sealed record ContentEdit(EditKind Kind, string Text, Landing Landing, Piece Piece, ContentPart? Part, ContentPart Root)
{
    /// <summary>Everything being edited, not only this language's part of it.</summary>
    public EditState State => this.Landing.State;

    /// <summary>What was drawn of it.</summary>
    public Laid Laid => this.Landing.Laid;

    /// <summary>Where the language's own source begins in the document.</summary>
    public int Start => this.Root.Start;

    /// <summary>Where it ends.</summary>
    public int End => this.Root.End();

    /// <summary>The language's own source, exactly as it was typed.</summary>
    public string Source => this.State.Source[this.Start..this.End];

    /// <summary>Whether a part is one of this language's, rather than of whatever it is written in.</summary>
    public bool Holds(ISourcePart part) => part.Start >= this.Start && part.End() <= this.End;
}

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

/// <summary>
/// What a language says an edit means in its own source.
///
/// <para>
/// <strong>Told everything, and it only answers.</strong> The engine walks from the caret to the language the edit landed in
/// (<see cref="ContentEdit"/>) and asks it what the edit is to be; the language says, and the engine makes it and lays the
/// content out again. What an edit brings with it elsewhere in the same source — a name renamed where it is declared, renamed
/// where it is used — is part of the answer, because it is part of what the edit is.
/// </para>
/// <para>
/// Null is the ordinary answer: what is typed is written at the caret, and what is taken back is a character.
/// </para>
/// </summary>
public interface IOnEdit
{
    /// <summary>What <paramref name="edit"/> is to be here, or null for what the key does anywhere.</summary>
    ContentChange? Edit(ContentEdit edit);
}
