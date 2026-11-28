using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ThisPc;
using Nexaflow.Features.WindowsFileSystem.Services;

using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.Model;

/// <summary>
/// Moving a file-system tab about: browsing to a folder, an archive or a place inside one, up, to This PC and back; the
/// trail the breadcrumb shows, the folder tree that follows each move, and This PC's places as their providers settle them.
/// </summary>
public sealed class FolderNavigation
{
    private readonly FolderLocation _location;
    private readonly FolderListing _listing;
    private readonly IVirtualFileSystem _vfs;
    private readonly IShellServices _shell;
    private readonly FileSystemFeatureRegistry _registry;
    private readonly DefaultFileOpener _opener;

    public FolderNavigation(FolderLocation location, FolderListing listing, IVirtualFileSystem vfs, IShellServices shell,
                            FileSystemFeatureRegistry registry, DefaultFileOpener opener)
    {
        _location = location;
        _listing  = listing;
        _vfs      = vfs;
        _shell    = shell;
        _registry = registry;
        _opener   = opener;
    }

    /// <summary>The folder tree: a folder tab's root folder, or This PC with its places.</summary>
    public ObservableCollection<FileSystemTreeNode> TreeRoots { get; } = [];

    /// <summary>
    /// Raised whenever the open folder changes, with the ordered trail from the root label to it — each segment the path a
    /// breadcrumb click browses to.
    /// </summary>
    public event Action<IReadOnlyList<(string Label, string Path)>>? NavigationChanged;

    /// <summary>Raised once the tab has arrived somewhere — a folder browsed to, This PC shown — or read where it is again:
    /// whatever was selected there is gone.</summary>
    public event Action? Arrived;

    /// <summary>The newest browse, done once the folder is open and its listing on the way — what a caller waiting for the
    /// tab to arrive awaits.</summary>
    public Task Browsing { get; private set; } = Task.CompletedTask;

    private bool _navigating;
    private bool _refreshing;

    /// <summary>The newest browse; an older one still asking whether its folder is one is dropped when it answers.</summary>
    private int _browseTicket;

    // ── Where a tab starts ────────────────────────────────────────────────────

    /// <summary>Starts the tab at <paramref name="folder"/>, with the tree rooted there.</summary>
    public void StartAt(string folder)
    {
        _location.RootPath = folder;
        BuildDirectoryTree(folder);
        BrowseToAsync(folder);
    }

    /// <summary>Starts the tab on This PC: every place listed, each settled as its provider answers.</summary>
    public void StartAtThisPc()
    {
        _location.RootPath    = string.Empty;
        _location.IsThisPc    = true;
        _location.CurrentPath = string.Empty;

        var thisPc = new FileSystemTreeNode(Str.Get("WindowsFileSystem.ThisPc.Title"), string.Empty, TreeNodeKind.ThisPc, _vfs)
        {
            IsExpanded = true
        };
        TreeRoots.Add(thisPc);

        using (Timing.Measure("ThisPC.FillThisPc (enumerate places, UI thread)"))
            FillThisPc(thisPc, addTreeNodes: true);

        using (Timing.Measure("ThisPC.RecountEntries"))
            _listing.Recount();
        NavigationChanged?.Invoke([(Str.Get("WindowsFileSystem.ThisPc.Title"), string.Empty)]);
    }

    // ── Browsing ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens <paramref name="path"/> in the list and the tree: a folder, an archive browsable as one, or a folder inside an
    /// archive. The file system is asked first — a round trip on a location — so a browse that a newer one overtakes while
    /// it waits is dropped, and a path that is no folder leaves the tab where it was.
    /// </summary>
    public Task BrowseToAsync(string path) => Browsing = BrowseAsync(path);

    private async Task BrowseAsync(string path)
    {
        if (_navigating) return;
        var ticket = ++_browseTicket;

        bool isFolder, inContainer;
        try
        {
            isFolder    = await _vfs.IsDirectoryAsync(path);
            inContainer = isFolder && (_vfs.SplitOutermostContainer(path).Inner is not null || await _vfs.IsContainerAsync(path));
        }
        catch (IOException) { return; }
        if (!isFolder || ticket != _browseTicket || _navigating) return;

        _navigating = true;
        try
        {
            _location.IsThisPc    = false;
            _location.InContainer = inContainer;
            _location.CurrentPath = path;
            _listing.ResetSortAndFilter();
            _listing.ReadFolder(inPlace: false);
            _ = ExpandToPathAsync(path);   // the tree follows as each level's folders are read
            FireNavigationChanged(path);   // sync breadcrumb
        }
        finally
        {
            _navigating = false;
        }

        // Arriving with nothing selected offers what the folder itself offers — "New" among it — at once.
        Arrived?.Invoke();
    }

    /// <summary>
    /// Shown again with a <c>path</c>: the folder already open is refreshed, another is browsed to. Without one the tab goes
    /// to This PC.
    /// </summary>
    public void Reinitialize(Dictionary<string, string> pageParams)
    {
        if (pageParams.TryGetValue("path", out var path) && !string.IsNullOrEmpty(path))
        {
            if (string.Equals(path, _location.CurrentPath, StringComparison.OrdinalIgnoreCase))
                Refresh();
            else
                _ = BrowseToAsync(path);
        }
        else
        {
            GoToThisPc(rebuildTree: true);
        }
    }

    public void NavigateUp()
    {
        if (_location.IsThisPc) return;
        // Through the VFS, not Directory.GetParent: inside a mount the chain runs up to the mount root
        // and then to This PC, never out into the real folder the mount maps onto. Entering the same
        // folder by its real path keeps the real chain — the two routes are separate journeys.
        var parent = _vfs.GetParentPath(_location.CurrentPath);
        if (parent is not null)
            _ = BrowseToAsync(parent);
        else
            GoToThisPc(rebuildTree: true); // at a drive or mount root, folder tree has no ThisPc node
    }

    /// <summary>
    /// Reads where the tab is again — the list and the tree at the open folder, or This PC's places — keeping the tree's
    /// expanded state.
    /// </summary>
    public void Refresh()
    {
        _refreshing = true;
        try
        {
            if (_location.IsThisPc)
            {
                ShowThisPc(rebuildTree: false);
            }
            else if (!string.IsNullOrEmpty(_location.CurrentPath))
            {
                _ = RefreshTreeAsync(_location.CurrentPath);
                _listing.ReadFolder(inPlace: true);
            }
            Arrived?.Invoke();
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>
    /// Opens a row: a folder, or an archive that expands in place, is browsed into; any other file opens with its default
    /// action.
    /// </summary>
    public async Task OpenAsync(FileSystemEntry entry)
    {
        if (entry.IsDirectory) { await BrowseToAsync(entry.FullPath); return; }
        // A real archive expands in place (browse its contents in this tab); a document that merely happens
        // to be a container (.docx, .eml) keeps its own viewer. The policy is the /archive filemap mapping.
        if (await IsExpandableAsync(entry.FullPath) && DefaultFileOpener.ExpandsInPlace(entry.FullPath))
        { await BrowseToAsync(entry.FullPath); return; }
        await OpenFileDefaultAsync(entry);
    }

    /// <summary>True when the (file) path can be browsed as a folder — a real archive or a nested archive
    /// entry. Real directories are handled separately.</summary>
    private async Task<bool> IsExpandableAsync(string path)
        => await _vfs.IsContainerAsync(path) || (_vfs.IsContainerName(Path.GetFileName(path)) && await _vfs.ExistsAsync(path));

    /// <summary>
    /// Resolves and executes the default "open" action for a file via <see cref="DefaultFileOpener"/>
    /// (FileExtension &gt; MagicNumber &gt; PerceivedType &gt; ContentType; internal beats shell at the
    /// same specificity), refreshing the tab when the action changed the file system.
    /// </summary>
    private async Task OpenFileDefaultAsync(FileSystemEntry entry)
    {
        // Resolves the default action by extension and opens it; files inside an archive open through the
        // VFS-aware readers (the opener's magic-byte sniff self-skips for a non-existent real path).
        try
        {
            if (await _opener.OpenAsync(entry.FullPath)) Refresh();
        }
        catch (Exception ex)
        {
            // Double-click is the single most-used path in the app, so whatever an action throws is reported naming the
            // file, not left to the app's last-chance handler, which names neither the file nor the viewer. A viewer that
            // opens and then fails to build its view reports that itself, on its own tab.
            _shell.ShowError(Str.Format("WindowsFileSystem.Open.FailedFormat", entry.Name, ex.Message));
        }
    }

    /// <summary>Opens <paramref name="folderPath"/> as a new File Explorer tab in the window's right
    /// pane, splitting the tab area first if it isn't already split.</summary>
    public void OpenInRightPane(string? folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return;
        _shell.OpenTab("FileSystem", new()
        {
            ["mode"]  = "path",
            ["path"]  = folderPath,
            ["label"] = _vfs.GetDisplayName(folderPath),
        }, inRightPane: true);
    }

    /// <summary>
    /// The nearest ancestor of <paramref name="path"/> that is still a folder, or null if none is (the caller then falls
    /// back to This PC). Used to re-home a tab after its folder is deleted — walk up until something is displayable.
    /// Walks through the file system, so under a mount the chain ends at the mount's root, on a location at its own.
    /// Assumes <paramref name="path"/> itself is already gone.
    /// </summary>
    public async Task<string?> NearestExistingAncestorAsync(string path)
    {
        for (var ancestor = _vfs.GetParentPath(path); ancestor is not null; ancestor = _vfs.GetParentPath(ancestor))
        {
            try
            {
                if (await _vfs.IsDirectoryAsync(ancestor)) return ancestor;
            }
            catch (IOException) { /* this one cannot say; ask its parent */ }
        }
        return null;
    }

    /// <summary>
    /// Builds the ordered breadcrumb segments for <paramref name="path"/> relative to the tree's root, then raises
    /// <see cref="NavigationChanged"/>.
    /// </summary>
    private void FireNavigationChanged(string path)
    {
        var segments = new List<(string Label, string Path)>();
        var rootPath = _location.RootPath;

        if (_location.IsThisPc)
        {
            // Showing the "This PC" drive list — single root crumb only
            segments.Add((Str.Get("WindowsFileSystem.ThisPc.Title"), string.Empty));
        }
        else if (string.IsNullOrEmpty(rootPath))
        {
            // Navigated from "This PC" into a drive, a mount, or one of their subdirectories.
            // Breadcrumb: This PC > C:\ > Folder > …, or This PC > OneDrive > Folder > …
            // The VFS segments it because only it knows where a mount root is and what to call it —
            // Path.GetPathRoot returns "" for a ::mount path and would collapse the trail.
            segments.Add((Str.Get("WindowsFileSystem.ThisPc.Title"), string.Empty));
            segments.AddRange(_vfs.GetBreadcrumbs(path));
        }
        else
        {
            // Standard directory mode rooted at the tree's root.
            var rootLabel = Path.GetFileName(rootPath) is { Length: > 0 } n ? n : rootPath;
            segments.Add((rootLabel, rootPath));

            if (!string.Equals(path, rootPath, StringComparison.OrdinalIgnoreCase))
            {
                var relative = Path.GetRelativePath(rootPath, path);
                var parts    = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
                var built    = rootPath;
                foreach (var part in parts)
                {
                    built = Path.Combine(built, part);
                    segments.Add((part, built));
                }
            }
        }

        NavigationChanged?.Invoke(segments);
    }

    // ── The tree ──────────────────────────────────────────────────────────────

    private void BuildDirectoryTree(string rootPath)
    {
        _location.IsThisPc = false;
        TreeRoots.Clear();
        var root = new FileSystemTreeNode(Path.GetFileName(rootPath) is { Length: > 0 } n ? n : rootPath, rootPath, _vfs)
        {
            IsExpanded = true
        };
        TreeRoots.Add(root);
    }

    /// <summary>
    /// Re-roots the tree at the open folder so the tab starts fresh from where the reader has navigated to. Used when
    /// pinning a tab to the ribbon.
    /// </summary>
    public void ResetRootToCurrentPath()
    {
        if (_location.IsThisPc || string.IsNullOrEmpty(_location.CurrentPath)) return;
        _location.RootPath = _location.CurrentPath;
        BuildDirectoryTree(_location.CurrentPath);
        // Select the new root node
        var root = TreeRoots.FirstOrDefault();
        if (root is not null) root.IsSelected = true;
    }

    public void OnTreeNodeSelected(FileSystemTreeNode node)
    {
        // Guard: if we're already inside a browse or a refresh (which selects the open folder's node, so the selection
        // changes), skip to avoid re-entry.
        if (_navigating || _refreshing) return;

        if (node.Kind == TreeNodeKind.ThisPc)
            GoToThisPc(rebuildTree: false);  // tree already has this node — don't rebuild
        else if (!string.IsNullOrEmpty(node.FullPath))
        {
            // Opening a folder re-selects its tree node, and that selection arrives back here after
            // _navigating has cleared — an echo of the navigation just done, not a new one. Left alone
            // it would enumerate the folder a second time and throw the first result away mid-stream, which is
            // visible whenever the cases differ (typing "D:\downloads" lands on the "D:\Downloads" node).
            // ExpandToPathAsync announces the selection it is about to make so exactly that one is
            // swallowed, and only that one.
            if (_expectedTreeEcho is { } echo
                && string.Equals(echo, node.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                _expectedTreeEcho = null;
                return;
            }

            // A real click on the folder already open means "reload this" — the cheap path that re-reads
            // the entries and diffs the tree, rather than a full navigation to where we already are.
            if (string.Equals(_location.CurrentPath, node.FullPath, StringComparison.OrdinalIgnoreCase))
            {
                Refresh();
                return;
            }

            _ = BrowseToAsync(node.FullPath);
        }
    }

    /// <summary>The one tree selection this tab is itself about to cause, so <see cref="OnTreeNodeSelected"/> can tell its
    /// own echo from a click by the user.</summary>
    private string? _expectedTreeEcho;

    /// <summary>The newest walk to a path in the tree; an older one still reading its way down gives up.</summary>
    private int _expandTicket;

    /// <summary>
    /// Selects <paramref name="path"/>'s node in the tree, expanding each level on the way down. Each level's folders are
    /// read before the walk goes into them — a round trip on a location — and a newer walk overtakes this one.
    /// </summary>
    public async Task ExpandToPathAsync(string path)
    {
        var ticket = ++_expandTicket;
        _expectedTreeEcho = path;
        // Clear any existing selection first
        ClearSelection(TreeRoots);
        // Walk the tree roots and expand each level to reach `path`
        foreach (var root in TreeRoots.ToList())
            if (await TryExpandToAsync(root, path, ticket)) break;
    }

    /// <summary>Every expanded node read again, then the open folder selected — what a refresh does to the tree.</summary>
    private async Task RefreshTreeAsync(string path)
    {
        foreach (var root in TreeRoots.ToList())
            await RefreshExpandedNodeAsync(root);
        if (string.Equals(_location.CurrentPath, path, StringComparison.OrdinalIgnoreCase))
            await ExpandToPathAsync(path);
    }

    private static void ClearSelection(IEnumerable<FileSystemTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsSelected) node.IsSelected = false;
            if (node.Children.Count > 0) ClearSelection(node.Children);
        }
    }

    /// <summary>
    /// Diffs an expanded node's children against its folder: removes entries that no longer exist,
    /// inserts new ones in sorted order. Recurses depth-first so deeper expansions are updated
    /// before their parents, preserving IsExpanded state on all surviving nodes.
    /// </summary>
    private static async Task RefreshExpandedNodeAsync(FileSystemTreeNode node)
    {
        // Recurse first — virtual roots like "This PC" (FullPath="") must propagate into children
        foreach (var child in node.Children.ToList())
            await RefreshExpandedNodeAsync(child);

        // Only diff nodes with a path and loaded children
        if (string.IsNullOrEmpty(node.FullPath)) return;
        if (!node.ChildrenLoaded) return;

        // The listing is expressed in THIS node's path space: under a mount the children are ::mount\… however the
        // folder behind it is reached, so the comparison below matches what the tree already holds.
        var names = await FileSystemTreeNode.ChildFoldersAsync(node.Vfs, node.FullPath);
        var childPaths = new HashSet<string>(names.Select(name => Path.Combine(node.FullPath, name)), StringComparer.OrdinalIgnoreCase);

        // Remove children no longer there
        for (int i = node.Children.Count - 1; i >= 0; i--)
        {
            if (!childPaths.Contains(node.Children[i].FullPath))
                node.Children.RemoveAt(i);
        }

        // Insert children new there (maintains alphabetical order)
        var existing = new HashSet<string>(node.Children.Select(c => c.FullPath), StringComparer.OrdinalIgnoreCase);
        var inserted = new List<FileSystemTreeNode>();
        foreach (var dir in childPaths.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            if (existing.Contains(dir)) continue;
            var newNode = new FileSystemTreeNode(Path.GetFileName(dir), dir, node.Vfs);
            InsertSorted(node.Children, newNode);
            inserted.Add(newNode);
        }
        // Same deal as a first read: a refreshed node's expander is settled off the UI thread.
        _ = FileSystemTreeNode.ProbeExpandersAsync(inserted);
    }

    private static void InsertSorted(ObservableCollection<FileSystemTreeNode> children, FileSystemTreeNode newNode)
    {
        for (int i = 0; i < children.Count; i++)
        {
            if (string.Compare(newNode.FullPath, children[i].FullPath, StringComparison.OrdinalIgnoreCase) < 0)
            {
                children.Insert(i, newNode);
                return;
            }
        }
        children.Add(newNode);
    }

    private async Task<bool> TryExpandToAsync(FileSystemTreeNode node, string target, int ticket)
    {
        if (ticket != _expandTicket) return true;   // overtaken: stop walking, and stop the caller too

        if (string.Equals(node.FullPath, target, StringComparison.OrdinalIgnoreCase))
        {
            node.IsSelected = true;
            node.IsExpanded = true;
            return true;
        }

        // Only descend if the target is under this node.
        // Empty FullPath means a virtual root (e.g. "This PC") — always descend into its children.
        if (!string.IsNullOrEmpty(node.FullPath) && !PathScope.IsWithin(target, node.FullPath))
            return false;

        node.IsExpanded = true; // starts the read, if nothing has yet
        await node.LoadChildrenAsync();

        foreach (var child in node.Children.ToList())
        {
            if (await TryExpandToAsync(child, target, ticket))
                return true;
        }
        return false;
    }

    // ── This PC ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows This PC: the list holds its places, the breadcrumb its one crumb, and — when <paramref name="rebuildTree"/> —
    /// the tree is rebuilt with This PC at its root. Each place is probed on its own, so a slow or unplugged drive keeps
    /// nobody waiting.
    /// </summary>
    public void GoToThisPc(bool rebuildTree = false)
    {
        if (ShowThisPc(rebuildTree)) Arrived?.Invoke();
    }

    private bool ShowThisPc(bool rebuildTree)
    {
        if (_navigating) return false;
        ++_browseTicket;   // a browse still asking about its folder is overtaken
        _navigating = true;
        try
        {
            _location.IsThisPc    = true;
            _location.InContainer = false;
            _location.CurrentPath = string.Empty;
            // Whatever was being read is replaced with the places.
            _listing.LeaveFolder();

            FileSystemTreeNode? thisPcNode;
            if (rebuildTree)
            {
                TreeRoots.Clear();
                thisPcNode = new FileSystemTreeNode(Str.Get("WindowsFileSystem.ThisPc.Title"), string.Empty, TreeNodeKind.ThisPc, _vfs)
                {
                    IsExpanded = true
                };
                TreeRoots.Add(thisPcNode);
            }
            else
            {
                thisPcNode = TreeRoots.FirstOrDefault(n => n.Kind == TreeNodeKind.ThisPc);
            }

            // Populate entries immediately with all drives in Loading state,
            // then resolve each drive individually on a background thread.
            FillThisPc(thisPcNode, addTreeNodes: rebuildTree);

            _listing.Recount();
            NavigationChanged?.Invoke([(Str.Get("WindowsFileSystem.ThisPc.Title"), string.Empty)]);

            if (thisPcNode is not null)
            {
                ClearSelection(TreeRoots);
                thisPcNode.IsSelected = true;
            }
            return true;
        }
        finally
        {
            _navigating = false;
        }
    }

    /// <summary>
    /// Follows the This PC contributors while the view is shown, so editing them in Options updates an open tab without a
    /// switch away and back — the providers and the file system are owned by the shell and outlive the tab.
    /// <para>
    /// The shell's own config→tab refresh cannot cover this: it maps a config to the page registrations
    /// whose constructors take it, and the file browser's registration cannot reference another
    /// feature's config type.
    /// </para>
    /// </summary>
    public void AttachThisPcProviderTracking()
    {
        foreach (var provider in _registry.ThisPcItemProviders) provider.Changed += OnThisPcItemsChanged;
        _vfs.MountsChanged += OnThisPcItemsChanged;

        // Catch up on what moved while unloaded — but only when something did. This runs on every switch
        // back to a This PC tab, and rebuilding unconditionally cost that switch a full teardown: the rows
        // were cleared and re-added, each one flashed back to Loading, and every drive was re-probed
        // (readiness, capacity, a no-seek-penalty device query) for an answer it already had.
        if (_location.IsThisPc && !ThisPcRowsMatch(ThisPcBrowsePaths())) OnThisPcItemsChanged();
    }

    /// <summary>Stops following the This PC contributors.</summary>
    public void DetachThisPcProviderTracking()
    {
        foreach (var provider in _registry.ThisPcItemProviders) provider.Changed -= OnThisPcItemsChanged;
        _vfs.MountsChanged -= OnThisPcItemsChanged;
    }

    /// <summary>
    /// The browse paths This PC would show right now, in order — the identity <see cref="FillThisPc"/>
    /// builds each row from. Costs nothing on the UI thread: every provider answers <c>GetItems</c> from what it already
    /// knows, and the slow half of a row is <c>GetDetailAsync</c>.
    /// </summary>
    private List<string> ThisPcBrowsePaths()
    {
        var paths = new List<string>();
        try
        {
            foreach (var row in ThisPcItemSet.Collect(_registry.ThisPcItemProviders))
                paths.Add(BrowsePathOf(row.Item));
        }
        catch { /* a broken contributor is not a reason to rebuild */ }
        return paths;
    }

    /// <summary>Where a This PC row is browsed: a volume at its own root, a contributed location under its own virtual root
    /// (<c>::{id}</c>), never the folder behind it.</summary>
    private static string BrowsePathOf(ThisPcItem item)
        => item.Backing == ThisPcItemBacking.LocalPath ? VirtualMount.RootFor(item.Id) : item.TargetPath;

    /// <summary>
    /// Whether the rows on screen are still the ones <paramref name="paths"/> describes — same places, same
    /// order. Per-row state a probe settles (readiness, free space, volume label) is deliberately not part
    /// of it: that arrives on its own and a change to it is not a reason to rebuild the list.
    /// </summary>
    private bool ThisPcRowsMatch(IReadOnlyList<string> paths)
    {
        var entries = _listing.Entries;
        if (paths.Count != entries.Count) return false;
        for (int i = 0; i < paths.Count; i++)
            if (!string.Equals(paths[i], entries[i].FullPath, StringComparison.OrdinalIgnoreCase))
                return false;
        return true;
    }

    /// <summary>A provider may signal from any thread (a file watcher, a background probe), so hop to the
    /// UI thread before touching the entry list.</summary>
    private void OnThisPcItemsChanged()
        => _ = _shell.RunOnUiAsync(() =>
        {
            if (_location.IsThisPc) GoToThisPc(rebuildTree: false);
        });

    /// <summary>
    /// Fills the list (and optionally the tree) with every row of This PC — volumes and contributed locations alike —
    /// from the one merge in <see cref="ThisPcItemSet.Collect"/>.
    /// <para>
    /// Every row gets the same treatment: it appears immediately in a Loading state and its provider's probe settles it
    /// Ready or Unavailable, so neither a disconnected drive nor a sync folder that has gone can sit there pretending to
    /// work.
    /// </para>
    /// </summary>
    private void FillThisPc(FileSystemTreeNode? thisPc, bool addTreeNodes)
    {
        IReadOnlyList<ThisPcRow> rows;
        using (Timing.Measure("ThisPC.ThisPcItemSet.Collect"))
        {
            try { rows = ThisPcItemSet.Collect(_registry.ThisPcItemProviders); }
            catch { _listing.Clear(); return; }   // never let a contributor break This PC itself
        }
        Timing.Note("ThisPC.rows", rows.Count.ToString());

        // Built in full, then committed in one go: an Add per row raised a CollectionChanged per row,
        // where one Reset says the same thing. The probes start only once the rows are on screen — each
        // settles the entry object it was handed, and that object is the same one either way.
        var entries = new List<FileSystemEntry>(rows.Count);
        var probes = new List<(ThisPcRow Row, FileSystemTreeNode? Node, FileSystemEntry Entry)>(rows.Count);

        foreach (var row in rows)
        {
            var item = row.Item;
            var path = BrowsePathOf(item);

            var entry = new FileSystemEntry
            {
                Name           = item.Label,
                FullPath       = path,
                IsDirectory    = true,
                IsThisPcItem   = true,
                ProviderId     = item.Backing == ThisPcItemBacking.OwnPath ? null : item.Id,
                DriveStatus    = DriveStatus.Loading,
                DriveIconType  = MapIcon(item.Icon),
                DriveKindLabel = item.TypeLabel,
            };
            entries.Add(entry);

            FileSystemTreeNode? node = null;
            if (thisPc is not null)
            {
                if (addTreeNodes)
                {
                    node = new FileSystemTreeNode(item.Label, path, TreeNodeKind.Drive, _vfs);
                    thisPc.Children.Add(node);
                }
                else
                {
                    // The tree already holds nodes — reuse the matching one so expansion state survives.
                    node = thisPc.Children.FirstOrDefault(
                               c => string.Equals(c.FullPath, path, StringComparison.OrdinalIgnoreCase))
                           ?? new FileSystemTreeNode(item.Label, path, TreeNodeKind.Drive, _vfs);
                }
                node.DriveIconType = entry.DriveIconType;
            }

            probes.Add((row, node, entry));
        }

        _listing.ShowRows(entries);

        foreach (var (row, node, entry) in probes) _ = CheckThisPcItemAsync(row, node, entry);
    }

    /// <summary>Maps the shared place-kind onto the browser's own render vocabulary. A solid-state volume is a device fact
    /// a row's detail reports once the drive has answered (<see cref="ThisPcItemDetail.Icon"/>).</summary>
    private static DriveIconType MapIcon(ThisPcItemIcon icon) => icon switch
    {
        ThisPcItemIcon.Removable  => DriveIconType.Removable,
        ThisPcItemIcon.Optical    => DriveIconType.CDDVD,
        ThisPcItemIcon.Network    => DriveIconType.Network,
        ThisPcItemIcon.Cloud      => DriveIconType.Cloud,
        ThisPcItemIcon.Linux      => DriveIconType.Linux,
        ThisPcItemIcon.SolidState => DriveIconType.SSD,
        _                         => DriveIconType.HDD,
    };

    /// <summary>Settles a This PC row from its provider's probe: its label, capacity, file system and icon once the device
    /// has answered, Ready or Unavailable, and an expander when it holds folders.</summary>
    private static async Task CheckThisPcItemAsync(ThisPcRow row, FileSystemTreeNode? node, FileSystemEntry entry)
    {
        using var _t = Timing.Measure($"ThisPC.Check {row.Item.Id} (probe + UI settle)");
        ThisPcItemDetail detail;
        try { detail = await row.DetailAsync(); }
        catch { detail = new ThisPcItemDetail { Available = false }; }

        // Resumes on the UI thread that asked — safe to touch the row and the node directly.
        if (detail.Label is { Length: > 0 } label)
        {
            entry.Name = label;
            if (node is not null) node.Name = label;
        }

        entry.DriveUsedBytes  = detail.UsedBytes;
        entry.DriveTotalBytes = detail.TotalBytes;
        if (detail.FileSystem is { } fileSystem) entry.FileSystem = fileSystem;
        if (detail.Icon is { } icon) entry.DriveIconType = MapIcon(icon);
        entry.DriveStatus = detail.Available ? DriveStatus.Ready : DriveStatus.Unavailable;

        if (node is not null)
        {
            node.DriveIconType = entry.DriveIconType;
            node.DriveStatus   = entry.DriveStatus;
            if (detail.Available && detail.HasChildren && node.Children.Count == 0)
                node.Children.Add(FileSystemTreeNode.Dummy);
        }
    }
}
