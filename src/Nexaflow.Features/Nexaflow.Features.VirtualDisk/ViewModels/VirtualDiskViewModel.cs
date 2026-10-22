using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.VirtualDisk.Models;
using Nexaflow.Features.VirtualDisk.Services;
using Nexaflow.IO.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

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

    /// <summary>The image's facts as read — invariant values the AI tools report. The bound <c>Format</c>,
    /// <c>PartitionScheme</c> and <see cref="Volumes"/> are their labels in the person's language.</summary>
    private DiskInfo? _info;

    /// <summary>The initial-load task — awaited by tests; not part of the UI flow.</summary>
    internal Task WhenLoaded { get; }

    public VirtualDiskViewModel(string path, IShellServices shell, IVirtualFileSystem vfs)
    {
        _shell = shell;
        _vfs = vfs;
        _diskPath = path ?? string.Empty;
        FileName = string.IsNullOrEmpty(_diskPath) ? Str.Get("VirtualDisk.Info.UntitledImage") : Path.GetFileName(_diskPath);
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
                Format = Str.Get("VirtualDisk.Info.NoFile");
                StatusText = Str.Get("VirtualDisk.Status.NoImage");
                return;
            }

            DiskInfo? info = null;
            string? error = null;
            await RunBusy(Str.Get("VirtualDisk.Status.ReadingDisk"), () =>
            {
                try { using var reader = DiskImageReader.Open(_diskPath); info = reader.Describe(); }
                catch (Exception ex) { error = ex.Message; }
            });

            if (info is null || info.Volumes.Count == 0)
            {
                IsRecognised = false;
                Format = info is null ? Str.Get("VirtualDisk.Info.Unrecognised") : FormatLabel(info.Format);
                StatusText = error is null
                    ? Str.Get("VirtualDisk.Status.NoFileSystem")
                    : Str.Format("VirtualDisk.Status.ReadFailedFormat", error);
                return;
            }

            _info = info;
            IsRecognised = true;
            Format = FormatLabel(info.Format);
            CapacityText = info.Capacity > 0 ? SizeFormatter.FormatBytes(info.Capacity) : "—";
            PartitionScheme = PartitionSchemeLabel(info.PartitionScheme);
            Volumes.Clear();
            foreach (var v in info.Volumes) Volumes.Add(DiskVolumeRow.From(v));

            await LoadRootAsync();
            StatusText = info.Volumes.Count == 1
                ? Str.Get("VirtualDisk.Status.OneVolume")
                : Str.Format("VirtualDisk.Status.VolumesFormat", info.Volumes.Count);
        }
        finally { IsLoaded = true; }
    }

    /// <summary>The format as the metadata pane shows it. Container and filesystem names are technical
    /// notation and read the same in every language; only the raw-image name is a phrase.</summary>
    private static string FormatLabel(string format) =>
        format == DiskInfo.RawDiskFormat ? Str.Get("VirtualDisk.Info.RawDisk") : format;

    /// <summary>The partition scheme as the metadata pane shows it: GPT and MBR are names, the other two are words.</summary>
    private static string PartitionSchemeLabel(string scheme) => scheme switch
    {
        DiskInfo.NoPartitionScheme    => Str.Get("VirtualDisk.Info.NoPartitionScheme"),
        DiskInfo.OtherPartitionScheme => Str.Get("VirtualDisk.Info.OtherPartitionScheme"),
        _                             => scheme,
    };

    private async Task LoadRootAsync()
    {
        _roots.Clear();
        IReadOnlyList<VirtualEntry> entries = [];
        await RunBusy(Str.Get("VirtualDisk.Status.Reading"), () => entries = _vfs.EnumerateEntries(_diskPath));
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
        await RunBusy(Str.Get("VirtualDisk.Status.Reading"), () => entries = _vfs.EnumerateEntries(path));
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
        await RunBusy(Str.Get("VirtualDisk.Status.Extracting"), () => _vfs.ExtractAll(path, dest));
        StatusText = Str.Format("VirtualDisk.Status.ExtractedToFormat", dest);
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

    /// <summary>The image's name as the model reads it. <see cref="FileName"/> is the person's label, which for a
    /// tab with no file is a phrase in their language.</summary>
    private string ContextFileName => string.IsNullOrEmpty(_diskPath) ? "Disk image" : Path.GetFileName(_diskPath);

    public string GetContext()
    {
        if (!IsLoaded)
            return $"Virtual disk image '{ContextFileName}' — still reading…";
        // The image's own facts, not the bound display labels: the model reads English whatever the UI language.
        if (!IsRecognised || _info is not { } info)
            return $"Virtual disk image '{ContextFileName}' — unrecognised or unreadable.";
        return $"Virtual disk image '{ContextFileName}' ({info.Format}, {info.PartitionScheme}) — " +
               $"{Volumes.Count} volume{(Volumes.Count == 1 ? "" : "s")}, capacity {CapacityText}.\n" +
               "(Use list_volumes for the volume table, list_files to browse an in-disk folder, " +
               "or read_file to read one file's text — all read the image without extracting.)";
    }

    /// <summary>The disk-image file path — the scope this page's tools act within (aspect-4 disambiguation).</summary>
    public string? GetSecurityContext() => string.IsNullOrEmpty(_diskPath) ? null : _diskPath;

    /// <summary>The disk image this tab shows.</summary>
    public ContextGrant GetContextGrant() => ContextGrant.OfFile(_diskPath);

    public IReadOnlyList<IClientTool> GetClientTools() =>
    [
        // ── Read / explore (auto-run) — the disk's structure and file contents, without extracting ──
        new DelegateClientTool(
            "list_volumes",
            "List the disk image's volumes — partition scheme, filesystem, label, and used/total size — the "
          + "metadata the one-line summary only counts.",
            [],
            ToolSafety.SafeOperation,
            (_, _) => Task.FromResult(
                IsRecognised && _info is { } info ? ListVolumes(info) : ToolResult.Error("No readable disk image is loaded."))),

        new DelegateClientTool(
            "list_files",
            "List the entries directly under an in-disk folder (a volume or directory) — name, type, size. "
          + "Give the path forward-slash separated (e.g. \"docs\" or \"Volume 1/Windows\"); omit it for the "
          + "disk root. Reads one directory at a time through the image without extracting.",
            [new ClientToolParameter("path", "In-disk folder path, forward-slash separated. Empty = disk root.", Required: false)],
            ToolSafety.SafeOperation,
            (args, _) => Task.FromResult(
                IsRecognised ? ListFiles(ToolArgs.Str(args, "path", "dir", "folder"))
                             : ToolResult.Error("No readable disk image is loaded."))),

        new DelegateClientTool(
            "read_file",
            "Read the text content of one file inside the disk image by its path (e.g. \"readme.txt\" or "
          + "\"docs/guide.txt\"). Reads through the image without extracting to disk; binary, oversized, or "
          + "unreadable files are refused.",
            [new ClientToolParameter("path", "The file's full in-disk path, forward-slash separated.")],
            ToolSafety.SafeOperation,
            (args, _) =>
            {
                if (!IsRecognised) return Task.FromResult(ToolResult.Error("No readable disk image is loaded."));
                var inner = ToolArgs.Str(args, "path", "file", "name");
                return Task.FromResult(string.IsNullOrEmpty(inner)
                    ? ToolResult.Error("No 'path' provided.")
                    : ReadFileText(inner));
            }),
    ];

    // ── AI read-tool helpers ─────────────────────────────────────────────────

    private const int  MaxListedEntries = 1000;
    private const long MaxFileReadBytes = 256 * 1024;

    /// <summary>The volume table for the model, built from the image's own facts: the metadata pane's rows are
    /// labels in the person's language, and the model reads English.</summary>
    private static ToolResult ListVolumes(DiskInfo info)
    {
        var count = $"{info.Volumes.Count} volume{(info.Volumes.Count == 1 ? "" : "s")}";
        var capacity = info.Capacity > 0 ? SizeFormatter.FormatBytes(info.Capacity) : "—";
        var sb = new StringBuilder();
        sb.Append($"{info.Format} image — {count}, {info.PartitionScheme} partition scheme, capacity {capacity}.\n");
        foreach (var v in info.Volumes)
        {
            var detail = v.Label is null ? v.FileSystem : $"{v.FileSystem} · {v.Label}";
            sb.Append($"  Volume {v.Index} — {detail}");
            if (v.Capacity > 0)
                sb.Append($" ({SizeFormatter.FormatBytes(v.Used)} used of {SizeFormatter.FormatBytes(v.Capacity)})");
            sb.Append('\n');
        }
        return ToolResult.Ok(count, sb.ToString().TrimEnd('\n'));
    }

    private ToolResult ListFiles(string? innerPath)
    {
        var inner = (innerPath ?? string.Empty).Replace('\\', '/').Trim('/');
        var full  = VfsPath(inner);
        try
        {
            if (!_vfs.Exists(full))
                return ToolResult.Error(inner.Length == 0
                    ? "The disk root could not be read." : $"No folder '{innerPath}' in the disk.");
            if (!_vfs.IsDirectory(full))
                return ToolResult.Error($"'{inner}' is a file, not a folder — use read_file to read it.");

            var entries = Order(_vfs.EnumerateEntries(full)).ToList();
            if (entries.Count == 0)
                return ToolResult.Ok("empty folder",
                    inner.Length == 0 ? "The disk root is empty." : $"'{inner}' is empty.");

            var sb = new StringBuilder();
            sb.Append(inner.Length == 0 ? "Disk root" : $"'{inner}'");
            sb.Append(". name | type | size\n");
            foreach (var e in entries.Take(MaxListedEntries))
                sb.Append(e.IsDirectory
                    ? $"  {e.Name}/  <dir>\n"
                    : $"  {e.Name}  file  {SizeFormatter.FormatBytes(e.Size)}\n");
            if (entries.Count > MaxListedEntries) sb.Append($"  … ({entries.Count - MaxListedEntries} more)\n");

            var listed = Math.Min(entries.Count, MaxListedEntries);
            return ToolResult.Ok($"{listed} entr{(listed == 1 ? "y" : "ies")} listed", sb.ToString().TrimEnd('\n'));
        }
        catch (Exception ex) { return ToolResult.Error($"Could not list '{inner}': {ex.Message}"); }
    }

    private ToolResult ReadFileText(string innerPath)
    {
        var inner = innerPath.Replace('\\', '/').Trim('/');
        var full  = VfsPath(inner);
        try
        {
            if (!_vfs.Exists(full))     return ToolResult.Error($"No file '{innerPath}' in the disk.");
            if (_vfs.IsDirectory(full)) return ToolResult.Error($"'{inner}' is a folder — use list_files to list it.");

            var size = _vfs.GetLength(full);
            if (size > MaxFileReadBytes)
                return ToolResult.Error(
                    $"'{inner}' is {SizeFormatter.FormatBytes(size)} — too large to read inline "
                  + $"(limit {SizeFormatter.FormatBytes(MaxFileReadBytes)}). Extract it instead.");

            byte[] bytes;
            using (var stream = _vfs.OpenRead(full))
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                bytes = ms.ToArray();
            }

            if (LooksBinary(bytes))
                return ToolResult.Error(
                    $"'{inner}' looks binary ({SizeFormatter.FormatBytes(bytes.LongLength)}) — not shown as text.");

            var text = DecodeText(bytes);
            return ToolResult.Ok($"read {inner} ({SizeFormatter.FormatBytes(bytes.LongLength)})",
                text.Length == 0 ? "(empty file)" : text);
        }
        catch (Exception ex) { return ToolResult.Error($"Could not read '{inner}': {ex.Message}"); }
    }

    /// <summary>NUL-byte heuristic: a text file has none in its first several KB.</summary>
    private static bool LooksBinary(byte[] bytes)
    {
        var n = Math.Min(bytes.Length, 8000);
        for (var i = 0; i < n; i++) if (bytes[i] == 0) return true;
        return false;
    }

    /// <summary>UTF-8 decode, dropping a leading BOM so it doesn't surface as a stray character.</summary>
    private static string DecodeText(byte[] bytes)
    {
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }
}
