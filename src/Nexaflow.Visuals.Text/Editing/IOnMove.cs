using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

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
/// <param name="Carried">The stretches being carried, in the document as it stands, in the order they were written — none, for a carry of the language's own.</param>
/// <param name="To">Where they are being let go, in the document as it stands.</param>
/// <param name="Landing">The document as it stands, what was drawn of it, and where the caret is.</param>
/// <param name="Piece">The piece of layout the drop landed on.</param>
/// <param name="Part">The part of the syntax tree that piece was drawn from — or null, where nothing on the way up names one.</param>
/// <param name="Root">The root of that part's tree: the language's own source, all of it.</param>
public sealed record ContentMove(IReadOnlyList<EditRange> Carried, int To, Landing Landing, LayoutPiece Piece,
                                 ContentPart? Part, ContentPart Root)
{
    /// <summary>
    /// What is being carried where it is a carry of the language's own, picked up by a press the language answered with it
    /// (<see cref="IOnMove.Lift"/>) — or null where what is carried is what was picked out, the stretches <see cref="Carried"/> names.
    /// </summary>
    public ContentCarry? Carry { get; init; }

    /// <summary>Where the pointer is, in the content's own coordinates — which point on <see cref="Piece"/> it is being let go at.</summary>
    public LayoutPoint At { get; init; }

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
/// What a language says a carry is in its own content: what a press there picks up, and what letting it go somewhere means.
///
/// <para>
/// <strong>The engine owns the gesture.</strong> Pressing, carrying, laying the content out as it would read after the drop, and
/// letting it go are all the engine's. What is picked up and what a drop means are the language's, because the layout knows
/// neither: where a press began and where a drop landed are only pieces drawn and the parts they were drawn from, which the
/// language alone can read.
/// </para>
/// <para>
/// <strong>There are three cases, and each is said.</strong> A language that is not asked — it has no <see cref="IOnMove"/> —
/// carries what is picked out as characters, emptied from where they were and written in at the drop, which is right for
/// everything whose source is what a reader sees. A language that is asked answers every press: <see cref="ContentLift.Selection"/>
/// to have what is picked out carried as the engine carries it, told to <see cref="Move"/> where it is let go; or a carry of its own
/// (<see cref="ContentCarry"/>), picked up by that press alone with nothing picked out first — the edge of a box, a handle — and only
/// ever let go in that language's own content.
/// </para>
/// </summary>
public interface IOnMove
{
    /// <summary>What a press at <paramref name="grip"/> picks up: what is picked out, as the engine carries it, or a carry of the language's own.</summary>
    ContentLift Lift(ContentGrip grip);

    /// <summary>
    /// What <paramref name="move"/> is to be here, or null for what a move of characters does anywhere. A carry of the language's own
    /// (<see cref="ContentMove.Carry"/>) is the language's to answer; null for it means it goes nowhere.
    /// </summary>
    ContentChange? Move(ContentMove move);
}

/// <summary>
/// What a press picks up, as the language it landed in answers it (<see cref="IOnMove.Lift"/>): what is picked out, carried as the
/// engine carries it (<see cref="Selection"/>), or a carry of the language's own (<see cref="ContentCarry"/>).
/// </summary>
public abstract record ContentLift
{
    private protected ContentLift() { }

    /// <summary>What is picked out, carried as the engine carries it: a press on it picks it up, and a press anywhere else picks up nothing.</summary>
    public static ContentLift Selection { get; } = new Picked();

    private sealed record Picked : ContentLift;
}

/// <summary>
/// A carry of a language's own — something its content has that is no stretch of characters picked out, such as the edge of a box
/// — picked up by the press that answered it and handed back to that language's <see cref="IOnMove.Move"/> wherever it is carried.
/// What it holds is the language's alone; the engine only carries it.
/// </summary>
public abstract record ContentCarry : ContentLift;

/// <summary>
/// A press, told to the language it landed in: the piece pressed, the part of that language's syntax tree it was drawn from (or its
/// nearest parent's), and where the pointer was — found the same way an edit is (<see cref="ContentEdit"/>).
/// </summary>
/// <param name="Landing">The document as it stands, what was drawn of it, and where the caret is.</param>
/// <param name="Piece">The piece of layout pressed.</param>
/// <param name="Part">The part of the syntax tree that piece was drawn from — or null, where nothing on the way up names one.</param>
/// <param name="Root">The root of that part's tree: the language's own source, all of it.</param>
/// <param name="At">Where the press was, in the content's own coordinates.</param>
public sealed record ContentGrip(Landing Landing, LayoutPiece Piece, ContentPart? Part, ContentPart Root, LayoutPoint At)
{
    /// <summary>Everything being pressed in, not only this language's part of it.</summary>
    public EditState State => this.Landing.State;
}
