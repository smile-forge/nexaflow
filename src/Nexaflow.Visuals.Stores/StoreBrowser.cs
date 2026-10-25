using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Stores;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Visuals.Stores;

/// <summary>
/// A file browser over an <see cref="IFileStore"/> — a USB drive's volumes, a phone over MTP — the way the file browser
/// browses a disk: a folder tree whose roots are the store's, the open folder's contents beside it, a path bar above, and
/// the tree, the list and the path bar kept on the same folder whichever of them moved. It changes the store too:
/// copies both ways, new folders, renames and deletes, each asked for first, every copy read back and checked
/// (<see cref="StoreTransfer"/>).
/// </summary>
/// <remarks>
/// A command takes what it acts on as its parameter — a row, a tree node, a path-bar step — or, given none, the rows
/// selected in the list (and, for one that puts something into a folder, the folder open). Ids are the store's and opaque,
/// so the browser keeps the way it came — root, folder, folder — as the trail it is on.
/// </remarks>
public sealed partial class StoreBrowser : ObservableObject, IDisposable
{
    static readonly DiskStore ThisPc = new("This PC", _ => Task.FromResult<IReadOnlyList<StoreRoot>>([]));

    readonly IFileStore _store;
    readonly IStoreBrowserHost _host;
    readonly CancellationTokenSource _lifetime = new();
    StoreFolderNode? _selectedNode;
    CancellationTokenSource? _copying;
    bool _revealing, _changePending, _disposed;

    public StoreBrowser(IFileStore store, IStoreBrowserHost host, IconRef rootIcon)
    {
        _store = store;
        _host = host;
        RootIcon = rootIcon;
        _store.Changed += OnStoreChanged;
    }

    public IconRef RootIcon { get; }

    /// <summary>What the store is called, for a heading and for the top of a path on a phone.</summary>
    public string StoreName => _store.Name;

    public ObservableCollection<StoreFolderNode> Roots { get; } = [];
    public ObservableCollection<StoreRow> Entries { get; } = [];
    public ObservableCollection<StoreCrumb> Crumbs { get; } = [];

    /// <summary>The store's roots as last read.</summary>
    public IReadOnlyList<StoreRoot> RootInfo { get; private set; } = [];

    /// <summary>The way to the folder open, its root first; empty while nothing is open.</summary>
    public IReadOnlyList<StoreEntry> Trail { get; private set; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CurrentRoot), nameof(RootSummary)), NotifyCanExecuteChangedFor(nameof(UpCommand))]
    private string? _folderPath;

    /// <summary>Why the store shows nothing, from its host — "unlock the phone" — shown when it has no roots.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Note))] private string? _emptyNote;

    /// <summary>The empty note, while the store has no roots to show.</summary>
    public string? Note => RootInfo.Count == 0 ? EmptyNote : null;

    /// <summary>Why the open folder could not be read, or what opening a file found; null when all is well.</summary>
    [ObservableProperty] private string? _notice;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SortColumn))] private StoreSortKey _sortKey = StoreSortKey.Name;
    [ObservableProperty] private bool _sortAscending = true;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private bool _isCopying;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string? _lastResult;

    /// <summary>Raised when the roots have been read: what a host shows of them may have changed.</summary>
    public event Action? RootsRead;

    public string SortColumn => SortKey.ToString();
    public IReadOnlyList<StoreRow> SelectedRows => [.. Entries.Where(e => e.IsSelected)];

    /// <summary>The root the open folder is on.</summary>
    public StoreRoot? CurrentRoot => Trail.Count > 0 ? RootInfo.FirstOrDefault(r => r.Id == Trail[0].Id) : null;

    /// <summary>"E: · FAT32 · 6.8 GB free of 7.3 GB", for the root open.</summary>
    public string? RootSummary => CurrentRoot is { } root ? DescribeRoot(root) : null;

    public static string DescribeRoot(StoreRoot root)
    {
        var parts = new List<string> { root.Name };
        if (!string.IsNullOrEmpty(root.FileSystem)) parts.Add(root.FileSystem);
        if (root.Capacity is { } total && root.Free is { } free)
            parts.Add(Str.Format("Stores.Root.Space", SizeFormatter.FormatBytes(free), SizeFormatter.FormatBytes(total)));
        if (root.IsReadOnly) parts.Add(Str.Get("Stores.Root.ReadOnly"));
        return string.Join(" · ", parts);
    }

    partial void OnIsCopyingChanged(bool value) => NotifyCommands();

    // ── Reading ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Reads the roots and shows them as the tree's top, keeping the open folder where its root is still there.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        IReadOnlyList<StoreRoot> roots;
        try { roots = await _store.RootsAsync(_lifetime.Token); }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            Notice = e.Message;
            roots = [];
        }

        if (!roots.Select(r => r.Id).SequenceEqual(RootInfo.Select(r => r.Id)))
        {
            Roots.Clear();
            foreach (var root in roots) Roots.Add(new StoreFolderNode(this, [root.AsEntry()], root));
        }
        else
        {
            foreach (var (node, root) in Roots.Zip(roots)) node.Root = root;
            foreach (var node in Roots) await node.ReloadAsync();
        }
        RootInfo = roots;
        OnPropertyChanged(nameof(CurrentRoot));
        OnPropertyChanged(nameof(RootSummary));
        OnPropertyChanged(nameof(Note));
        RootsRead?.Invoke();

        if (Trail.Count > 0 && roots.Any(r => r.Id == Trail[0].Id)) await NavigateAsync(Trail, keepSelection: true);
        else if (Roots.FirstOrDefault() is { } first) await NavigateAsync(first.Trail);
        else Clear();
    }

    /// <summary>Opens a folder: lists it, walks the path bar to it and opens the tree down to it. A row named
    /// <paramref name="select"/> is selected once listed — what a copy or a new folder just made.</summary>
    public async Task NavigateAsync(IReadOnlyList<StoreEntry> trail, string? select = null, bool keepSelection = false)
    {
        if (trail.Count == 0) return;
        IReadOnlyList<StoreEntry> entries;
        try { entries = await _store.ListAsync(trail[^1].Id, _lifetime.Token); }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            Notice = e.Message;
            return;
        }

        var keep = keepSelection ? SelectedRows.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        if (select is not null) keep = [select];

        foreach (var row in Entries) row.PropertyChanged -= OnRowChanged;
        Entries.Clear();
        foreach (var entry in Sorted(entries))
        {
            var row = new StoreRow(entry, PathOf([.. trail, entry])) { IsSelected = keep.Contains(entry.Name) };
            row.PropertyChanged += OnRowChanged;
            Entries.Add(row);
        }
        Trail = trail;
        FolderPath = PathOf(trail);
        Notice = null;
        BuildCrumbs();
        Summarise();
        NotifyCommands();
        await RevealAsync(trail);
    }

    /// <summary>Up is offered only below a root.</summary>
    bool CanGoUp() => Trail.Count > 1;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private Task UpAsync() => Trail.Count > 1 ? NavigateAsync(Trail.Take(Trail.Count - 1).ToList(), select: Trail[^1].Name) : Task.CompletedTask;

    /// <summary>A folder opens in place; a file opens in the app's viewer for it — fetched to this PC first, and
    /// checked, when it is on a phone.</summary>
    [RelayCommand]
    private async Task OpenAsync(object? target)
    {
        switch (target)
        {
            case StoreRow { IsFolder: true } folder: await NavigateAsync([.. Trail, folder.Entry]); break;
            case StoreRow file: await OpenFileAsync(file); break;
            case StoreFolderNode node: await NavigateAsync(node.Trail); break;
            case StoreCrumb crumb: await NavigateAsync(crumb.Trail); break;
            case null when SelectedRows is [var only]: await OpenAsync(only); break;
        }
    }

    [LocalDiskOnly("the folder the host keeps on this computer for files fetched off a device")]
    async Task OpenFileAsync(StoreRow file)
    {
        string? local = _store.LocalPathOf(file.Entry.Id);
        if (local is null)
        {
            // On a phone a file has no path here: it is fetched into the folder the host keeps for that, read back like any
            // copy, and opened from there.
            string folder = Directory.CreateDirectory(_host.FetchedFilesFolder).FullName;
            var result = await CopyAsync(_store, [file.Entry], DiskStore.Over(folder), folder, CancellationToken.None, refresh: false);
            if (result is not { Verified: [var fetched] }) return;
            local = System.IO.Path.Combine(folder, fetched.Destination);
        }
        if (!_host.OpenFile(local)) Notice = Str.Format("Stores.Notice.NoViewer", file.Name);
    }

    /// <summary>Sorts by a column; the same column again turns the order round. Folders stay above files either way.</summary>
    [RelayCommand]
    private void Sort(string column)
    {
        if (!Enum.TryParse<StoreSortKey>(column, out var key)) return;
        if (SortKey == key) SortAscending = !SortAscending;
        else (SortKey, SortAscending) = (key, key == StoreSortKey.Name);
        var rows = Sorted(Entries.Select(r => r.Entry)).ToList();
        for (int i = 0; i < rows.Count; i++)
        {
            int at = IndexOf(rows[i].Id);
            if (at != i) Entries.Move(at, i);
        }
    }

    int IndexOf(string id)
    {
        for (int i = 0; i < Entries.Count; i++) if (Entries[i].Entry.Id == id) return i;
        return -1;
    }

    // ── What a command acts on ─────────────────────────────────────────────────────────────────────────────

    /// <summary>The entries a command acts on: the target, or the selected rows. A row that is part of the selection
    /// stands for all of it, as a right-click on a selected row does in the file browser.</summary>
    public IReadOnlyList<StoreEntry> EntriesOf(object? target) => target switch
    {
        StoreRow { IsSelected: true } => [.. SelectedRows.Select(r => r.Entry)],
        StoreRow row => [row.Entry],
        StoreFolderNode node => [node.Entry],
        StoreCrumb crumb => [crumb.Trail[^1]],
        _ => [.. SelectedRows.Select(r => r.Entry)],
    };

    /// <summary>The trail of the folder a command puts something into: a folder target, or the folder open.</summary>
    public IReadOnlyList<StoreEntry> FolderOf(object? target) => target switch
    {
        StoreRow { IsFolder: true } row => [.. Trail, row.Entry],
        StoreFolderNode node => node.Trail,
        StoreCrumb crumb => crumb.Trail,
        _ => Trail,
    };

    /// <summary>The trail an entry the command acts on sits under — its parent folder's.</summary>
    IReadOnlyList<StoreEntry> ParentOf(object? target) => target switch
    {
        StoreFolderNode node when node.Trail.Count > 1 => node.Trail.Take(node.Trail.Count - 1).ToList(),
        StoreCrumb crumb when crumb.Trail.Count > 1 => crumb.Trail.Take(crumb.Trail.Count - 1).ToList(),
        _ => Trail,
    };

    bool IsRoot(StoreEntry entry) => RootInfo.Any(r => r.Id == entry.Id);

    bool Writable(IReadOnlyList<StoreEntry> trail) =>
        !IsCopying && trail.Count > 0 && RootInfo.FirstOrDefault(r => r.Id == trail[0].Id) is { IsReadOnly: false };

    /// <summary>Where a folder or file is, as Copy path gives it: its path on a disk; on a phone the way down to it,
    /// "Galaxy S21\Internal storage\DCIM".</summary>
    public string PathOf(IReadOnlyList<StoreEntry> trail)
    {
        if (trail.Count == 0) return "";
        if (_store.LocalPathOf(trail[^1].Id) is { } local) return local;
        var root = RootInfo.FirstOrDefault(r => r.Id == trail[0].Id);
        return string.Join("\\", new[] { _store.Name, root?.Name ?? trail[0].Name }.Concat(trail.Skip(1).Select(e => e.Name)));
    }

    /// <summary>What Copy path puts on the clipboard: the target's paths, or the open folder's when nothing is picked.</summary>
    public string? PathTextOf(object? target)
    {
        switch (target)
        {
            case StoreFolderNode node: return node.Path;
            case StoreCrumb crumb: return crumb.Path;
        }
        var rows = target is StoreRow { IsSelected: false } row ? [row] : SelectedRows;
        return rows.Count > 0 ? string.Join(Environment.NewLine, rows.Select(r => r.Path)) : FolderPath;
    }

    void NotifyCommands()
    {
        CopyToComputerCommand.NotifyCanExecuteChanged();
        AddFilesCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        NewFolderCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        UpCommand.NotifyCanExecuteChanged();
    }

    // ── Copies ─────────────────────────────────────────────────────────────────────────────────────────────

    bool CanCopyToComputer(object? target) => !IsCopying && EntriesOf(target).Count > 0;

    [RelayCommand(CanExecute = nameof(CanCopyToComputer))]
    private async Task CopyToComputerAsync(object? target)
    {
        var entries = EntriesOf(target);
        if (entries.Count == 0 || await _host.PickFolderAsync() is not { } destination) return;
        await CopyAsync(_store, entries, DiskStore.Over(destination), destination, CancellationToken.None, refresh: false);
    }

    bool CanPutInto(object? target) => Writable(FolderOf(target));

    [RelayCommand(CanExecute = nameof(CanPutInto))]
    private async Task AddFilesAsync(object? target)
    {
        var folder = FolderOf(target);
        var files = await _host.PickOpenFilesAsync();
        if (files.Count > 0) await CopyOntoAsync(files, folder);
    }

    [RelayCommand(CanExecute = nameof(CanPutInto))]
    private async Task AddFolderAsync(object? target)
    {
        var folder = FolderOf(target);
        if (await _host.PickFolderAsync() is { } source) await CopyOntoAsync([source], folder);
    }

    /// <summary>Whether files dragged in from this PC may land in <paramref name="target"/> — a folder row or node, or
    /// the folder open when null.</summary>
    public bool CanDropInto(object? target) => CanPutInto(target);

    /// <summary>Files and folders dropped from this PC, copied into <paramref name="target"/>'s folder — asked first,
    /// like any copy onto the store.</summary>
    public async Task DropAsync(IReadOnlyList<string> paths, object? target)
    {
        var folder = FolderOf(target);
        if (paths.Count > 0 && Writable(folder)) await CopyOntoAsync(paths, folder);
    }

    async Task CopyOntoAsync(IReadOnlyList<string> paths, IReadOnlyList<StoreEntry> folder)
    {
        string where = PathOf(folder);
        if (!await _host.ConfirmAsync(Str.Get("Stores.Confirm.Title"), Str.Format("Stores.Confirm.Message", Phrase(paths), where),
                Str.Get("Stores.Confirm.Copy"), CancellationToken.None))
            return;
        var entries = paths.Select(DiskStore.EntryOf).ToList();
        await CopyAsync(ThisPc, entries, _store, folder[^1].Id, CancellationToken.None, refresh: true, show: folder, select: entries[0].Name);
    }

    /// <summary>Copies from the PC into a folder on the store, for the agent; the folder is a path as Copy path gives it.</summary>
    public async Task<string> CopyInAsync(IReadOnlyList<string> paths, string folderPath, CancellationToken ct)
    {
        if (await ResolveAsync(folderPath, ct) is not { } folder || !folder[^1].IsFolder) return Str.Format("Stores.Agent.NotHere", folderPath, StoreName);
        if (!Writable(folder)) return Str.Format("Stores.Agent.ReadOnly", folderPath);
        await CopyAsync(ThisPc, [.. paths.Select(DiskStore.EntryOf)], _store, folder[^1].Id, ct, refresh: true, show: folder);
        return LastResult ?? "";
    }

    /// <summary>Copies from the store to a folder on this PC, for the agent; the sources are paths as Copy path gives them.</summary>
    public async Task<string> CopyOutAsync(IReadOnlyList<string> paths, string destination, CancellationToken ct)
    {
        var entries = new List<StoreEntry>();
        foreach (var path in paths)
        {
            if (await ResolveAsync(path, ct) is not { } trail) return Str.Format("Stores.Agent.NotHere", path, StoreName);
            entries.Add(trail[^1]);
        }
        await CopyAsync(_store, entries, DiskStore.Over(destination), destination, ct, refresh: false);
        return LastResult ?? "";
    }

    [RelayCommand]
    private void Cancel() => _copying?.Cancel();

    /// <summary>One copy run, reported as it goes and summed up when done; the summary is what the host's log and the
    /// agent hear.</summary>
    async Task<StoreCopyResult?> CopyAsync(IFileStore from, IReadOnlyList<StoreEntry> entries, IFileStore to, string folderId, CancellationToken ct,
        bool refresh, IReadOnlyList<StoreEntry>? show = null, string? select = null)
    {
        if (IsCopying) { LastResult = Str.Get("Stores.Result.Busy"); return null; }
        using var copying = _copying = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        IsCopying = true;
        Progress = 0;
        ProgressText = Str.Get("Stores.Progress.Measuring");
        try
        {
            var result = await StoreTransfer.CopyAsync(from, entries, to, folderId, new Progress<TransferProgress>(Report), copying.Token);
            var sb = new StringBuilder(Str.Format("Stores.Result.Copied", result.Files, SizeFormatter.FormatBytes(result.Bytes), to.LocalPathOf(folderId) ?? PathOf(show ?? Trail)));
            sb.Append(' ').Append(Str.Format("Stores.Result.Verified", result.Verified.Count));
            if (result.Failures.Count > 0) sb.Append(' ').Append(Str.Format("Stores.Result.Failed", result.Failures.Count));
            if (!result.Completed) sb.Append(' ').Append(Str.Get("Stores.Result.Stopped"));
            LastResult = sb.ToString();
            _host.Log(LastResult);
            foreach (var failure in result.Failures) _host.Log($"{failure.Verb} {failure.Path}: {failure.Message}");
            return result;
        }
        catch (OperationCanceledException)
        {
            LastResult = Str.Get("Stores.Result.Cancelled");
            return null;
        }
        finally
        {
            IsCopying = false;
            _copying = null;
            if (refresh && !_disposed) await AfterChangeAsync(show ?? Trail, select);
        }
    }

    void Report(TransferProgress p)
    {
        Progress = p.BytesTotal > 0 ? (double)p.BytesDone / p.BytesTotal : 0;
        string bytes = $"{SizeFormatter.FormatBytes(p.BytesDone)} / {SizeFormatter.FormatBytes(p.BytesTotal)}";
        ProgressText = p.Verifying
            ? Str.Format("Stores.Progress.Verifying", p.CurrentItem ?? "", bytes)
            : Str.Format("Stores.Progress.Copying", p.CurrentItem ?? "", bytes, $"{SizeFormatter.FormatBytes(p.BytesPerSecond)}/s");
    }

    // ── Folders, renames, deletes ──────────────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanPutInto))]
    private async Task NewFolderAsync(object? target)
    {
        var folder = FolderOf(target);
        if (folder.Count == 0) return;
        string suggested = await FreeNameAsync(folder[^1], Str.Get("Stores.NewFolder.Default"));
        if (await _host.PromptAsync(Str.Get("Stores.NewFolder.Title"), Str.Get("Stores.NewFolder.Label"), suggested) is not { } name) return;
        name = name.Trim();
        if (!ValidName(name)) return;
        await ChangeAsync(() => _store.CreateFolderAsync(folder[^1].Id, name, _lifetime.Token),
            Str.Format("Stores.Result.FolderMade", name, PathOf(folder)), folder, name);
    }

    bool CanRename(object? target) =>
        EntriesOf(target) is [var only] && !IsRoot(only) && Writable(target is StoreFolderNode node ? node.Trail : Trail);

    [RelayCommand(CanExecute = nameof(CanRename))]
    private async Task RenameAsync(object? target)
    {
        if (EntriesOf(target) is not [var entry] || IsRoot(entry)) return;
        var parent = ParentOf(target);
        if (await _host.PromptAsync(Str.Get("Stores.Rename.Title"), Str.Get("Stores.Rename.Label"), entry.Name) is not { } name) return;
        name = name.Trim();
        if (name == entry.Name || !ValidName(name)) return;

        // A folder renamed on the way to the open one: the open folder is under its new name afterwards.
        int at = IndexInTrail(entry.Id);
        var above = Trail.Take(Math.Max(at, 0)).ToList();

        StoreEntry? renamed = null;
        await ChangeAsync(async () => renamed = await _store.RenameAsync(entry, name, _lifetime.Token),
            Str.Format("Stores.Result.Renamed", entry.Name, name), at > 0 ? [] : parent, name);
        if (renamed is not null && at > 0) await NavigateAsync([.. above, renamed]);
    }

    bool CanDelete(object? target) =>
        EntriesOf(target) is { Count: > 0 } entries && !entries.Any(IsRoot) && Writable(target is StoreFolderNode node ? node.Trail : Trail);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(object? target)
    {
        var entries = EntriesOf(target);
        if (entries.Count == 0 || entries.Any(IsRoot)) return;
        var parent = ParentOf(target);
        if (!await _host.ConfirmAsync(Str.Get("Stores.Delete.Title"), Str.Format("Stores.Delete.Message", Phrase(entries.Select(e => e.Name).ToList()), PathOf(parent)),
                Str.Get("Stores.Delete.Confirm"), CancellationToken.None))
            return;

        var failures = new List<string>();
        foreach (var entry in entries)
        {
            try { await _store.DeleteAsync(entry, _lifetime.Token); }
            catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException) { failures.Add($"{entry.Name}: {e.Message}"); }
        }
        LastResult = failures.Count == 0
            ? Str.Format("Stores.Result.Deleted", Phrase(entries.Select(e => e.Name).ToList()))
            : Str.Format("Stores.Result.DeleteFailed", failures.Count, string.Join("; ", failures));
        _host.Log(LastResult);

        // The open folder deleted, or one on the way to it: back up to what is left.
        int at = entries.Select(e => IndexInTrail(e.Id)).Where(i => i > 0).DefaultIfEmpty(-1).Min();
        await AfterChangeAsync(at > 0 ? Trail.Take(at).ToList() : Trail, null);
    }

    bool CanCopyPath(object? target) => PathTextOf(target) is not null;

    [RelayCommand(CanExecute = nameof(CanCopyPath))]
    private void CopyPath(object? target)
    {
        if (PathTextOf(target) is not { } text) return;
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException) { LastResult = Str.Get("Stores.Result.ClipboardBusy"); }
    }

    /// <summary>Makes one change to the store, says how it went, and shows the folder it happened in.</summary>
    async Task ChangeAsync(Func<Task> change, string said, IReadOnlyList<StoreEntry> show, string? select)
    {
        try
        {
            await change();
            LastResult = said;
            _host.Log(said);
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            LastResult = e.Message;
            _host.Log(e.Message);
        }
        await AfterChangeAsync(show, select);
    }

    /// <summary>After a change: the roots (and room left) read again, the tree's open branches and the folder listed afresh.</summary>
    async Task AfterChangeAsync(IReadOnlyList<StoreEntry> show, string? select)
    {
        IReadOnlyList<StoreRoot> roots;
        try { roots = await _store.RootsAsync(_lifetime.Token); }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException) { Notice = e.Message; return; }
        foreach (var (node, root) in Roots.Zip(roots)) node.Root = root;
        RootInfo = roots;
        OnPropertyChanged(nameof(CurrentRoot));
        OnPropertyChanged(nameof(RootSummary));
        RootsRead?.Invoke();
        foreach (var node in Roots) await node.ReloadAsync();
        if (show.Count > 0) await NavigateAsync(show, select, keepSelection: select is null);
    }

    bool ValidName(string name)
    {
        if (name.Length > 0 && name is not ("." or "..") && name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0) return true;
        LastResult = Str.Format("Stores.Result.BadName", name);
        return false;
    }

    async Task<string> FreeNameAsync(StoreEntry folder, string name)
    {
        var taken = (await _store.ListAsync(folder.Id, _lifetime.Token)).Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string candidate = name;
        for (int n = 2; taken.Contains(candidate); n++) candidate = $"{name} ({n})";
        return candidate;
    }

    static string Phrase(IReadOnlyList<string> names) =>
        names.Count == 1 ? $"“{System.IO.Path.GetFileName(names[0].TrimEnd('\\'))}”" : Str.Format("Stores.Confirm.Items", names.Count);

    int IndexInTrail(string id)
    {
        for (int i = 0; i < Trail.Count; i++) if (Trail[i].Id == id) return i;
        return -1;
    }

    // ── The agent ──────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The trail to a folder or file given as Copy path gives it — a path on a disk, the way down on a phone
    /// — or null when it is not on this store.</summary>
    public async Task<IReadOnlyList<StoreEntry>?> ResolveAsync(string path, CancellationToken ct)
    {
        var steps = path.Split('\\', StringSplitOptions.RemoveEmptyEntries).ToList();
        foreach (var root in RootInfo)
        {
            List<string> below;
            if (_store.LocalPathOf(root.Id) is { } rootPath)
            {
                string trimmed = rootPath.TrimEnd('\\');
                if (!path.TrimEnd('\\').Equals(trimmed, StringComparison.OrdinalIgnoreCase)
                    && !path.StartsWith(trimmed + "\\", StringComparison.OrdinalIgnoreCase))
                    continue;
                below = [.. path[trimmed.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries)];
            }
            else
            {
                var named = steps.Count > 0 && steps[0].Equals(_store.Name, StringComparison.OrdinalIgnoreCase) ? steps.Skip(1).ToList() : steps;
                if (named.Count == 0 || !named[0].Equals(root.Name, StringComparison.OrdinalIgnoreCase)) continue;
                below = named.Skip(1).ToList();
            }

            var trail = new List<StoreEntry> { root.AsEntry() };
            foreach (var step in below)
            {
                var next = (await _store.ListAsync(trail[^1].Id, ct)).FirstOrDefault(e => e.Name.Equals(step, StringComparison.OrdinalIgnoreCase));
                if (next is null) return null;
                trail.Add(next);
            }
            return trail;
        }
        return null;
    }

    /// <summary>A folder's contents as text, for the agent: name, size and date of each entry, folders first.</summary>
    public async Task<string> ListTextAsync(string? path, int most, CancellationToken ct)
    {
        IReadOnlyList<StoreEntry>? trail = path is null ? Trail : await ResolveAsync(path, ct);
        if (trail is null or { Count: 0 }) return Str.Format("Stores.Agent.NotHere", path ?? "", StoreName);
        var entries = Sorted(await _store.ListAsync(trail[^1].Id, ct)).ToList();
        var sb = new StringBuilder($"{PathOf(trail)}: {entries.Count} item(s)\n");
        foreach (var entry in entries.Take(most))
            sb.AppendLine(entry.IsFolder ? $"  [folder] {entry.Name}" : $"  {entry.Name} — {SizeFormatter.FormatBytes(entry.Size)}, {entry.Modified:yyyy-MM-dd HH:mm}");
        if (entries.Count > most) sb.AppendLine($"  … {entries.Count - most} more");
        return sb.ToString();
    }

    public string Describe()
    {
        var sb = new StringBuilder();
        if (RootInfo.Count == 0) sb.AppendLine(EmptyNote ?? "Nothing shared.");
        foreach (var root in RootInfo) sb.AppendLine($"{DescribeRoot(root)} — at '{PathOf([root.AsEntry()])}'");
        if (FolderPath is { } folder) sb.AppendLine($"Browsing {folder}: {Entries.Count} item(s), {SelectedRows.Count} selected.");
        if (IsCopying) sb.AppendLine($"Copying: {ProgressText}");
        if (LastResult is not null) sb.AppendLine($"Last change: {LastResult}");
        return sb.ToString();
    }

    // ── The tree and the list ──────────────────────────────────────────────────────────────────────────────

    internal async Task<IReadOnlyList<StoreEntry>> FoldersInAsync(StoreEntry folder) =>
        [.. (await _store.ListAsync(folder.Id, _lifetime.Token)).Where(e => e.IsFolder).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)];

    internal void OnNodeSelected(StoreFolderNode node)
    {
        // One folder is selected at a time, whether the tree view or the browser itself selected it.
        if (_selectedNode is { } previous && previous != node) previous.IsSelected = false;
        _selectedNode = node;
        if (!_revealing && !node.Trail.Select(e => e.Id).SequenceEqual(Trail.Select(e => e.Id))) _ = NavigateAsync(node.Trail);
    }

    /// <summary>Opens the tree down to the folder open and selects it, without that selection navigating again.</summary>
    async Task RevealAsync(IReadOnlyList<StoreEntry> trail)
    {
        if (Roots.FirstOrDefault(r => r.Entry.Id == trail[0].Id) is not { } node) return;
        foreach (var step in trail.Skip(1))
        {
            await node.LoadAsync();
            node.IsExpanded = true;
            if (node.ChildWithId(step.Id) is not { } next) break;
            node = next;
        }
        // The folder open opens in the tree too, as in the file browser, so its subfolders are there to pick.
        if (node.Entry.Id == trail[^1].Id) node.IsExpanded = true;
        _revealing = true;
        try { node.IsSelected = true; }
        finally { _revealing = false; }
    }

    void BuildCrumbs()
    {
        Crumbs.Clear();
        for (int i = 1; i <= Trail.Count; i++)
        {
            var trail = Trail.Take(i).ToList();
            string label = i == 1 ? CurrentRoot?.Name ?? trail[0].Name : trail[^1].Name;
            Crumbs.Add(new StoreCrumb(label, trail, PathOf(trail)));
        }
    }

    void Clear()
    {
        foreach (var row in Entries) row.PropertyChanged -= OnRowChanged;
        Entries.Clear();
        Crumbs.Clear();
        Trail = [];
        FolderPath = null;
        Summarise();
        NotifyCommands();
    }

    void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(StoreRow.IsSelected)) return;
        Summarise();
        NotifyCommands();
    }

    void Summarise()
    {
        var selected = Entries.Where(e => e.IsSelected).ToList();
        var files = selected.Where(e => !e.IsFolder).ToList();
        Summary = FolderPath is null ? ""
            : selected.Count == 0 ? Str.Format("Stores.Summary.Items", Entries.Count)
            : files.Count == 0 ? Str.Format("Stores.Summary.Selected", Entries.Count, selected.Count)
            : Str.Format("Stores.Summary.SelectedSize", Entries.Count, selected.Count, SizeFormatter.FormatBytes(files.Sum(e => e.Size)));
    }

    IEnumerable<StoreEntry> Sorted(IEnumerable<StoreEntry> entries)
    {
        var folders = entries.OrderByDescending(e => e.IsFolder);
        var ordered = SortKey switch
        {
            StoreSortKey.Size => SortAscending ? folders.ThenBy(e => e.Size) : folders.ThenByDescending(e => e.Size),
            StoreSortKey.Modified => SortAscending ? folders.ThenBy(e => e.Modified) : folders.ThenByDescending(e => e.Modified),
            _ => SortAscending
                ? folders.ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                : folders.ThenByDescending(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        return ordered.ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>The store changed on its own — a phone unlocked, a file added on it: read it again, once, on the UI thread.</summary>
    void OnStoreChanged(object? sender, EventArgs e)
    {
        if (_disposed || _changePending) return;
        _changePending = true;
        _ = Task.Delay(400).ContinueWith(_ => _host.RunOnUiAsync(() =>
        {
            _changePending = false;
            if (!_disposed && !IsCopying) _ = RefreshAsync();
        }), TaskScheduler.Default);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _store.Changed -= OnStoreChanged;
        _lifetime.Cancel();
        _copying?.Cancel();
        _store.Dispose();
    }
}
