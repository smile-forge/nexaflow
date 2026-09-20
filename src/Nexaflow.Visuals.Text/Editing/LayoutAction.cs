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
}

/// <summary>
/// What a gesture on a piece means: a verb and its argument, never a delegate.
///
/// <para>
/// A builder is a static function handed a source and a palette — it cannot close over a host's state, and nothing it
/// draws should have to know what a host will do about it. So it says what a press <em>means</em>, and the host decides.
/// That is also what lets one diagram be drawn in a viewer, in an editor and in a test with no wiring of its own.
/// </para>
/// </summary>
/// <param name="Verb">What is meant — one of <see cref="LayoutVerbs"/>, or a host's own.</param>
/// <param name="Target">What it is meant about: a url, a node's id, whatever the verb takes.</param>
/// <param name="Tip">What to say about it while it is pointed at, or in a menu — null to say the target itself.</param>
public readonly record struct LayoutIntent(string Verb, string? Target = null, string? Tip = null);

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
