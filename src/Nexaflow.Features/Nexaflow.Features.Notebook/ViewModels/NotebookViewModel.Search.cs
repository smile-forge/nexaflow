using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Features.Notebook.ViewModels;

/// <summary>
/// The notebook as an <see cref="ISearchable"/> page: "?" searches every cell's source — markdown and code
/// alike — paints the matches and steps the view through them, the way the code viewer does for a file.
/// <para>
/// The difference from a code file is that a notebook is not one buffer, so <b>a hit is a cell</b>: the cell
/// is the only thing the page can scroll to, and it is the id <c>read_cell</c> already speaks. Several
/// matches inside one cell therefore collapse to one hit, and the count is a count of cells.
/// </para>
/// <para>
/// Nothing is filtered. A notebook is read in order — cells build on the ones above them — so hiding what
/// missed would take the context away from the match. A matching cell is marked, and inside a <b>code</b>
/// cell the matched words themselves are washed: a code cell shows its source verbatim, where a markdown
/// cell renders to a document whose offsets are not the source's.
/// </para>
/// </summary>
public sealed partial class NotebookViewModel : ISearchable
{
    /// <summary>Cell indices of the current hits, in document order.</summary>
    private readonly List<int> _searchHits = [];
    private int _currentHit = -1;

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>Cell index the view should bring into view, or -1 for none. Reset to -1 between steps so
    /// stepping onto the same cell twice still moves the page.</summary>
    [ObservableProperty] private int _scrollToCellIndex = -1;

    public bool HasSearchMatches => SearchMatchCount > 0;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        Cells.Count == 0
            ? $"the notebook '{FileName}' (it has not parsed yet)"
            : $"the {Cells.Count} cell(s) of the notebook '{FileName}', by cell source";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!TextSearchMatcher.TryCreate(request, out var matcher, out var error))
            return Task.FromResult(SearchOutcome.Unsupported(error));

        // Marshalled even when not displaying: Cells is the bound cell list, and the agent reads it from
        // its own thread.
        return _shell.RunOnUiAsync(() => Task.FromResult(RunSearch(matcher, request, display)));
    }

    private SearchOutcome RunSearch(TextSearchMatcher matcher, SearchRequest request, bool display)
    {
        if (Cells.Count == 0)
            return SearchOutcome.None(IsLoaded
                ? $"'{FileName}' has no cells to search."
                : $"'{FileName}' is still being parsed — try again in a moment.");

        var hits  = new List<int>();
        var spans = new Dictionary<int, IReadOnlyList<(int, int)>>();
        for (var i = 0; i < Cells.Count; i++)
        {
            var occurrences = matcher.Occurrences(Cells[i].Source);
            if (occurrences.Count == 0) continue;
            hits.Add(i);
            spans[i] = occurrences;
        }

        // Kept whether or not this search displays: the agent searches without display and then asks for a
        // subset, and re-deriving the spans then would mean running the query twice.
        _lastSpans = spans;

        if (display) Apply(hits, spans, SearchSyntax.Format(request));

        return hits.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(HitsFor(hits, spans));
    }

    /// <summary>Marks exactly the cells the agent chose and scrolls to the first.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct) =>
        _shell.RunOnUiAsync(() =>
        {
            var chosen = hits.Select(h => int.TryParse(h.Id, out var n) ? n - 1 : -1)
                             .Where(i => i >= 0 && i < Cells.Count)
                             .Distinct()
                             .OrderBy(i => i)
                             .ToList();
            if (chosen.Count == 0) return Task.FromResult(false);

            // The spans came from the search that produced these ids; keeping the ones still on a chosen
            // cell means a narrowed result is still painted where it matched.
            var kept = chosen.Where(_lastSpans.ContainsKey).ToDictionary(i => i, i => _lastSpans[i]);
            Apply(chosen, kept, CurrentSearchTerm.Length == 0 ? $"{chosen.Count} selected" : CurrentSearchTerm);
            return Task.FromResult(true);
        });

    // ── Marking + stepping ────────────────────────────────────────────────────

    /// <summary>Spans of the last search, by cell index — kept so <see cref="ShowResultsAsync"/> can re-paint
    /// a subset without re-running the query.</summary>
    private Dictionary<int, IReadOnlyList<(int, int)>> _lastSpans = [];

    private void Apply(
        IReadOnlyList<int> hits, Dictionary<int, IReadOnlyList<(int, int)>> spans, string term)
    {
        _searchHits.Clear();
        _searchHits.AddRange(hits);

        for (var i = 0; i < Cells.Count; i++)
        {
            var hit = _searchHits.Contains(i);
            Cells[i].IsSearchHit = hit;
            Cells[i].SearchSpans = hit && spans.TryGetValue(i, out var s) ? s : [];
        }

        CurrentSearchTerm = term;
        SearchMatchCount  = _searchHits.Count;
        IsSearchActive    = true;      // a zero-match search is still a result the user should see
        _currentHit       = _searchHits.Count > 0 ? 0 : -1;

        if (_currentHit >= 0) ScrollTo(_searchHits[0]);
    }

    [RelayCommand]
    private void FindNextMatch() => Step(+1);

    [RelayCommand]
    private void FindPreviousMatch() => Step(-1);

    private void Step(int delta)
    {
        if (_searchHits.Count == 0) return;
        _currentHit = ((_currentHit + delta) % _searchHits.Count + _searchHits.Count) % _searchHits.Count;
        ScrollTo(_searchHits[_currentHit]);
    }

    /// <summary>Reset first: the property is what the view watches, so stepping back onto the same cell
    /// would otherwise raise nothing and the page would sit still.</summary>
    private void ScrollTo(int cellIndex)
    {
        ScrollToCellIndex = -1;
        ScrollToCellIndex = cellIndex;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchHits.Clear();
        _lastSpans = [];
        _currentHit = -1;
        foreach (var cell in Cells) { cell.IsSearchHit = false; cell.SearchSpans = []; }
        IsSearchActive    = false;
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;
        ScrollToCellIndex = -1;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IReadOnlyList<SearchHit> HitsFor(
        IEnumerable<int> indices, Dictionary<int, IReadOnlyList<(int, int)>> spans) =>
        indices.Select(i => new SearchHit(
                   (i + 1).ToString(),      // 1-based: the id read_cell already takes
                   $"Cell {i + 1} ({(Cells[i].IsCode ? "code" : Cells[i].IsMarkdown ? "markdown" : "raw")})",
                   LineAround(Cells[i].Source, spans[i][0].Item1)))
               .ToList();

    /// <summary>The source line the first match sits on — what makes a hit judgeable without opening it.</summary>
    private static string LineAround(string source, int offset)
    {
        var start = source.LastIndexOf('\n', System.Math.Min(offset, source.Length - 1)) + 1;
        var end   = source.IndexOf('\n', start);
        var line  = (end < 0 ? source[start..] : source[start..end]).Trim();
        return line.Length <= 160 ? line : line[..160] + "…";
    }
}
