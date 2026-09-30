using System;
using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Editing;

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
