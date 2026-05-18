using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsSearch.Services;
using Nexaflow.Features.WindowsSearch.ViewModels;

namespace Nexaflow.Features.WindowsSearch;

/// <summary>
/// Globally registered query handler.
/// Activates only when the active tab is a Search tab (its ViewModel is <see cref="SearchViewModel"/>).
/// Merges the new input with the original query and re-runs a new Windows Search query
/// so all constraints (original + refinement) are applied together.
/// </summary>
public sealed class SearchRefineQueryHandler : IQueryHandler
{
    public string Symbol => "?";

    public string Description =>
        "Refines the current search by adding more constraints to the existing query. " +
        "Merges with the original search term and re-queries Windows Search.";

    public float CanProcess(string input, IPageViewModel? pageVm = null)
    {
        if (pageVm is not SearchViewModel vm) return 0f;
        if (vm.IsSearching || string.IsNullOrEmpty(vm.SearchRoot)) return 0f;
        return SearchQueryScorer.Score(input);
    }

    public async Task<string?> ProcessAsync(string input, IPageViewModel? pageVm = null)
    {
        if (pageVm is not SearchViewModel vm)
            return "No active Search tab.";

        await vm.MergeAndSearchAsync(input);
        return null;
    }
}
