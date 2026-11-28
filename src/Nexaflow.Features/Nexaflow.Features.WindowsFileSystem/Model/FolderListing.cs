using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Services;

using Nexaflow.Interaction;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Cloud;
using Nexaflow.IO.Common.Listing;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Common.Shell;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.Model;

/// <summary>
/// What the open folder holds, as the tab lists it: read off the UI thread and committed a batch at a time, sorted and
/// filtered as the reader asks, kept up with while the view is shown, and held back while the shell is changing the folder.
/// </summary>
public sealed partial class FolderListing : ObservableObject
{
    private const int StreamThreshold = 1000;
    private const int ChunkSize       = 512;

    private readonly FolderLocation _location;
    private readonly IVirtualFileSystem _vfs;
    private readonly IShellServices _shell;
    private readonly FileCompanions _companions;

    public FolderListing(FolderLocation location, IVirtualFileSystem vfs, IShellServices shell, FileCompanions companions)
    {
        _location   = location;
        _vfs        = vfs;
        _shell      = shell;
        _companions = companions;
        _location.PropertyChanged += OnLocationChanged;
    }

    /// <summary>The rows the tab lists: the open folder's entries, or This PC's places.</summary>
    public RangeObservableCollection<FileSystemEntry> Entries { get; } = [];

    // ── Counts ────────────────────────────────────────────────────────────────

    /// <summary>Folders in <see cref="Entries"/>, kept in step by whatever fills it — never counted off the live
    /// collection, which the loader is still appending to.</summary>
    public int FolderCount { get; private set; }

    /// <summary>Files in <see cref="Entries"/>, kept as <see cref="FolderCount"/> is.</summary>
    public int FileCount { get; private set; }

    /// <summary>Raised when <see cref="FolderCount"/> and <see cref="FileCount"/> were set, on the thread that owns
    /// <see cref="Entries"/>.</summary>
    public event Action? Counted;

    private void SetCounts(int folders, int files)
    {
        FolderCount = folders;
        FileCount   = files;
        Counted?.Invoke();
    }

    /// <summary>Recounts in a single pass — for what fills <see cref="Entries"/> directly (This PC's places) rather than
    /// through the loader. Runs on the thread that owns the collection, right after it changed it.</summary>
    public void Recount()
    {
        int folders = 0, files = 0;
        foreach (var e in Entries) { if (e.IsDirectory) folders++; else files++; }
        SetCounts(folders, files);
    }

    /// <summary>Shows <paramref name="rows"/> in place of whatever is listed, with one notification.</summary>
    public void ShowRows(IReadOnlyList<FileSystemEntry> rows) => Entries.ReplaceAll(rows);

    /// <summary>Lists nothing.</summary>
    public void Clear() => Entries.Clear();

    // ── Sort and filter ───────────────────────────────────────────────────────

    /// <summary>The column the rows are sorted by — a <see cref="FileSystemEntry"/> property name.</summary>
    [ObservableProperty] private string _sortColumn = nameof(FileSystemEntry.Name);
    [ObservableProperty] private bool   _sortAscending = true;

    /// <summary>Which rows the reader asked to see; a view shows those <see cref="FilterFor"/> passes.</summary>
    [ObservableProperty] private EntryFilter _activeFilter = EntryFilter.None;

    /// <summary>The rows <paramref name="filter"/> lets through, or null when it lets every row through.</summary>
    public static Predicate<FileSystemEntry>? FilterFor(EntryFilter filter) => filter switch
    {
        EntryFilter.FoldersOnly => static e => e.IsDirectory,
        EntryFilter.FilesOnly   => static e => !e.IsDirectory,
        _                       => null,
    };

    /// <summary>Shows only <paramref name="filter"/>'s rows, or every row again when they are what is shown.</summary>
    public void ToggleFilter(EntryFilter filter) => ActiveFilter = ActiveFilter == filter ? EntryFilter.None : filter;

    /// <summary>Sorts by <paramref name="column"/>, or the other way when it is the column already sorted by. A folder still
    /// being read is sorted once it is read through.</summary>
    public void SortBy(string column)
    {
        if (SortColumn == column)
            SortAscending = !SortAscending;
        else
        {
            SortColumn    = column;
            SortAscending = true;
        }

        if (IsLoadingEntries) { _resortPending = true; return; }
        ResortEntries();
    }

    /// <summary>Sorts by name, ascending, and shows every row — where each folder starts.</summary>
    public void ResetSortAndFilter()
    {
        SortColumn    = nameof(FileSystemEntry.Name);
        SortAscending = true;
        ActiveFilter  = EntryFilter.None;
    }

    private IEnumerable<FileSystemEntry> ApplySort(IEnumerable<FileSystemEntry> source)
    {
        Func<FileSystemEntry, object> key = SortColumn switch
        {
            nameof(FileSystemEntry.Modified)  => e => e.Modified,
            // Drives report capacity via DriveTotalBytes; files via SizeBytes.
            nameof(FileSystemEntry.SizeBytes) => e => (object)(e.IsThisPcItem ? e.DriveTotalBytes : e.SizeBytes),
            nameof(FileSystemEntry.TypeLabel) => e => e.TypeLabel,
            _ => e => e.Name
        };

        // Folders always first, then sort within group
        return SortAscending
            ? source.OrderBy(e => !e.IsDirectory).ThenBy(key)
            : source.OrderBy(e => !e.IsDirectory).ThenByDescending(key);
    }

    /// <summary>Reorders <see cref="Entries"/> by the active sort without reading the folder again, so it works on This PC
    /// too. A sort needs the whole set, so it commits with one Reset; a large set is sorted off the UI thread.</summary>
    private async void ResortEntries()
    {
        var path     = _location.CurrentPath;
        var snapshot = Entries.ToList();

        var sorted = snapshot.Count > StreamThreshold
            ? await Task.Run(() => ApplySort(snapshot).ToList())
            : ApplySort(snapshot).ToList();

        // A navigation may have replaced the contents while we were sorting.
        if (_location.CurrentPath != path) return;
        Entries.ReplaceAll(sorted);
    }

    // ── Reading the folder ────────────────────────────────────────────────────
    // The open folder is read off the UI thread. Each read runs under its own token; starting a new one cancels the previous,
    // so rapid folder clicks settle on the last-clicked folder and stale enumerations are abandoned. Folders at or below
    // StreamThreshold are sorted and shown in one shot; larger folders stream in batches of at least ChunkSize (unsorted,
    // listing order) so the first page appears at once and the read never waits on the list. Every batch reaches the list as
    // one notification, never one per entry.

    [ObservableProperty] private bool _isLoadingEntries;

    /// <summary>The open folder lies in a folder a cloud sync provider keeps (OneDrive, say), so the list carries a Status
    /// column: where each file stands with the cloud, as Explorer shows it.</summary>
    [ObservableProperty] private bool _showsCloudStatus;

    /// <summary>The folder is on a location that reports POSIX permissions, owner and group, so the list shows them.</summary>
    [ObservableProperty] private bool _showsPosixColumns;

    /// <summary>Why the folder could not be read through — gone, access denied, a dropped connection — or null when it was.</summary>
    [ObservableProperty] private string? _listingFailure;

    /// <summary>The newest read of the open folder, done once its every row is in <see cref="Entries"/>.</summary>
    public Task EntriesLoaded { get; private set; } = Task.CompletedTask;

    private CancellationTokenSource? _loadCts;
    private bool _resortPending;

    /// <summary>The open folder changed while it was being read: read it again, in place, once this reading is done.</summary>
    private bool _reloadWhenLoaded;

    /// <summary>Reads the open folder. <paramref name="inPlace"/> is a reload of the folder already listed — after a delete,
    /// a paste, a refresh — which keeps its rows until the new ones replace them; otherwise the list empties at once.</summary>
    public void ReadFolder(bool inPlace) => EntriesLoaded = RefreshEntriesAsync(inPlace);

    /// <summary>Cancels any read in flight, so no folder handle is held open — asked before a viewlet changes or deletes the
    /// open folder.</summary>
    public void CancelRead() => _loadCts?.Cancel();

    /// <summary>The tab left its folder for This PC: whatever was being read is abandoned, and what described the folder
    /// goes with it.</summary>
    public void LeaveFolder()
    {
        _loadCts?.Cancel();
        IsLoadingEntries  = false;
        ShowsCloudStatus  = false;
        ShowsPosixColumns = false;
        ListingFailure    = null;
        ResetSortAndFilter();
    }

    /// <summary>One unit of work handed from the background enumerator to the UI consumer.
    /// <paramref name="Replace"/> = the batch replaces whatever the list holds — the whole sorted set of a
    /// small folder, or the first chunk of a streamed one; otherwise its items are appended after the
    /// chunks already shown. Either way the list hears of it as one Reset.</summary>
    /// <param name="Failure">Why the listing stopped before the folder was read through, on the batch that ends it; null when it did not.</param>
    private readonly record struct LoadBatch(
        IReadOnlyList<FileSystemEntry> Items, bool Replace, int Folders, int Files, bool InSyncRoot, string? Failure = null);

    /// <summary>
    /// Loads the current directory's contents, cancelling any load already in flight.
    /// The background enumerator (<see cref="ProduceEntriesAsync"/>) runs off the UI thread
    /// and hands batches through a bounded channel; this method consumes them and is the
    /// only place that touches <see cref="Entries"/>. Because it was invoked on the UI
    /// thread, every <c>await</c> here resumes on that thread's own context — no toolkit
    /// reference needed. The channel holds one batch, and the producer does not wait on it
    /// mid-folder: what it reads while a batch is still waiting joins the next, so each turn here takes
    /// everything found since the last.
    /// <para>
    /// Every batch is committed with one notification. One <c>Add</c> per entry, arriving straight after
    /// the list was cleared and before it had been laid out again, left a scrolled list's virtualizing
    /// panel working from the offset it had before the clear: it kept hundreds of rows realized and
    /// arranged them with blank bands between, until the tab was closed.
    /// </para>
    /// <para>
    /// <paramref name="inPlace"/> is a reload of the folder already listed — after a delete, a paste, a
    /// refresh. Its rows stay until the first batch replaces them, so the list keeps the reader's place
    /// rather than emptying and starting again from the top. A navigation empties the list at once: the
    /// old folder's rows must not stand in for the new one's while it is read.
    /// </para>
    /// </summary>
    private async Task RefreshEntriesAsync(bool inPlace)
    {
        using var _t = Timing.Measure($"Folder.RefreshEntriesAsync {_location.CurrentPath} (total, UI thread)");
        var path = _location.CurrentPath;

        // A mutation is in flight on this folder (e.g. it's being deleted): don't enumerate it — the view
        // shows the "please wait" panel and we reload once the shell clears the busy mark.
        if (IsFolderBusy) { Entries.Clear(); SetCounts(0, 0); IsLoadingEntries = false; return; }

        // Every folder — on disk, under a mount, inside an archive, on a location — streams through the one listing
        // the file system gives, so none of them is read on the UI thread and the first rows show before the last.

        // Cancel (don't dispose) the previous load: a background thread may still hold its
        // token, so disposing here could throw instead of cancelling cleanly.
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        if (!inPlace)
        {
            Entries.Clear();
            SetCounts(0, 0);
            ListingFailure = null;
        }
        _resortPending = false;
        IsLoadingEntries = true;
        ShowsPosixColumns = (_vfs.GetCapabilities(path) & LocationCapabilities.Permissions) != 0;
        var names = StringComparer.FromComparison(_vfs.GetNameComparison(path));

        var channel = Channel.CreateBounded<LoadBatch>(new BoundedChannelOptions(1)
        {
            FullMode     = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });

        // Linked scope so we can always unblock the producer when the consumer stops.
        using var scope = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Reloading in place, the rows are already on screen, so there is nothing to show sooner by streaming: the folder is read
        // and sorted whole, and only what differs from the rows shown is applied, so the list keeps what the reader is looking at.
        var producer = Task.Run(() => ProduceEntriesAsync(path, channel.Writer, scope.Token, stream: !inPlace), scope.Token);

        var batches = 0;
        try
        {
            await foreach (var batch in channel.Reader.ReadAllAsync(ct))
            {
                if (_location.CurrentPath != path) break; // superseded mid-stream
                batches++;
                // The UI-thread half: everything below raises CollectionChanged, so this is the cost the
                // list actually pays to show what the producer found.
                using (Timing.Measure($"Folder.applyBatch#{batches} n={batch.Items.Count} replace={batch.Replace}"))
                {
                    if (batch.Replace && inPlace)
                        Entries.MatchTo(batch.Items, entry => entry.FullPath, names, (kept, fresh) => kept.TakeOn(fresh));
                    else if (batch.Replace)
                        Entries.ReplaceAll(batch.Items);
                    else
                        Entries.AddRange(batch.Items);
                    SetCounts(batch.Folders, batch.Files);
                    ShowsCloudStatus = batch.InSyncRoot;
                    ListingFailure   = batch.Failure;
                }
            }
            Timing.Note("Folder.batches", batches.ToString());
        }
        catch (OperationCanceledException) { /* superseded by a newer navigation */ }
        catch (Exception ex) { ListingFailure = ex.Message; }
        finally
        {
            scope.Cancel();                       // unblock the producer if still writing
            try { await producer; } catch { /* observe */ }

            // Only finish if we're still the current load; a newer load owns the UI now.
            if (!ct.IsCancellationRequested && _location.CurrentPath == path)
            {
                // Nothing arrived: the folder could not be read (gone, access denied). The rows an in-place
                // reload kept were waiting to be replaced, and there is nothing to replace them with.
                if (batches == 0)
                {
                    Entries.Clear();
                    SetCounts(0, 0);
                    ShowsCloudStatus = false;
                }
                IsLoadingEntries = false;
                if (_resortPending)
                {
                    _resortPending = false;
                    ResortEntries();
                }
                if (_reloadWhenLoaded)
                {
                    _reloadWhenLoaded = false;
                    EntriesLoaded = RefreshEntriesAsync(inPlace: true);
                }
            }
        }
    }

    /// <summary>
    /// Background enumeration. Buffers entries; if the buffer crosses
    /// <see cref="StreamThreshold"/> it streams chunks (listing order, unsorted) so the
    /// first page shows while the rest loads; the first chunk replaces what the list holds and
    /// the rest append. After the first, a chunk is at least <see cref="ChunkSize"/> and holds everything read
    /// while the list was taking the one before, so the read goes at the disk's pace and the list at its own. Smaller folders are sorted and sent as a single replace batch. Reads the
    /// file system's listing, which carries name, size, timestamps, flags and — in a synced folder —
    /// cloud state from one pass, with no second stat per entry.
    /// </summary>
    private async Task ProduceEntriesAsync(string path, ChannelWriter<LoadBatch> writer, CancellationToken ct, bool stream)
    {
        using var _t = Timing.Measure($"Folder.ProduceEntriesAsync {path} (background, incl. writer waits)");
        Exception? error = null;
        try
        {
            var buffer    = new List<FileSystemEntry>(StreamThreshold + 1);
            bool streaming = false;
            string? failure = null;
            int folders = 0, files = 0;
            var companions = _companions.Listing();

            // Only a folder on this machine can be in a sync root; the listing carries each file's state in it.
            bool inSyncRoot = _vfs.ResolveOnDisk(path) is { } realPath && CloudPlaceholders.IsInSyncRoot(realPath);
            var enumerate = Timing.Enabled ? System.Diagnostics.Stopwatch.StartNew() : null;

            try
            {
                await foreach (var listed in _vfs.EnumerateAsync(path, ct))
                {
                    FileSystemEntry entry;
                    try { entry = ToEntry(listed, path); }
                    catch { continue; } // skip an entry we can't read

                    if (companions.Hold(entry)) continue;   // decided once the whole folder has been read

                    if (entry.IsDirectory) folders++; else files++;
                    buffer.Add(entry);

                    if (!streaming)
                    {
                        if (stream && buffer.Count > StreamThreshold)
                        {
                            // The first page goes over at once, so the list shows rows while the rest is read.
                            streaming = true;
                            await writer.WriteAsync(new(buffer, Replace: true, folders, files, inSyncRoot), ct);
                            buffer = new List<FileSystemEntry>(ChunkSize);
                        }
                    }
                    // Never waits on the list: while it is still taking the last batch, what is read here joins the next one.
                    // Each turn of the UI thread ends in a rendered frame, so a read that waited would go at one chunk per frame
                    // however fast the disk answers — seconds for a folder of tens of thousands.
                    else if (buffer.Count >= ChunkSize && writer.TryWrite(new(buffer, false, folders, files, inSyncRoot)))
                    {
                        buffer = new List<FileSystemEntry>(ChunkSize);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Gone, access denied, a dropped connection — part way through, or before the first entry. What was read
                // is listed, and the view says why the rest is not.
                failure = ex.Message;
            }

            // A companion can be read before its file, so they were held back until now: those whose file is here
            // are dropped (the file shows their badge), the rest — orphans — are listed as the ordinary files they are.
            foreach (var orphan in companions.Finish())
            {
                files++;
                buffer.Add(orphan);
            }

            if (Timing.Enabled)
            {
                Timing.Note($"Folder.enumerate+ToEntry {path}", $"{enumerate!.Elapsed.TotalMilliseconds:F1} ms");
                Timing.Note($"Folder.entries {path}", $"{folders} folders, {files} files");
            }

            ct.ThrowIfCancellationRequested();

            if (!streaming)
            {
                // Small/medium folder: sort the whole set (off the UI thread), one Reset.
                List<FileSystemEntry> sorted;
                using (Timing.Measure($"Folder.sort n={buffer.Count}"))
                    sorted = ApplySort(buffer).ToList();
                await writer.WriteAsync(new(sorted, true, folders, files, inSyncRoot, failure), ct);
            }
            else if (buffer.Count > 0 || failure is not null)
            {
                await writer.WriteAsync(new(buffer, false, folders, files, inSyncRoot, failure), ct);
            }
        }
        catch (Exception ex) { error = ex; }
        finally { writer.TryComplete(error); }
    }

    /// <summary>
    /// Builds a row from one entry of the folder's listing; reads nothing more.
    /// <para>
    /// <paramref name="parentPath"/> is the location the user navigated to, which is <b>not</b> always where the bytes
    /// are: under a mount it is the virtual root. The child's path is composed from it, because a mount exists precisely so
    /// that the real location never surfaces. Inside an archive a nested archive is itself browsable, so it is presented as
    /// a folder — it gets the folder affordance and double-click descends into it — while its type still names what it is.
    /// </para>
    /// </summary>
    private FileSystemEntry ToEntry(LocationEntry listed, string parentPath)
    {
        bool inArchive = listed.Archive is not null;
        bool isDir     = listed.IsDirectory || (inArchive && _vfs.IsContainerName(listed.Name));
        var  fullPath  = Path.Combine(parentPath, listed.Name);
        return new FileSystemEntry
        {
            Name        = listed.Name,
            // A location lists a name as the segment its path holds; its row shows the name its folder has.
            ShownName   = _vfs.IsOnLocation(fullPath) ? _vfs.GetDisplayName(fullPath) : listed.Name,
            FullPath    = fullPath,
            IsDirectory = isDir,
            SizeBytes   = isDir ? 0 : listed.Size,
            Modified    = listed.Modified,
            InContainer = inArchive,
            TypeName    = ShellFileTypes.NameFor(listed.IsDirectory ? string.Empty : Path.GetExtension(listed.Name), listed.IsDirectory),
            CloudState  = listed.CloudState,
            PosixMode   = listed.Mode,
            Owner       = listed.Owner,
            Group       = listed.Group,
            SymlinkTarget = listed.SymlinkTarget,
        };
    }

    // ── The shell changing the folder ─────────────────────────────────────────
    // While another part of the app is mid-mutation on this folder (deleting this worktree, say) the shell marks it busy; the
    // view shows a "please wait" panel in place of the list until it clears, then re-evaluates the folder. The shell's signal
    // is followed only while the view is shown, since the shell outlives the tab.

    /// <summary>True while a long mutation is in flight on the open folder.</summary>
    [ObservableProperty] private bool   _isFolderBusy;
    [ObservableProperty] private string _folderBusyMessage = string.Empty;

    /// <summary>Raised when a mutation on the open folder finishes (its busy mark cleared), so the view can refresh or
    /// re-home. Runs on the UI thread.</summary>
    public event Action? FolderBusyCleared;

    /// <summary>Follows the shell's folder-busy signal and syncs the current state, while the view is shown.</summary>
    public void AttachBusyTracking()
    {
        _shell.FolderBusyChanged += OnShellFolderBusyChanged;
        RefreshBusyState();
    }

    /// <summary>Stops following the shell's folder-busy signal, so the long-lived shell does not hold this tab.</summary>
    public void DetachBusyTracking() => _shell.FolderBusyChanged -= OnShellFolderBusyChanged;

    private void OnShellFolderBusyChanged() => RefreshBusyState();

    /// <summary>Re-reads whether the open folder is busy and, on a busy→idle transition, raises
    /// <see cref="FolderBusyCleared"/> so the view re-evaluates the folder.</summary>
    private void RefreshBusyState()
    {
        var message = string.IsNullOrEmpty(_location.CurrentPath) ? null : _shell.GetFolderBusyMessage(_location.CurrentPath);
        var nowBusy = !string.IsNullOrEmpty(message);   // empty message ⇒ not busy
        var wasBusy = IsFolderBusy;

        FolderBusyMessage = message ?? string.Empty;
        IsFolderBusy      = nowBusy;

        if (wasBusy && !nowBusy) FolderBusyCleared?.Invoke();
    }

    private void OnLocationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FolderLocation.CurrentPath)) return;
        RefreshBusyState();
        WatchCurrentFolder();
    }

    // ── Keeping up with the folder ────────────────────────────────────────────

    /// <summary>The open folder's watch, held while the view is shown; null in This PC and inside an archive.</summary>
    private IDisposable? _folderWatch;

    /// <summary>The real directory <see cref="_folderWatch"/> watches, which the shell names changed entries under.</summary>
    private string _watchedPath = string.Empty;

    /// <summary>The view is shown, so the open folder is watched.</summary>
    private bool _watchingFolder;

    /// <summary>The view was hidden while a folder was watched: whatever changed meanwhile is caught up on its return.</summary>
    private bool _missedFolderChanges;

    /// <summary>
    /// Watches the open folder while the view is shown, so the list keeps up with what changes there — a file added or deleted
    /// elsewhere, one a sync provider downloads, freed or failed to sync — without the reader refreshing. Coming back after
    /// being away catches up on what changed meanwhile.
    /// </summary>
    public void AttachFolderWatching()
    {
        _watchingFolder = true;
        WatchCurrentFolder();
        if (!_missedFolderChanges) return;
        _missedFolderChanges = false;
        OnFolderChanged(new FolderChange(new HashSet<string>(), Whole: true));
    }

    public void DetachFolderWatching()
    {
        _watchingFolder = false;
        if (_folderWatch is not null) _missedFolderChanges = true;
        _folderWatch?.Dispose();
        _folderWatch = null;
    }

    private void WatchCurrentFolder()
    {
        _folderWatch?.Dispose();
        _folderWatch = null;
        var path = _location.CurrentPath;
        if (!_watchingFolder || _location.IsThisPc || string.IsNullOrEmpty(path)) return;

        // Inside an archive there is nothing to watch: its container is a file. A folder on disk — a mounted one where it
        // really is — is watched by the shell; one on a location through the location, where it can be.
        if (_location.InContainer) return;
        if (!_vfs.GetBacking(path).IsFullyOnDisk() || _vfs.IsOnLocation(path))
        {
            _ = WatchLocationAsync(path);
            return;
        }
        if (_vfs.TryResolveReal(path) is not { } realPath) return;

        _watchedPath = realPath;
        _folderWatch = _shell.WatchFolder(realPath, OnFolderChanged);
    }

    /// <summary>Watches the open folder on a location that can watch; on one that cannot, the folder is read again when
    /// the reader refreshes. The location reports from its own thread, so the change comes back through the shell.</summary>
    private async Task WatchLocationAsync(string path)
    {
        ILocationWatch? watch;
        try
        {
            watch = await _vfs.WatchChangesAsync(path, recursive: false);
        }
        catch (IOException) { return; }
        if (watch is null) return;

        // A newer folder, or the view going away, while the watch was being set up: it is not wanted any more.
        if (!_watchingFolder || _location.CurrentPath != path || _folderWatch is not null)
        {
            watch.Dispose();
            return;
        }
        watch.Changed += _ => _shell.RunOnUiAsync(() => OnFolderChanged(new FolderChange(new HashSet<string>(), Whole: true)));
        _folderWatch = watch;
    }

    /// <summary>
    /// The open folder changed: the rows the shell named have their sync status read again, and the folder is read again in
    /// place, so every row stays where it was and only what differs changes.
    /// </summary>
    private void OnFolderChanged(FolderChange change)
    {
        if (_location.IsThisPc) return;
        if (IsFolderBusy) return;   // the shell's own operation is changing it; the folder is read again when that finishes

        if (ShowsCloudStatus)
            foreach (var entry in Entries)
                if (change.Touches(Path.Combine(_watchedPath, entry.Name))) entry.RecheckStatus();

        if (IsLoadingEntries)
        {
            _reloadWhenLoaded = true;
            return;
        }
        ReadFolder(inPlace: true);
    }
}
