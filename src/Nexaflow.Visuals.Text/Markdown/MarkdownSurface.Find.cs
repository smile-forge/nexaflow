using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

using Nexaflow.Features.Common.Search;
using Nexaflow.Markdown.Ast;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Finding a place in the document: a search, a line, a heading, a reference somebody saved.
///
/// <para>
/// <strong>Looking is done in the source and never in the drawing.</strong> The parser only ever copies, so the source is
/// the one place the words are whole; the offsets come back out of it and the laid tree turns them into places on the
/// page (<see cref="MarkdownFind"/>). Nothing in the tree is changed to mark what was found — it is washed at paint time —
/// so stopping a search is a repaint; and what was looked for is asked again of the document each time it is written in, so a
/// search survives that too (<see cref="Refound"/>).
/// </para>
/// </summary>
public sealed partial class MarkdownSurface
{
    /// <summary>Everywhere the document says what was looked for, in the order a reader comes to them.</summary>
    public IReadOnlyList<(int Start, int Length)> Found { get; private set; } = [];

    /// <summary>Which of them is being looked at, or -1 for none.</summary>
    public int At { get; private set; } = -1;

    /// <summary>
    /// Looks for <paramref name="term"/> and goes to the first place it is — both where it is written and where it is only
    /// drawn, which is what a reader means by what the page says.
    /// </summary>
    public int Find(string? term) => Looked(() => _engine.ReadsAsDrawn
        ? MarkdownFind.Drawn(_engine.DrawnWords, term)
        : MarkdownFind.In(_shown.Laid, _shown.Markdown, term));

    /// <summary>
    /// The same, for a question more complicated than a word — what a search box worked out — going to the first place at or
    /// after <paramref name="from"/> in the source, and round to the first where none is.
    /// </summary>
    public int Find(Func<string, IReadOnlyList<(int Index, int Length)>>? occurrences, int from = 0) =>
        Looked(() => _engine.ReadsAsDrawn
            ? _engine.DrawnWords.Every(occurrences)
            : MarkdownFind.In(_shown.Laid, _shown.Markdown, occurrences), from);

    /// <summary>
    /// Shows every place <paramref name="matcher"/> finds and goes to the first, handing back what was found so the host
    /// can count them or say what each one reads.
    /// </summary>
    public IReadOnlyList<RenderedMatch> FindInRendered(TextSearchMatcher matcher) => FindInRendered(matcher, from: 0);

    /// <summary>
    /// The same, going to the first place at or after <paramref name="from"/> in the source — for a search taken up again, so it
    /// goes on from the match the reader was on rather than starting over.
    /// </summary>
    public IReadOnlyList<RenderedMatch> FindInRendered(TextSearchMatcher matcher, int from)
    {
        Find(matcher.Occurrences, from);

        return [.. Enumerable.Range(0, Found.Count).Select(at => new RenderedMatch(at, Preview(at)))];
    }

    private int Looked(Func<IReadOnlyList<(int Start, int Length)>> looking, int from = 0)
    {
        _lookedFor = looking;
        _foundIn = _shown.Markdown;

        Found = looking();
        At = -1;

        _shown.Showing = (Found, At);

        if (Found.Count > 0) Goes(FirstAtOrAfter(from));

        return Found.Count;
    }

    /// <summary>
    /// What was looked for, to be asked again of the document as it comes to read; null where nothing is being looked for, or a
    /// host has narrowed what was found to a list of its own (<see cref="Restrict"/>), which asking again would undo.
    /// </summary>
    private Func<IReadOnlyList<(int Start, int Length)>>? _lookedFor;

    /// <summary>The document as it read when <see cref="Found"/> was found in it.</summary>
    private string _foundIn = string.Empty;

    /// <summary>
    /// What was found, found again in the document as it now reads — written in here, written in somewhere else, taken back — with
    /// the one being looked at kept to where its place went. Nothing is scrolled to or picked out: the reader is writing, not
    /// looking. Done before the page says it was laid out (<see cref="PreRender"/>), so whoever reads what was found then reads it
    /// as it is.
    /// </summary>
    private void Refound()
    {
        if (_lookedFor is not { } looking) return;

        var now = _shown.Markdown;
        if (ReferenceEquals(_foundIn, now) || string.Equals(_foundIn, now, StringComparison.Ordinal)) return;

        var was = At >= 0 ? SourceDifference.Between(_foundIn, now).Moved(Found[At].Start) : 0;

        _foundIn = now;
        Found = looking();
        At = Found.Count == 0 ? -1 : FirstAtOrAfter(was);

        _shown.Showing = (Found, At);
    }

    /// <summary>The first place found at or after <paramref name="from"/> in the source, or the first of them all where none is.</summary>
    private int FirstAtOrAfter(int from)
    {
        for (var at = 0; at < Found.Count; at++)
            if (Found[at].Start >= from) return at;

        return 0;
    }

    /// <summary>
    /// Narrows what is shown to the places named by their order in <see cref="Found"/>, and says how many are left — for a
    /// host whose own list of results has been filtered since.
    /// </summary>
    public int Restrict(IReadOnlySet<int> keep)
    {
        var kept = Found.Where((_, at) => keep.Contains(at)).ToList();

        _lookedFor = null;
        Found = kept;
        At = kept.Count > 0 ? 0 : -1;

        _shown.Showing = (Found, At);

        if (At >= 0) _shown.Show(Found[At]);

        return kept.Count;
    }

    /// <summary>What a place reads as, for a host listing what it found — the line it is on, tidied.</summary>
    public string Preview(int at)
    {
        if (at < 0 || at >= Found.Count) return string.Empty;

        var (start, _) = Found[at];

        // A page read as what it draws is quoted as what it draws: its source is formatting nobody would recognise.
        if (_engine.ReadsAsDrawn) return _engine.DrawnWords.LineAt(start);
        var line = MarkdownFind.Line(_shown.Markdown, MarkdownFind.LineAt(_shown.Markdown, start));

        return _shown.Markdown.Substring(line.Start, line.Length).Trim();
    }

    /// <summary>
    /// How far down the document each place found is, from nought at the top to one at the bottom — for a strip beside the
    /// scrollbar marking where they are. Every place has its mark, since the reader is told how many were found and each of them
    /// is somewhere (<see cref="Standing"/>).
    /// </summary>
    public IReadOnlyList<double> SearchMarkPositions()
    {
        var laid = _shown.Laid;
        var height = laid.Size.Height;
        if (height <= 0) return [];

        return [.. Found.Select(place => Math.Clamp(Standing(laid, place) / height, 0, 1))];
    }

    /// <summary>
    /// How far down the page <paramref name="place"/> stands: where it is drawn; where it would come, in a long document not laid
    /// that far yet, going by what is laid; and where nothing on the page draws it — inside a section the reader folded — where
    /// what holds it is drawn, at the last place a caret can stand before it.
    /// </summary>
    private double Standing(Laid laid, (int Start, int Length) place)
    {
        if (laid.Root.RangeRects(place.Start, place.Length) is { Count: > 0 } rects) return rects[0].Y;
        if (_engine.UnlaidTop(place.Start) is { } unlaid) return unlaid;

        var stops = laid.Stops;
        var (low, high, held) = (0, stops.Count - 1, -1);

        while (low <= high)
        {
            var middle = (low + high) / 2;
            if (stops[middle] <= place.Start) (held, low) = (stops[middle], middle + 1);
            else high = middle - 1;
        }

        return held >= 0 && laid.Root.CaretRect(held) is { IsEmpty: false } at ? at.Y : 0;
    }

    /// <summary>The next place, coming back round to the first.</summary>
    public bool Next() => Goes(At + 1);

    /// <summary>The one before, coming back round to the last.</summary>
    public bool Previous() => Goes(At - 1);

    /// <summary>Steps to the next place (<paramref name="delta"/> of one) or the one before.</summary>
    public void StepSearch(int delta)
    {
        if (delta >= 0) Next();
        else Previous();
    }

    /// <summary>Stops looking, which is a repaint and nothing else.</summary>
    public void Stop()
    {
        _lookedFor = null;
        Found = [];
        At = -1;

        _shown.Showing = ([], -1);
    }

    /// <summary>The same, as a host that has finished searching says it.</summary>
    public void ClearSearch() => Stop();

    /// <summary>Narrows what is shown to the given places; what a host with its own filtered list asks.</summary>
    public int RestrictSearch(IReadOnlySet<int> keep) => Restrict(keep);

    private bool Goes(int to)
    {
        if (Found.Count == 0) return false;

        At = (to + Found.Count) % Found.Count;

        _shown.Showing = (Found, At);
        _shown.Show(Found[At]);

        return true;
    }

    // ── Going to a place ────────────────────────────────────────────────────

    /// <summary>Goes to a line, counting from one — and to the whole of whatever drew it, where a line is inside a picture.</summary>
    public bool GoTo(int line) => _shown.Show(MarkdownFind.Line(_shown.Markdown, line), choose: false);

    /// <summary>
    /// Goes where a saved reference leads, or as far as it still does — and says whether the document had it at all, which
    /// it can answer before anything has been laid out.
    /// </summary>
    public bool GoTo(ContentPath path)
    {
        if (MarkdownFind.Followed(Read, path) is not { } place) return false;

        _shown.Show(place, choose: false);

        return true;
    }

    /// <summary>How to find what is in the middle of the page again later, after the document has been written in.</summary>
    public ContentPath Reference() =>
        ContentPath.Of(_shown.Laid.Root.PieceAt(Middle()).Part.TreePart());

    /// <summary>
    /// Scrolls the heading whose headings above it say <paramref name="titlePath"/> to the top — matched on the whole path,
    /// so two headings of one name under different parents stay apart. Nothing, where there is none. Done once the page is
    /// laid out, since a heading has nowhere to be scrolled to before.
    /// </summary>
    public void ScrollToHeading(IReadOnlyList<string>? titlePath)
    {
        var source = _shown.Markdown;
        var blocks = MarkdownBlocks.Split(source);
        var index = MarkdownBlocks.FindHeadingBlock(blocks, titlePath);
        if (index < 0) return;

        var start = 0;
        for (var at = 0; at <= index; at++)
        {
            var found = source.IndexOf(blocks[at], start, StringComparison.Ordinal);
            if (found < 0) return;

            if (at == index)
            {
                Dispatcher.BeginInvoke(() => _shown.Show((found, blocks[at].Length), choose: false), DispatcherPriority.Loaded);

                return;
            }

            start = found + blocks[at].Length;
        }
    }

    /// <summary>
    /// Where the page stands: the character of the source that the first thing it has not scrolled past was drawn
    /// from, or -1 before there is a laid-out page to be anywhere in. A page showing one document twice keeps its
    /// halves together by this, since a place in the source is the one thing both of them can say.
    /// </summary>
    public int ShownFrom
    {
        get
        {
            if (_scroller.ViewportHeight <= 0) return -1;

            var top = _scroller.TranslatePoint(new Point(0, 0), _shown).Y;

            return _shown.Laid.Root.StopBelow(top / _shown.Zoom);
        }
    }

    /// <summary>
    /// Puts the character at <paramref name="offset"/> at the top of the page, which is what <see cref="ShownFrom"/>
    /// then reads back.
    ///
    /// <para>
    /// Not the same as going to something that was found. That brings a place onto the page and leaves the page
    /// where it is when the place is already on it, which is right for a search and wrong here: what matters is
    /// where the page stands, not whether the character can be seen from it.
    /// </para>
    /// </summary>
    public bool ShowFrom(int offset)
    {
        if (offset < 0 || _scroller.ViewportHeight <= 0) return false;

        var root = _shown.Laid.Root;

        // Where the page can stand from that character on. The character itself is often one the page draws
        // nothing for — a heading's hashes, the line a fence opens with — and a caret put there stands at the end
        // of whatever came before it, which is a line or a whole paragraph too early.
        var drawn = root.CaretStops().FirstOrDefault(stop => stop >= offset, -1);
        if (drawn < 0) return false;

        var at = root.CaretRect(drawn);
        if (at.IsEmpty) return false;

        // How far below the top of the page it stands now, which is how much further down the document to stand.
        var below = _shown.TranslatePoint(new Point(0, at.Y * _shown.Zoom), _scroller).Y;

        _scroller.ScrollToVerticalOffset(_scroller.VerticalOffset + below);

        return true;
    }

    /// <summary>
    /// The stretch of source the block holding <paramref name="offset"/> was written as, or nothing where no block
    /// holds it. Two showings of a document agree about blocks whatever else they disagree about, so it is what a
    /// page holding both of them pairs them by.
    /// </summary>
    public (int Start, int Length) BlockAt(int offset) =>
        _engine.Blocked(offset) is { } block ? (block.Start, block.Length) : default;

    /// <summary>
    /// Whether the whole of this page's drawing of <paramref name="block"/> stands on the page at once. False for a
    /// diagram drawn taller than the page: no one line of such a drawing answers to a line of its source, so a
    /// second showing of the document does better to hold the whole of that source beside it than to follow a line
    /// into it.
    /// </summary>
    public bool ShowsWhole((int Start, int Length) block)
    {
        if (block.Length <= 0 || _scroller.ViewportHeight <= 0) return false;
        if (_engine.Blocked(block.Start) is not { } part) return false;

        var where = _engine.Where(part);

        return !where.IsEmpty && where.Height * _shown.Zoom <= _scroller.ViewportHeight;
    }

    /// <summary>
    /// The stretch of source the block after the one holding <paramref name="offset"/> was written as, or nothing
    /// where none follows it. See <see cref="ShowsWhole"/> for when a page wants it rather than the block itself.
    /// </summary>
    public (int Start, int Length) BlockAfter(int offset) =>
        _engine.BlockedAfter(offset) is { } block ? (block.Start, block.Length) : default;
}

/// <summary>
/// One place a search found in a document — its ordinal among them, which is the id a searchable page hands back and forth,
/// and the line it is on, tidied, as a preview.
/// </summary>
public readonly record struct RenderedMatch(int Ordinal, string Preview);
