using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.Search;
using Nexaflow.Features.VirtualDisk.Services;
using Nexaflow.IO.Common;
using Nexaflow.Search;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.VirtualDisk.ViewModels;

/// <summary>
/// The disk inspector as an <see cref="ISearchable"/> page: "?" searches the files and folders inside the
/// image, by name or by path within it. Filename globs are understood (<c>?*.dll</c>) because these really
/// are files — the glob is judged against the entry's name, never against its path.
/// <para>
/// The search <b>walks the image</b> rather than the rows on screen. The contents tree is lazy — only the
/// folders the user opened have been read — so filtering what is loaded would silently answer a much
/// smaller question, and an empty result would read as "not in this image" when it means "not in the bit
/// you happened to open". The walk is bounded and cancellable instead: a floor with a "+" is honest where a
/// number that quietly meant "the first few folders" is not.
/// </para>
/// <para>
/// Results are shown by <b>rebuilding the tree from the hits</b> — each match plus the folders it lives
/// under, expanded. That keeps the answer inside the one surface this tab has, and it is why dismissing
/// the search re-reads the root: the filtered tree is a different tree, not a hidden subset of the real
/// one. Every row on screen is a match, so there is nowhere to step to — see <see cref="HasSearchMatches"/>.
/// </para>
/// </summary>
public sealed partial class VirtualDiskViewModel : ISearchable
{
    /// <summary>Folders read before the walk gives up. A small image finishes far inside this; a Windows
    /// volume does not, and is told so rather than freezing the tab.</summary>
    private const int FolderVisitCap = 20_000;

    /// <summary>Hits kept. The agent gets a workable set without a tool result the size of a filesystem.</summary>
    private const int SearchHitCap = 200;

    /// <summary>The last scan's matches by path, so <see cref="ShowResultsAsync"/> can rebuild rows for the
    /// subset the agent chose without re-reading the image. Not visible state — a non-displaying search
    /// fills it and the page looks identical.</summary>
    private readonly Dictionary<string, DiskSearchScanner.DiskMatch> _lastScan =
        new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private bool _isSearchActive;
    [ObservableProperty] private int _searchMatchCount;
    [ObservableProperty] private bool _isSearchTruncated;
    [ObservableProperty] private string _currentSearchTerm = string.Empty;

    /// <summary>Whether the chip should offer previous/next. Always false: this page answers a search by
    /// showing only the matches, so every row is a match and stepping has nowhere to go.</summary>
    public bool HasSearchMatches => false;

    /// <summary>"+" when the walk stopped at a cap, so a floor never reads as an exact total.</summary>
    public string SearchCountSuffix => IsSearchTruncated ? "+" : string.Empty;

    partial void OnIsSearchTruncatedChanged(bool value) => OnPropertyChanged(nameof(SearchCountSuffix));

    // ── ISearchable ───────────────────────────────────────────────────────────

    public IReadOnlyList<ISearchTermRecognizer> TermRecognizers { get; } = [new GlobTermRecognizer()];

    public string SearchTargetDescription =>
        IsRecognised
            ? $"the files and folders inside the disk image '{FileName}', by name or path within it"
            : $"the contents of '{FileName}' (the image isn't readable, so there is nothing to search)";

    public async Task<SearchOutcome> SearchAsync(SearchRequest request, bool display, CancellationToken ct)
    {
        if (!request.TryValidate(out var invalid))
            return SearchOutcome.Unsupported($"Invalid regular expression: {invalid}");

        if (request.Terms.Count == 0)
            return SearchOutcome.Unsupported("Nothing to search for.");

        // Not Unsupported: the page understands the query perfectly well, it just has no filesystem to run
        // it against. "This page can't do patterns" would be the wrong thing for the user to conclude.
        if (!IsRecognised)
            return SearchOutcome.None($"'{FileName}' isn't a readable disk image, so there is nothing to search.");

        var path = _diskPath;
        var scan = await Task.Run(
            () => DiskSearchScanner.Scan(_vfs, path, request, FolderVisitCap, SearchHitCap, ct), ct);

        // A cancelled walk returns what it had; without this the partial set would read as the whole answer.
        ct.ThrowIfCancellationRequested();

        _lastScan.Clear();
        foreach (var m in scan.Matches) _lastScan[m.InnerPath] = m;

        if (display)
            await _shell.RunOnUiAsync(() =>
            {
                Apply(scan.Matches, SearchSyntax.Format(request), scan.Truncated);
                return Task.FromResult(true);
            });

        var hits = scan.Matches.Select(HitFor).ToList();

        if (hits.Count == 0)
            return scan.Truncated
                ? SearchOutcome.None($"No matches in the first {scan.FoldersVisited:N0} folders of "
                                     + $"'{FileName}' — the walk stopped there.")
                : SearchOutcome.None();

        return scan.Truncated
            // Found, not Narrowed: the hits are real, only the total is a floor.
            ? SearchOutcome.Found(hits, hits.Count,
                $"Stopped after {scan.FoldersVisited:N0} folders — there may be more inside '{FileName}'.")
            : SearchOutcome.Found(hits);
    }

    /// <summary>Narrows the contents tree to exactly the entries the agent chose.</summary>
    public Task<bool> ShowResultsAsync(IReadOnlyList<SearchHit> hits, CancellationToken ct)
    {
        // Only ids this page produced: an id it never returned has no row to rebuild, and inventing one
        // from the string would put a file on screen that may not be in the image at all.
        var chosen = hits.Select(h => h.Id)
                         .Where(id => _lastScan.ContainsKey(id))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Select(id => _lastScan[id])
                         .ToList();
        if (chosen.Count == 0) return Task.FromResult(false);

        return _shell.RunOnUiAsync(() =>
        {
            Apply(chosen, CurrentSearchTerm.Length == 0 ? $"{chosen.Count} selected" : CurrentSearchTerm,
                  truncated: false);
            return Task.FromResult(true);
        });
    }

    // ── Applying ──────────────────────────────────────────────────────────────

    /// <summary>Replaces the contents tree with the matches and the folder spine above them.</summary>
    private void Apply(IReadOnlyList<DiskSearchScanner.DiskMatch> matches, string term, bool truncated)
    {
        _roots.Clear();
        var folders = new Dictionary<string, DiskNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in matches) Place(m, folders);
        SortChildren(_roots);
        RebuildVisibleRows();

        CurrentSearchTerm = term;
        SearchMatchCount  = matches.Count;
        IsSearchTruncated = truncated;
        IsSearchActive    = true;      // a zero-match search is still a result the user should see
    }

    private void Place(DiskSearchScanner.DiskMatch match, Dictionary<string, DiskNode> folders)
    {
        if (match.IsFolder) { EnsureFolder(match.InnerPath, folders, match).IsSearchHit = true; return; }

        var slash = match.InnerPath.LastIndexOf('/');
        var node = new DiskNode
        {
            Name        = slash < 0 ? match.InnerPath : match.InnerPath[(slash + 1)..],
            InnerPath   = match.InnerPath,
            IsFolder    = false,
            Depth       = Depth(match.InnerPath),
            Size        = match.Size,
            Modified    = match.Modified,
            IsSearchHit = true,
        };

        if (slash < 0) _roots.Add(node);
        else EnsureFolder(match.InnerPath[..slash], folders).Children.Add(node);
    }

    /// <summary>The folder node for this path, creating it (and its own ancestors) as unmarked context
    /// rows. Already <c>Loaded</c> and expanded: a filtered tree has exactly the children it was built
    /// with, and a hit five folders down is no use if reaching it still takes five clicks.</summary>
    private DiskNode EnsureFolder(
        string innerPath, Dictionary<string, DiskNode> folders, DiskSearchScanner.DiskMatch? self = null)
    {
        if (folders.TryGetValue(innerPath, out var existing)) return existing;

        var slash = innerPath.LastIndexOf('/');
        var node = new DiskNode
        {
            Name       = slash < 0 ? innerPath : innerPath[(slash + 1)..],
            InnerPath  = innerPath,
            IsFolder   = true,
            Depth      = Depth(innerPath),
            Size       = self?.Size ?? 0,
            Modified   = self?.Modified ?? default,
            Loaded     = true,
            IsExpanded = true,
        };
        folders[innerPath] = node;

        if (slash < 0) _roots.Add(node);
        else EnsureFolder(innerPath[..slash], folders).Children.Add(node);
        return node;
    }

    private static int Depth(string innerPath) => innerPath.Count(c => c == '/');

    /// <summary>Folders before files, by name — the order the unfiltered tree uses. Needed because a
    /// context folder is created when its first hit is placed, which may be after a sibling file.</summary>
    private static void SortChildren(List<DiskNode> nodes)
    {
        nodes.Sort((a, b) =>
            a.IsFolder != b.IsFolder ? (a.IsFolder ? -1 : 1)
            : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var n in nodes) SortChildren(n.Children);
    }

    /// <summary>Dismisses the search and re-reads the image root — the filtered tree is a different tree,
    /// so there is no hidden full one to un-hide.</summary>
    [RelayCommand]
    private async Task ClearSearch()
    {
        IsSearchActive    = false;
        IsSearchTruncated = false;
        SearchMatchCount  = 0;
        CurrentSearchTerm = string.Empty;
        _lastScan.Clear();
        await LoadRootAsync();
    }

    /// <summary>Declared rather than omitted so the chip's bindings resolve instead of failing silently.
    /// A filtering page has no "next match" — see <see cref="HasSearchMatches"/>.</summary>
    [RelayCommand] private void FindNextMatch() { }

    [RelayCommand] private void FindPreviousMatch() { }

    private static SearchHit HitFor(DiskSearchScanner.DiskMatch m)
    {
        var slash = m.InnerPath.LastIndexOf('/');
        var name  = slash < 0 ? m.InnerPath : m.InnerPath[(slash + 1)..];
        return new SearchHit(
            m.InnerPath,      // the id list_files / read_file already speak, so a hit round-trips into a read
            name,
            m.IsFolder
                ? $"{m.InnerPath}/ — folder"
                : $"{m.InnerPath} — {SizeFormatter.FormatBytes(m.Size)}");
    }
}
