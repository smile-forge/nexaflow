using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.SystemInfo.Models;
using Nexaflow.Search;

namespace Nexaflow.Features.SystemInfo.ViewModels;

/// <summary>
/// The Environment Variables page as an <see cref="ISearchable"/> page: "?" matches a variable's <b>name
/// or its value</b> and drives the filter box the tab already had — parsed by the shared query parser, so
/// regex, quoted phrases and prefix wildcards mean here what they mean everywhere else.
/// <para>
/// The value is in because it is what people are looking for: "which variable still has the old SDK path
/// in it" is the question this page exists to answer, and the names alone cannot. The typed box matches
/// the same two fields for the same reason — one box filtering by a narrower rule than "?" would have the
/// same string give two different answers depending on where it was typed.
/// </para>
/// <para>
/// A row whose <em>value</em> matched looks like any other in a list of names, which is why the hit
/// preview carries the value: the agent can see why it matched without a second call, and selecting the
/// row shows it to the user.
/// </para>
/// <para>
/// Scoped to the scope on screen. The page shows User or Machine, never both, so searching the other one
/// would report matches on rows the user cannot see; switching scope drops the search instead.
/// </para>
/// </summary>
public sealed partial class EnvironmentVariablesViewModel : ISearchable
{
    /// <summary>The compiled query behind the filter box, or null when the box holds plain typed text.</summary>
    private SearchRequest? _filterRequest;

    /// <summary>Variable names the agent pinned via <see cref="ShowResultsAsync"/>, or null.</summary>
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
        $"the {SelectedScope} environment variables on this machine, by variable name or value";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        // Variables have names, not files — a glob term has nothing here it could constrain. (A PATH entry
        // looks like a filename, but it is one value among many inside one variable, not the row's identity.)
        if (request.HasNameOnlyTerms)
            return Task.FromResult(SearchOutcome.Unsupported(
                "Filename filters don't apply to environment variables — search by variable name or value."));

        if (!request.TryValidate(out var invalid))
            return Task.FromResult(SearchOutcome.Unsupported($"Invalid regular expression: {invalid}"));

        if (request.Terms.Count == 0)
            return Task.FromResult(SearchOutcome.Unsupported("Nothing to search for."));

        // Marshalled even when not displaying: Variables is the bound row list, and the agent reads it
        // from its own thread.
        return _shell.RunOnUiAsync(() => Task.FromResult(RunSearch(request, display)));
    }

    private SearchOutcome RunSearch(SearchRequest request, bool display)
    {
        var matches = Variables.Where(r => request.Matches(r.Name) || request.Matches(r.Value)).ToList();

        if (display) ApplyFilter(request, null, SearchSyntax.Format(request), matches.Count);

        return matches.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(HitsFor(matches));
    }

    /// <summary>Pins the list to exactly the variables the agent chose, by name.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var names = hits.Select(h => h.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (names.Count == 0) return Task.FromResult(false);

        return _shell.RunOnUiAsync(() =>
        {
            // See ServicesViewModel: narrowing to nothing and then claiming success is the failure mode.
            var kept = Variables.Count(r => names.Contains(r.Name));
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
        VariablesView.Refresh();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _suppressFilterReset = true;
        FilterText = string.Empty;
        _suppressFilterReset = false;

        ClearSearchState();
        VariablesView.Refresh();
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

    private static IReadOnlyList<SearchHit> HitsFor(IEnumerable<EnvVarRow> rows) =>
        rows.Select(r => new SearchHit(
                r.Name,                 // the id the environment tools already speak
                r.Name,
                Preview(r.Value)))
            .ToList();

    private static string Preview(string value) =>
        value.Length <= 120 ? value : value[..120] + "…";
}
