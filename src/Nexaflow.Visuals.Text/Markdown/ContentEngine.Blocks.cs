using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The blocks content is made of — a paragraph, a table, a fence — where each came out on the page, and the buttons in the
/// corner of the one the pointer is over.
///
/// <para>
/// <strong>The corner is laid out here, beside the content rather than in it.</strong> Only the engine has both things the
/// buttons need: the layout, which says which block the pointer is over and where it came out, and the languages, which say
/// what that block offers — a picture of a diagram is worth keeping, and a picture of a code fence is a worse copy of the
/// code. So the buttons are a small layout of their own (<see cref="Corner"/>), painted over the content and pressed like any
/// piece that answers to a press: what a button means goes to the host, with the block it was pressed on.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>How far in from the page's right edge the corner stands, and how far down from the top of its block.</summary>
    private const double CornerInset = 12;
    private const double CornerDrop = 2;

    /// <summary>How big a button with a mark on it is, how much room a named one leaves round its name, and how round both are.</summary>
    private const double ButtonSize = 24;
    private const double ButtonPad = 6;
    private const double ButtonRound = 3;
    private const double MarkSize = 13;

    /// <summary>A document with nothing in it, for before anything has been laid.</summary>
    private static readonly ContentPart NothingRead = ContentPart.Of(ContentNode.Branch(MarkdownKinds.Document, []));

    private Point? _pointer;
    private ContentPart? _over;
    private Laid? _corner;
    private Rect? _showing;

    /// <summary>
    /// The content as it was read — the tree the laid layout was drawn from — so every question about what it is made of is
    /// asked of that one reading. A paragraph that needed no piece of its own is still a paragraph in it.
    /// </summary>
    internal ContentPart ReadRoot => Reading?.Root ?? NothingRead;

    /// <summary>The buttons in the corner of the block the pointer is over, laid out where they stand — or null where it is over none that offers any.</summary>
    internal Laid? Corner => _corner;

    /// <summary>The part of the content on screen, in its own units, which the corner of a block taller than that stays inside — or null where all of it is.</summary>
    internal Rect? OnScreen
    {
        get => _showing;
        set
        {
            if (_showing == value) return;

            _showing = value;
            Recornered();
        }
    }

    /// <summary>The pointer moved over the content, or left it.</summary>
    private void Hover(Point? at)
    {
        // Over the corner's own buttons, the block they belong to is still the one pointed at.
        if (at is { } over && _corner is { } shown && shown.Root.Bounds.Contains(over)) return;

        _pointer = at;

        var block = at is { } where ? Blocked(where) : null;
        if (ReferenceEquals(block, _over)) return;

        _over = block;
        _corner = block is null ? null : Cornered(block);

        Changed?.Invoke(this, false);
    }

    /// <summary>The corner laid again for where the pointer is, since the blocks, or the part of them on screen, have moved.</summary>
    private void Recornered()
    {
        if (_pointer is not { } at)
        {
            if (_corner is null) return;

            (_over, _corner) = (null, null);
            Changed?.Invoke(this, false);
            return;
        }

        _over = Blocked(at);
        _corner = _over is null ? null : Cornered(_over);

        Changed?.Invoke(this, false);
    }

    // ── Blocks ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The block a point is in: not the word or the run under it, but the thing the content is made of that holds them — a
    /// paragraph, a table, a fence — and nothing where the point is on none of them.
    ///
    /// <para>
    /// Found by the offset rather than by walking up from the piece. A piece knows the characters it was drawn from, but not
    /// always as a part of this content's tree: a code fence's runs carry plain spans, because the thing that drew them was
    /// reading code and not markdown. An offset is an offset whatever drew it, and every language is laid at the offset its
    /// body starts at — so this is the one question that works the same everywhere.
    /// </para>
    /// </summary>
    internal ContentPart? Blocked(Point at)
    {
        var piece = _laid.Root.PieceAt(at);
        if (!piece.Exists) return null;

        // The nearest piece answers a point that is on nothing, which is a question about what is near and not about what the
        // point is on.
        return Blocked(piece.Sits().Start) is { } block && Where(block) is { IsEmpty: false } box && box.Contains(at) ? block : null;
    }

    /// <summary>The block of the content an offset is in — the whole of it, where it is written in one language.</summary>
    internal ContentPart? Blocked(int offset)
    {
        if (_named is not null) return ReadRoot;

        foreach (var block in ReadRoot.Children)
            if (!block.Derived && block.Role != Roles.Trivia && offset >= block.Start && offset < block.End)
                return block;

        return null;
    }

    /// <summary>
    /// Where a block came out: the whole of what it drew, from its top to its bottom and across the page — a short line is still
    /// a block the width of the page, and its corner stands at the page's edge, so the way from the words to the corner never
    /// leaves the block.
    ///
    /// <para>
    /// Every block of a document is a piece of its own, holding all it drew — a barcode's bars and a chart's wedges too, which
    /// stand for no characters and so lie in no stretch of them. So the block's own piece is what answers, found by the first
    /// thing in it that was written; the stretch of its characters answers only where there is no such piece.
    /// </para>
    /// </summary>
    internal Rect Where(ContentPart block)
    {
        Rect Across(Rect box) => new(0, box.Y, Math.Max(box.Right, _laid.Size.Width), box.Height);

        // Content in one language is one block, and all of what was laid is it.
        if (block.Parent is null) return new Rect(_laid.Size);

        foreach (var whole in _laid.Root.Children)
        {
            if (whole.Kind != MarkdownPieces.Whole) continue;
            if (whole.SelfAndDescendants().Select(piece => piece.Part).FirstOrDefault(part => part is { Length: > 0 }) is not { } named) continue;

            if (named.Start >= block.Start && named.Start < block.End) return Across(whole.Bounds);
        }

        var rects = _laid.Root.RangeRects(block.Start, Math.Max(block.Length, 1));
        if (rects.Count == 0) return Rect.Empty;

        var box = rects[0];

        foreach (var rect in rects) box = Rect.Union(box, rect);

        return Across(box);
    }

    /// <summary>
    /// Shows the block at <paramref name="at"/> as it was written, with the caret where it was pressed — the whole block,
    /// whatever it holds. It is drawn again once the caret leaves it.
    /// </summary>
    /// <returns>
    /// Whether it did: not where nobody may write, and not in a block already shown as written, where two presses pick out a
    /// word as they do in any text.
    /// </returns>
    internal bool OpenAsWritten(Point at)
    {
        if (_readOnly || Blocked(at) is not { } block) return false;

        // As much of it as is drawn when it is shown: the line ending that closes it is not somewhere to write.
        var zone = new RawZone(block.Start, block.Start + block.Print().TrimEnd('\n', '\r').Length);

        if (zone.Length == 0 || (_state.Raw is { } shown && shown.Start < zone.End && zone.Start < shown.End)) return false;

        var caret = Math.Clamp(_laid.OffsetAt(at), zone.Start, zone.End);

        Apply(_state.MoveCaretTo(caret) with { Raw = zone }, notify: false);
        Refresh();
        TakeCaret(caret);

        return true;
    }

    // ── What a block offers ─────────────────────────────────────────────────

    /// <summary>What the block at a point offers in its corner — whichever of the usual buttons it allows, and whatever it adds of its own.</summary>
    internal IReadOnlyList<LayoutIntent> Offers(Point at) => Blocked(at) is { } block ? [.. Offered(block)] : [];

    /// <summary>
    /// What a corner button means for the block at <paramref name="at"/>, told to the host with the block it was pressed on —
    /// the same as a press on the button would.
    /// </summary>
    internal void Raise(LayoutIntent offer, Point at)
    {
        if (Blocked(at) is not { } block) return;

        var piece = _laid.Root.PieceAt(at);

        Actions?.Invoke(new LayoutAct(LayoutGesture.Click, offer, piece, block, block, [piece], at));
    }

    /// <summary>What a block's corner offers: whichever of the usual ones it allows, and whatever it adds.</summary>
    private IEnumerable<LayoutIntent> Offered(ContentPart block)
    {
        var corner = CornerOf(block);

        if (corner.Copies) yield return new LayoutIntent(LayoutVerbs.Copy, null, "Copy");
        if (corner.Saves) yield return new LayoutIntent(LayoutVerbs.Save, null, "Save as a picture");

        foreach (var added in corner.Adds) yield return added;
    }

    /// <summary>What the language drawing a block says about its corner, or nothing where no language is.</summary>
    private BlockCorner CornerOf(ContentPart block)
    {
        for (var at = block; at is not null; at = at.Parent)
        {
            if (ContentLanguages.Held(at) is not { } language) continue;

            var ask = new ContentAsk(ContentNested.Language(at)!, at.Part(Roles.Body)!.Text) { Part = block, IsReadOnly = _readOnly };
            return language.Editing.Corner(ask);
        }

        // Content in one language is one block of it.
        if (_named is { } named)
            return Language(named).Editing.Corner(new ContentAsk(named, _state.Source) { Part = block, IsReadOnly = _readOnly });

        // Prose is read rather than handled: it is no picture to keep, and copying it is what selecting it is for.
        return BlockCorner.None;
    }

    // ── The buttons ─────────────────────────────────────────────────────────

    /// <summary>
    /// The buttons <paramref name="block"/> offers, laid out in a row at its top right-hand corner — and kept on screen while
    /// the block runs off the top of it — or null where it offers none.
    /// </summary>
    private Laid? Cornered(ContentPart block)
    {
        var offers = Offered(block).ToList();
        if (offers.Count == 0) return null;

        var box = Where(block);
        if (box.IsEmpty) return null;

        var faces = offers.Select(offer => (Offer: offer, Face: Face(offer))).ToList();
        var width = faces.Sum(face => Width(face.Face, face.Offer));

        var top = Math.Max(box.Y, _showing?.Y ?? box.Y) + CornerDrop;
        var x = Math.Max(box.Right, _laid.Size.Width) - CornerInset - width;

        var build = new LayoutBuilder();
        build.Open(CornerPieces.Corner, stops: Stops.None);

        foreach (var (offer, face) in faces)
        {
            var wide = Width(face, offer);
            var where = new Rect(x, top, wide, ButtonSize);

            var shape = new RectangleGeometry(where, ButtonRound, ButtonRound);
            shape.Freeze();

            build.Open(CornerPieces.Button, stops: Stops.None);
            build.Acts(new LayoutActions { Click = offer });
            build.Draw(new GeometryMark(shape, _style.QuoteBg, null, 0));
            build.Occupies(shape);
            build.Draw(new TextMark(face, new Point(x + ((wide - face.Width) / 2), top + ((ButtonSize - face.Height) / 2)), _style.Text));
            build.Close();

            x += wide;
        }

        build.Close();

        var tree = build.Seal();
        return new Laid(tree, tree.Root.Bounds.Size, []);
    }

    /// <summary>What a button shows: the mark for what it does, where there is one, and its name where there is not.</summary>
    private FormattedText Face(LayoutIntent offer) =>
        DiagramRibbon.Icon(offer) is { } mark
            ? new FormattedText(mark, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(DiagramRibbon.IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                                MarkSize, _style.Text, LayoutText.Density)
            : new FormattedText(DiagramRibbon.Names(offer), CultureInfo.CurrentCulture, FlowDirection.LeftToRight, _style.Face(_style.TextFont),
                                MarkSize, _style.Text, LayoutText.Density);

    /// <summary>How wide a button is: square where it shows a mark, and its name's width with room round it where it does not.</summary>
    private static double Width(FormattedText face, LayoutIntent offer) =>
        DiagramRibbon.Icon(offer) is not null ? ButtonSize : face.Width + (ButtonPad * 2);

    /// <summary>What the corner's pieces are.</summary>
    private static class CornerPieces
    {
        public const string Corner = "corner";
        public const string Button = "corner-button";
    }
}
