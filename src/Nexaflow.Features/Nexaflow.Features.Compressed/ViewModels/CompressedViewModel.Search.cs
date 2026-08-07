using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.IO.Common;
using Nexaflow.Search;

namespace Nexaflow.Features.Compressed.ViewModels;

/// <summary>
/// The archive inspector as an <see cref="ISearchable"/> page: "?" searches the manifest the tab is
/// showing — every entry, at every depth, whether or not its folder happens to be expanded.
/// <para>
/// An entry matches on its <b>name or its path inside the archive</b>. One rule, and the path half is what
/// makes <c>?docs/guide</c> work and what makes typing a folder's name give you that folder <em>with its
/// contents</em> rather than a single row you then have to open. Filename globs are understood here
/// (<c>?*.md</c>) because these really are files — the glob is judged against the entry's name, never
/// against its path.
/// </para>
/// <para>
/// The page answers by <b>filtering</b> the tree down to the hits plus the folders they live in, expanded.
/// Keeping the ancestors is what makes a filtered tree readable: a bare list of leaf names in an archive
/// with four <c>readme.txt</c>s says nothing about which is which. And because every surviving row is a
/// match, there is nowhere to "step" to — see <see cref="HasSearchMatches"/>.
/// </para>
/// <para>
/// No scan cap and no background thread: the manifest is already fully in memory (the tab read it to draw
/// the tree), so the search is a walk over what is on screen. Only the hit list handed to the agent is
/// capped.
/// </para>
/// </summary>
public sealed partial class CompressedViewModel : ISearchable
{
    /// <summary>Hits returned to the agent. The count it is told is the real total; this bounds the
    /// tool result, not the filter — every match stays visible on the page.</summary>
    private const int SearchHitCap = 200;

    /// <summary>The rows a search filter allows through, or null when no search is active. Reference
    /// identity: nodes are rebuilt from scratch by <see cref="Load"/>, which also drops the search.</summary>
    private HashSet<ArchiveNode>? _searchVisible;

    /// <summary>Which folders the user had open before the search expanded things, so dismissing the
    /// search puts the tree back rather than leaving it splayed open.</summary>
    private HashSet<ArchiveNode>? _expandedBeforeSearch;

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>Whether the chip should offer previous/next. Always false: this page answers a search by
    /// filtering, so every row still on screen is a match and stepping has nowhere to go.</summary>
    public bool HasSearchMatches => false;

    // ── ISearchable ───────────────────────────────────────────────────────────

    /// <summary>Globs belong here — the rows are files, so <c>*.md</c> is a constraint this page can
    /// actually judge (against the entry name, which is what a glob is about).</summary>
    public IReadOnlyList<ISearchTermRecognizer> TermRecognizers { get; } = [new GlobTermRecognizer()];

    public string SearchTargetDescription =>
        IsRecognised
            ? $"the entries inside the archive '{FileName}', by entry name or path within it"
            : $"the entries inside '{FileName}' (no handler recognises this archive, so there is nothing to search)";

    public Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!request.TryValidate(out var invalid))
            return Task.FromResult(SearchOutcome.Unsupported($"Invalid regular expression: {invalid}"));

        if (request.Terms.Count == 0)
            return Task.FromResult(SearchOutcome.Unsupported("Nothing to search for."));

        // Marshalled even when not displaying: the tree is the bound row list, and the agent reads it from
        // its own thread.
        return _shell.RunOnUiAsync(() => Task.FromResult(RunSearch(request, display)));
    }

    private SearchOutcome RunSearch(SearchRequest request, bool display)
    {
        if (_root is null)
            return SearchOutcome.None($"No recognised archive is loaded in '{FileName}'.");

        var hits = new List<ArchiveNode>();
        Collect(_root, request, hits);

        if (display) Apply(hits, SearchSyntax.Format(request));

        if (hits.Count == 0) return SearchOutcome.None();

        // The count is the true total; only the returned list is capped, so "1,412 matches" never comes
        // back as "200".
        return SearchOutcome.Found(HitsFor(hits.Take(SearchHitCap)), hits.Count);
    }

    /// <summary>Pre-order, so hits arrive in the order the tree draws them.</summary>
    private static void Collect(ArchiveNode node, SearchRequest request, List<ArchiveNode> into)
    {
        foreach (var child in node.Children)
        {
            // Name or path: a name-scoped term (a glob) is judged against the name alone, everything else
            // may be satisfied by either — which is per-term, so "*.md docs" means a .md file under docs.
            if (request.MatchesFile(child.Name, child.ArchivePath)) into.Add(child);
            Collect(child, request, into);
        }
    }

    /// <summary>Narrows the tree to exactly the entries the agent chose — the same view change the user's
    /// own search makes, so "I've filtered the list to those four" is true when it says so.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        var wanted = hits.Select(h => h.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (wanted.Count == 0) return Task.FromResult(false);

        return _shell.RunOnUiAsync(() =>
        {
            if (_root is null) return Task.FromResult(false);

            var chosen = Descendants(_root).Where(n => wanted.Contains(n.ArchivePath)).ToList();
            if (chosen.Count == 0) return Task.FromResult(false);

            Apply(chosen, CurrentSearchTerm.Length == 0 ? $"{chosen.Count} selected" : CurrentSearchTerm);
            return Task.FromResult(true);
        });
    }

    // ── Applying the filter ───────────────────────────────────────────────────

    private void Apply(IReadOnlyList<ArchiveNode> hits, string term)
    {
        if (_root is null) return;

        // Snapshot the user's own expansion state once per search run, not per query — re-snapshotting on
        // a second search would record the first search's forced expansion as "what they had open".
        _expandedBeforeSearch ??= Descendants(_root).Where(n => n.IsExpanded).ToHashSet();

        var hitSet = hits.ToHashSet();
        var visible = new HashSet<ArchiveNode>();
        KeepMatchingBranches(_root, hitSet, visible);

        foreach (var node in Descendants(_root))
        {
            node.IsSearchHit = hitSet.Contains(node);
            // Everything that survives is opened: a hit five folders down is no use to anyone if reaching
            // it still takes five clicks.
            if (node.IsFolder && visible.Contains(node)) node.IsExpanded = true;
        }

        _searchVisible    = visible;
        CurrentSearchTerm = term;
        SearchMatchCount  = hits.Count;
        IsSearchActive    = true;      // a zero-match search is still a result the user should see
        RebuildVisibleRows();
    }

    /// <summary>Adds every hit and every folder on the way to one. Returns whether this node's subtree held
    /// a hit, which is how an ancestor learns it has to stay.</summary>
    private static bool KeepMatchingBranches(
        ArchiveNode node, HashSet<ArchiveNode> hits, HashSet<ArchiveNode> visible)
    {
        var any = false;
        foreach (var child in node.Children)
        {
            var keep = KeepMatchingBranches(child, hits, visible) || hits.Contains(child);
            if (keep) { visible.Add(child); any = true; }
        }
        return any;
    }

    [RelayCommand]
    private void ClearSearch()
    {
        _searchVisible    = null;
        IsSearchActive    = false;
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;

        if (_root is not null)
        {
            var open = _expandedBeforeSearch;
            foreach (var node in Descendants(_root))
            {
                node.IsSearchHit = false;
                if (open is not null) node.IsExpanded = open.Contains(node);
            }
        }
        _expandedBeforeSearch = null;
        RebuildVisibleRows();
    }

    /// <summary>Declared rather than omitted so the chip's bindings resolve instead of failing silently.
    /// A filtering page has no "next match" — see <see cref="HasSearchMatches"/>.</summary>
    [RelayCommand] private void FindNextMatch() { }

    [RelayCommand] private void FindPreviousMatch() { }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<ArchiveNode> Descendants(ArchiveNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            foreach (var grand in Descendants(child)) yield return grand;
        }
    }

    private static IReadOnlyList<SearchHit> HitsFor(IEnumerable<ArchiveNode> nodes) =>
        nodes.Select(n => new SearchHit(
                  n.ArchivePath,      // the id read_entry already speaks, so a hit round-trips into a read
                  n.Name,
                  n.IsFolder
                      ? $"{n.ArchivePath}/ — folder"
                      : $"{n.ArchivePath} — {ArchiveNode.FormatBytes(n.Size)}"))
             .ToList();
}
