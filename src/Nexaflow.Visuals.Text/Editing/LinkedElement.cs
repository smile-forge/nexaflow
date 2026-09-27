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
public class LinkedElement(string source, StyleFormat palette, ContentEngine engine, string? language, ILayoutActions? actions)
    : ContentElement(source, palette, engine, language, actions)
{
    /// <inheritdoc/>
    protected override Cursor Pointing(Point at)
    {
        if (Offered(at, LayoutGesture.Click) is not { } act) return base.Pointing(at);

        Tip(act.Intent.Tip ?? act.Intent.Target);
        return Cursors.Hand;
    }

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

        // Over words that answer to nothing themselves, the menu is still about where the caret is: what the content
        // and the host offer there.
        if (over.Count == 0 && Laid.Root.PieceAt(where) is { Exists: true } there) over = [there];
        if (over.Count == 0) return null;

        var act = new LayoutAct(LayoutGesture.ContextMenu, under?.Intent ?? default, over[0],
                                over[0].Naming(), over[0].Naming() as ContentPart, over, where);

        var offers = Shared(over).Concat(Offering(act)).Concat(actions.Menu(act)).DistinctBy(offer => offer.Verb).ToList();
        return offers.Count == 0 ? null : new DiagramRibbon(offers, meant => Invoke(meant, over, where));
    }

    /// <summary>
    /// What the content itself offers where the gesture landed, beside what its pieces answer to.
    ///
    /// <para>
    /// Nothing, unless what is being shown knows better. A piece says what a press on <em>it</em> means; this
    /// is for what may be done <em>there</em> — the things a reader can add, and the things they can do to
    /// what is already around them, which only whatever is being shown knows.
    /// </para>
    /// </summary>
    protected virtual IEnumerable<LayoutIntent> Offering(LayoutAct act) => [];

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

    /// <summary>
    /// Does what was chosen from the menu: to each piece it was offered for, or — where no piece offered it, because the
    /// content or the host did — once, for where the menu was opened.
    /// </summary>
    private void Invoke(LayoutIntent meant, IReadOnlyList<Piece> over, Point where)
    {
        if (actions is null) return;

        var asked = false;

        foreach (var piece in over)
            if (Offers(piece).FirstOrDefault(offer => offer.Verb == meant.Verb) is { Verb.Length: > 0 } theirs)
            {
                var part = piece.Naming();
                actions.Invoke(new LayoutAct(LayoutGesture.ContextMenu, theirs, piece, part, part as ContentPart, over, where));
                asked = true;
            }

        if (asked || over.Count == 0) return;

        var named = over[0].Naming();
        actions.Invoke(new LayoutAct(LayoutGesture.ContextMenu, meant, over[0], named, named as ContentPart, over, where));
    }
}
