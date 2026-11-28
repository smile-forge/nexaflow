using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Finding a place in the content and going to it: a search, a line, a heading, a reference somebody saved — and where the page stands, as a
/// place in the source both showings of one document can say.
///
/// <para>
/// <strong>Looking is done in the source and never in the drawing.</strong> The parser only ever copies, so the source is the one place the
/// words are whole; the offsets come back out of it and the laid tree turns them into places on the page (<see cref="MarkdownFind"/>).
/// Nothing in the tree is changed to mark what was found — the view's marks wash it at paint time — so stopping a search is a repaint; and
/// what was looked for is asked again of the document each time it is laid again, so a search survives writing too (<see cref="Refound"/>).
/// </para>
/// <para>
/// <strong>Going somewhere is the engine's; bringing it onto a screen is whatever holds the content's.</strong> Content that scrolls itself
/// is moved here; content shown whole says which stretch its holder must bring onto its screen (<see cref="Bringing"/>), and these hand
/// back the stretch where the holder brings it as it is.
/// </para>
/// </summary>
public sealed partial class ContentEngine
{
    /// <summary>Everywhere the document says what was looked for, in the order a reader comes to them.</summary>
    internal IReadOnlyList<(int Start, int Length)> Found => _finding?.Places ?? [];

    /// <summary>Which of them is being looked at, or -1 for none.</summary>
    internal int FoundAt => _finding?.At ?? -1;

    /// <summary>
    /// What was looked for and found, a place at a time — the shared search over a document held whole, washed over the view by its own
    /// marks and gone to by bringing each place onto the page. Made the first time anything is looked for.
    /// </summary>
    private PlaceFind Finding => _finding ??= new PlaceFind(() => this.State.Document, () => this.Editor.Found, place => ShowPlace(place));

    private PlaceFind? _finding;

    /// <summary>
    /// Looks for <paramref name="term"/> and goes to the first place it is — both where it is written and where it is only drawn, which is
    /// what a reader means by what the page says.
    /// </summary>
    internal int FindWord(string? term) => Finding.Find(() => ReadsAsDrawn
        ? MarkdownFind.Drawn(DrawnWords, term)
        : MarkdownFind.In(_laid, this.State.Document.ToString(), term));

    /// <summary>
    /// The same, for a question more complicated than a word — what a search box worked out — going to the first place at or after
    /// <paramref name="from"/> in the source, and round to the first where none is. Content laid line by line — code, text — is searched a
    /// line at a time, and after an edit only on the lines it touched (<see cref="ContentSearch"/>).
    /// </summary>
    internal int FindIn(Func<string, IReadOnlyList<(int Index, int Length)>>? occurrences, int from = 0) =>
        Lined && occurrences is { } inLines
            ? Finding.Find(new LineQuery(inLines), from)
            : Finding.Find(() => ReadsAsDrawn
                ? DrawnWords.Every(occurrences)
                : MarkdownFind.In(_laid, this.State.Document.ToString(), occurrences), from);

    /// <summary>
    /// What was found, found again in the document as it now reads — written in here, written in somewhere else, taken back — with the one
    /// being looked at kept to where its place went. Nothing is scrolled to or picked out: the reader is writing, not looking. Done before
    /// anything is told the content was laid (<see cref="PreRender"/>), so whoever reads what was found then reads it as it is; and only where
    /// the document is another version than the one searched, so laying out again to scroll costs nothing.
    /// </summary>
    private void Refound() => _finding?.Refind();

    /// <summary>Narrows what is shown to the places named by their order in <see cref="Found"/>, and says how many are left.</summary>
    internal int RestrictFound(IReadOnlySet<int> keep) => Finding.Restrict(keep);

    /// <summary>
    /// Shows <paramref name="places"/>, which a host found itself, with the one at <paramref name="at"/> the one being looked at — moving
    /// nothing, since the host says when to go to one. They are carried through each edit, not found again: the host found them.
    /// </summary>
    internal void ShowFound(IReadOnlyList<(int Start, int Length)> places, int at) => Finding.Show(places, at);

    /// <summary>The next place, coming back round to the first.</summary>
    internal bool NextFound() => Finding.Next();

    /// <summary>The one before, coming back round to the last.</summary>
    internal bool PreviousFound() => Finding.Previous();

    /// <summary>Steps to the next place (<paramref name="delta"/> of one or more) or the one before.</summary>
    internal void StepFound(int delta) => Finding.Step(delta >= 0 ? +1 : -1);

    /// <summary>Stops looking, which is a repaint and nothing else.</summary>
    internal void StopFinding() => Finding.Clear();

    /// <summary>
    /// What a place found reads as, for a host listing them — the line it is on, tidied; and, for a page read as what it draws, what it
    /// draws there, since its source is formatting nobody would recognise.
    /// </summary>
    internal string FoundPreview(int at)
    {
        if (at < 0 || at >= Found.Count) return string.Empty;

        var (start, _) = Found[at];

        if (ReadsAsDrawn) return DrawnWords.LineAt(start);
        var document = this.State.Document;
        var (from, length) = MarkdownFind.Line(document, MarkdownFind.LineAt(document, start));

        return document.Slice(from, length).Trim();
    }

    /// <summary>
    /// How far down the document each place found is, from nought at the top to one at the bottom — for a strip beside the scrollbar
    /// marking where they are. Every place has its mark, since the reader is told how many were found and each of them is somewhere
    /// (<see cref="Standing"/>).
    /// </summary>
    internal IReadOnlyList<double> FoundMarkPositions()
    {
        var height = _laid.Size.Height;
        var found = Found;
        if (height <= 0 || found.Count == 0) return [];

        var positions = new double[found.Count];
        for (var at = 0; at < found.Count; at++) positions[at] = Math.Clamp(Standing(found[at]) / height, 0, 1);

        return positions;
    }

    /// <summary>
    /// How far down the page <paramref name="place"/> stands: where it is drawn; where it would come, in a long document not laid that far
    /// yet, going by what is laid; and where nothing on the page draws it — inside a section the reader folded — where what holds it is
    /// drawn, at the last place a caret can stand before it.
    /// </summary>
    private double Standing((int Start, int Length) place)
    {
        if (_laid.Root.RangeRects(place.Start, place.Length) is { Count: > 0 } rects) return rects[0].Y;
        if (UnlaidTop(place.Start) is { } unlaid) return unlaid;

        var stops = _laid.Stops;
        var (low, high, held) = (0, stops.Count - 1, -1);

        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (stops[middle] <= place.Start) (held, low) = (stops[middle], middle + 1);
            else high = middle - 1;
        }

        return held >= 0 && _laid.Root.CaretRect(held) is { IsEmpty: false } at ? at.Y : 0;
    }

    // ── Going to a place ────────────────────────────────────────────────────

    /// <summary>
    /// Raised for content shown whole, which has no view of its own: the stretch, in the content's own units, that whatever holds it is to
    /// bring onto its screen as it is.
    /// </summary>
    internal event EventHandler<LayoutRect>? Bringing;

    /// <summary>
    /// Brings <paramref name="shown"/> onto the page as a place gone to is brought: left where it stands where it is on the screen already,
    /// and otherwise stood half way down it, so what comes before and after it is there to read it by — or, taller than the screen, its top
    /// at the top. Content that scrolls itself has its view moved here; content shown whole asks whatever holds it (<see cref="Bringing"/>).
    /// </summary>
    internal void BringOnto(LayoutRect shown)
    {
        if (Scrolls)
        {
            BringToView(shown);
            return;
        }

        var box = shown;

        if (OnScreen is { } screen)
        {
            if (screen.Contains(box)) return;
            if (box.Height < screen.Height) box = new LayoutRect(box.X, box.Y + (box.Height / 2) - (screen.Height / 2), box.Width, screen.Height);
        }

        Bringing?.Invoke(this, box);
    }

    /// <summary>
    /// Brings a stretch of the source onto the page and picks it out, and says whether the content had it to show.
    ///
    /// <para>
    /// Where it lands is worked out from the offsets, which every piece carries whatever language drew it — so a hit inside a diagram is
    /// found the same way a hit in a paragraph is. A place inside a section the reader folded away is opened first; one a long document has
    /// not laid yet is laid to, and brought onto the page once it is, picked out now so the caret is there when it arrives.
    /// </para>
    /// <para>
    /// <strong>What is brought onto the page is not always what was found.</strong> A word inside a diagram is a few pixels of a picture,
    /// and bringing only those shows a reader the middle of a chart with no idea what they are looking at — so where the hit sits inside
    /// content another language laid out, the whole of that is brought instead, with the hit still picked out inside it
    /// (<see cref="ShownWhole"/>).
    /// </para>
    /// </summary>
    internal bool ShowPlace((int Start, int Length) what, bool choose = true)
    {
        var found = _laid.Root.RangeRects(what.Start, what.Length);

        if (found.Count == 0 && Unfolded(what.Start)) found = _laid.Root.RangeRects(what.Start, what.Length);

        if (found.Count == 0)
        {
            if (!Uncover(what.Start)) return false;

            if (choose && what.Length > 0) Apply(this.State.Select(what.Start, what.Length), notify: false);

            return true;
        }

        // A box slid away from it slides to it: its end, and then its start, which is what shows where it cannot all show at once.
        SlideOnto(what.Start + what.Length);
        SlideOnto(what.Start);

        BringOnto(ShownWhole(what));

        if (choose && what.Length > 0) Apply(this.State.Select(what.Start, what.Length), notify: false);

        return true;
    }

    /// <summary>
    /// What a reader has to see for a place to mean anything: the place itself, unless it sits inside content another language drew, in which
    /// case the whole of that.
    /// </summary>
    private LayoutRect ShownWhole((int Start, int Length) what)
    {
        var found = _laid.Root.RangeRects(what.Start, what.Length);
        var place = found[0];

        foreach (var rect in found) place = LayoutRect.Union(place, rect);

        for (var piece = _laid.Root.PieceAt(new LayoutPoint(place.X + (place.Width / 2), place.Y + (place.Height / 2)));
             piece.Exists;
             piece = piece.Parent)
        {
            if (piece.Part.TreePart() is not { } part) continue;
            if (ContentLanguages.Held(part) is null) continue;

            return piece.Bounds;
        }

        return place;
    }

    /// <summary>Goes to a line, counting from one — and to the whole of whatever drew it, where a line is inside a picture.</summary>
    internal bool GoToLine(int line) => ShowPlace(MarkdownFind.Line(this.State.Document, line), choose: false);

    /// <summary>
    /// Goes where a deep link leads (<see cref="ContentPath"/>) — a heading by the name it answers to, and what the link says under it in its
    /// section — or as far as it still does, and says whether the document had any of it.
    /// </summary>
    internal bool GoToPath(ContentPath path)
    {
        if (Located(path) is not { } place) return false;

        ShowPlace(place, choose: false);

        return true;
    }

    /// <summary>Goes to a heading a list of them handed on, wherever it now stands; false where no heading answers to it any more.</summary>
    internal bool ShowHeading(OutlineHeading heading)
    {
        if (Placed(heading) is not { } place) return false;

        ShowPlace(place, choose: false);

        return true;
    }

    /// <summary>
    /// Goes to the heading whose headings above it say <paramref name="titles"/>, outermost first — matched on the whole path, so two headings
    /// of one name under different parents stay apart, and each title read as the markdown it was written as. False where there is none.
    /// </summary>
    internal bool ShowTitled(IReadOnlyList<string> titles)
    {
        if (titles.Count == 0 || Titled([.. titles.Select(WithOutline.Titled)]) is not { } place) return false;

        ShowPlace(place, choose: false);

        return true;
    }

    /// <summary>
    /// A deep link to what is in the middle of the page — a view <paramref name="seen"/> large, in the content's own units, standing where the
    /// view does — to find it again later, after the document has been written in.
    /// </summary>
    internal ContentPath ReferenceAt(LayoutSize seen)
    {
        var middle = new LayoutPoint(ViewOrigin.X + (seen.Width / 2), ViewOrigin.Y + (seen.Height / 2));

        return _laid.Root.PieceAt(middle).Part.TreePart() is { } part ? PathTo(part.Start) : ContentPath.Nowhere;
    }

    // ── Where the page stands ───────────────────────────────────────────────

    /// <summary>
    /// Where the page stands: the character of the source that the first thing it has not scrolled past was drawn from. A page showing one
    /// document twice keeps its halves together by this, since a place in the source is the one thing both of them can say.
    /// </summary>
    internal int ShownFrom => _laid.Root.StopBelow(ViewOrigin.Y);

    /// <summary>
    /// Puts the character at <paramref name="offset"/> at the top of the page, which is what <see cref="ShownFrom"/> then reads back — and
    /// hands back where it stands, for content shown whole to have whatever holds it bring it onto its screen; null where it stands nowhere.
    ///
    /// <para>
    /// Not the same as going to something that was found. That brings a place onto the page and leaves the page where it is when the place
    /// is already on it, which is right for a search and wrong here: what matters is where the page stands, not whether the character can be
    /// seen from it.
    /// </para>
    /// </summary>
    internal LayoutRect? StandFrom(int offset)
    {
        if (offset < 0) return null;

        // A long document lays where it is asked, and nothing before it, first.
        Uncover(offset);

        var root = _laid.Root;

        // Where the page can stand from that character on. The character itself is often one the page draws nothing for — a heading's
        // hashes, the line a fence opens with — and a caret put there stands at the end of whatever came before it, which is a line or a
        // whole paragraph too early.
        var stops = root.CaretStops();
        var (low, high) = (0, stops.Count);

        while (low < high)
        {
            var middle = (low + high) / 2;
            if (stops[middle] >= offset) high = middle;
            else low = middle + 1;
        }

        if (low >= stops.Count) return null;

        var at = root.CaretRect(stops[low]);
        if (at.IsEmpty) return null;

        if (Scrolls) TopAt(at.Y);

        return at;
    }

    /// <summary>
    /// Moves the page so line <paramref name="line"/>, counting from nought, stands where <paramref name="place"/> says, on a page
    /// <paramref name="pageHeight"/> tall in the content's own units: at its top; half way down it, whether or not it was on the page; or,
    /// nearest, brought onto it only where it is not. Hands back, for content shown whole, what whatever holds it is to bring onto its screen.
    /// </summary>
    internal LayoutRect? ScrollToLine(int line, ShownPlace place, double pageHeight)
    {
        var document = this.State.Document;
        var at = Math.Clamp(line, 0, document.LineCount - 1);

        switch (place)
        {
            case ShownPlace.Top:
                return StandFrom(document.OffsetOf(at)) is { } top && !Scrolls ? top : null;

            // At the top, and then up half a page: the line stands half way down.
            case ShownPlace.Centre:
                if (StandFrom(document.OffsetOf(at)) is not { } centre) return null;
                if (!Scrolls) return centre;

                ScrollBy(-(pageHeight / 2));
                return null;

            default:
                GoToLine(at + 1);
                return null;
        }
    }

    /// <summary>
    /// Whether the whole of the drawing of <paramref name="block"/> stands on a page <paramref name="pageHeight"/> tall at once. False for a
    /// diagram drawn taller than the page: no one line of such a drawing answers to a line of its source, so a second showing of the document
    /// does better to hold the whole of that source beside it than to follow a line into it.
    /// </summary>
    internal bool ShowsWhole((int Start, int Length) block, double pageHeight)
    {
        if (block.Length <= 0 || pageHeight <= 0) return false;
        if (Blocked(block.Start) is not { } part) return false;

        var where = Where(part);

        return !where.IsEmpty && where.Height <= pageHeight;
    }

    /// <summary>
    /// Whether every row <paramref name="stretch"/> of the source is drawn on stands on a page <paramref name="pageHeight"/> tall at once — for
    /// content shown as typed, where a stretch of source is its own drawing. False where either end of it is not laid: what is laid reaches a
    /// screen either side of the page, so a stretch running past that is taller than the page.
    /// </summary>
    internal bool ShowsAtOnce((int Start, int Length) stretch, double pageHeight)
    {
        if (stretch.Length <= 0 || pageHeight <= 0) return false;

        var root = _laid.Root;
        var (first, last) = (root.CaretRect(stretch.Start), root.CaretRect(stretch.Start + stretch.Length));

        return !first.IsEmpty && !last.IsEmpty && last.Bottom - first.Y <= pageHeight;
    }

    /// <summary>Whether the content is laid line by line — code, or text shown as typed — where a search reads it a line at a time.</summary>
    internal bool LaysLines => Lined;

    /// <summary>
    /// Whether the code shown is too long to be coloured, folded and read as code at the pace of typing, and is shown as written instead — for
    /// a host to say so where the reader would otherwise wonder where its colours went.
    /// </summary>
    internal bool ColoursWithheld => Lined && Language(_named).Syntax is not null && this.State.Document.Length > Code.CodeSyntax.ColoursUpTo;
}
