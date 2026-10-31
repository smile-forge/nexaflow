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
/// What a gesture on a piece means — what a builder said a press on it means (<see cref="LayoutActions"/>), a block's corner button,
/// or what a host answers — and what it would be done to. Answered by the engine or the host by its verb; never a control of the
/// mini ribbon, which is the language's own (<see cref="MiniRibbonControl"/>).
/// </summary>
/// <param name="Verb">Which of the things it is — <see cref="LayoutVerbs"/>, or a content's own.</param>
/// <param name="Target">What it would be done to, said the way the content that offered it says such things.</param>
/// <param name="Tip">What it is called where a reader has to read it.</param>
public readonly record struct LayoutIntent(string Verb, string? Target = null, string? Tip = null);

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
