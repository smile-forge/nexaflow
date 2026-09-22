using System.Collections.Generic;
using System.Windows;
using Nexaflow.Markdown.Ast;

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

/// <summary>The verbs the renderer resolves itself, where the host does not take them on first.</summary>
public static class LayoutVerbs
{
    /// <summary>Go where <see cref="LayoutIntent.Target"/> says.</summary>
    public const string Navigate = "navigate";

    /// <summary>Show what is folded behind the node <see cref="LayoutIntent.Target"/> names.</summary>
    public const string Expand = "expand";

    /// <summary>Fold away what is behind the node <see cref="LayoutIntent.Target"/> names.</summary>
    public const string Collapse = "collapse";

    /// <summary>The node <see cref="LayoutIntent.Target"/> names was chosen.</summary>
    public const string Select = "select";

    /// <summary>
    /// Put what is chosen where things are copied to.
    ///
    /// <para>
    /// <strong>The renderer never touches a clipboard.</strong> It knows what was picked out and how to say it
    /// as markdown, as plain words and as marked-up text; a clipboard is the application's, shared with every
    /// other thing in the window, and a control that reached for it would be reaching past its host. So this
    /// is raised and the host does it.
    /// </para>
    /// </summary>
    public const string Copy = "copy";

    /// <summary>Keep a picture of what is under the pointer. The host decides where, and whether at all.</summary>
    public const string Save = "save";
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
/// What a piece answers to. Held in a table beside the pieces rather than a slot on each, as a run of words is
/// (<see cref="LayoutWords"/>): almost nothing drawn answers to a press, and a piece that does not costs a null.
/// </summary>
public sealed record LayoutActions
{
    /// <summary>What one press on it means.</summary>
    public LayoutIntent? Click { get; init; }

    /// <summary>What two mean, where that is something else.</summary>
    public LayoutIntent? DoubleClick { get; init; }

    /// <summary>What choosing it means, for a host following the selection.</summary>
    public LayoutIntent? Select { get; init; }

    /// <summary>What can be done to it, offered when it is asked for.</summary>
    public IReadOnlyList<LayoutIntent> Menu { get; init; } = [];

    /// <summary>What <paramref name="gesture"/> means here, or null where it means nothing.</summary>
    public LayoutIntent? For(LayoutGesture gesture) => gesture switch
    {
        LayoutGesture.Click => Click,
        LayoutGesture.DoubleClick => DoubleClick,
        LayoutGesture.Select => Select,
        _ => null,
    };
}

/// <summary>
/// A gesture, and everything answering it needs: what was meant, the piece it was meant on, the part of the source
/// that piece stands for, and — where an AST is behind the source — that part as the tree knows it.
/// </summary>
/// <param name="Part">
/// The piece's own part, else the nearest ancestor's: the walk up the tree from what was pressed to the thing it
/// belongs to. See <see cref="Piece.Naming"/>.
/// </param>
/// <param name="Node">The same, as the AST knows it, where it is a node of one — a piece may name a span with no tree behind it.</param>
/// <param name="Over">What the gesture is about: the piece alone, or the whole selection it was made inside.</param>
/// <param name="At">Where the pointer was, in the content's own coordinates.</param>
public readonly record struct LayoutAct(
    LayoutGesture Gesture,
    LayoutIntent Intent,
    Piece Piece,
    ISourcePart? Part,
    ContentPart? Node,
    IReadOnlyList<Piece> Over,
    Point At);

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
