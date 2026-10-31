using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

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
public class LinkedElement(string source, LayoutStyle palette, ContentEngine engine, string? language, ILayoutActions? actions)
    : ContentElement(source, palette, engine, language, actions)
{
    /// <inheritdoc/>
    protected override Cursor Pointing(LayoutPoint at)
    {
        if (Offered(at, LayoutGesture.Click) is not { } act) return base.Pointing(at);

        Tip(act.Intent.Tip ?? act.Intent.Target);
        return Cursors.Hand;
    }

    /// <summary>
    /// The mini ribbon a right-click opens where the pointer is: what the piece under it answers to, what the language drawn there
    /// offers and the clipboard's ways it allows, and what the host adds — or, where the press landed inside a selection, only what
    /// every chosen piece answers to, so a control appears when it means the same thing for all of them.
    /// </summary>
    public FrameworkElement? BuildRibbon(Point? at = null)
    {
        if (at is not { } pointer) return null;

        var where = Unscaled(pointer);
        var over = Selected();
        var under = Offered(where, LayoutGesture.ContextMenu) ?? Offered(where, LayoutGesture.Click);

        // A press outside what is picked out means the thing it landed on, which is what every editor does.
        if (under is { } one && !over.Any(piece => piece.At == one.Piece.At)) over = [one.Piece];

        // Over words that answer to nothing themselves, the ribbon is still about where the caret is: what the content
        // and the host offer there.
        if (over.Count == 0 && Laid.Root.PieceAt(where) is { Exists: true } there) over = [there];
        if (over.Count == 0) return null;

        var act = new LayoutAct(LayoutGesture.ContextMenu, under?.Intent ?? default, over[0],
                                over[0].Naming(), over[0].Naming().TreePart(), over, where);

        var controls = Shared(over).Select(intent => Answering(intent, over, where))
            .Concat(Asked(where))
            .Concat(Hosted(act).Select(intent => Answering(intent, over, where)))
            .DistinctBy(control => control.Id)
            .ToList();

        return controls.Count == 0 ? null : new DiagramRibbon(controls, made => Use(where, made));
    }

    /// <summary>What every one of <paramref name="over"/> offers, by verb, in the order the first of them says it.</summary>
    private static List<LayoutIntent> Shared(IReadOnlyList<LayoutPiece> over)
    {
        var first = Offers(over[0]);
        return [.. first.Where(offer => over.All(piece => Offers(piece).Any(other => other.Verb == offer.Verb)))];
    }

    /// <summary>Everything a piece says can be done to it: what a press on it means.</summary>
    private static IEnumerable<LayoutIntent> Offers(LayoutPiece piece) =>
        LayoutAnswers.Offer(piece.Acts?.Click) is { } press ? [press] : [];

    /// <summary>
    /// A button for what a piece answers to, or the host adds: pressed, it is told to each piece it was offered for, or — where no piece
    /// offered it, because the host did — once, for where the ribbon was opened. Answered by whoever hosts the content, so it writes
    /// nothing itself.
    /// </summary>
    private MiniRibbonButton Answering(LayoutIntent meant, IReadOnlyList<LayoutPiece> over, LayoutPoint where) =>
        new(meant.Verb, DiagramRibbon.Names(meant), _ =>
        {
            var told = false;

            foreach (var piece in over)
                if (Offers(piece).FirstOrDefault(offer => offer.Verb == meant.Verb) is { Verb.Length: > 0 } theirs)
                {
                    var part = piece.Naming();
                    Answered(new LayoutAct(LayoutGesture.ContextMenu, theirs, piece, part, part.TreePart(), over, where));
                    told = true;
                }

            if (!told && over.Count > 0)
            {
                var named = over[0].Naming();
                Answered(new LayoutAct(LayoutGesture.ContextMenu, meant, over[0], named, named.TreePart(), over, where));
            }

            return null;
        }) { Shape = DiagramRibbon.Marked(meant) };
}
