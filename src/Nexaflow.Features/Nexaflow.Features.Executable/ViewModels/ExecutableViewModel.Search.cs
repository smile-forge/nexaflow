using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Executable.Models;
using Nexaflow.Search;

namespace Nexaflow.Features.Executable.ViewModels;

/// <summary>
/// <c>?</c> scoped to the section on screen. The page holds a great deal of unrelated material —
/// header fields, thousands of imports, a whole string table — so searching all of it at once would
/// mostly return matches from a tab the user is not looking at. Searching what is visible is both
/// cheaper and what "search here" means.
/// </summary>
public sealed partial class ExecutableViewModel : ISearchable
{
    private readonly List<object> _searchHits = [];
    private int _currentHit = -1;

    /// <summary>Applied by <see cref="StringView"/>; null means show everything.</summary>
    private Func<InspectorRow, bool>? _stringFilter;

    /// <summary>Raised when the current hit changes so the view can bring it into view — a tinted
    /// row three thousand entries down is not a search result anyone can see.</summary>
    public event Action<object>? ScrollToHitRequested;

    [ObservableProperty] private bool   _isSearchActive;
    [ObservableProperty] private int    _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    public bool HasSearchMatches => SearchMatchCount > 0;

    partial void OnSearchMatchCountChanged(int value) => OnPropertyChanged(nameof(HasSearchMatches));

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription => SelectedSection switch
    {
        Sections.Overview       => $"the headers, sections and version info of '{FileName}'",
        Sections.ImportsExports => $"the imported and exported functions of '{FileName}'",
        Sections.Dependencies   => $"the dependency tree of '{FileName}'",
        Sections.Resources      => $"the resource tree of '{FileName}'",
        Sections.Manifest       => $"the application manifest of '{FileName}'",
        Sections.Dotnet         => $"the .NET metadata and assembly references of '{FileName}'",
        Sections.Strings        => $"the strings embedded in '{FileName}'",
        Sections.Analysis       => $"the signature, entropy and debug analysis of '{FileName}'",
        _                       => $"the PE inspection of '{FileName}'",
    };

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        // A binary's structure has no filenames to judge, so a glob term has nothing to constrain here.
        if (request.HasNameOnlyTerms)
            return Task.FromResult(SearchOutcome.Unsupported(
                "This page inspects one binary's structure; there are no file names to match a glob against."));

        if (!TextSearchMatcher.TryCreate(request, out var matcher, out var error))
            return Task.FromResult(SearchOutcome.Unsupported(error));

        // The bound collections are UI-thread state and the agent calls in from its own thread.
        return _shell.RunOnUiAsync(() => Task.FromResult(RunSearch(matcher, request, display)));
    }

    private SearchOutcome RunSearch(TextSearchMatcher matcher, SearchRequest request, bool display)
    {
        if (_image is null)
            return SearchOutcome.Unsupported("The binary is still being parsed.");

        var hits = Candidates(SelectedSection)
                   .Where(c => matcher.Matches(TextOf(c)))
                   .ToList();

        if (display) Apply(hits, SearchSyntax.Format(request));

        if (hits.Count == 0) return SearchOutcome.None();

        // Ids are content-keyed, not positional, so they survive a re-parse or a refreshed tab.
        var results = hits.Take(200)
                          .Select(c => new SearchHit(IdOf(c), LabelOf(c), DetailOf(c)))
                          .ToList();
        return SearchOutcome.Found(results, hits.Count);
    }

    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct) =>
        _shell.RunOnUiAsync(() =>
        {
            var wanted = hits.Select(h => h.Id).ToHashSet(StringComparer.Ordinal);
            var chosen = Candidates(SelectedSection).Where(c => wanted.Contains(IdOf(c))).ToList();
            if (chosen.Count == 0) return Task.FromResult(false);

            Apply(chosen, CurrentSearchTerm.Length == 0 ? $"{chosen.Count} selected" : CurrentSearchTerm);
            return Task.FromResult(true);
        });

    // ── What each section offers up ───────────────────────────────────────────

    private IEnumerable<object> Candidates(string section) => section switch
    {
        Sections.Overview =>
            OverviewCards.SelectMany(c => c.Rows)
                         .Concat<object>(SectionNodes.SelectMany(n => n.Descend()))
                         .Concat(RelocationRows),

        Sections.ImportsExports =>
            ImportNodes.SelectMany(n => n.Descend()).Concat<object>(ExportRows),

        Sections.Dependencies => DependencyNodes.SelectMany(n => n.Descend()),
        Sections.Resources    => ResourceNodes.SelectMany(n => n.Descend()),
        Sections.Manifest     => ManifestCards.SelectMany(c => c.Rows),
        Sections.Dotnet       => DotnetCards.SelectMany(c => c.Rows),
        Sections.Strings      => _allStrings,
        Sections.Analysis     => AnalysisCards.SelectMany(c => c.Rows),
        _                     => [],
    };

    private static string TextOf(object candidate) => candidate switch
    {
        InspectorRow  row  => row.SearchText,
        InspectorNode node => node.SearchText,
        _                  => string.Empty,
    };

    private static string LabelOf(object candidate) => candidate switch
    {
        InspectorRow  row  => row.Label,
        InspectorNode node => node.Label,
        _                  => string.Empty,
    };

    private static string? DetailOf(object candidate) => candidate switch
    {
        InspectorRow  row  => row.Value.Length > 0 ? row.Value : row.Detail,
        InspectorNode node => node.Detail,
        _                  => null,
    };

    /// <summary>
    /// A stable id for a hit. Keyed on the section and the row's own content rather than its index,
    /// so an id the agent quotes still resolves after the page reloads or a list is refreshed.
    /// </summary>
    private string IdOf(object candidate) => candidate switch
    {
        InspectorRow  row  => $"{SelectedSection}:{row.Label}={row.Value}",
        InspectorNode node => $"{SelectedSection}:{node.Label}",
        _                  => $"{SelectedSection}:?",
    };

    // ── Chip state + stepping ─────────────────────────────────────────────────

    private void Apply(IReadOnlyList<object> hits, string term)
    {
        ClearHitFlags();

        _searchHits.Clear();
        _searchHits.AddRange(hits);

        foreach (var hit in hits)
        {
            switch (hit)
            {
                case InspectorRow row:   row.IsSearchHit = true; break;
                case InspectorNode node: MarkNode(node);         break;
            }
        }

        // The string table is the one section where tinting is useless: there can be a hundred
        // thousand rows, and the matches are what the user asked to see. Narrow it instead.
        if (SelectedSection == Sections.Strings)
        {
            var matches = hits.OfType<InspectorRow>().ToHashSet();
            _stringFilter = matches.Count > 0 ? matches.Contains : null;
            StringView?.Refresh();
        }

        CurrentSearchTerm = term;
        SearchMatchCount  = hits.Count;
        IsSearchActive    = true;    // a zero-match search is still a result worth showing
        _currentHit       = hits.Count > 0 ? 0 : -1;

        if (_currentHit >= 0) ScrollToHitRequested?.Invoke(_searchHits[0]);
    }

    /// <summary>Marking a node also expands its ancestors, or a hit deep in a collapsed tree is
    /// reported and then invisible.</summary>
    private void MarkNode(InspectorNode node)
    {
        node.IsSearchHit = true;
        foreach (var root in AllTrees())
            if (Expand(root, node)) break;
    }

    private bool Expand(InspectorNode current, InspectorNode target)
    {
        if (ReferenceEquals(current, target)) return true;
        foreach (var child in current.Children)
        {
            if (!Expand(child, target)) continue;
            current.IsExpanded = true;
            return true;
        }
        return false;
    }

    private IEnumerable<InspectorNode> AllTrees()
        => SectionNodes.Concat(ImportNodes).Concat(ResourceNodes).Concat(DependencyNodes);

    private void ClearHitFlags()
    {
        foreach (var card in OverviewCards.Concat(ManifestCards).Concat(DotnetCards).Concat(AnalysisCards))
            foreach (var row in card.Rows) row.IsSearchHit = false;

        foreach (var row in ExportRows)     row.IsSearchHit = false;
        foreach (var row in _allStrings)    row.IsSearchHit = false;
        foreach (var row in RelocationRows) row.IsSearchHit = false;

        foreach (var node in AllTrees().SelectMany(n => n.Descend())) node.IsSearchHit = false;
    }

    [RelayCommand]
    private void FindNextMatch() => Step(+1);

    [RelayCommand]
    private void FindPreviousMatch() => Step(-1);

    /// <summary>Steps the selection through the hits. Only tree hits can be selected; row hits stay
    /// washed, which is the whole feedback for a card.</summary>
    private void Step(int delta)
    {
        if (_searchHits.Count == 0) return;

        _currentHit = ((_currentHit + delta) % _searchHits.Count + _searchHits.Count) % _searchHits.Count;

        var hit = _searchHits[_currentHit];
        if (hit is InspectorNode node) node.IsSelected = true;
        ScrollToHitRequested?.Invoke(hit);
    }

    [RelayCommand]
    private void ClearSearch()
    {
        ClearHitFlags();
        _searchHits.Clear();
        _currentHit       = -1;
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;
        IsSearchActive    = false;

        if (_stringFilter is not null)
        {
            _stringFilter = null;
            StringView?.Refresh();
        }
    }
}
