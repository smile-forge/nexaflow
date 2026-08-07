using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.ProductManager.Services;
using Nexaflow.Search;

namespace Nexaflow.Features.ProductManager.ViewModels;

/// <summary>
/// The results page answering <c>?</c> itself — a second search runs here rather than opening a third tab.
/// <para>
/// Both halves of the page honour the whole query: a pattern is compiled once and used for the node names
/// and the source lines alike, and a case-sensitive request re-filters the graph's always-insensitive grep
/// rather than quietly ignoring the flag (see <see cref="GraphTextSearch"/>).
/// </para>
/// </summary>
public sealed partial class ProductSearchViewModel : ISearchable
{
    public string SearchTargetDescription =>
        $"the knowledge graph of '{System.IO.Path.GetFileName(_productRoot.TrimEnd('\\', '/'))}' — "
      + "node names (features, types, members, files) and the source behind them";

    /// <summary>Whether the chip should offer previous/next. Always false: the page IS the result list, so
    /// every row is a match and there is nowhere to step to.</summary>
    public bool HasSearchMatches => false;

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    public async Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (request.HasNameOnlyTerms)
            return SearchOutcome.Unsupported(
                "Filename filters don't apply to the graph — search a node name or a word in the source.");

        if (request.Terms.Count == 0 || request.Text.Trim().Length == 0)
            return SearchOutcome.Unsupported("Nothing to search for.");

        if (GraphTextSearch.TryCompile(request) is null)
            return SearchOutcome.Unsupported($"Invalid regular expression: {request.Text}");

        var root = _productRoot;

        if (display)
        {
            Query = request.Text.Trim();
            var shown = await RunAndShowAsync(request, ct);
            CurrentSearchTerm = SearchSyntax.Format(request);
            SearchMatchCount  = Results.Count;
            IsSearchActive    = true;      // a zero-match search is still a result the user should see
            return Rows(shown);
        }

        // Not displaying: run the same passes without touching the page the user is looking at.
        var found = await Task.Run(() => Run(root, request, ct), ct).ConfigureAwait(false);
        return Rows(found.Rows);
    }

    private static SearchOutcome Rows(IReadOnlyCollection<ProductSearchRow> rows) =>
        rows.Count == 0
            ? SearchOutcome.None()
            : SearchOutcome.Found(rows.Select(r => new SearchHit(
                  r.NodeId,                                     // the id every graph tool takes
                  $"[{r.Kind}] {r.Label}",
                  r.IsSourceHit ? $"{r.Detail} — {r.Text}" : r.Detail)).ToList());

    /// <summary>Narrows the page to the rows the agent kept, by node id.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var ids = hits.Select(h => h.Id).ToHashSet(System.StringComparer.Ordinal);
        if (ids.Count == 0) return Task.FromResult(false);

        var kept = Results.Where(r => ids.Contains(r.NodeId)).ToList();
        if (kept.Count == 0) return Task.FromResult(false);

        Results.Clear();
        foreach (var row in kept) Results.Add(row);

        SearchMatchCount = Results.Count;
        IsSearchActive   = true;
        if (CurrentSearchTerm.Length == 0) CurrentSearchTerm = $"{kept.Count} selected";
        StatusText = $"{kept.Count} result(s) kept by the assistant.";
        return Task.FromResult(true);
    }

    /// <summary>Dismisses the chip and puts the full result set back by re-running the query — the rows the
    /// agent dropped are gone from the list, so there is nothing to un-hide.</summary>
    [RelayCommand]
    private async Task ClearSearch()
    {
        IsSearchActive    = false;
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;
        await SearchAsync();
    }

    /// <summary>Declared rather than omitted so the chip's bindings resolve instead of failing silently.
    /// The page is the result list — see <see cref="HasSearchMatches"/>.</summary>
    [RelayCommand] private void FindNextMatch() { }

    [RelayCommand] private void FindPreviousMatch() { }
}
