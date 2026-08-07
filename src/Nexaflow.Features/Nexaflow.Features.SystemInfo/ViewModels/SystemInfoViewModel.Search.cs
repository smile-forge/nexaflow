using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Search;

namespace Nexaflow.Features.SystemInfo.ViewModels;

/// <summary>
/// The device dashboard as an <see cref="ISearchable"/> page: "?" matches the facts on the cards — each
/// row's label and its value — and marks the ones that hit.
/// <para>
/// Marking is the whole answer here, and deliberately so. The dashboard is a fixed set of cards the user
/// is reading, not a list they are looking through: hiding the rows that missed would take away the card
/// they are being shown, and there is no selection to step. So a hit gets a wash and the chip gets a
/// count — "where does this machine mention hyper-v" answered in place.
/// </para>
/// </summary>
public sealed partial class SystemInfoViewModel : ISearchable
{
    /// <summary>Ids of the current hits — <c>"Section/Label"</c>, the way a person would name a fact.</summary>
    private readonly HashSet<string> _searchHits = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>Whether the chip should offer previous/next. Always false: every hit is already marked and
    /// on screen, and the dashboard has no selection to move through them with.</summary>
    public bool HasSearchMatches => false;

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        Sections.Count == 0
            ? "this machine's device summary (still gathering)"
            : $"the {Sections.Count} device-summary cards on this page, by fact label or value";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!TextSearchMatcher.TryCreate(request, out var matcher, out var error))
            return Task.FromResult(SearchOutcome.Unsupported(error));

        // Marshalled even when not displaying: Sections is the bound card list, and the agent reads it
        // from its own thread.
        return _shell.RunOnUiAsync(() => Task.FromResult(RunSearch(matcher, request, display)));
    }

    private SearchOutcome RunSearch(TextSearchMatcher matcher, SearchRequest request, bool display)
    {
        if (Sections.Count == 0)
            return SearchOutcome.None(IsLoading
                ? "The device summary is still being gathered — try again in a moment."
                : "There is no device summary to search.");

        var hits = Facts()
            .Where(f => matcher.Matches(f.Item.Label) || matcher.Matches(f.Item.Value))
            .ToList();

        if (display) Apply(hits.Select(Id), SearchSyntax.Format(request));

        return hits.Count == 0
            ? SearchOutcome.None()
            : SearchOutcome.Found(hits.Select(f => new SearchHit(Id(f), f.Item.Label,
                                                                $"{f.Section}: {f.Item.Value}")).ToList());
    }

    /// <summary>Marks exactly the facts the agent chose — the same view change the user's own search
    /// makes, so "I've highlighted those three" is true when it says so.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct) =>
        _shell.RunOnUiAsync(() =>
        {
            var known = Facts().Select(Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var chosen = hits.Select(h => h.Id).Where(known.Contains).ToList();
            if (chosen.Count == 0) return Task.FromResult(false);

            Apply(chosen, CurrentSearchTerm.Length == 0 ? $"{chosen.Count} selected" : CurrentSearchTerm);
            return Task.FromResult(true);
        });

    // ── Marking ───────────────────────────────────────────────────────────────

    private void Apply(IEnumerable<string> hitIds, string term)
    {
        _searchHits.Clear();
        foreach (var id in hitIds) _searchHits.Add(id);

        foreach (var fact in Facts()) fact.Item.IsSearchHit = _searchHits.Contains(Id(fact));

        CurrentSearchTerm = term;
        SearchMatchCount  = _searchHits.Count;
        IsSearchActive    = true;      // a zero-match search is still a result the user should see
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchHits.Clear();
        foreach (var fact in Facts()) fact.Item.IsSearchHit = false;
        IsSearchActive    = false;
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;
    }

    /// <summary>Declared rather than omitted so the chip's bindings resolve instead of failing silently.
    /// There is nothing to step to — see <see cref="HasSearchMatches"/>.</summary>
    [RelayCommand] private void FindNextMatch() { }

    [RelayCommand] private void FindPreviousMatch() { }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private IEnumerable<(string Section, SystemInfoItem Item)> Facts() =>
        Sections.SelectMany(s => s.Items.Select(i => (s.Title, i)));

    private static string Id((string Section, SystemInfoItem Item) fact) =>
        $"{fact.Section}/{fact.Item.Label}";
}
