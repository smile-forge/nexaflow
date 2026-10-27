using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// A link as the content is laid out: where it goes, what it says to a reader, and where it is.
/// <para>
/// The engine's answer to "what can be followed here", for anything that has to name the links rather than press a
/// point — an automation peer reading them to a screen reader or a UI test. It belongs to the layout it was read from
/// (<see cref="ContentEngine.LaidLinks"/>); once the content is laid out again it is a description of nothing, and
/// <see cref="ContentEngine.Open"/> says so rather than following it.
/// </para>
/// </summary>
public sealed record LaidLink
{
    /// <summary>Where it goes, as written.</summary>
    public string Target => Intent.Target ?? string.Empty;

    /// <summary>What it says of itself when pointed at — its title, or what the host said links of its kind are.</summary>
    public string? Tip => Intent.Tip;

    /// <summary>The words a reader sees, without any mark a host set beside them; its tip or target where it has none.</summary>
    public string Text { get; internal init; } = string.Empty;

    /// <summary>Everything it covers, in the content's own units — a link that wraps covers both its lines.</summary>
    public LayoutRect Bounds { get; internal init; }

    /// <summary>A point on it, in the content's own units: the middle of where it starts.</summary>
    public LayoutPoint At { get; internal init; }

    internal LayoutIntent Intent { get; init; }

    /// <summary>The piece a press on <see cref="At"/> lands on.</summary>
    internal LayoutPiece Piece { get; init; }

    /// <summary>The layout it was read from.</summary>
    internal Laid? From { get; init; }
}

public sealed partial class ContentEngine
{
    /// <summary>
    /// Every link in the content as it is laid out, in reading order.
    /// <para>
    /// A link is what a reader meets as one: an unbroken stretch of words that answer the same press. The words of one
    /// are laid as several runs wherever its text changes face or wraps, each answering for itself, and other words
    /// between two of them make two links even where they go to the same place.
    /// </para>
    /// </summary>
    public IReadOnlyList<LaidLink> LaidLinks()
    {
        var links = new List<LaidLink>();
        var text = new StringBuilder();

        LayoutPiece first = default;
        LayoutRect start = LayoutRect.Empty, covers = LayoutRect.Empty;
        LayoutIntent intent = default;
        var open = false;

        foreach (var (piece, where) in _laid.Tree.Root.Placed())
        {
            if (LayoutAnswers.Offer(piece.Acts?.Click) is { Verb: LayoutVerbs.Navigate } press)
            {
                if (open && press == intent)
                {
                    covers = LayoutRect.Union(covers, where);
                    continue;
                }

                Close();
                (open, first, intent, start, covers) = (true, piece, press, where, where);
                continue;
            }

            if (piece.Words is not { } words) continue;

            if (piece.Ancestors().Any(up => up.Acts?.Click is { Verb: LayoutVerbs.Navigate }))
            {
                if (words.Maps) text.Append(words.Glyphs.Text);
            }
            else Close();
        }

        Close();
        return links;

        void Close()
        {
            if (!open) return;

            var said = text.ToString().Trim();
            links.Add(new LaidLink
            {
                Text   = said.Length > 0 ? said : intent.Tip ?? intent.Target ?? string.Empty,
                Bounds = covers,
                At     = new LayoutPoint(start.X + start.Width / 2, start.Y + start.Height / 2),
                Intent = intent,
                Piece  = first,
                From   = _laid,
            });

            text.Clear();
            open = false;
        }
    }

    /// <summary>Whether <paramref name="link"/> was read from the content as it is laid out now.</summary>
    public bool Holds(LaidLink link) => ReferenceEquals(link.From, _laid);

    /// <summary>
    /// Follows a link exactly as a press on it would — into the content when it names a heading here, to the host
    /// otherwise — and says whether anything took it. A link read from a layout since replaced is not followed: the
    /// content has moved under it, and the caller asks for the links again.
    /// </summary>
    public bool Open(LaidLink link)
    {
        if (!Holds(link)) return false;

        var part = link.Piece.Naming();
        return Meant(new LayoutAct(LayoutGesture.Click, link.Intent, link.Piece, part, part as ContentPart, [link.Piece], link.At));
    }
}
