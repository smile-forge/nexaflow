using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// Content whose pieces answer to a gesture (<see cref="LayoutActions"/>), and which hands what they mean to whatever
/// the host put behind them. A press that is answered means that rather than a place to put the caret, and the pointer
/// is a hand over one saying what it leads to.
///
/// <para>
/// Separate from <see cref="ContentElement"/> because almost no content declares an action: what does pays for the
/// dispatch, and everything else is built and pressed exactly as it was.
/// </para>
/// </summary>
/// <param name="actions">What answers a gesture — null where nothing here answers one.</param>
public class LinkedElement(string source, MarkdownPalette palette, IContent content, ILayoutActions? actions)
    : ContentElement(source, palette, content), IEditableBlock
{
    /// <inheritdoc/>
    protected override Cursor Pointing(Point at)
    {
        if (Offered(at, LayoutGesture.Click) is not { } act) return base.Pointing(at);

        ToolTip = act.Intent.Tip ?? act.Intent.Target;
        return Cursors.Hand;
    }

    /// <inheritdoc/>
    /// <remarks>Only a plain press: Ctrl and Shift are adding to a selection, which is not what a press on a node means.</remarks>
    protected override bool Pressed(Point at, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && Answered(at, LayoutGesture.Click);

    /// <inheritdoc/>
    protected override bool Chosen(Point at) => Answered(at, LayoutGesture.DoubleClick);

    /// <inheritdoc/>
    protected override void Picked(Point at) => Answered(at, LayoutGesture.Select);

    /// <summary>
    /// What may be done where the pointer is: what the piece under it answers to, and what the host adds — or, where
    /// the press landed inside a selection, only what every chosen piece offers, so an item appears when it means the
    /// same thing for all of them.
    /// </summary>
    public FrameworkElement? BuildRibbon(Point? at = null)
    {
        if (actions is null || at is not { } pointer) return null;

        var where = Unscaled(pointer);
        var over = Selected();
        var under = Offered(where, LayoutGesture.ContextMenu) ?? Offered(where, LayoutGesture.Click);

        // A press outside what is picked out means the thing it landed on, which is what every editor does.
        if (under is { } one && !over.Any(piece => piece.At == one.Piece.At)) over = [one.Piece];

        if (over.Count == 0) return null;

        var act = new LayoutAct(LayoutGesture.ContextMenu, under?.Intent ?? default, over[0],
                                over[0].Naming(), over[0].Naming() as ContentPart, over, where);

        var offers = Shared(over).Concat(actions.Menu(act)).ToList();
        return offers.Count == 0 ? null : new DiagramRibbon(offers, meant => Invoke(meant, over, where));
    }

    /// <summary>What every one of <paramref name="over"/> offers, by verb, in the order the first of them says it.</summary>
    private static List<LayoutIntent> Shared(IReadOnlyList<Piece> over)
    {
        var first = Offers(over[0]);
        return [.. first.Where(offer => over.All(piece => Offers(piece).Any(other => other.Verb == offer.Verb)))];
    }

    /// <summary>Everything a piece says can be done to it: what a press means, what two mean, and what it lists besides.</summary>
    private static IEnumerable<LayoutIntent> Offers(Piece piece) =>
        piece.Acts is not { } acts
            ? []
            : new[] { acts.Click, acts.DoubleClick }.OfType<LayoutIntent>().Concat(acts.Menu)
                .GroupBy(offer => offer.Verb)
                .Select(one => one.First());

    /// <summary>Does what was chosen from the menu, to each piece it was offered for.</summary>
    private void Invoke(LayoutIntent meant, IReadOnlyList<Piece> over, Point where)
    {
        if (actions is null) return;

        foreach (var piece in over)
            if (Offers(piece).FirstOrDefault(offer => offer.Verb == meant.Verb) is { Verb.Length: > 0 } theirs)
            {
                var part = piece.Naming();
                actions.Invoke(new LayoutAct(LayoutGesture.ContextMenu, theirs, piece, part, part as ContentPart, over, where));
            }
    }

    /// <summary>Puts what the gesture meant to whatever answers it, and says whether that took it on.</summary>
    private bool Answered(Point at, LayoutGesture gesture) =>
        actions is not null && Offered(at, gesture) is { } act && actions.Invoke(act);

    /// <summary>
    /// What the piece under a point means by <paramref name="gesture"/>: the innermost one that means anything by it,
    /// or null where none does.
    /// </summary>
    private LayoutAct? Offered(Point at, LayoutGesture gesture)
    {
        var found = default(Piece);
        LayoutIntent? meant = null;

        foreach (var (piece, where) in Laid.Tree.Root.Placed())
            if (where.Contains(at) && piece.Acts?.For(gesture) is { } intent)
                (found, meant) = (piece, intent);

        if (meant is not { } intended) return null;

        var part = found.Naming();
        return new LayoutAct(gesture, intended, found, part, part as ContentPart, [found], at);
    }
}
