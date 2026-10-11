using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
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
    private static readonly ContentPart NothingRead = ContentPart.Of(new BlockNode(MarkdownParser.Language, []));

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
        // Over the corner's own buttons, the block they belong to is still the one pointed at — but which of them the pointer
        // is on may have changed, and only that one is drawn as the one a press would answer.
        if (at is { } over && _corner is { } shown && shown.Root.Bounds.Contains(over))
        {
            var moved = ButtonUnder(shown, _pointer) != ButtonUnder(shown, over);
            _pointer = at;

            if (!moved || _over is not { } on) return;

            _corner = Cornered(on);
            Changed?.Invoke(this, false);

            return;
        }

        _pointer = at;

        var block = at is { } where ? Blocked(where) : null;
        if (ReferenceEquals(block, _over)) return;

        _over = block;
        _corner = block is null ? null : Cornered(block);

        Changed?.Invoke(this, false);
    }

    /// <summary>Where the button a point is on stands, or nothing where it is on none of them.</summary>
    private static Rect? ButtonUnder(Laid? corner, Point? at) =>
        corner is { } shown && at is { } point
            ? shown.Root.SelfAndDescendants()
                   .Where(piece => piece.Acts is not null && piece.Bounds.Contains(point))
                   .Select(piece => (Rect?)piece.Bounds)
                   .FirstOrDefault()
            : null;

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
    /// <strong>A block owns a band of the page.</strong> Markdown is one column, so the blocks partition the page from top to
    /// bottom and nothing is beside anything: a point is on the block whose band holds it, and how far across the page it is
    /// never comes into the question. A short line is as much a block as a wide one, and the empty room beside it is still
    /// that block's — which is what carries a reader from the words to the buttons standing away to the right of them.
    /// </para>
    /// <para>
    /// Found by the offset rather than by walking up from the piece. A piece knows the characters it was drawn from, but not
    /// always as a part of this content's tree: a code fence's runs carry plain spans, because the thing that drew them was
    /// reading code and not markdown. An offset is an offset whatever drew it, and every language is laid at the offset its
    /// body starts at — so this is the one question that works the same everywhere.
    /// </para>
    /// <para>
    /// The nearest piece answers first and usually answers rightly, but it belongs to whoever drew nearest — and in the empty
    /// parts of a diagram's card, the corners a pie's disc never reaches, that is the paragraph above or below it, because the
    /// diagram drew nothing there to be near. So where the nearest piece names a block whose band does not hold the point, the
    /// blocks as they were laid are asked instead, by the same scan <see cref="Where"/> makes. Without that, crossing the empty
    /// corner of a diagram on the way to its buttons answers with no block at all, and the buttons go while the pointer is
    /// still on the diagram.
    /// </para>
    /// </summary>
    internal ContentPart? Blocked(Point at)
    {
        bool Holds(Rect band) => !band.IsEmpty && at.Y >= band.Y && at.Y < band.Bottom;

        var piece = _laid.Root.PieceAt(at);

        if (piece.Exists && Blocked(piece.Sits().Start) is { } nearest && Holds(Where(nearest))) return nearest;

        // Content in one language is one block of it, and the whole of what was laid is that block.
        if (_named is not null) return Holds(new Rect(_laid.Size)) ? ReadRoot : null;

        foreach (var whole in _laid.Root.Children)
        {
            if (whole.Kind != MarkdownPieces.Whole || !Holds(whole.Bounds)) continue;

            if (whole.SelfAndDescendants().Select(inside => inside.Part).FirstOrDefault(part => part is { Length: > 0 }) is { } named)
                return Blocked(named.Start);
        }

        return null;
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
    /// The block after the one holding <paramref name="offset"/>, or null where none follows it. What a second
    /// showing of the document means when this one is partway through a block whose insides it cannot answer for.
    /// </summary>
    internal ContentPart? BlockedAfter(int offset)
    {
        if (_named is not null) return null;

        var passed = false;

        foreach (var block in ReadRoot.Children)
        {
            if (block.Derived || block.Role == Roles.Trivia) continue;
            if (passed) return block;

            passed = offset >= block.Start && offset < block.End;
        }

        return null;
    }

    /// <summary>
    /// Where a block came out: the whole of what it drew, from its top to its bottom and across as much of the page as it
    /// reaches — which is what a picture of the block is, and so is the block's own width and not the page's.
    ///
    /// <para>
    /// Every block of a document is a piece of its own, holding all it drew — a barcode's bars and a chart's wedges too, which
    /// stand for no characters and so lie in no stretch of them. So the block's own piece is what answers, found by the first
    /// thing in it that was written; the stretch of its characters answers only where there is no such piece.
    /// </para>
    /// <para>
    /// Reaching the buttons in its corner asks nothing of this. They stand in from the panel (<see cref="Cornered"/>), and a
    /// point is on a block by the band of the page it owns (<see cref="Blocked"/>) — so neither needs a block widened to the
    /// page, and widening one here would only make every picture of it as wide as the longest line in the document.
    /// </para>
    /// </summary>
    internal Rect Where(ContentPart block)
    {
        // Content in one language is one block, and all of what was laid is it.
        if (block.Parent is null) return new Rect(_laid.Size);

        foreach (var whole in _laid.Root.Children)
        {
            if (whole.Kind != MarkdownPieces.Whole) continue;
            if (whole.SelfAndDescendants().Select(piece => piece.Part).FirstOrDefault(part => part is { Length: > 0 }) is not { } named) continue;

            if (named.Start >= block.Start && named.Start < block.End) return whole.Bounds;
        }

        var rects = _laid.Root.RangeRects(block.Start, Math.Max(block.Length, 1));
        if (rects.Count == 0) return Rect.Empty;

        var box = rects[0];

        foreach (var rect in rects) box = Rect.Union(box, rect);

        return box;
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

        Meant(new LayoutAct(LayoutGesture.Click, offer, piece, block, block, [piece], at));
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

    /// <summary>
    /// Whether a picture of a block is worth keeping, as the language drawing it says — which is the same answer that decides
    /// whether its corner offers to save one. A diagram is; a code fence is not, a picture of code being a worse copy of it.
    /// </summary>
    internal bool KeepsAPicture(ContentPart block) => CornerOf(block).Saves;

    // ── The buttons ─────────────────────────────────────────────────────────

    /// <summary>
    /// The buttons <paramref name="block"/> offers, laid out in a row at its top right-hand corner — and kept on screen while
    /// the block runs off the top of it — or null where it offers none.
    ///
    /// <para>
    /// A corner is worked out of code the language wrote, worked out again every time the pointer moves, and worked out
    /// inside laying out but past the catch that covers it. So it is caught here rather than at each caller, and here is
    /// the one place in the pipeline where swallowing is right: there is nothing to show a reader about a button that
    /// failed to appear, and showing a whole document as written because the pointer moved over it would be worse than
    /// the fault it reported.
    /// </para>
    /// </summary>
    private Laid? Cornered(ContentPart block)
    {
        try
        {
            var offers = Offered(block).ToList();
            if (offers.Count == 0) return null;

            var box = Where(block);
            if (box.IsEmpty) return null;

            var faces = offers.Select(offer => (Offer: offer, Face: Face(offer))).ToList();
            var width = faces.Sum(face => Width(face.Face, face.Offer));

            var top = Math.Max(box.Y, _showing?.Y ?? box.Y) + CornerDrop;
            // In from the right-hand edge of the panel, which is where a reader looks — not in from the widest line, which is
            // wherever the longest paragraph happens to reach and moves when one is typed into. The panel is the room the content
            // was last laid for; a block owns a band of the page rather than a box (see Blocked), so buttons standing out past the
            // words are still on the block they belong to.
            var x = Math.Max(box.Right, Room) - CornerInset - width;

            var build = new LayoutBuilder();
            build.Open(CornerPieces.Corner, stops: Stops.None);

            foreach (var (offer, face) in faces)
            {
                var wide = Width(face, offer);
                var where = new Rect(x, top, wide, ButtonSize);

                var shape = new RectangleGeometry(where, ButtonRound, ButtonRound);
                shape.Freeze();

                // The one the pointer is on is outlined, so a reader pressing sees which of them would answer. Faintness is the
                // whole corner's and the element's to draw; which button is meant is this one's to say.
                var under = _pointer is { } point && where.Contains(point);

                build.Open(CornerPieces.Button, stops: Stops.None);
                build.Acts(new LayoutActions { Click = offer });
                build.Draw(new GeometryMark(shape, _style.QuoteBg, under ? _style.Accent : null, under ? 1 : 0));
                build.Occupies(shape);
                build.Draw(new TextMark(face, new Point(x + ((wide - face.Width) / 2), top + ((ButtonSize - face.Height) / 2)), _style.Text));
                build.Close();

                x += wide;
            }

            build.Close();

            var tree = build.Seal();
            return new Laid(tree, tree.Root.Bounds.Size, []);
        }
        catch
        {
            return null;
        }
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

    // ── Blocks put in on somebody's behalf ──────────────────────────────────

    /// <summary>
    /// Puts the caret in the first thing written, whatever language that is written in — for focus arriving without a press, or
    /// a host opening straight onto the content. False where nothing is written.
    /// </summary>
    internal bool TakeFirstBlock()
    {
        // Content written in one language is one block of it, so this is that; a document of blocks gives the first.
        if (Blocked(0) is not { } first) return false;

        TakeCaret(first.End);
        return true;
    }

    /// <summary>
    /// Writes what was dragged in from somewhere else where it was let go: the words to the language it landed in, which says
    /// what a drop on that piece comes to; failing that, the markdown it came as, written at the nearest place to it.
    /// </summary>
    /// <param name="at">Where it was let go, in the content's own units — which piece that is, is this to work out.</param>
    internal bool Brought(string words, string markdown, Point at)
    {
        if (_readOnly) return false;

        TakeCaret(_laid.Root.OffsetAt(at), _laid.StopNear(at));

        if (words.Length > 0 && Edited(EditKind.Dropping, words, Landing, _laid.Root.PieceAt(at)) is { } dropped)
        {
            Apply(dropped, notify: true);
            return true;
        }

        if (markdown.Length == 0) return false;

        Replace(_state.Insert(markdown.ReplaceLineEndings("\n")));
        return true;
    }

    /// <summary>
    /// What a press on something the content drew means where the content means it itself.
    ///
    /// <para>
    /// A corner's Copy is the very question Ctrl+C over that block asks, so the button and the key cannot drift apart. A corner's
    /// Save is a file, and where a file goes is a host's alone — so that one is asked rather than answered. False where a press
    /// means nothing here, which leaves it to whoever hosts the content.
    /// </para>
    /// </summary>
    private bool Acted(LayoutAct act) => act.Intent.Verb switch
    {
        LayoutVerbs.Navigate => Followed(act),

        LayoutVerbs.Copy when act.Intent.Target is { Length: > 0 } what =>
            this.Events?.OnCopy(MarkdownClipboard.Copied(what, null)) == true,

        LayoutVerbs.Copy when act.Gesture == LayoutGesture.Click && act.Node is { } block =>
            this.Events?.OnCopy(CopyOf(block)) == true,

        LayoutVerbs.Save when act.Node is { } block => this.Events?.OnBlockSave(block) == true,

        // A chip pressed on a diagram, or the node offering what is left of an over-wide set of its children.
        LayoutVerbs.Expand   => Folded(act, open: true),
        LayoutVerbs.Collapse => Folded(act, open: false),

        // Whatever the language offering it makes of it, which for a paste is to ask for the engine's own.
        LayoutVerbs.Paste => Choose(LayoutVerbs.Paste, act.At),

        _ => false,
    };

    /// <summary>
    /// Shows what is folded behind the node the press names, or folds it away again. True wherever the press landed on
    /// a block of content, so a chip is answered here and never goes further.
    ///
    /// <para>
    /// <strong>The opening is written down before anything is told of it.</strong> Whatever supplied the diagram
    /// answers by handing back a larger graph, which is read into the block again; an opening recorded after that would
    /// be recorded against a reading already thrown away, and the node would spring shut as it opened.
    /// </para>
    /// <para>
    /// Where nothing supplied it, the diagram opens the node out of its own source — which is what makes an ordinary
    /// flowchart with a <c>defaultExpansion</c> explorable with no host behind it at all. Only that block is read
    /// again; every other block on the page stands as it was laid.
    /// </para>
    /// </summary>
    private bool Folded(LayoutAct act, bool open)
    {
        if (act.Intent.Target is not { Length: > 0 } key) return false;
        if (Blocked(act.At) is not { } block || Opened(block) is not { } view) return false;

        view.Expansion[key] = open;

        // A diagram written in a fence is a tree of its own, and the block holding it holds only the characters it was
        // written as — so whatever supplied the diagram is reached from the piece pressed upwards, never from the block
        // the press landed in. A node the drawing invented stands for no part at all, and nothing supplied that either.
        var diagram = act.Part as ContentPart;
        while (diagram?.Parent is { } holder) diagram = holder;

        if (diagram is not null && Expand(diagram, key, open)) return true;

        _unchanged.Forget(held => ReferenceEquals(held, block.Node));
        Relay();

        return true;
    }

    /// <summary>
    /// A press on something that leads somewhere: the language it was written in has first say, and failing that whoever hosts the
    /// content, which is the only thing that can leave the content at all. A place within the same content never reaches here —
    /// <see cref="Anchored"/> scrolls to it before this is asked.
    ///
    /// <para>
    /// Where it leads is what was drawn for it, which is what whoever drew it read off the tree — a link's address in prose, a
    /// chart's own reading of where one of its nodes leads. One link inside another's words answers for itself, because the run
    /// drawn for it carries its own.
    /// </para>
    /// </summary>
    private bool Followed(LayoutAct act)
    {
        if (act.Intent.Target is not { Length: > 0 } where) return false;
        if (Choose(LayoutVerbs.Navigate, act.At)) return true;

        return Uri.TryCreate(where, UriKind.Absolute, out _) && this.Events?.OnNavigate(where) == true;
    }
}
