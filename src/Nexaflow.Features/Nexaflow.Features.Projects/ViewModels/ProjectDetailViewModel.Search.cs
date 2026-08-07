using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Features.Projects.ViewModels;

/// <summary>
/// The project detail tab as an <see cref="ISearchable"/> page: "?" searches the <b>backlog</b> — each
/// item's title and its markdown detail — and narrows the board to what matched.
/// <para>
/// The backlog and not the description, because the backlog is the list: it is the thing that grows to
/// eighty items and needs finding things in, where the description is one document already on screen.
/// A search run from the Project Details tab therefore switches to Backlog — answering a question about
/// a list the user cannot see would be worse than not answering it.
/// </para>
/// </summary>
public partial class ProjectDetailViewModel : ISearchable
{
    /// <summary>Index of the Backlog tab — the one "?" searches.</summary>
    private const int BacklogTab = 1;

    private ICollectionView? _searchView;
    private SearchRequest? _searchRequest;
    private HashSet<Guid>? _pinnedIds;

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>Whether the chip should offer previous/next. Always false: this page answers by filtering,
    /// so every item still on the board is a match and stepping has nowhere to go.</summary>
    public bool HasSearchMatches => false;

    /// <summary>The board's own view — the one the <c>ItemsSource="{Binding Backlog}"</c> binding resolves
    /// to — with this page's filter attached the first time it is needed.</summary>
    private ICollectionView SearchView =>
        _searchView ??= Attach(CollectionViewSource.GetDefaultView(Backlog));

    private ICollectionView Attach(ICollectionView view)
    {
        view.Filter = o => o is BacklogItemViewModel b && Passes(b);
        return view;
    }

    private bool Passes(BacklogItemViewModel b)
    {
        if (_pinnedIds is not null) return _pinnedIds.Contains(b.Id);
        if (_searchRequest is not { } r) return true;
        return Matches(r, b);
    }

    private static bool Matches(SearchRequest r, BacklogItemViewModel b) =>
        r.Matches(b.Title) || r.Matches(b.Description);

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        Backlog.Count == 0
            ? $"the backlog of project '{ProjectName}' (it is currently empty)"
            : $"the {Backlog.Count} backlog item(s) of project '{ProjectName}', by title or detail";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        // Backlog items have titles, not filenames — a glob term has nothing here it could constrain.
        if (request.HasNameOnlyTerms)
            return OnUi(() => Task.FromResult(SearchOutcome.Unsupported(
                "Filename filters don't apply to a backlog — search by item title or detail.")));

        if (!request.TryValidate(out var invalid))
            return OnUi(() => Task.FromResult(SearchOutcome.Unsupported($"Invalid regular expression: {invalid}")));

        if (request.Terms.Count == 0)
            return OnUi(() => Task.FromResult(SearchOutcome.Unsupported("Nothing to search for.")));

        // Marshalled even when not displaying: Backlog is the bound item list, and the agent reads it
        // from its own thread.
        return OnUi(() => Task.FromResult(RunSearch(request, display)));
    }

    private SearchOutcome RunSearch(SearchRequest request, bool display)
    {
        if (Backlog.Count == 0)
            return SearchOutcome.None($"Project '{ProjectName}' has no backlog items to search.");

        var matches = Backlog.Where(b => Matches(request, b)).ToList();

        if (display) Apply(request, null, SearchSyntax.Format(request), matches.Count);

        return matches.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(HitsFor(matches));
    }

    /// <summary>Narrows the board to exactly the items the agent chose, by id.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var ids = hits.Select(h => Guid.TryParse(h.Id, out var g) ? g : Guid.Empty)
                      .Where(g => g != Guid.Empty)
                      .ToHashSet();
        if (ids.Count == 0) return Task.FromResult(false);

        return OnUi(() =>
        {
            var kept = Backlog.Count(b => ids.Contains(b.Id));
            if (kept == 0) return Task.FromResult(false);

            Apply(null, ids, CurrentSearchTerm.Length == 0 ? $"{kept} selected" : CurrentSearchTerm, kept);
            return Task.FromResult(true);
        });
    }

    // ── Applying ──────────────────────────────────────────────────────────────

    private void Apply(SearchRequest? request, HashSet<Guid>? pinned, string term, int count)
    {
        _searchRequest = request;
        _pinnedIds     = pinned;
        SearchView.Refresh();

        CurrentSearchTerm = term;
        SearchMatchCount  = count;
        IsSearchActive    = true;      // a zero-match search is still a result the user should see
        SelectedTabIndex  = BacklogTab;

        // Keep the editor on something still on the board: an item filtered away leaves the right pane
        // editing something the list no longer shows.
        if (SelectedItem is null || !Passes(SelectedItem))
            SelectedItem = Backlog.FirstOrDefault(Passes);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchRequest = null;
        _pinnedIds     = null;
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

    /// <summary>The shell is optional here (the page runs without one in tests), so a missing one means
    /// "already on the right thread" rather than "can't search".</summary>
    private Task<T> OnUi<T>(Func<Task<T>> work) => _shell?.RunOnUiAsync(work) ?? work();

    private static IReadOnlyList<SearchHit> HitsFor(IEnumerable<BacklogItemViewModel> items) =>
        items.Select(b => new SearchHit(
                  b.Id.ToString(),     // the id read_backlog_item already speaks
                  b.Title,
                  $"[{b.StatusLabel}] {Preview(b.Description)}"))
             .ToList();

    private static string Preview(string detail)
    {
        var flat = detail.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 120 ? flat : flat[..120] + "…";
    }
}
