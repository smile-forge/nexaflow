using System.Collections.Generic;
using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>What was done to a piece.</summary>
public enum LayoutGesture
{
    /// <summary>Pressed once.</summary>
    Click,

    /// <summary>Pressed twice.</summary>
    DoubleClick,

    /// <summary>Asked what can be done to it.</summary>
    ContextMenu,

    /// <summary>Chosen — by the press that picked it, or by a sweep that took it in.</summary>
    Select,
}

/// <summary>
/// One thing that may be done, and what it would be done to.
/// </summary>
/// <param name="Verb">Which of the things it is — <see cref="LayoutVerbs"/>, or a content's own.</param>
/// <param name="Target">What it would be done to, said the way the content that offered it says such things.</param>
/// <param name="Tip">What it is called where a reader has to read it.</param>
public readonly record struct LayoutIntent(string Verb, string? Target = null, string? Tip = null)
{
    /// <summary>
    /// Which of the two kinds of thing this is.
    ///
    /// <para>
    /// A reader choosing between them is doing two different things. Adding something means browsing what
    /// there is to add, and there are usually many — so they are gathered behind one button and read as a
    /// list. Doing something to what is already there is not browsing: a reader reaching for "Delete"
    /// knows what they want, and hiding it one level down makes them hunt for it.
    /// </para>
    /// </summary>
    public LayoutOffer Offer { get; init; }

    /// <summary>
    /// The choice this is one of the options of — a legend's position, a hole's size — or null for something done on its own.
    /// Options of one choice are shown side by side, one of them <see cref="Current"/>.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// What <see cref="Group"/> is called where a reader has to read it, in the window's language — or null, where the group's own
    /// name says it. The group is what the options are known by, so it stays the same whichever language is shown.
    /// </summary>
    public string? GroupTip { get; init; }

    /// <summary>Whether this option is what the content says now.</summary>
    public bool Current { get; init; }

    /// <summary>
    /// What it is drawn as where a picture says it better than a word — a shape in a 16 by 16 box, filled in the ink of whatever
    /// shows it — or null, to be named. Its name is still what a reader hovering over it, or hearing the screen read, is told.
    /// </summary>
    public System.Windows.Media.Geometry? Shape { get; init; }

    /// <summary>
    /// The colour this option is, where the option is itself a colour — drawn as a swatch of that colour rather than as a shape in
    /// the ink of whatever shows it, and drawn as nothing at all where it is clear. Null for every offer that is not a colour.
    /// </summary>
    public System.Windows.Media.Color? Shade { get; init; }

    /// <summary>
    /// The few letters it is drawn as, where neither a shape nor a colour says it — a flowchart's <c>text</c> shape, whose whole
    /// shape is having none, so a picture of it would be a picture of the shape standing next to it. Null for everything else.
    /// </summary>
    public string? Letters { get; init; }
}

/// <summary>Which of the two kinds of thing an offer is.</summary>
public enum LayoutOffer
{
    /// <summary>Something done to what is there.</summary>
    Change,

    /// <summary>Something new, put where the gesture landed.</summary>
    Insert,
}

/// <summary>
/// What the pieces of a layout answer to, read as gestures: what a builder said a press or a choosing means
/// (<see cref="LayoutActions"/>), said as an offer the host answers.
/// </summary>
public static class LayoutAnswers
{
    /// <summary>What <paramref name="gesture"/> means on a piece answering with <paramref name="acts"/>, or null where it means nothing.</summary>
    public static LayoutIntent? For(this LayoutActions? acts, LayoutGesture gesture) => gesture switch
    {
        LayoutGesture.Click => Offer(acts?.Click),
        LayoutGesture.Select => Offer(acts?.Select),
        _ => null,
    };

    /// <summary>What a builder said, as an offer.</summary>
    public static LayoutIntent? Offer(LayoutAsk? intent) =>
        intent is { } said ? new LayoutIntent(said.Verb, said.Target, said.Tip) : null;
}

/// <summary>
/// A gesture, and everything answering it needs: what was meant, the piece it was meant on, the part of the source
/// that piece stands for, and — where an AST is behind the source — that part as the tree knows it.
/// </summary>
/// <param name="Part">
/// The piece's own part, else the nearest ancestor's: the walk up the tree from what was pressed to the thing it
/// belongs to. See <see cref="LayoutPiece.Naming"/>.
/// </param>
/// <param name="Node">The same, as the AST knows it, where it is a node of one — a piece may name a span with no tree behind it.</param>
/// <param name="Over">What the gesture is about: the piece alone, or the whole selection it was made inside.</param>
/// <param name="At">Where the pointer was, in the content's own coordinates.</param>
public readonly record struct LayoutAct(
    LayoutGesture Gesture,
    LayoutIntent Intent,
    LayoutPiece Piece,
    ISourcePart? Part,
    ContentPart? Node,
    IReadOnlyList<LayoutPiece> Over,
    LayoutPoint At);

/// <summary>
/// What answers a gesture — the far end of the chain a builder starts by declaring an intent.
/// </summary>
public interface ILayoutActions
{
    /// <summary>
    /// Answers it, and says whether it did. False leaves the press meaning what it otherwise would, so a node that
    /// offers something the host does not want is still an ordinary thing to select.
    /// </summary>
    bool Invoke(LayoutAct act);

    /// <summary>What may be done where the gesture landed. Nothing, unless a host says otherwise.</summary>
    IReadOnlyList<LayoutIntent> Menu(LayoutAct act) => [];
}
