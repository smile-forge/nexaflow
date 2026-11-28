using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

using Nexaflow.Features.Common.Search;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Finding a place in the document — a search, a line, a heading, a reference somebody saved — asked of the engine, which finds it and
/// goes there (<see cref="ContentEngine.FindWord"/>, <see cref="ContentEngine.ShowPlace"/>). What is left here is the page's: how much of
/// the content it shows, and, for content shown whole, bringing a place onto the screen of whatever holds it.
/// </summary>
public sealed partial class NexaTextSurface
{
    /// <summary>Everywhere the document says what was looked for, in the order a reader comes to them.</summary>
    public IReadOnlyList<(int Start, int Length)> Found => _engine.Found;

    /// <summary>Which of them is being looked at, or -1 for none.</summary>
    public int At => _engine.FoundAt;

    /// <summary>
    /// Looks for <paramref name="term"/> and goes to the first place it is — both where it is written and where it is only drawn, which is
    /// what a reader means by what the page says.
    /// </summary>
    public int Find(string? term) => _engine.FindWord(term);

    /// <summary>
    /// The same, for a question more complicated than a word — what a search box worked out — going to the first place at or after
    /// <paramref name="from"/> in the source, and round to the first where none is (<see cref="ContentEngine.FindIn"/>).
    /// </summary>
    public int Find(Func<string, IReadOnlyList<(int Index, int Length)>>? occurrences, int from = 0) => _engine.FindIn(occurrences, from);

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

    /// <summary>
    /// Narrows what is shown to the places named by their order in <see cref="Found"/>, and says how many are left — for a
    /// host whose own list of results has been filtered since.
    /// </summary>
    public int Restrict(IReadOnlySet<int> keep) => _engine.RestrictFound(keep);

    /// <summary>
    /// Shows <paramref name="places"/>, which a host found itself, with the one at <paramref name="at"/> the one being looked at — moving
    /// nothing, since the host says when to go to one (<see cref="PickOut"/>). They are carried through each edit, not found again: the host
    /// found them.
    /// </summary>
    public void ShowFound(IReadOnlyList<(int Start, int Length)> places, int at) => _engine.ShowFound(places, at);

    /// <summary>Picks out <paramref name="place"/> and brings it onto the page, as going to a place found does.</summary>
    public bool PickOut((int Start, int Length) place) => _engine.ShowPlace(place);

    /// <summary>What a place reads as, for a host listing what it found — the line it is on, tidied.</summary>
    public string Preview(int at) => _engine.FoundPreview(at);

    /// <summary>
    /// How far down the document each place found is, from nought at the top to one at the bottom — for a strip beside the scrollbar marking
    /// where they are.
    /// </summary>
    public IReadOnlyList<double> SearchMarkPositions() => _engine.FoundMarkPositions();

    /// <summary>The next place, coming back round to the first.</summary>
    public bool Next() => _engine.NextFound();

    /// <summary>The one before, coming back round to the last.</summary>
    public bool Previous() => _engine.PreviousFound();

    /// <summary>Steps to the next place (<paramref name="delta"/> of one) or the one before.</summary>
    public void StepSearch(int delta) => _engine.StepFound(delta);

    /// <summary>Stops looking, which is a repaint and nothing else.</summary>
    public void Stop() => _engine.StopFinding();

    /// <summary>The same, as a host that has finished searching says it.</summary>
    public void ClearSearch() => Stop();

    /// <summary>Narrows what is shown to the given places; what a host with its own filtered list asks.</summary>
    public int RestrictSearch(IReadOnlySet<int> keep) => Restrict(keep);

    // ── Going to a place ────────────────────────────────────────────────────

    /// <summary>Goes to a line, counting from one — and to the whole of whatever drew it, where a line is inside a picture.</summary>
    public bool GoTo(int line) => _engine.GoToLine(line);

    /// <summary>
    /// Goes where a deep link leads (<see cref="ContentPath"/>) — a heading by the name it answers to, and what the link says under it in
    /// its section — or as far as it still does, and says whether the document had any of it.
    /// </summary>
    public bool GoTo(ContentPath path) => _engine.GoToPath(path);

    /// <summary>Every heading of the document's own laid, in the order written — what an outline of it lists (<see cref="OutlineChanged"/>).</summary>
    public IReadOnlyList<OutlineHeading> Headings => _engine.Headings;

    /// <summary>Goes to a heading a list of them handed on, wherever it now stands; false where no heading answers to it any more.</summary>
    public bool Show(OutlineHeading heading) => _engine.ShowHeading(heading);

    /// <summary>Where a heading a list of them handed on now starts in the source, or null where no heading answers to it any more.</summary>
    public int? StartOf(OutlineHeading heading) => _engine.Placed(heading)?.Start;

    /// <summary>A deep link to what is in the middle of the page, to find it again later, after the document has been written in.</summary>
    public ContentPath Reference() => _engine.ReferenceAt(Seen);

    /// <summary>
    /// Scrolls the heading whose headings above it say <paramref name="titlePath"/> to the top (<see cref="ContentEngine.ShowTitled"/>). Nothing,
    /// where there is none. Done once the page is laid out, since the headings are the outline of what was laid.
    /// </summary>
    public void ScrollToHeading(IReadOnlyList<string>? titlePath)
    {
        if (titlePath is not { Count: > 0 } titles) return;

        Dispatcher.BeginInvoke(() => _engine.ShowTitled(titles), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Where the page stands: the character of the source that the first thing it has not scrolled past was drawn from
    /// (<see cref="ContentEngine.ShownFrom"/>), or -1 before there is a laid-out page to be anywhere in.
    /// </summary>
    public int ShownFrom => _shown.ActualHeight <= 0 ? -1 : _engine.ShownFrom;

    /// <summary>
    /// Puts the character at <paramref name="offset"/> at the top of the page, which is what <see cref="ShownFrom"/> then reads back
    /// (<see cref="ContentEngine.StandFrom"/>).
    /// </summary>
    public bool ShowFrom(int offset)
    {
        if (offset < 0 || _shown.ActualHeight <= 0) return false;
        if (_engine.StandFrom(offset) is not { } at) return false;

        // Content shown whole has whatever holds it bring the place onto its screen.
        if (!_engine.Scrolls) HolderBrings(at);

        return true;
    }

    /// <summary>Has whatever holds content shown whole bring <paramref name="box"/>, in the content's own units, onto its screen at its next layout pass.</summary>
    private void HolderBrings(LayoutRect box)
    {
        _shown.BringIntoView(_shown.FromContent(box));
        PageMoving();
    }

    /// <summary>How much of the content the page shows, in the content's own units.</summary>
    private LayoutSize Seen => new(_shown.ActualWidth / _shown.Zoom, _shown.ActualHeight / _shown.Zoom);

    /// <summary>
    /// The stretch of source the block holding <paramref name="offset"/> was written as, or nothing where no block
    /// holds it. Two showings of a document agree about blocks whatever else they disagree about, so it is what a
    /// page holding both of them pairs them by.
    /// </summary>
    public (int Start, int Length) BlockAt(int offset) =>
        _engine.Blocked(offset) is { } block ? (block.Start, block.Length) : default;

    /// <summary>
    /// Whether the whole of this page's drawing of <paramref name="block"/> stands on the page at once (<see cref="ContentEngine.ShowsWhole"/>).
    /// </summary>
    public bool ShowsWhole((int Start, int Length) block) => _shown.ActualHeight > 0 && _engine.ShowsWhole(block, Seen.Height);

    /// <summary>
    /// Whether every row <paramref name="stretch"/> of the source is drawn on stands on the page at once (<see cref="ContentEngine.ShowsAtOnce"/>).
    /// </summary>
    public bool ShowsAtOnce((int Start, int Length) stretch) => _shown.ActualHeight > 0 && _engine.ShowsAtOnce(stretch, Seen.Height);

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
