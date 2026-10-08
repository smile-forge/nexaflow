using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// A move, told to the language it is being let go in: what is being carried, where it is being dropped, and the part of
/// that language's syntax tree the drop landed on.
///
/// <para>
/// Found the same way an edit is (<see cref="ContentEdit"/>): from the piece under the drop, up the layout to the first
/// piece drawn from a part, and from that part up its own tree to the root, which names the language. The language asked
/// is the one the carry is let go in, because that is where what a move means is decided — a column let go between two
/// columns is the matrix's business, and the same gesture over a paragraph is characters arriving.
/// </para>
/// <para>
/// <strong>What is carried is said rather than looked up.</strong> A handler that read it off the selection would be
/// working out again what the engine already knows, and would have to know for itself how a selection of several
/// stretches maps onto the parts it covers — which is exactly the knowledge that makes a move of whole cells different
/// from a move of characters.
/// </para>
/// <para>
/// <strong>Offsets are the document's</strong>, as they are for an edit: every stretch carried and the place it is let go
/// are offsets into the whole document, so a language laid inside another compares them with its own parts without
/// anything being moved.
/// </para>
/// </summary>
/// <param name="Carried">The stretches being carried, in the document as it stands, in the order they were written.</param>
/// <param name="To">Where they are being let go, in the document as it stands.</param>
/// <param name="Landing">The document as it stands, what was drawn of it, and where the caret is.</param>
/// <param name="Piece">The piece of layout the drop landed on.</param>
/// <param name="Part">The part of the syntax tree that piece was drawn from — or null, where nothing on the way up names one.</param>
/// <param name="Root">The root of that part's tree: the language's own source, all of it.</param>
public sealed record ContentMove(IReadOnlyList<EditRange> Carried, int To, Landing Landing, Piece Piece,
                                 ContentPart? Part, ContentPart Root)
{
    /// <summary>Everything being moved in, not only this language's part of it.</summary>
    public EditState State => this.Landing.State;

    /// <summary>What was drawn of it.</summary>
    public Laid Laid => this.Landing.Laid;

    /// <summary>Where the language's own source begins in the document.</summary>
    public int Start => this.Root.Start;

    /// <summary>Where it ends.</summary>
    public int End => this.Root.End();

    /// <summary>The characters being carried, in the order they were written.</summary>
    public string Text =>
        string.Concat(this.Carried.Select(range => this.State.Source.Substring(range.Start, range.Length)));

    /// <summary>
    /// Whether everything being carried was written in this language's own source — a column of this matrix rather than
    /// something brought in from the prose around it.
    /// </summary>
    public bool Holds => this.Carried.All(range => range.Start >= this.Start && range.End <= this.End);

    /// <summary>
    /// The parts of this language's tree that the carried stretches cover whole, outermost first.
    ///
    /// <para>
    /// A walk, not a property: a drag asks this once per place the drop crosses, and a handler that wants the parts
    /// should be able to see what it costs.
    /// </para>
    /// </summary>
    public IReadOnlyList<ContentPart> Carrying() =>
        [.. this.Root.SelfAndDescendants()
                .Where(part => !part.Derived
                               && part.Length > 0
                               && this.Carried.Any(range => range.Start <= part.Start && part.End <= range.End))];
}

/// <summary>
/// What a language says a move means in its own source, where it says anything.
///
/// <para>
/// <strong>The engine owns the gesture.</strong> Pressing on what is picked out, carrying it, laying the content out as
/// it would read after the drop, and letting it go are all the engine's — and so is what a move means by default: the
/// characters carried, emptied from where they were and written in at the drop. That is right for everything whose
/// source is what a reader sees, which is why most languages say nothing here.
/// </para>
/// <para>
/// A language says otherwise when what is carried is something of its own: a column of a matrix, a slice of a pie, a
/// note of a tune. None of those is a run of characters to be spliced — the stretches they stand for are not next to one
/// another, and where they land is a place in a structure rather than an offset — so the language says which stretches
/// to write and what goes in them, exactly as it does for an edit.
/// </para>
/// <para>
/// Null is the ordinary answer, and means the characters move as they would anywhere.
/// </para>
/// </summary>
public interface IOnMove
{
    /// <summary>What <paramref name="move"/> is to be here, or null for what a move does anywhere.</summary>
    ContentChange? Move(ContentMove move);
}
