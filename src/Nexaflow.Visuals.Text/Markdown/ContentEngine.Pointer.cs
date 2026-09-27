using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Presses: where one lands, what it means there, and what a drag after it chooses or carries.
///
/// <para>
/// <strong>A press means what the piece it lands on says it means, before it means a place.</strong> A piece that answers to
/// a gesture (<see cref="LayoutActions"/>) is asked first: a box ticked is written, a link into the content is followed here,
/// and anything else is the host's (<see cref="Actions"/>). Only a press nothing answers puts the caret down or picks
/// something out — and which of those is decided by whether it landed squarely on a thing or at a stop beside it.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    private Piece _anchorNode;
    private Point _pressedAt;
    private bool _dragging;

    private bool _moving;
    private int _dropAt;

    /// <summary>The piece what is carried is over, which the language it is written in is told where it is let go.</summary>
    private Piece _dropOver;
    private Laid? _preview;
    private Moved? _previewOf;
    private (int Start, int End) _previewMoved;

    /// <summary>How near a stop a press has to be to mean the stop rather than the thing, in layout pixels.</summary>
    private const double CaretReach = 3.0;

    /// <summary>What answers what the pieces of the content mean by a gesture — the host — or null where nothing does.</summary>
    internal ILayoutActions? Actions { get; set; }

    /// <summary>Raised to bring a stretch of the laid content into view — the heading a link into the content goes to.</summary>
    internal event EventHandler<Rect>? Revealing;

    /// <summary>The content as it would read if what is being carried were let go where it is now, with what is carried, or null.</summary>
    internal (Laid Laid, int Start, int End)? Preview => _preview is { } preview ? (preview, _previewMoved.Start, _previewMoved.End) : null;

    /// <summary>Where what is being carried would land, or null where nothing is.</summary>
    internal int? Dropping => _moving ? _dropAt : null;

    /// <summary>What a press, a drag or a release comes to.</summary>
    private bool Pointed(ContentInput input)
    {
        switch (input)
        {
            case ContentPress { Clicks: >= 2 } twice:
                Twice(twice.At);
                return true;

            case ContentPress press:
                Press(press.At, press.Modifiers);
                return true;

            case ContentDrag drag:
                Drag(drag.At);
                return true;

            case ContentRelease:
            Release();
            return true;

            case ContentHover hover:
            Hover(hover.At);
            return true;

            default:
                return false;
        }
    }

    // ── A press ─────────────────────────────────────────────────────────────

    private void Press(Point at, ModifierKeys modifiers)
    {
        _pressedAt = at;
        _moving = false;

        // A piece that answers to a press means what it answers with, and not a place to put the caret. Only a plain press:
        // Ctrl and Shift are adding to a selection, which is not what a press on a node means.
        if (modifiers == ModifierKeys.None && Offered(at, LayoutGesture.Click) is { } act && Meant(act)) return;

        // Several things chosen at once: what Ctrl presses is added to what is chosen, or taken back out of it.
        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            _dragging = false;
            Toggle(at);
            return;
        }

        // From where the choosing started to the press, as a drag from there would choose — from the caret, where nothing is
        // chosen yet — and a drag after it goes on choosing from the same place.
        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (!_state.HasSelection)
            {
                Anchor = _state.Caret;
                _anchorNode = _laid.Root.WordsAt(Anchor);
            }

            _dragging = true;
            ChooseTo(at);
            return;
        }

        Anchor = _laid.OffsetAt(at);
        _anchorNode = _laid.PieceAt(at);

        // Content a binding supplied has nowhere to put the caret: a press on it picks out what was pressed, whole.
        if (Supplied(_anchorNode)) { PickPressed(_anchorNode); Picked(at); return; }

        _dragging = true;

        // Pressing on what is already selected is how a move begins — the reader is picking the term up, not starting a new
        // selection over it. The selection is kept until the button comes back up, so a press that turns out to be an
        // ordinary click can still fall through to placing the caret.
        if (Covers(Anchor)) { _moving = true; _dropAt = Anchor; return; }

        ClearSelection();

        // A press squarely on something means that thing; a press at a stop — between two things, or at the edge of one —
        // means the place. One rule for every kind of content: a note pressed is a note picked, and a letter pressed at its
        // edge is a caret put down beside it. A run of text is written in rather than picked up, so a press inside one is a
        // caret between two of its letters — including the press that has to show it as written before there is anywhere to
        // put one.
        if (Writing(_anchorNode, at)) return;

        if (On(_anchorNode, at)) { SelectNodes(ContentSelection.Of(_anchorNode)); Picked(at); return; }

        TakeCaret(_laid.Root.OffsetAt(at), _laid.StopNear(at));
    }

    /// <summary>Adds what a press lands on to what is chosen — or, where all of it is chosen already, takes it back out.</summary>
    private void Toggle(Point at)
    {
        if (Supplied(_laid.PieceAt(at))) { TogglePicked(_laid.PieceAt(at)); return; }

        var piece = _laid.PieceAt(at).Selectable();
        if (!piece.Exists || piece.Sits() is not { Length: > 0 } sits) return;

        Anchor = sits.Start;
        _anchorNode = piece;

        var pressed = new EditRange(sits.Start, sits.Length);
        var chosen = _state.Selection;

        IReadOnlyList<EditRange> next = chosen.Any(range => range.Start <= pressed.Start && range.End >= pressed.End)
            ? [.. chosen.SelectMany(range => Outside(range, pressed))]
            : [.. chosen, pressed];

        if (next.Count == 0) { ClearSelection(); return; }

        if (!SelectRanges(next)) return;

        _chosenWhole = _state.Selection;
        Picked(at);
    }

    /// <summary>What of <paramref name="range"/> lies outside <paramref name="taken"/>.</summary>
    private static IEnumerable<EditRange> Outside(EditRange range, EditRange taken)
    {
        if (taken.End <= range.Start || taken.Start >= range.End)
        {
            yield return range;
            yield break;
        }

        if (taken.Start > range.Start) yield return new EditRange(range.Start, taken.Start - range.Start);
        if (taken.End < range.End) yield return new EditRange(taken.End, range.End - taken.End);
    }

    /// <summary>
    /// Puts the caret inside a run of text that was pressed, and says whether it did. A run showing something worked out (a
    /// rounded value, a percentage) has nowhere to put a caret since what is drawn isn't what was written — pressing it reveals
    /// the source first, then re-answers the press against that.
    /// </summary>
    private bool Writing(Piece piece, Point at)
    {
        if (piece.Words is not { } words || piece.Part is not { } part) return false;

        if (!words.Maps)
        {
            // A run that only says something about its part — the share of a pie a slice takes — is not written in: the press
            // means the slice, which is what the ordinary rules already do with it.
            if (_readOnly || !words.Writes) return false;

            Apply(_state.MoveCaretTo(part.Start) with { Raw = new RawZone(part.Start, part.End()) }, notify: false);
        }

        TakeCaret(_laid.Root.OffsetAt(_pressedAt), -1);
        return true;
    }

    /// <summary>
    /// Whether a press lands on <paramref name="piece"/> itself rather than at one of its stops. The reach is capped at a
    /// quarter of the piece's width so a letter as narrow as an "i" still has a place at each side.
    /// </summary>
    private static bool On(Piece piece, Point at)
    {
        if (!piece.Exists) return false;

        var box = piece.Ink();
        if (box.IsEmpty || !box.Contains(at)) return false;

        var reach = Math.Min(CaretReach, box.Width / 4);
        return at.X - box.Left > reach && box.Right - at.X > reach;
    }

    // ── A drag, and letting go ──────────────────────────────────────────────

    /// <summary>
    /// The pointer moved while pressed, far enough to be a drag — how far that is being the pointer's to say, since it is
    /// counted in the pixels of the screen.
    /// </summary>
    private void Drag(Point at)
    {
        if (!_dragging) return;

        // Carrying something: the content is shown as it would read if it were let go here, with the carried part marked out,
        // so the reader is choosing between finished results.
        if (_moving)
        {
            var drop = _laid.OffsetAt(at);
            if (drop == _dropAt) return;

            _dropAt = drop;
            _dropOver = _laid.PieceAt(at);
            BuildPreview();

            PreRender?.Invoke(this, EventArgs.Empty);
            Changed?.Invoke(this, true);
            return;
        }

        ChooseTo(at);
    }

    /// <summary>Chooses from the anchor to <paramref name="at"/>: what a drag there chooses, and what Shift and a press there choose.</summary>
    private void ChooseTo(Point at)
    {
        // From content a binding supplied, there are no characters to choose between: it picks out every piece of it spanned.
        if (Supplied(_anchorNode)) { PickSpanned(at); return; }

        // Inside one run of text it picks out characters, because that is what dragging through text means. Everywhere else it
        // is whole pieces — see below.
        if (_anchorNode.Words is { Maps: true } && _laid.PieceAt(at) == _anchorNode)
        {
            ExtendSelectionTo(_laid.OffsetAt(at));
            return;
        }

        // What was dragged over is a set of pieces, not a stretch of text. Inside a matrix that is what makes a drag down a
        // column select the column rather than everything written between its top cell and its bottom one.
        if (_anchorNode.Selectable() is { Exists: true } from && _laid.PieceAt(at).Selectable() is { Exists: true } focus)
        {
            // Through whatever owns each end. Landing on a bracket means the group it opens or closes: half a pair is not a
            // smaller selection, it is one that cannot be read.
            SelectNodes(ContentSelection.Between(_laid.Root, from, focus));
            return;
        }

        ExtendSelectionTo(_laid.OffsetAt(at));
    }

    private void Release()
    {
        _dragging = false;
        if (!_moving) return;

        _moving = false;
        var settled = _previewOf;
        ClearPreview();

        if (_readOnly) return;

        // The press never became a drag: an ordinary click on the selection, which places the caret there and drops the
        // selection, as clicking a selection does everywhere.
        if (settled is not { } moved) { ClearSelection(); TakeCaret(Anchor); return; }

        // Exactly what was on screen a moment ago — settling is letting go of it, not recomputing something the reader has to
        // check.
        Apply(new EditState(moved.Source, moved.Caret), notify: true);
    }

    /// <summary>Lays the content out as it would read if what is carried were dropped where it is now.</summary>
    private void BuildPreview()
    {
        _preview = null;
        _previewOf = null;
        _previewMoved = default;

        // What the language it is let go in says a drop there is, where it says; otherwise the characters carried, cut and put
        // back at the offset they are over.
        var moved = Edited(EditKind.Dropping, string.Empty, Landing, _dropOver) is { } dropped
            ? new Moved(dropped.Source, dropped.Caret, new EditRange(dropped.Caret, 0))
            : Moving(_dropAt);

        if (moved is not { } carried) return;

        _previewOf = carried;
        _previewMoved = (carried.Wrote.Start, carried.Wrote.End);
        _preview = LaidOut(new EditState(carried.Source, carried.Caret));
    }

    private void ClearPreview()
    {
        var had = _preview is not null;

        _preview = null;
        _previewOf = null;
        _previewMoved = default;

        if (had) PreRender?.Invoke(this, EventArgs.Empty);
    }

    // ── Two presses ─────────────────────────────────────────────────────────

    /// <summary>
    /// Two presses: what the piece under them means by it, and otherwise the thing pressed chosen rather than the whole of what
    /// holds it — inside content, "the word you clicked" is the symbol you clicked.
    /// </summary>
    private void Twice(Point at)
    {
        // Two presses on a block show it as it was written, where it can be written in and is not already.
        if (OpenAsWritten(at)) return;

        // Content a binding supplied is picked out whole however often it is pressed.
        if (Supplied(_laid.PieceAt(at))) { PickPressed(_laid.PieceAt(at)); return; }

        if (Offered(at, LayoutGesture.DoubleClick) is { } act && Actions?.Invoke(act) == true) return;

        var here = _laid.OffsetAt(at);

        var under = _laid.PieceAt(at).Selectable();

        // In a run of text, the word you pressed is a word of it rather than the whole run.
        if (under is { Words: { Maps: true } words, Part: { } part })
        {
            var (from, to) = words.WordAt(here - part.Start);
            Select(part.Start + from, to - from);
        }
        else if (under.Exists && under.Sits() is { Length: > 0 } sits) Select(sits.Start, sits.Length);
        else Select(Math.Max(0, here - 1), 1);
    }

    // ── What a press means ──────────────────────────────────────────────────

    /// <summary>
    /// What the piece under a point means by <paramref name="gesture"/>: the innermost one that means anything by it, or null
    /// where none does. The corner's buttons stand over the content, so they answer first — each for the block it is the corner of.
    /// </summary>
    internal LayoutAct? Offered(Point at, LayoutGesture gesture)
    {
        if (_corner is { } corner && _over is { } block && Answering(corner, at, gesture) is var (button, offer))
            return new LayoutAct(gesture, offer, button, block, block, [button], at);

        if (Answering(_laid, at, gesture) is not var (found, meant)) return null;

        var part = found.Naming();
        return new LayoutAct(gesture, meant, found, part, part as ContentPart, [found], at);
    }

    /// <summary>The innermost piece of <paramref name="laid"/> under a point that means something by <paramref name="gesture"/>, and what.</summary>
    private static (Piece Piece, LayoutIntent Intent)? Answering(Laid laid, Point at, LayoutGesture gesture)
    {
        (Piece, LayoutIntent)? found = null;

        foreach (var (piece, where) in laid.Tree.Root.Placed())
            if (where.Contains(at) && piece.Acts?.For(gesture) is { } intent)
                found = (piece, intent);

        return found;
    }

    /// <summary>
    /// What a press on a piece answering to it comes to, and whether it was taken on: what the content answers itself — a box
    /// ticked, a link into it followed — and then whatever the host says.
    /// </summary>
    private bool Meant(LayoutAct act) => Ticked(act) || Anchored(act) || Actions?.Invoke(act) == true;

    /// <summary>
    /// A press on a task's box: the mark between its brackets written over as the press means — an edit like any other,
    /// through the content's own edit handling, told to whoever follows it.
    /// </summary>
    private bool Ticked(LayoutAct act)
    {
        if (_readOnly || act.Intent.Verb != MarkdownVerbs.Tick) return false;
        if (act.Part is not ContentPart box
            || (box.Part(MarkdownRoles.Done) ?? box.Part(MarkdownRoles.Todo)) is not { Length: 1 } mark) return false;

        WriteOver(mark.Start, mark.Length, act.Intent.Target == "on" ? "x" : " ");

        return true;
    }

    /// <summary>
    /// Goes to the heading a link into the content names.
    ///
    /// <para>
    /// <strong>A link into the content is never the host's.</strong> Nobody else can answer it: the heading is in this content,
    /// laid out here, and a host handed <c>#getting-started</c> has no way to know what that means or where it went. So it is
    /// answered here and not offered onwards — and a name the content has no heading for is still not the host's, because it
    /// is still a link into this content. It does nothing, which is what a reader sees when they follow a link to a section
    /// somebody deleted.
    /// </para>
    /// </summary>
    private bool Anchored(LayoutAct act)
    {
        if (act.Intent is not { Verb: LayoutVerbs.Navigate, Target: { } where }) return false;
        if (!MarkdownAnchors.IsInPage(where, out var anchor)) return false;

        if (MarkdownAnchors.Sought(_laid, anchor) is { Exists: true } heading) Revealing?.Invoke(this, heading.Bounds);

        return true;
    }

    /// <summary>A piece was picked by a press at <paramref name="at"/>. It is chosen either way; this is only the telling.</summary>
    private void Picked(Point at)
    {
        if (Offered(at, LayoutGesture.Select) is { } act) Actions?.Invoke(act);
    }
}
