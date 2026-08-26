using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Search;

namespace Nexaflow.Features.SystemInfo.ViewModels;

/// <summary>
/// The Services page as an <see cref="ISearchable"/> page: "?" drives the filter box the tab already had,
/// over the same two fields (service name and display name), but parsed by the shared query parser — so
/// regex, quoted phrases and prefix wildcards mean here what they mean in a text tab.
/// <para>
/// Reusing the box rather than growing a second search is the point. Two searches over one list, each with
/// its own rules, put two different answers on one screen; this way the query the AI ran is visible in the
/// box, dismissible from the chip, and dropped the moment the user types over it.
/// </para>
/// </summary>
public sealed partial class ServicesViewModel : ISearchable
{
    /// <summary>The compiled query behind the filter box, or null when the box holds plain typed text.</summary>
    private SearchRequest? _filterRequest;

    /// <summary>Service names the agent pinned via <see cref="ShowResultsAsync"/>, or null.</summary>
    private HashSet<string>? _pinnedNames;

    /// <summary>True while the code is writing the box, so <see cref="OnFilterTextChanged"/> doesn't read
    /// its own write as the user typing and drop the query behind it.</summary>
    private bool _suppressFilterReset;

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>Whether the chip should offer previous/next. Always false: this page answers by filtering,
    /// so every row still on screen is a match and stepping has nowhere to go.</summary>
    public bool HasSearchMatches => false;

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        "the Windows services on this machine, by service name or display name";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        // Services have names, not files — a glob term has nothing here it could constrain.
        if (request.HasNameOnlyTerms)
            return Task.FromResult(SearchOutcome.Unsupported(
                "Filename filters don't apply to the service list — search by service or display name."));

        if (!request.TryValidate(out var invalid))
            return Task.FromResult(SearchOutcome.Unsupported($"Invalid regular expression: {invalid}"));

        if (request.Terms.Count == 0)
            return Task.FromResult(SearchOutcome.Unsupported("Nothing to search for."));

        // Marshalled even when not displaying: Services is the bound row list, and the agent reads it from
        // its own thread.
        return _shell.RunOnUiAsync(() => Task.FromResult(RunSearch(request, display)));
    }

    private SearchOutcome RunSearch(SearchRequest request, bool display)
    {
        var matches = Services.Where(r => request.Matches(r.Name) || request.Matches(r.DisplayName)).ToList();

        if (display) ApplyFilter(request, null, SearchSyntax.Format(request), matches.Count);

        return matches.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(HitsFor(matches));
    }

    /// <summary>Pins the list to exactly the services the agent chose, by service name.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var names = hits.Select(h => h.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return Task.FromResult(false);

        return _shell.RunOnUiAsync(() =>
        {
            // Decline rather than pin to nothing. The agent composes these ids freely, so ones this list
            // does not hold DO arrive; filtering first and reporting success afterwards leaves the user
            // looking at an empty list that the assistant has just told them is their search result.
            var kept = Services.Count(r => names.Contains(r.Name));
            if (kept == 0) return Task.FromResult(false);

            ApplyFilter(null, names, $"{kept} selected", kept);
            return Task.FromResult(true);
        });
    }

    // ── The filter the box and "?" share ──────────────────────────────────────

    private void ApplyFilter(SearchRequest? request, HashSet<string>? pinned, string boxText, int count)
    {
        _suppressFilterReset = true;
        FilterText     = boxText;
        _filterRequest = request;
        _pinnedNames   = pinned;
        _suppressFilterReset = false;

        CurrentSearchTerm = boxText;
        SearchMatchCount  = count;
        IsSearchActive    = true;      // a zero-match search is still a result the user should see
        ServicesView.Refresh();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _suppressFilterReset = true;
        FilterText = string.Empty;
        _suppressFilterReset = false;

        ClearSearchState();
        ServicesView.Refresh();
    }

    /// <summary>Drops the query behind the box and the chip, without touching the box's text — so the
    /// user typing over an AI-run search keeps what they typed.</summary>
    private void ClearSearchState()
    {
        _filterRequest    = null;
        _pinnedNames      = null;
        IsSearchActive    = false;
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;
    }

    /// <summary>Declared rather than omitted so the chip's bindings resolve instead of failing silently.
    /// A filtering page has no "next match" — see <see cref="HasSearchMatches"/>.</summary>
    [RelayCommand] private void FindNextMatch() { }

    [RelayCommand] private void FindPreviousMatch() { }

    private static IReadOnlyList<SearchHit> HitsFor(IEnumerable<ServiceRow> rows) =>
        rows.Select(r => new SearchHit(
                r.Name,                 // the id the service tools already speak
                r.DisplayName,
                $"{r.Name} — {r.Status}, {r.StartMode}"))
            .ToList();
}
