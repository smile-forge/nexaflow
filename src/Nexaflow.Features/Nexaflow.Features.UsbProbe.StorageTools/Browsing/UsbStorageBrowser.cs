using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Icons;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe.StorageTools;

/// <summary>A file or folder in the folder being browsed; <see cref="IsSelected"/> follows the list's selection.</summary>
public sealed partial class UsbFileRow(FileSystemInfo info) : ObservableObject, IClipboardText
{
    /// <summary>A copied row is its path, so it can be pasted straight into a command or a dialog.</summary>
    public string ClipboardText => FullPath;
    public string Name { get; } = info.Name;
    public string FullPath { get; } = info.FullName;
    public bool IsFolder { get; } = info is DirectoryInfo;
    public long Size { get; } = info is FileInfo f ? f.Length : 0;
    public DateTime Modified { get; } = info.LastWriteTime;
    public IconRef Icon => IconRef.Fluent(IsFolder ? "folder" : "document");

    public string SizeText => IsFolder ? "" : SizeFormatter.FormatBytes(Size);
    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");

    [ObservableProperty] private bool _isSelected;
}

/// <summary>One step of the path bar: a volume's root, then each folder down to the one open.</summary>
public sealed record UsbPathCrumb(string Label, string Path) : IClipboardText
{
    public string ClipboardText => Path;
}

/// <summary>The columns the list sorts by.</summary>
public enum UsbFileSortKey { Name, Size, Modified }

/// <summary>
/// Browsing the device's volumes the way the file browser browses a disk: a folder tree whose roots are the volumes, the
/// open folder's contents beside it, a path bar above, and the tree, the list and the path bar kept on the same folder
/// whichever of them moved. Nothing here writes; the tool's copies, renames and deletes come back through
/// <see cref="RefreshAsync"/>.
/// </summary>
public sealed partial class UsbStorageBrowser : ObservableObject
{
    readonly IUsbToolHost _host;
    UsbFolderNode? _selectedNode;
    bool _revealing;

    public UsbStorageBrowser(IUsbToolHost host) => _host = host;

    public ObservableCollection<UsbFolderNode> Roots { get; } = [];
    public ObservableCollection<UsbFileRow> Entries { get; } = [];
    public ObservableCollection<UsbPathCrumb> Crumbs { get; } = [];

    /// <summary>Raised when the rows selected in the list change, for commands that act on them.</summary>
    public event Action? SelectionChanged;

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(UpCommand)), NotifyPropertyChangedFor(nameof(CurrentVolume), nameof(VolumeSummary))]
    private string? _folder;

    /// <summary>Why the open folder could not be read, or what opening a file found; null when all is well.</summary>
    [ObservableProperty] private string? _notice;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(SortColumn))] private UsbFileSortKey _sortKey = UsbFileSortKey.Name;
    [ObservableProperty] private bool _sortAscending = true;
    [ObservableProperty] private string _summary = "";

    /// <summary>The sort column by name, as the column headers carry it.</summary>
    public string SortColumn => SortKey.ToString();

    public IReadOnlyList<UsbFileRow> SelectedRows => [.. Entries.Where(e => e.IsSelected)];

    /// <summary>The volume the open folder is on.</summary>
    public UsbVolumeRow? CurrentVolume => Folder is { } folder ? RootOf(folder)?.Volume : null;

    public string? VolumeSummary => CurrentVolume is { } v ? string.Join(" · ", new[] { v.Name, v.FileSystem, v.Space }.Where(s => s.Length > 0)) : null;

    /// <summary>Shows the device's volumes as the tree's roots, keeping the open folder where its volume is still there.</summary>
    public async Task ShowVolumesAsync(IReadOnlyList<UsbVolumeRow> volumes)
    {
        var wanted = volumes.Select(v => v.Root).ToList();
        if (!wanted.SequenceEqual(Roots.Select(r => r.FullPath), StringComparer.OrdinalIgnoreCase))
        {
            Roots.Clear();
            foreach (var volume in volumes) Roots.Add(UsbFolderNode.ForVolume(volume, OnNodeSelected));
        }
        else
        {
            foreach (var (root, volume) in Roots.Zip(volumes)) root.Update(volume);
        }
        OnPropertyChanged(nameof(CurrentVolume));
        OnPropertyChanged(nameof(VolumeSummary));

        if (Folder is { } open && RootOf(open) is not null && Directory.Exists(open)) await RefreshAsync();
        else if (Roots.FirstOrDefault() is { } first) await NavigateAsync(first.FullPath);
        else Clear();
    }

    /// <summary>Opens <paramref name="folder"/>: lists it, walks the path bar to it and opens the tree down to it. A row
    /// named <paramref name="select"/> is selected once listed — what a copy or a new folder just made.</summary>
    public async Task NavigateAsync(string folder, string? select = null)
    {
        if (RootOf(folder) is null) return;
        List<UsbFileRow> rows;
        try { rows = await Task.Run(() => List(folder)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Notice = e.Message;
            return;
        }

        bool same = Folder is { } open && UsbStoragePaths.Same(open, folder);
        var keep = same ? SelectedRows.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase) : [];
        if (select is not null) keep = [select];

        foreach (var row in Entries) row.PropertyChanged -= OnRowChanged;
        Entries.Clear();
        foreach (var row in Sorted(rows))
        {
            row.IsSelected = keep.Contains(row.Name);
            row.PropertyChanged += OnRowChanged;
            Entries.Add(row);
        }
        Folder = folder;
        Notice = null;
        BuildCrumbs(folder);
        Summarise();
        SelectionChanged?.Invoke();
        await RevealAsync(folder);
    }

    /// <summary>Reads the open folder and the tree's open branches again.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        foreach (var root in Roots) await root.ReloadAsync();
        if (Folder is { } folder) await NavigateAsync(Directory.Exists(folder) ? folder : NearestExisting(folder));
    }

    /// <summary>Up is offered only below a volume's root.</summary>
    bool CanGoUp() => Folder is { } folder && RootOf(folder) is { } root && !UsbStoragePaths.Same(folder, root.FullPath);

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private async Task UpAsync()
    {
        if (Folder is not { } folder || Path.GetDirectoryName(UsbStoragePaths.Trimmed(folder)) is not { } parent) return;
        await NavigateAsync(RootOf(parent) is { } root && UsbStoragePaths.Same(parent, root.FullPath) ? root.FullPath : parent,
            select: Path.GetFileName(UsbStoragePaths.Trimmed(folder)));
    }

    /// <summary>A folder opens in place; a file opens in the app's viewer for it, as from the file browser.</summary>
    [RelayCommand]
    private async Task OpenAsync(object? target)
    {
        switch (target)
        {
            case UsbFileRow { IsFolder: true } folder: await NavigateAsync(folder.FullPath); break;
            case UsbFileRow file:
                if (!_host.OpenFile(file.FullPath)) Notice = Str.Format("StorageTools.Notice.NoViewer", file.Name);
                break;
            case UsbFolderNode node: await NavigateAsync(node.FullPath); break;
            case UsbPathCrumb crumb: await NavigateAsync(crumb.Path); break;
            case null when SelectedRows is [var only]: await OpenAsync(only); break;
        }
    }

    /// <summary>Sorts by a column; the same column again turns the order round. Folders stay above files either way.</summary>
    [RelayCommand]
    private void Sort(string column)
    {
        if (!Enum.TryParse<UsbFileSortKey>(column, out var key)) return;
        if (SortKey == key) SortAscending = !SortAscending;
        else (SortKey, SortAscending) = (key, key == UsbFileSortKey.Name);
        var rows = Sorted(Entries).ToList();
        for (int i = 0; i < rows.Count; i++)
        {
            int at = Entries.IndexOf(rows[i]);
            if (at != i) Entries.Move(at, i);
        }
    }

    public void Clear()
    {
        foreach (var row in Entries) row.PropertyChanged -= OnRowChanged;
        Entries.Clear();
        Crumbs.Clear();
        Folder = null;
        Summarise();
    }

    /// <summary>The tree node at <paramref name="path"/>, where the tree has read that far.</summary>
    public UsbFolderNode? NodeAt(string path)
    {
        if (RootOf(path) is not { } node) return null;
        foreach (var step in StepsBelow(node.FullPath, path))
        {
            if (node.ChildAt(step) is not { } next) return null;
            node = next;
        }
        return node;
    }

    /// <summary>Whether <paramref name="path"/> is on one of the device's volumes.</summary>
    public bool OnDevice(string path) => RootOf(Path.GetFullPath(path)) is not null;

    UsbFolderNode? RootOf(string path) => Roots.FirstOrDefault(r => UsbStoragePaths.Within(path, r.FullPath));

    void OnNodeSelected(UsbFolderNode node)
    {
        // One folder is selected at a time, whether the tree view or the browser itself selected it.
        if (_selectedNode is { } previous && previous != node) previous.IsSelected = false;
        _selectedNode = node;
        if (!_revealing && (Folder is null || !UsbStoragePaths.Same(Folder, node.FullPath))) _ = NavigateAsync(node.FullPath);
    }

    /// <summary>Opens the tree down to <paramref name="folder"/> and selects it, without that selection navigating again.</summary>
    async Task RevealAsync(string folder)
    {
        if (RootOf(folder) is not { } node) return;
        foreach (var step in StepsBelow(node.FullPath, folder))
        {
            await node.LoadAsync();
            node.IsExpanded = true;
            if (node.ChildAt(step) is not { } next) break;
            node = next;
        }
        // The folder open opens in the tree too, as in the file browser, so its subfolders are there to pick.
        if (UsbStoragePaths.Same(node.FullPath, folder)) node.IsExpanded = true;
        _revealing = true;
        try { node.IsSelected = true; }
        finally { _revealing = false; }
    }

    /// <summary>Each folder from just below <paramref name="root"/> down to <paramref name="path"/>.</summary>
    static IEnumerable<string> StepsBelow(string root, string path)
    {
        var steps = new Stack<string>();
        for (var at = UsbStoragePaths.Trimmed(path); !UsbStoragePaths.Same(at, root); at = Path.GetDirectoryName(at)!)
        {
            if (Path.GetDirectoryName(at) is null) break;
            steps.Push(at);
        }
        return steps;
    }

    void BuildCrumbs(string folder)
    {
        Crumbs.Clear();
        if (RootOf(folder) is not { } root) return;
        Crumbs.Add(new UsbPathCrumb(root.Name, root.FullPath));
        foreach (var step in StepsBelow(root.FullPath, folder)) Crumbs.Add(new UsbPathCrumb(Path.GetFileName(step), step));
    }

    string NearestExisting(string folder)
    {
        for (var at = folder; at is not null; at = Path.GetDirectoryName(UsbStoragePaths.Trimmed(at)))
            if (Directory.Exists(at) && RootOf(at) is not null) return at;
        return Roots.First().FullPath;
    }

    void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(UsbFileRow.IsSelected)) return;
        Summarise();
        SelectionChanged?.Invoke();
    }

    void Summarise()
    {
        var selected = Entries.Where(e => e.IsSelected).ToList();
        var files = selected.Where(e => !e.IsFolder).ToList();
        Summary = Folder is null ? ""
            : selected.Count == 0 ? Str.Format("StorageTools.Summary.Items", Entries.Count)
            : files.Count == 0 ? Str.Format("StorageTools.Summary.Selected", Entries.Count, selected.Count)
            : Str.Format("StorageTools.Summary.SelectedSize", Entries.Count, selected.Count, SizeFormatter.FormatBytes(files.Sum(e => e.Size)));
    }

    IEnumerable<UsbFileRow> Sorted(IEnumerable<UsbFileRow> rows)
    {
        var folders = rows.OrderByDescending(r => r.IsFolder);
        var ordered = SortKey switch
        {
            UsbFileSortKey.Size => SortAscending ? folders.ThenBy(r => r.Size) : folders.ThenByDescending(r => r.Size),
            UsbFileSortKey.Modified => SortAscending ? folders.ThenBy(r => r.Modified) : folders.ThenByDescending(r => r.Modified),
            _ => SortAscending
                ? folders.ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                : folders.ThenByDescending(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        return ordered.ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    internal static List<UsbFileRow> List(string folder) =>
        [.. new DirectoryInfo(folder).EnumerateFileSystemInfos().Where(UsbStoragePaths.IsShown).Select(i => new UsbFileRow(i))];
}
