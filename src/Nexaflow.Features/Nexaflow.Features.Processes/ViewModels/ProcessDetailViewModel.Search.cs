using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.Processes.Models;
using Nexaflow.Search;

namespace Nexaflow.Features.Processes.ViewModels;

/// <summary>
/// The per-process inspector as an <see cref="ISearchable"/> page. Unlike every other searchable page this
/// one has no single body: it is five sub-tabs showing five different things, so "?" is scoped to the
/// section the user is actually looking at — TIDs on Threads, module name and path on Modules, the handle
/// name on Handles.
/// <para>
/// Scoping to the visible section is what makes a single query box work here at all. A search that swept
/// all three lists would report matches on tabs the user can't see, and the numbers would be the sum of
/// three unrelated things. Switching section therefore drops the search rather than carrying it over: a
/// pattern that meant a TID means nothing against a module path.
/// </para>
/// <para>
/// It answers by <em>filtering</em>, like the process list and the app list — the lists are small, fully
/// loaded, and the useful answer to "?ntdll" on Modules is the four rows that match, not a wash on four
/// rows among two hundred.
/// </para>
/// </summary>
public sealed partial class ProcessDetailViewModel : ISearchable
{
    /// <summary>The <c>Tag</c> values on the view's TabItems — bound through <c>SelectedValuePath</c>, so
    /// reordering the tabs can't silently re-point the search at a different list.</summary>
    internal static class Sections
    {
        public const string General     = "General";
        public const string Performance = "Performance";
        public const string Threads     = "Threads";
        public const string Modules     = "Modules";
        public const string Handles     = "Handles";
    }

    /// <summary>Which sub-tab is on screen. Two-way from the TabControl.</summary>
    [ObservableProperty] private string _selectedSection = Sections.General;

    // The live search: the rule, the section it was scoped to, and (once the agent has narrowed) the exact
    // rows it chose. Null section means nothing is searching.
    private SearchRequest?   _sectionRequest;
    private HashSet<string>? _pinnedIds;
    private string?          _searchSection;

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>
    /// Whether the chip should offer previous/next. Always false here: this page answers a search by
    /// filtering, so every row still on screen is a match and there is nowhere to step to. The chip keeps
    /// its count and its dismiss button.
    /// </summary>
    public bool HasSearchMatches => false;

    partial void OnSelectedSectionChanged(string value)
    {
        // A search belongs to the list it was run against. Carrying "4242" from Threads onto Modules would
        // filter module names by a thread id and show an empty tab with no explanation.
        if (_searchSection is not null && _searchSection != value) ClearSearch();
    }

    // ── ISearchable ───────────────────────────────────────────────────────────

    public string SearchTargetDescription => SelectedSection switch
    {
        Sections.Threads => $"the threads of {ProcessLabel}, by thread id",
        Sections.Modules => $"the modules loaded by {ProcessLabel}, by module name and path",
        Sections.Handles => $"the open handles of {ProcessLabel}, by handle name",
        _                => $"the {SelectedSection} tab of {ProcessLabel} — switch to Threads, Modules or "
                          + "Handles to search a list",
    };

    private string ProcessLabel => Detail is { } d ? $"{d.Name} (PID {d.Pid})" : $"PID {Pid}";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (request.HasNameOnlyTerms)
            return Task.FromResult(SearchOutcome.Unsupported(
                "Filename filters don't apply here — search the thread, module or handle lists as text."));

        if (!request.TryValidate(out var invalid))
            return Task.FromResult(SearchOutcome.Unsupported($"Invalid regular expression: {invalid}"));

        return _shell.RunOnUiAsync(() => Task.FromResult(RunSearch(request, display)));
    }

    private SearchOutcome RunSearch(SearchRequest request, bool display)
    {
        var section = SelectedSection;

        // Not Unsupported: the search itself is fine, there is simply no list on this tab. Reporting it as
        // a failure would read as "this page can't do regex", which is a different and untrue claim.
        if (section is not (Sections.Threads or Sections.Modules or Sections.Handles))
            return SearchOutcome.None(
                $"The {section} tab has no list to search. Switch to Threads, Modules or Handles.");

        if (section == Sections.Handles && !HandlesLoaded)
            return SearchOutcome.None(
                "Handles haven't been loaded yet — they need one administrator-approved read. "
                + "Use 'Load handles (admin)' on the Handles tab first.");

        var hits = MatchesIn(section, request);

        if (display) Apply(section, request, pinned: null, SearchSyntax.Format(request), hits.Count);

        return hits.Count == 0 ? SearchOutcome.None() : SearchOutcome.Found(hits);
    }

    /// <summary>Narrows the section's list to exactly the rows the agent chose.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var ids = hits.Select(h => h.Id).Where(id => id.Length > 0).ToHashSet(StringComparer.Ordinal);
        if (ids.Count == 0) return Task.FromResult(false);

        return _shell.RunOnUiAsync(() =>
        {
            var section = SelectedSection;
            if (section is not (Sections.Threads or Sections.Modules or Sections.Handles))
                return Task.FromResult(false);

            // Count what this section actually holds before pinning. Two things were wrong with taking
            // ids.Count on trust: ids the list does not have pinned it to an empty view while returning
            // true, and the chip read "5 selected" over three rows whenever the agent's set was stale.
            var kept = RowsIn(section).Count(ids.Contains);
            if (kept == 0) return Task.FromResult(false);

            Apply(section, request: null, pinned: ids, $"{kept} selected", kept);
            return Task.FromResult(true);
        });
    }

    // ── Matching ──────────────────────────────────────────────────────────────

    private List<SearchHit> MatchesIn(string section, SearchRequest request) => section switch
    {
        Sections.Threads => Threads.Where(t => request.Matches(t.Tid.ToString()))
                                   .Select(t => new SearchHit(ThreadId(t), $"TID {t.Tid}",
                                                              $"{t.State} · {t.Priority} · {t.CpuTimeText}"))
                                   .ToList(),

        Sections.Modules => Modules.Where(m => request.Matches(m.Name) || request.Matches(m.Path))
                                   .Select(m => new SearchHit(ModuleId(m), m.Name, m.Path))
                                   .ToList(),

        Sections.Handles => Handles.Where(h => request.Matches(h.Name))
                                   .Select(h => new SearchHit(HandleId(h), h.Name, $"{h.Type} {h.HandleValue}"))
                                   .ToList(),

        _ => [],
    };

    /// <summary>The ids currently in a section, in the same key space <see cref="MatchesIn"/> hands out.</summary>
    private IEnumerable<string> RowsIn(string section) => section switch
    {
        Sections.Threads => Threads.Select(ThreadId),
        Sections.Modules => Modules.Select(ModuleId),
        Sections.Handles => Handles.Select(HandleId),
        _                => [],
    };

    // Ids are the same keys ProcessDetailViewModel.Reconcile uses, so a hit survives a refresh tick
    // replacing the row instance underneath it.
    private static string ThreadId(ThreadInfo t) => t.Tid.ToString();
    private static string ModuleId(ModuleInfo m) => $"{m.Name}|{m.Path}";
    private static string HandleId(HandleInfo h) => $"{h.HandleValue}|{h.Type}";

    /// <summary>The predicate behind all three section views — one rule, so a filtered list and a reported
    /// count can never disagree.</summary>
    private bool PassesSearch(string section, object item)
    {
        if (_searchSection != section) return true;      // this list isn't the one being searched
        if (_pinnedIds is not null)
            return _pinnedIds.Contains(item switch
            {
                ThreadInfo t => ThreadId(t),
                ModuleInfo m => ModuleId(m),
                HandleInfo h => HandleId(h),
                _            => "",
            });
        if (_sectionRequest is not { } r) return true;
        return item switch
        {
            ThreadInfo t => r.Matches(t.Tid.ToString()),
            ModuleInfo m => r.Matches(m.Name) || r.Matches(m.Path),
            HandleInfo h => r.Matches(h.Name),
            _            => true,
        };
    }

    private void Apply(string section, SearchRequest? request, HashSet<string>? pinned, string term, int count)
    {
        _searchSection    = section;
        _sectionRequest   = request;
        _pinnedIds        = pinned;
        CurrentSearchTerm = term;
        SearchMatchCount  = count;
        IsSearchActive    = true;    // true even at zero matches: "no matches for X" is a result to show
        RefreshSectionViews();
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchSection    = null;
        _sectionRequest   = null;
        _pinnedIds        = null;
        IsSearchActive    = false;
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;
        RefreshSectionViews();
    }

    // The chip binds these by convention; this page filters rather than steps, so they are inert. Declared
    // rather than omitted so the chip's bindings resolve instead of failing silently at runtime.
    [RelayCommand] private void FindNextMatch() { }
    [RelayCommand] private void FindPreviousMatch() { }

    private void RefreshSectionViews()
    {
        ThreadsView.Refresh();
        ModulesView.Refresh();
        HandlesView.Refresh();
    }
}
