using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Tabular.Streaming;
using Nexaflow.Search;
using System.Globalization;

namespace Nexaflow.Features.Tabular.ViewModels;

/// <summary>
/// The grid as a searchable page. A <c>?</c> query is pushed straight into
/// <see cref="IRowSource.GetVisibleAsync"/> as a cross-column predicate, so one existing call scans the
/// WHOLE file — no new IO path and no index, and the same cost a scroll already pays.
/// <para>
/// It composes with the typed per-column filters rather than replacing them. A column filter is
/// per-column and typed (numeric range, date range, tri-state boolean); "?" asks whether ANY cell
/// matches, which no column filter can express. Writing the query into the filter panel would also
/// silently rewrite the user's filters and inherit that filter's swallow-bad-regex behaviour.
/// </para>
/// </summary>
public sealed partial class TabularViewModel : ISearchable
{
    /// <summary>Matching rows collected in one pass. The cost is the file scan, not the row count, so
    /// this bounds how much we hold — past it the reported total is a floor and says so.</summary>
    private const int SearchScanCap = 5000;

    /// <summary>Hits handed to the agent in one call.</summary>
    private const int SearchHitCap = 200;

    // ── Visible search state (the toolbar chip binds these) ───────────────────

    [ObservableProperty] private bool   _isSearchActive;
    [ObservableProperty] private int    _searchMatchCount;
    [ObservableProperty] private bool   _isSearchTruncated;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    private int[]         _matchRows = [];       // absolute row indices, ascending
    private int           _currentMatchIndex = -1;
    private HashSet<int>? _restrictRows;         // non-null ⇒ the grid is pinned to these rows

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription => string.IsNullOrEmpty(FileName)
        ? "the loaded rows"
        : $"every row of '{FileName}' ({Columns.Count} columns), matched cell by cell";

    public float ScoreQuery(string input) => SearchScoring.TermCount(input) switch
    {
        1 => 0.9f,
        2 => 0.8f,
        3 => 0.6f,
        4 => 0.2f,
        _ => 0f,
    };

    public async Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (_source is null) return SearchOutcome.Unsupported("No tabular data is loaded yet.");
        if (!TextSearchMatcher.TryCreate(request, out var matcher, out var error))
            return SearchOutcome.Unsupported(error);

        // Snapshot the column filters on the CALLING thread — Columns is a bound ObservableCollection,
        // and the agent calls this from its own thread.
        var columnFilter = BuildColumnFilter();

        var rows = await _source.GetVisibleAsync(
            0, SearchScanCap, cells => columnFilter(cells) && cells.Any(matcher!.Matches), ct);

        // A cancelled scan returns a PARTIAL list rather than throwing, so treating it as a result
        // would report "these are all the matches" for a search the user abandoned.
        ct.ThrowIfCancellationRequested();

        var truncated = rows.Count >= SearchScanCap;
        var hits      = rows.Take(SearchHitCap).Select(r => HitFor(r, matcher!)).ToList();

        // RunOnUiAsync has no Func<Task> overload — a bare `() => Show(...)` would bind to the Action one,
        // discard the Task and run the window refresh fire-and-forget. Return a value to reach Func<Task<T>>.
        if (display)
            await _shell.RunOnUiAsync(async () =>
            {
                await Show(rows, SearchSyntax.Format(request), truncated);
                return true;
            });

        if (rows.Count == 0) return SearchOutcome.None();

        return truncated
            ? SearchOutcome.Found(hits, rows.Count,
                $"Stopped after the first {SearchScanCap:N0} matching rows — there may be more.")
            : SearchOutcome.Found(hits, rows.Count);
    }

    /// <summary>
    /// A hit is a ROW; the matching column goes in the preview. The id must round-trip through
    /// <see cref="ShowResultsAsync"/>, and rows are the only thing the grid can honestly narrow to —
    /// a "row:col" id would come back as a narrowing it cannot render.
    /// </summary>
    private SearchHit HitFor(HydratedRow row, TextSearchMatcher matcher)
    {
        for (var i = 0; i < row.Cells.Length; i++)
        {
            if (!matcher.Matches(row.Cells[i])) continue;
            var header = i < Columns.Count ? Columns[i].Header : $"column {i + 1}";
            return new SearchHit(row.AbsoluteIndex.ToString(CultureInfo.InvariantCulture),
                                 $"row {row.AbsoluteIndex + 1}",
                                 $"{header}: {row.Cells[i]}");
        }
        return new SearchHit(row.AbsoluteIndex.ToString(CultureInfo.InvariantCulture),
                             $"row {row.AbsoluteIndex + 1}",
                             string.Join(" | ", row.Cells));
    }

    /// <summary>Narrows the grid to the agent's chosen rows. Honest here in a way it isn't for a single
    /// document: the window really can be pinned to an arbitrary set of absolute indices.</summary>
    public async Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var rows = hits.Select(h => int.TryParse(h.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) ? r : -1)
                       .Where(r => r >= 0)
                       .Distinct()
                       .OrderBy(r => r)
                       .ToArray();
        if (rows.Length == 0) return false;

        return await _shell.RunOnUiAsync(async () =>
        {
            _matchRows         = rows;
            _restrictRows      = [.. rows];
            _currentMatchIndex = 0;
            SearchMatchCount   = rows.Length;
            IsSearchTruncated  = false;
            IsSearchActive     = true;
            FocalRow           = rows[0];
            await RefreshWindowAsync();
            return true;
        });
    }

    // ── Display (UI thread) ───────────────────────────────────────────────────

    private async Task Show(IReadOnlyList<HydratedRow> rows, string term, bool truncated)
    {
        _matchRows         = [.. rows.Select(r => r.AbsoluteIndex)];
        _currentMatchIndex = _matchRows.Length > 0 ? 0 : -1;
        _restrictRows      = null;   // a search reveals; only ShowResultsAsync pins
        SearchMatchCount   = rows.Count;
        IsSearchTruncated  = truncated;
        CurrentSearchTerm  = term;
        IsSearchActive     = true;   // true even at zero matches: "no matches for X" is a result to show

        if (_matchRows.Length == 0) { ApplySearchHitsToWindow(); return; }

        FocalRow = _matchRows[0];
        await RefreshWindowAsync();
    }

    private void ApplySearchHitsToWindow()
    {
        if (_matchRows.Length == 0)
        {
            foreach (var row in Window) row.IsSearchHit = false;
            return;
        }
        var hits = new HashSet<int>(_matchRows);
        foreach (var row in Window) row.IsSearchHit = hits.Contains(row.AbsoluteIndex);
    }

    // ── Navigation / dismissal (the toolbar chip) ─────────────────────────────

    public bool HasSearchMatches => SearchMatchCount > 0;

    /// <summary>"+" when the scan stopped at the cap, so "5,000 match(es)" never reads as an exact total.</summary>
    public string SearchCountSuffix => IsSearchTruncated ? "+" : string.Empty;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));
    partial void OnIsSearchTruncatedChanged(bool value) => OnPropertyChanged(nameof(SearchCountSuffix));

    [RelayCommand]
    private Task FindNextMatchAsync() => StepMatchAsync(+1);

    [RelayCommand]
    private Task FindPreviousMatchAsync() => StepMatchAsync(-1);

    private async Task StepMatchAsync(int delta)
    {
        if (_matchRows.Length == 0) return;
        _currentMatchIndex = (_currentMatchIndex + delta + _matchRows.Length) % _matchRows.Length;
        FocalRow = _matchRows[_currentMatchIndex];
        await RefreshWindowAsync();
    }

    [RelayCommand]
    private async Task ClearSearchAsync()
    {
        var wasPinned      = _restrictRows is not null;
        _matchRows         = [];
        _currentMatchIndex = -1;
        _restrictRows      = null;
        SearchMatchCount   = 0;
        IsSearchTruncated  = false;
        CurrentSearchTerm  = string.Empty;
        IsSearchActive     = false;

        // Un-pinning changes which rows belong in the window; a plain reveal doesn't.
        if (wasPinned) await RefreshWindowAsync();
        else ApplySearchHitsToWindow();
    }
}
