using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.WindowsRegistry.Services;
using Nexaflow.Search;

namespace Nexaflow.Features.WindowsRegistry.ViewModels;

/// <summary>
/// The registry browser as an <see cref="ISearchable"/> page: "?" searches the hive the tab has open —
/// key names, value names and value data — the way regedit's Find does, and the chip steps the tree
/// through the results.
/// <para>
/// A hit is a <b>key</b>, not a value. That is forced by what the page can show: the tree selects a key
/// and the list shows that key's values, so a key is the only thing an id can round-trip into something
/// the user can be taken to. Several matching values in one key therefore collapse to one hit, whose
/// preview names the first — and navigating to it also selects that value row, so the match is on screen
/// and not merely nearby.
/// </para>
/// <para>
/// The scan is bounded and cancellable rather than complete. A walk of HKLM reads millions of values;
/// stopping at a cap and saying so is the honest answer, where a number that quietly meant "the first
/// hundred thousand keys" is not.
/// </para>
/// </summary>
public sealed partial class RegistryViewModel : ISearchable
{
    /// <summary>Keys visited before the walk gives up. Bounds the one operation on this page that can run
    /// for minutes; HKCU fits comfortably, HKLM does not and is told so.</summary>
    private const int KeyVisitCap = 100_000;

    /// <summary>Hits kept. The agent gets a workable set without a tool result the size of a hive.</summary>
    private const int SearchHitCap = 200;

    /// <summary>Full paths of the current hits, in walk order.</summary>
    private string[] _searchHits = [];
    private int _currentHit = -1;

    /// <summary>Which value made each key a hit, so navigating there selects the row that matched rather
    /// than dropping the user at the top of a key with forty values.</summary>
    private readonly Dictionary<string, string?> _hitValue = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private bool _isSearchTruncated;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    public bool HasSearchMatches => SearchMatchCount > 0;

    /// <summary>"+" when the walk stopped at a cap, so a floor never reads as an exact total.</summary>
    public string SearchCountSuffix => IsSearchTruncated ? "+" : string.Empty;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));
    partial void OnIsSearchTruncatedChanged(bool value) => OnPropertyChanged(nameof(SearchCountSuffix));

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        $"the registry under {SearchBaseLabel} — key names, value names and value data";

    private string SearchBaseLabel =>
        _basePath.Length == 0 ? _baseRoot.DisplayName : $"{_baseRoot.Token}\\{_basePath}";

    public async Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        // The registry has key names and value names, not filenames — a glob term has nothing to bind to.
        if (request.HasNameOnlyTerms)
            return SearchOutcome.Unsupported(
                "Filename filters don't apply to the registry — search key names, value names or data.");

        if (!request.TryValidate(out var invalid))
            return SearchOutcome.Unsupported($"Invalid regular expression: {invalid}");

        if (request.Terms.Count == 0)
            return SearchOutcome.Unsupported("Nothing to search for.");

        // Snapshot the base on the calling thread: the hive dropdown can move it, and the walk must not
        // read a root that changed underneath it half way down.
        var root     = _baseRoot;
        var basePath = _basePath;

        var scan = await Task.Run(
            () => RegistrySearchScanner.Scan(root.Key, basePath, request, KeyVisitCap, SearchHitCap, ct), ct);

        ct.ThrowIfCancellationRequested();

        var hits = scan.Matches
            .Select(m => new SearchHit(
                FullPath(root.Token, m.SubPath),
                m.SubPath.Length == 0 ? root.Token : LeafOf(m.SubPath),
                $"{FullPath(root.Token, m.SubPath)} — {m.Preview}"))
            .ToList();

        if (display)
            await _shell.RunOnUiAsync(() =>
            {
                Apply(scan.Matches, root.Token, SearchSyntax.Format(request), scan.Truncated);
                return Task.FromResult(true);
            });

        if (hits.Count == 0)
            return scan.Truncated
                ? SearchOutcome.None($"No matches in the first {scan.KeysVisited:N0} keys under "
                                     + $"{SearchBaseLabel} — the scan stopped there. Navigate deeper and search again.")
                : SearchOutcome.None();

        return scan.Truncated
            // Found, not Narrowed: the hits are real, only the total is a floor.
            ? SearchOutcome.Found(hits, hits.Count,
                $"Stopped after {scan.KeysVisited:N0} keys — there may be more matches under {SearchBaseLabel}.")
            : SearchOutcome.Found(hits);
    }

    /// <summary>Narrows the step-through set to the keys the agent chose and navigates to the first.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var paths = hits.Select(h => h.Id)
                        .Where(p => p.Length > 0 && TrySplit(p, out _, out _))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
        if (paths.Length == 0) return Task.FromResult(false);

        return _shell.RunOnUiAsync(() =>
        {
            _searchHits       = paths;
            _currentHit       = 0;
            SearchMatchCount  = paths.Length;
            IsSearchTruncated = false;
            IsSearchActive    = true;
            if (CurrentSearchTerm.Length == 0) CurrentSearchTerm = $"{paths.Length} selected";
            GoToHit(0);
            return Task.FromResult(true);
        });
    }

    // ── Applying + stepping ───────────────────────────────────────────────────

    private void Apply(
        IReadOnlyList<RegistrySearchScanner.KeyMatch> matches, string token, string term, bool truncated)
    {
        _hitValue.Clear();
        _searchHits = new string[matches.Count];
        for (int i = 0; i < matches.Count; i++)
        {
            var path = FullPath(token, matches[i].SubPath);
            _searchHits[i]   = path;
            _hitValue[path]  = matches[i].ValueName;
        }

        CurrentSearchTerm = term;
        SearchMatchCount  = matches.Count;
        IsSearchTruncated = truncated;
        IsSearchActive    = true;   // true even at zero matches: "no matches for X" is a result to show
        _currentHit       = matches.Count > 0 ? 0 : -1;

        if (_currentHit >= 0) GoToHit(0);
    }

    /// <summary>Navigates the tree to hit <paramref name="index"/> and selects the value that matched.</summary>
    private void GoToHit(int index)
    {
        var path = _searchHits[index];
        NavigateTo(path);

        if (_hitValue.TryGetValue(path, out var valueName) && valueName is not null)
            SelectedValue = Values.FirstOrDefault(
                v => string.Equals(v.RawName, valueName, StringComparison.Ordinal));
    }

    [RelayCommand]
    private void FindNextMatch() => Step(+1);

    [RelayCommand]
    private void FindPreviousMatch() => Step(-1);

    private void Step(int delta)
    {
        if (_searchHits.Length == 0) return;
        _currentHit = ((_currentHit + delta) % _searchHits.Length + _searchHits.Length) % _searchHits.Length;
        GoToHit(_currentHit);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchHits       = [];
        _currentHit       = -1;
        _hitValue.Clear();
        IsSearchActive    = false;
        IsSearchTruncated = false;
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;
    }

    private static string FullPath(string token, string subPath) =>
        subPath.Length == 0 ? token : $"{token}\\{subPath}";
}
