using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Search;

namespace Nexaflow.Features.Font.ViewModels;

/// <summary>
/// The font comparison list as an <see cref="ISearchable"/> page: "?" matches the name of each font in the
/// list and steps the selection through the hits.
/// <para>
/// It <em>marks and selects</em> rather than filters, which is the one design decision here worth stating.
/// Every other list page in the app answers a search by narrowing itself, but this list is the comparison —
/// the user assembled it deliberately, and a row hidden for not matching takes the thing being compared
/// against off the screen. So a hit gets a wash and the selection, and the rows that missed stay put.
/// </para>
/// </summary>
public sealed partial class FontViewModel : ISearchable
{
    /// <summary>Ids of the current hits, in list order — the 1-based indices <see cref="GetContext"/> and
    /// <see cref="ResolveFont"/> already use, so the agent and the user name a font the same way.</summary>
    private readonly List<int> _searchHits = [];
    private int _currentHit = -1;

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    public bool HasSearchMatches => SearchMatchCount > 0;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription =>
        Fonts.Count == 0
            ? "the font comparison list, by font name (it is currently empty)"
            : $"the {Fonts.Count} font(s) in this comparison list, by font name";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        // A font has a name, not a filename — a glob term has nothing here it could constrain. (Fonts
        // loaded from a file do have a path, but the row is identified by its family name and matching a
        // path the list doesn't show would answer a different question than the one on screen.)
        if (!TextSearchMatcher.TryCreate(request, out var matcher, out var error))
            return Task.FromResult(SearchOutcome.Unsupported(error));

        // Marshalled even when not displaying: Fonts is the bound compare list, and the agent reads it
        // from its own thread.
        return _shell.RunOnUiAsync(() => Task.FromResult(RunSearch(matcher, request, display)));
    }

    private SearchOutcome RunSearch(TextSearchMatcher matcher, SearchRequest request, bool display)
    {
        var hits = new List<int>();
        for (int i = 0; i < Fonts.Count; i++)
            if (matcher.Matches(Fonts[i].DisplayName)) hits.Add(i);

        if (display) Apply(hits, SearchSyntax.Format(request));

        return hits.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(HitsFor(hits));
    }

    /// <summary>Marks exactly the fonts the agent chose and selects the first — the same view change a
    /// user's own search makes, so "I've highlighted those three" is true when it says so.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct) =>
        _shell.RunOnUiAsync(() =>
        {
            var chosen = hits.Select(h => int.TryParse(h.Id, out var n) ? n - 1 : -1)
                             .Where(i => i >= 0 && i < Fonts.Count)
                             .Distinct()
                             .OrderBy(i => i)
                             .ToList();
            if (chosen.Count == 0) return Task.FromResult(false);

            Apply(chosen, CurrentSearchTerm.Length == 0 ? $"{chosen.Count} selected" : CurrentSearchTerm);
            return Task.FromResult(true);
        });

    // ── Chip state + stepping ─────────────────────────────────────────────────

    private void Apply(IReadOnlyList<int> hits, string term)
    {
        _searchHits.Clear();
        _searchHits.AddRange(hits);

        for (int i = 0; i < Fonts.Count; i++)
            Fonts[i].IsSearchHit = _searchHits.Contains(i);

        CurrentSearchTerm = term;
        SearchMatchCount  = _searchHits.Count;
        IsSearchActive    = true;      // a zero-match search is still a result the user should see
        _currentHit       = _searchHits.Count > 0 ? 0 : -1;

        if (_currentHit >= 0) SelectedFont = Fonts[_searchHits[0]];
    }

    [RelayCommand]
    private void FindNextMatch() => Step(+1);

    [RelayCommand]
    private void FindPreviousMatch() => Step(-1);

    private void Step(int delta)
    {
        if (_searchHits.Count == 0) return;
        _currentHit  = ((_currentHit + delta) % _searchHits.Count + _searchHits.Count) % _searchHits.Count;
        SelectedFont = Fonts[_searchHits[_currentHit]];
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchHits.Clear();
        _currentHit = -1;
        foreach (var f in Fonts) f.IsSearchHit = false;
        IsSearchActive    = false;
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;
    }

    private IReadOnlyList<SearchHit> HitsFor(IEnumerable<int> indices) =>
        indices.Select(i => new SearchHit(
                   (i + 1).ToString(),            // 1-based: the id the AI already uses to name a font
                   Fonts[i].DisplayName,
                   Fonts[i].SourceLabel))
               .ToList();
}
