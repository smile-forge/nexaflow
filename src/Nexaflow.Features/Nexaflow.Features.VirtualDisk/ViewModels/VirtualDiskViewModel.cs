using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.VirtualDisk.Models;
using Nexaflow.Features.VirtualDisk.Services;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.VirtualDisk.ViewModels;

/// <summary>
/// The "As Disk" inspector: disk/partition/volume metadata on the left, a <b>lazily-expanded</b> contents
/// tree on the right, and an action bar (Extract-all, Mount). Metadata comes from a short-lived
/// <see cref="DiskImageReader"/> (superblock reads only); the tree reads one directory at a time through
/// <see cref="IVirtualFileSystem"/> as folders are expanded. No disk handle is held between operations, so
/// switching away from and back to the tab leaks nothing.
/// </summary>
public sealed partial class VirtualDiskViewModel : ObservableObject, IPageViewModel
{
    private readonly IShellServices _shell;
    private readonly IVirtualFileSystem _vfs;
    private readonly string _diskPath;

    [ObservableProperty] private string _fileName;
    [ObservableProperty] private string _format = "…";
    [ObservableProperty] private string _capacityText = string.Empty;
    [ObservableProperty] private string _partitionScheme = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _isBusy;

    /// <summary>False until the initial superblock read finishes (success OR failure). Gates the AI send so
    /// the model is never handed the "unrecognised" fallback for a disk that is simply still loading.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsContextReady))]
    private bool _isLoaded;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExtractCommand))]
    private bool _isRecognised;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MountCommand))]
    private bool _canMount;

    /// <summary>Formatted volume rows for the metadata pane.</summary>
    public ObservableCollection<DiskVolumeRow> Volumes { get; } = [];

    /// <summary>Flattened, expand-aware list of visible contents rows.</summary>
    public ObservableCollection<DiskNode> VisibleRows { get; } = [];

    private readonly List<DiskNode> _roots = [];

    /// <summary>The initial-load task — awaited by tests; not part of the UI flow.</summary>
    internal Task WhenLoaded { get; }

    public VirtualDiskViewModel(string path, IShellServices shell, IVirtualFileSystem vfs)
    {
        _shell = shell;
        _vfs = vfs;
        _diskPath = path ?? string.Empty;
        FileName = string.IsNullOrEmpty(_diskPath) ? "Disk image" : Path.GetFileName(_diskPath);
        CanMount = !string.IsNullOrEmpty(_diskPath) && MountSupport.CanMount(_diskPath);
        WhenLoaded = LoadAsync();
    }

    // ── Loading ──────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        // IsLoaded flips true on every exit path (incl. the early returns below), so the readiness gate
        // always releases and GetContext can tell "still loading" apart from "genuinely unreadable".
        try
        {
            if (string.IsNullOrEmpty(_diskPath) || !File.Exists(_diskPath))
            {
                IsRecognised = false;
                Format = "No file";
                StatusText = "No disk image to show.";
                return;
            }

            DiskInfo? info = null;
            string? error = null;
            await RunBusy("Reading disk…", () =>
            {
                try { using var reader = DiskImageReader.Open(_diskPath); info = reader.Describe(); }
                catch (Exception ex) { error = ex.Message; }
            });

            if (info is null || info.Volumes.Count == 0)
            {
                IsRecognised = false;
                Format = info?.Format ?? "Unrecognised";
                StatusText = error is null
                    ? "No readable filesystem found in this image."
                    : $"Couldn't read image: {error}";
                return;
            }

            IsRecognised = true;
            Format = info.Format;
            CapacityText = info.Capacity > 0 ? SizeFormatter.FormatBytes(info.Capacity) : "—";
            PartitionScheme = info.PartitionScheme;
            Volumes.Clear();
            foreach (var v in info.Volumes) Volumes.Add(DiskVolumeRow.From(v));

            await LoadRootAsync();
            StatusText = $"{info.Volumes.Count} volume{(info.Volumes.Count == 1 ? "" : "s")}";
        }
        finally { IsLoaded = true; }
    }

    private async Task LoadRootAsync()
    {
        _roots.Clear();
        IReadOnlyList<VirtualEntry> entries = [];
        await RunBusy("Reading…", () => entries = _vfs.EnumerateEntries(_diskPath));
        foreach (var e in Order(entries))
            _roots.Add(NewNode(e, string.Empty, 0));
        RebuildVisibleRows();
    }

    private static DiskNode NewNode(VirtualEntry e, string parentInner, int depth) => new()
    {
        Name = e.Name,
        InnerPath = parentInner.Length == 0 ? e.Name : parentInner + "/" + e.Name,
        IsFolder = e.IsDirectory,
        Depth = depth,
        Size = e.Size,
        Modified = e.Modified,
    };

    private static IEnumerable<VirtualEntry> Order(IReadOnlyList<VirtualEntry> entries) =>
        entries.OrderBy(e => e.IsDirectory ? 0 : 1)
               .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

    // ── Row interaction (lazy expand) ────────────────────────────────────────

    [RelayCommand]
    private async Task ActivateRow(DiskNode? node)
    {
        // The "As Disk" tab is a read-only display of the image itself — it NEVER opens or launches a file
        // (no internal viewer, no external shell). Content is opened by browsing the disk in the file
        // explorer (double-click the image there). Folders still expand so the on-disk structure can be
        // inspected here.
        if (node is null || !node.IsFolder) return;

        if (node.IsExpanded)
        {
            node.IsExpanded = false;
        }
        else
        {
            await EnsureChildrenAsync(node);
            node.IsExpanded = true;
        }
        RebuildVisibleRows();
    }

    private async Task EnsureChildrenAsync(DiskNode node)
    {
        if (node.Loaded) return;
        var path = VfsPath(node.InnerPath);
        IReadOnlyList<VirtualEntry> entries = [];
        await RunBusy("Reading…", () => entries = _vfs.EnumerateEntries(path));
        node.Children.Clear();
        foreach (var e in Order(entries))
            node.Children.Add(NewNode(e, node.InnerPath, node.Depth + 1));
        node.Loaded = true;
    }

    private string VfsPath(string innerPath) =>
        Path.Combine(_diskPath, innerPath.Replace('/', Path.DirectorySeparatorChar));

    private void RebuildVisibleRows()
    {
        VisibleRows.Clear();
        void Walk(DiskNode n)
        {
            foreach (var child in n.Children)
            {
                VisibleRows.Add(child);
                if (child.IsFolder && child.IsExpanded) Walk(child);
            }
        }
        foreach (var root in _roots)
        {
            VisibleRows.Add(root);
            if (root.IsFolder && root.IsExpanded) Walk(root);
        }
    }

    // ── Actions ──────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(IsRecognised))]
    private async Task Extract()
    {
        var dest = await _shell.PickFolderAsync(Path.GetDirectoryName(_diskPath));
        if (string.IsNullOrEmpty(dest)) return;
        var path = _diskPath;
        await RunBusy("Extracting…", () => _vfs.ExtractAll(path, dest));
        StatusText = $"Extracted to {dest}";
    }

    [RelayCommand(CanExecute = nameof(CanMount))]
    private Task Mount() => DiskMountService.MountAsync(_shell, _diskPath);

    private async Task RunBusy(string status, Action work)
    {
        IsBusy = true;
        StatusText = status;
        try { await Task.Run(work); }
        catch (Exception ex) { StatusText = ex.Message; }
        finally { IsBusy = false; }
    }

    // ── IPageViewModel ───────────────────────────────────────────────────────

    /// <summary>Held until the superblock read finishes, so the AI never sees a mid-load state.</summary>
    public bool IsContextReady => IsLoaded;

    public string GetContext()
    {
        if (!IsLoaded)
            return $"Virtual disk image '{FileName}' — still reading…";
        return IsRecognised
            ? $"Virtual disk image '{FileName}' ({Format}, {PartitionScheme}) — " +
              $"{Volumes.Count} volume{(Volumes.Count == 1 ? "" : "s")}, capacity {CapacityText}."
            : $"Virtual disk image '{FileName}' — unrecognised or unreadable.";
    }
}
