using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Features.Projects.ViewModels;

/// <summary>
/// The project list as an <see cref="ISearchable"/> page: "?" matches a project's name, its folder name
/// and its description, and narrows the list to what matched.
/// <para>
/// The description is in on purpose. A project's name is a folder someone picked in a hurry; what the
/// project is actually about is written in the description, and "the one about the invoice importer" is
/// how people look for it. Matching only names would answer a question nobody asks.
/// </para>
/// <para>
/// Scoped to the bucket on screen — Projects, Shelf or Archives. The page shows one at a time, so
/// searching the others would report matches the user cannot see; switching bucket drops the search.
/// </para>
/// </summary>
public partial class ProjectsViewModel : ISearchable
{
    private ICollectionView? _searchView;
    private SearchRequest? _searchRequest;
    private HashSet<string>? _pinnedFolders;

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>Whether the chip should offer previous/next. Always false: this page answers by filtering,
    /// so every row still on screen is a match and stepping has nowhere to go.</summary>
    public bool HasSearchMatches => false;

    /// <summary>The list's own view — the one the <c>ItemsSource="{Binding Projects}"</c> binding resolves
    /// to — with this page's filter attached the first time it is needed.</summary>
    private ICollectionView SearchView =>
        _searchView ??= Attach(CollectionViewSource.GetDefaultView(Projects));

    private ICollectionView Attach(ICollectionView view)
    {
        view.Filter = o => o is ProjectSummaryItem p && Passes(p);
        return view;
    }

    private bool Passes(ProjectSummaryItem p)
    {
        if (_pinnedFolders is not null) return _pinnedFolders.Contains(p.FolderName);
        if (_searchRequest is not { } r) return true;
        return Matches(r, p);
    }

    private static bool Matches(SearchRequest r, ProjectSummaryItem p) =>
        r.Matches(p.DisplayName) || r.Matches(p.FolderName) || r.Matches(p.Description);

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        $"the {SelectedBucket} projects, by project name, folder name or description";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        // A project is a folder with a name, not a file — a filename glob has nothing here to constrain.
        if (request.HasNameOnlyTerms)
            return OnUi(() => Task.FromResult(SearchOutcome.Unsupported(
                "Filename filters don't apply to the project list — search by project name or description.")));

        if (!request.TryValidate(out var invalid))
            return OnUi(() => Task.FromResult(SearchOutcome.Unsupported($"Invalid regular expression: {invalid}")));

        if (request.Terms.Count == 0)
            return OnUi(() => Task.FromResult(SearchOutcome.Unsupported("Nothing to search for.")));

        // Marshalled even when not displaying: Projects is the bound row list, and the agent reads it
        // from its own thread.
        return OnUi(() => Task.FromResult(RunSearch(request, display)));
    }

    private SearchOutcome RunSearch(SearchRequest request, bool display)
    {
        if (!IsEnabled)
            return SearchOutcome.None("The Projects feature is disabled for this workspace.");

        var matches = Projects.Where(p => Matches(request, p)).ToList();

        if (display) Apply(request, null, SearchSyntax.Format(request), matches.Count);

        return matches.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(HitsFor(matches));
    }

    /// <summary>Narrows the list to exactly the projects the agent chose, by folder name.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var folders = hits.Select(h => h.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (folders.Count == 0) return Task.FromResult(false);

        return OnUi(() =>
        {
            var kept = Projects.Count(p => folders.Contains(p.FolderName));
            if (kept == 0) return Task.FromResult(false);

            Apply(null, folders, CurrentSearchTerm.Length == 0 ? $"{kept} selected" : CurrentSearchTerm, kept);
            return Task.FromResult(true);
        });
    }

    // ── Applying ──────────────────────────────────────────────────────────────

    private void Apply(SearchRequest? request, HashSet<string>? pinned, string term, int count)
    {
        _searchRequest = request;
        _pinnedFolders = pinned;
        SearchView.Refresh();

        CurrentSearchTerm = term;
        SearchMatchCount  = count;
        IsSearchActive    = true;      // a zero-match search is still a result the user should see

        // Keep the summary pane on something that is still on screen: a selection filtered away leaves the
        // pane describing a project the list no longer shows.
        if (SelectedProject is null || !Passes(SelectedProject))
            SelectedProject = Projects.FirstOrDefault(Passes);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchRequest = null;
        _pinnedFolders = null;
        if (_searchView is not null) _searchView.Refresh();

        IsSearchActive    = false;
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;
    }

    /// <summary>Declared rather than omitted so the chip's bindings resolve instead of failing silently.
    /// A filtering page has no "next match" — see <see cref="HasSearchMatches"/>.</summary>
    [RelayCommand] private void FindNextMatch() { }

    [RelayCommand] private void FindPreviousMatch() { }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The shell is optional here (the list runs without one in tests), so a missing one means
    /// "already on the right thread" rather than "can't search".</summary>
    private Task<T> OnUi<T>(Func<Task<T>> work) => _shell?.RunOnUiAsync(work) ?? work();

    private static IReadOnlyList<SearchHit> HitsFor(IEnumerable<ProjectSummaryItem> items) =>
        items.Select(p => new SearchHit(
                  p.FolderName,        // the id read_project already speaks
                  p.DisplayName,
                  string.IsNullOrWhiteSpace(p.DescriptionPreview) ? p.CountsText : p.DescriptionPreview))
             .ToList();
}
