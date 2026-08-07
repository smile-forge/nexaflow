using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;
using System.Globalization;

namespace Nexaflow.Features.Logs.ViewModels;

/// <summary>
/// The log tab as a searchable page. <c>?</c> scans the whole loaded document through the shared
/// <see cref="TextSearchMatcher"/>, so a term means here exactly what it means in the text viewer, the
/// editor and the markdown surface.
/// <para>
/// This is a THIRD, independent layer over the log — it neither reads nor writes the regex fade-filter or
/// the custom highlight term. Those are different questions: the filter dims lines that don't match (and
/// is deliberately case-insensitive), the custom term is the user's own persistent substring marker, and a
/// search finds, counts and steps through occurrences of a word. Folding any two together would make one
/// of them lie.
/// </para>
/// </summary>
public sealed partial class LogViewModel : ISearchable
{
    /// <summary>Hits handed to the agent in one call — enough to reason over without flooding its context.</summary>
    private const int SearchHitCap = 200;

    /// <summary>Ceiling on painted highlight spans. A one-character pattern over a large log produces tens
    /// of thousands of geometries; past a screenful they cost render time and tell the user nothing.</summary>
    private const int HighlightCap = 5000;

    // ── Visible search state (the status-bar chip binds these) ────────────────

    [ObservableProperty] private bool   _isSearchActive;
    [ObservableProperty] private int    _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    private int[]          _matchLines = [];
    private int            _currentMatchIndex = -1;
    private SearchRequest? _activeSearch;   // kept so an append or the head landing can re-derive the spans

    private IReadOnlyList<(int offset, int length)> _searchHighlights = [];

    /// <summary>Spans the view paints behind matches. Watched via <c>PropertyChanged</c>, like
    /// <see cref="CustomTermHighlights"/>.</summary>
    public IReadOnlyList<(int offset, int length)> SearchHighlights
    {
        get => _searchHighlights;
        private set { _searchHighlights = value; OnPropertyChanged(); }
    }

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        string.IsNullOrEmpty(FileName)  ? "the open log file"
      : IsLoadingHead                   ? $"the log file '{FileName}' — the most recent lines (earlier history is still loading)"
      :                                   $"the log file '{FileName}'";

    /// <summary>Mirrors the other document viewers' curve: a term or two is almost certainly a search;
    /// four is borderline. Past that <see cref="SearchScoring.LooksLikeProse"/> has already bowed out.</summary>
    public float ScoreQuery(string input) => SearchScoring.TermCount(input) switch
    {
        1 => 0.9f,
        2 => 0.8f,
        3 => 0.6f,
        4 => 0.2f,
        _ => 0f,
    };

    // The document is thread-affine and the agent calls with display:false from its own thread, so BOTH
    // paths marshal — reading Document.Text off the UI thread is as wrong as writing to it.
    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
        => _shell.RunOnUiAsync(() => Task.FromResult(RunSearch(request, display, ct)));

    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
        => _shell.RunOnUiAsync(() => Task.FromResult(NarrowTo(hits)));

    // ── The scan (UI thread only) ─────────────────────────────────────────────

    private SearchOutcome RunSearch(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!TextSearchMatcher.TryCreate(request, out var matcher, out var error))
            return SearchOutcome.Unsupported(error);

        var body  = Document.Text;
        var lines = new List<int>();
        var hits  = new List<SearchHit>();
        var spans = new List<(int offset, int length)>();

        foreach (var line in matcher.ScanLines(body))
        {
            ct.ThrowIfCancellationRequested();
            lines.Add(line.Index);

            if (hits.Count < SearchHitCap)
                hits.Add(HitFor(line.Index, line.Text));

            if (display && spans.Count < HighlightCap)
                foreach (var (index, length) in matcher.Occurrences(line.Text))
                {
                    if (spans.Count >= HighlightCap) break;
                    spans.Add((line.Offset + index, length));
                }
        }

        if (display)
        {
            _activeSearch = request;
            Apply(lines, spans, SearchSyntax.Format(request));
        }

        return Outcome(lines.Count, hits);
    }

    /// <summary>
    /// While the head is still streaming in only the tail is in the document, so say so rather than
    /// reporting a count that is about to change. We never await the head: it can be gigabytes, and
    /// <c>SearchAsync</c> is driven both from a keystroke and from the agent loop. <see cref="RescanSearch"/>
    /// re-runs when it lands, so the number completes itself.
    /// </summary>
    private SearchOutcome Outcome(int total, IReadOnlyList<SearchHit> hits)
    {
        if (!IsLoadingHead)
            return total == 0 ? SearchOutcome.None() : SearchOutcome.Found(hits, total);

        var message = $"Searched the {LineCount} most recent line(s) — the earlier history of "
                    + $"'{FileName}' is still loading.";

        // Narrowed() would report hits.Count as the total, which under-counts once the cap bites.
        return new SearchOutcome(total, hits, message);
    }

    private static SearchHit HitFor(int line, string text) =>
        new(line.ToString(CultureInfo.InvariantCulture), $"line {line + 1}", text.TrimEnd('\r'));

    // ── Display ───────────────────────────────────────────────────────────────

    /// <param name="keepIndex">Match to stay parked on. A live tail re-runs the scan on every append;
    /// resetting to 0 each time would drag the user back to the top of the log as it grows.</param>
    private void Apply(IReadOnlyList<int> lines, IReadOnlyList<(int offset, int length)> spans,
                       string term, int keepIndex = 0)
    {
        _matchLines        = [.. lines];
        _currentMatchIndex = lines.Count > 0 ? Math.Clamp(keepIndex, 0, lines.Count - 1) : -1;
        SearchHighlights   = spans;
        SearchMatchCount   = lines.Count;
        CurrentSearchTerm  = term;
        IsSearchActive     = true;   // true even at zero matches: "no matches for X" is a result to show
        if (lines.Count > 0 && keepIndex == 0) RequestScrollToLine(lines[0]);
    }

    /// <summary>Narrows the match set to the agent's chosen lines — the count, the highlights and
    /// next/previous all follow it, so "show me only the ones about auth" really does filter the view.</summary>
    private bool NarrowTo(IReadOnlyList<SearchHit> hits)
    {
        var lines = hits.Select(h => int.TryParse(h.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : -1)
                        .Where(l => l >= 0)
                        .Distinct()
                        .OrderBy(l => l)
                        .ToList();
        if (lines.Count == 0) return false;

        // Re-derive the spans for exactly these lines: the highlights must agree with the narrowed set,
        // not keep painting the matches the agent discarded.
        var kept = new HashSet<int>(lines);
        List<(int offset, int length)> spans = _searchHighlights.Count == 0 ? [] : SpansOn(kept);

        Apply(lines, spans, CurrentSearchTerm);
        return true;
    }

    private List<(int offset, int length)> SpansOn(HashSet<int> lines)
    {
        var kept = new List<(int offset, int length)>();
        foreach (var span in _searchHighlights)
        {
            var line = Document.GetLineByOffset(Math.Clamp(span.offset, 0, Document.TextLength)).LineNumber - 1;
            if (lines.Contains(line)) kept.Add(span);
        }
        return kept;
    }

    /// <summary>
    /// Re-runs the active search over the whole document. Called wherever the document is mutated: an
    /// append leaves existing offsets valid but adds lines, and the head landing shifts EVERY offset. One
    /// full rescan covers both, and costs the same as the <c>ScanForCustomHighlights</c> pass already
    /// running beside it.
    /// </summary>
    internal void RescanSearch()
    {
        if (!IsSearchActive || _activeSearch is null) return;
        if (!TextSearchMatcher.TryCreate(_activeSearch, out var matcher, out _)) return;

        var body  = Document.Text;
        var lines = new List<int>();
        var spans = new List<(int offset, int length)>();

        foreach (var line in matcher.ScanLines(body))
        {
            lines.Add(line.Index);
            if (spans.Count >= HighlightCap) continue;
            foreach (var (index, length) in matcher.Occurrences(line.Text))
            {
                if (spans.Count >= HighlightCap) break;
                spans.Add((line.Offset + index, length));
            }
        }

        Apply(lines, spans, CurrentSearchTerm, keepIndex: Math.Max(_currentMatchIndex, 0));
    }

    // ── Navigation / dismissal (the status-bar chip) ──────────────────────────

    public bool HasSearchMatches => SearchMatchCount > 0;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));

    [RelayCommand]
    private void FindNextMatch() => StepMatch(+1);

    [RelayCommand]
    private void FindPreviousMatch() => StepMatch(-1);

    private void StepMatch(int delta)
    {
        if (_matchLines.Length == 0) return;
        _currentMatchIndex = (_currentMatchIndex + delta + _matchLines.Length) % _matchLines.Length;
        RequestScrollToLine(_matchLines[_currentMatchIndex]);
    }

    /// <summary>
    /// Scrolls to a 0-based line. <c>ScrollToOffset</c> is an observable int, so re-assigning the SAME
    /// offset raises no change and the view would sit still — stepping onto a repeated target has to
    /// pass through the ignored -1 first.
    /// </summary>
    private void RequestScrollToLine(int line0)
    {
        if (Document.LineCount == 0) return;
        var offset   = Document.GetLineByNumber(Math.Clamp(line0 + 1, 1, Document.LineCount)).Offset;
        ScrollToOffset = -1;
        ScrollToOffset = offset;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _matchLines        = [];
        _currentMatchIndex = -1;
        _activeSearch      = null;
        SearchHighlights   = [];
        SearchMatchCount   = 0;
        CurrentSearchTerm  = string.Empty;
        IsSearchActive     = false;
    }
}
