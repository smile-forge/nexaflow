using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Icons;
using Nexaflow.IO.Common;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Storage;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Visuals.Common.Behaviors;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe.StorageTools;

/// <summary>One mounted volume of the device.</summary>
public sealed class UsbVolumeRow(UsbVolume volume) : IClipboardText
{
    public string ClipboardText => $"{Name}\t{FileSystem}\t{Space}";
    public UsbVolume Volume { get; } = volume;
    public string Name => Volume.DisplayName;
    public string Root => Volume.Root;
    public string FileSystem => Volume.FileSystem ?? "";
    public bool IsReadOnly => Volume.IsReadOnly;
    public string Space => Volume.Capacity is { } total && Volume.Free is { } free
        ? Str.Format("StorageTools.Volume.Space", SizeFormatter.FormatBytes(free), SizeFormatter.FormatBytes(total))
        : Volume.Error ?? "";
}

/// <summary>
/// H1, mass storage: the device's volumes in a file browser (<see cref="UsbStorageBrowser"/>), and what changes them —
/// copies both ways, new folders, renames and deletes. Every copy is read back from where it landed and checked against
/// its source's SHA-256 before it counts; every change to the device is asked for first.
/// </summary>
/// <remarks>
/// Copies go through <see cref="FileTransferEngine"/>, the same engine the file browser uses, always with
/// <see cref="FileTransferRequest.Verify"/> on: a stick that accepts bytes and keeps different ones is caught here, and
/// the bad copy removed, rather than found out later. There is no way to copy without it.
/// <para>
/// A command takes what it acts on as its parameter — a row, a tree node, a path-bar step — or, given none, the rows
/// selected in the list (and, for one that puts something into a folder, the folder open).
/// </para>
/// </remarks>
public sealed partial class UsbStorageTool : ObservableObject, IUsbTool
{
    const int ListedEntries = 200;

    readonly UsbFunctionNode _function;
    readonly IUsbStorageLocator _locator;
    readonly IUsbToolHost _host;
    CancellationTokenSource? _copying;
    bool _read, _disposed;

    public UsbStorageTool(UsbFunctionNode function, IUsbStorageLocator locator, IUsbToolHost host)
    {
        _function = function;
        _locator = locator;
        _host = host;
        _status = Str.Get("StorageTools.Status.NotRead");
        Browser = new UsbStorageBrowser(host);
        Browser.SelectionChanged += NotifyCommands;
        Browser.PropertyChanged += OnBrowserChanged;
    }

    public string ToolId => "usb.storage";
    public string Key => $"{ToolId}:{_function.InstanceId}";
    public string Title => Str.Get("StorageTools.Title");
    public IconRef Icon => IconRef.Fluent("usb_stick");
    public string Description => Str.Get("StorageTools.Description");
    public string? FunctionInstanceId => _function.InstanceId;
    public UsbToolSize Size => UsbToolSize.Wide;
    public bool IsBusy => IsCopying;

    public UsbStorageBrowser Browser { get; }
    public ObservableCollection<UsbVolumeRow> Volumes { get; } = [];

    [ObservableProperty] private string _status;
    [ObservableProperty] private string? _note;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsBusy))] private bool _isCopying;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string? _lastResult;

    partial void OnIsCopyingChanged(bool value) => NotifyCommands();

    public async Task ActivateAsync(CancellationToken ct)
    {
        if (!_read) await RefreshAsync();
    }

    /// <summary>Nothing here is live: a copy in progress carries on with the tile shut.</summary>
    public void Deactivate() { }

    // ── Volumes ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Reads the device's volumes again, then the open folder and the tree's open branches.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        var disks = await Task.Run(() => _locator.DisksOf(_function));
        _read = true;
        Volumes.Clear();
        foreach (var volume in disks.SelectMany(d => d.Volumes)) Volumes.Add(new UsbVolumeRow(volume));
        var errors = disks.Where(d => d.Error is not null).Select(d => d.Error!).ToList();
        Note = Volumes.Count > 0 ? null
            : errors.Count > 0 ? string.Join(" ", errors)
            : disks.Count > 0 ? Str.Get("StorageTools.Note.NoVolume") : Str.Get("StorageTools.Note.NoDisk");
        await Browser.ShowVolumesAsync(Volumes);
        Status = Browser.CurrentVolume is { } v ? $"{v.Name} · {v.Space}" : Note ?? "";
    }

    void OnBrowserChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UsbStorageBrowser.Folder)) NotifyCommands();
    }

    void NotifyCommands()
    {
        CopyToComputerCommand.NotifyCanExecuteChanged();
        AddFileCommand.NotifyCanExecuteChanged();
        AddFolderCommand.NotifyCanExecuteChanged();
        NewFolderCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
    }

    // ── What a command acts on ─────────────────────────────────────────────────────────────────────────────

    /// <summary>The paths a command acts on: the target, or the selected rows. A row that is part of the selection
    /// stands for all of it, as a right-click on a selected row does in the file browser.</summary>
    internal IReadOnlyList<string> PathsOf(object? target) => target switch
    {
        UsbFileRow { IsSelected: true } => [.. Browser.SelectedRows.Select(r => r.FullPath)],
        UsbFileRow row => [row.FullPath],
        UsbFolderNode node => [node.FullPath],
        UsbPathCrumb crumb => [crumb.Path],
        string path => [path],
        _ => [.. Browser.SelectedRows.Select(r => r.FullPath)],
    };

    /// <summary>The folder a command puts something into: a folder target, or the folder open.</summary>
    internal string? FolderOf(object? target) => target switch
    {
        UsbFileRow { IsFolder: true } row => row.FullPath,
        UsbFolderNode node => node.FullPath,
        UsbPathCrumb crumb => crumb.Path,
        string path when Directory.Exists(path) => path,
        _ => Browser.Folder,
    };

    bool IsVolumeRoot(string path) => Volumes.Any(v => UsbStoragePaths.Same(v.Root, path));

    bool Writable(string? folder) =>
        !IsCopying && folder is not null && Volumes.FirstOrDefault(v => UsbStoragePaths.Within(folder, v.Root)) is { IsReadOnly: false };

    /// <summary>What Copy path puts on the clipboard: the target's paths, or the open folder's when nothing is picked.</summary>
    public string? PathTextOf(object? target)
    {
        var paths = PathsOf(target);
        return paths.Count > 0 ? string.Join(Environment.NewLine, paths) : Browser.Folder;
    }

    // ── Copies ─────────────────────────────────────────────────────────────────────────────────────────────

    bool CanCopyToComputer(object? target) => !IsCopying && PathsOf(target).Count > 0;

    [RelayCommand(CanExecute = nameof(CanCopyToComputer))]
    private async Task CopyToComputerAsync(object? target)
    {
        var sources = PathsOf(target);
        if (sources.Count == 0 || await _host.PickFolderAsync() is not { } destination) return;
        await CopyAsync(sources, destination, CancellationToken.None);
    }

    bool CanPutInto(object? target) => Writable(FolderOf(target));

    [RelayCommand(CanExecute = nameof(CanPutInto))]
    private async Task AddFileAsync(object? target)
    {
        if (FolderOf(target) is not { } folder || await _host.PickOpenFileAsync(null) is not { } file) return;
        await CopyOntoDeviceAsync([file], folder);
    }

    [RelayCommand(CanExecute = nameof(CanPutInto))]
    private async Task AddFolderAsync(object? target)
    {
        if (FolderOf(target) is not { } folder || await _host.PickFolderAsync() is not { } source) return;
        await CopyOntoDeviceAsync([source], folder);
    }

    /// <summary>Files and folders dropped onto the browser from elsewhere, copied into <paramref name="folder"/> (the
    /// folder open when null) — asked first, like any copy onto the device.</summary>
    public async Task DropAsync(IReadOnlyList<string> sources, string? folder)
    {
        folder ??= Browser.Folder;
        if (folder is null || !Writable(folder)) return;
        var outside = sources.Where(s => !UsbStoragePaths.Within(folder, s)).ToList();
        if (outside.Count > 0) await CopyOntoDeviceAsync(outside, folder);
    }

    public bool CanDropInto(string? folder) => Writable(folder ?? Browser.Folder);

    async Task CopyOntoDeviceAsync(IReadOnlyList<string> sources, string folder)
    {
        if (!await _host.ConfirmAsync(Str.Get("StorageTools.Confirm.Title"), Str.Format("StorageTools.Confirm.Message", Phrase(sources), folder),
                CancellationToken.None, Str.Get("StorageTools.Confirm.Copy")))
            return;
        await CopyAsync(sources, folder, CancellationToken.None);
    }

    [RelayCommand]
    private void Cancel() => _copying?.Cancel();

    /// <summary>One copy run, reported as it goes and summed up when done; the summary is also what the agent reads.</summary>
    async Task<string> CopyAsync(IReadOnlyList<string> sources, string destination, CancellationToken ct)
    {
        if (IsCopying) return Str.Get("StorageTools.Result.Busy");
        using var copying = _copying = CancellationTokenSource.CreateLinkedTokenSource(ct);
        IsCopying = true;
        Progress = 0;
        ProgressText = Str.Get("StorageTools.Progress.Measuring");
        try
        {
            var scan = await FileTransferEngine.ScanAsync(sources, copying.Token);
            var items = sources.Select(s => new TransferItem(s, Path.Combine(destination, Path.GetFileName(UsbStoragePaths.Trimmed(s))))).ToList();
            var progress = new Progress<TransferProgress>(Report);
            var result = await FileTransferEngine.RunAsync(new FileTransferRequest(TransferKind.Copy, items, ConflictPolicy.AutoRename, Verify: true),
                scan, progress, null, copying.Token);

            var summary = Summarise(result, destination);
            LastResult = summary;
            _host.Log(this, summary);
            foreach (var failure in result.Failures) _host.Log(this, $"{failure.Verb} {failure.Path}: {failure.Message}");
            return summary;
        }
        catch (OperationCanceledException)
        {
            LastResult = Str.Get("StorageTools.Result.Cancelled");
            return LastResult;
        }
        finally
        {
            IsCopying = false;
            _copying = null;
            if (!_disposed) await AfterChangeAsync(destination, sources.Count > 0 ? Path.GetFileName(UsbStoragePaths.Trimmed(sources[0])) : null);
        }
    }

    void Report(TransferProgress p)
    {
        Progress = p.BytesTotal > 0 ? (double)p.BytesDone / p.BytesTotal : 0;
        string bytes = $"{SizeFormatter.FormatBytes(p.BytesDone)} / {SizeFormatter.FormatBytes(p.BytesTotal)}";
        ProgressText = p.Verifying
            ? Str.Format("StorageTools.Progress.Verifying", p.CurrentItem ?? "", bytes)
            : Str.Format("StorageTools.Progress.Copying", p.CurrentItem ?? "", bytes, $"{SizeFormatter.FormatBytes(p.BytesPerSecond)}/s");
    }

    static string Summarise(TransferResult result, string destination)
    {
        var sb = new StringBuilder(Str.Format("StorageTools.Result.Copied", result.ItemsTransferred, SizeFormatter.FormatBytes(result.BytesTransferred), destination));
        sb.Append(' ').Append(Str.Format("StorageTools.Result.Verified", result.Verified.Count));
        if (result.Failures.Count > 0) sb.Append(' ').Append(Str.Format("StorageTools.Result.Failed", result.Failures.Count));
        if (!result.Completed) sb.Append(' ').Append(Str.Get("StorageTools.Result.Stopped"));
        return sb.ToString();
    }

    // ── Folders, renames, deletes ──────────────────────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanPutInto))]
    private async Task NewFolderAsync(object? target)
    {
        if (FolderOf(target) is not { } folder) return;
        string suggested = UniqueName(folder, Str.Get("StorageTools.NewFolder.Default"));
        if (await _host.PromptAsync(Str.Get("StorageTools.NewFolder.Title"), Str.Get("StorageTools.NewFolder.Label"), suggested) is not { } name) return;
        if (!ValidName(name)) return;
        await ChangeAsync(() => Directory.CreateDirectory(Path.Combine(folder, name.Trim())),
            Str.Format("StorageTools.Result.FolderMade", name.Trim(), folder), folder, name.Trim());
    }

    bool CanRename(object? target) => PathsOf(target) is [var only] && !IsVolumeRoot(only) && Writable(only);

    [RelayCommand(CanExecute = nameof(CanRename))]
    private async Task RenameAsync(object? target)
    {
        if (PathsOf(target) is not [var path] || IsVolumeRoot(path)) return;
        string old = Path.GetFileName(UsbStoragePaths.Trimmed(path));
        string parent = Path.GetDirectoryName(UsbStoragePaths.Trimmed(path))!;
        if (await _host.PromptAsync(Str.Get("StorageTools.Rename.Title"), Str.Get("StorageTools.Rename.Label"), old) is not { } name) return;
        name = name.Trim();
        if (name == old || !ValidName(name)) return;
        string renamed = Path.Combine(parent, name);
        bool openWithin = Browser.Folder is { } open && UsbStoragePaths.Within(open, path);
        await ChangeAsync(() =>
            {
                if (Directory.Exists(path)) Directory.Move(path, renamed);
                else File.Move(path, renamed);
            },
            Str.Format("StorageTools.Result.Renamed", old, name), openWithin ? renamed : parent, openWithin ? null : name);
    }

    bool CanDelete(object? target) => PathsOf(target) is { Count: > 0 } paths && !paths.Any(IsVolumeRoot) && paths.All(Writable);

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync(object? target)
    {
        var paths = PathsOf(target);
        if (paths.Count == 0 || paths.Any(IsVolumeRoot)) return;
        string parent = Path.GetDirectoryName(UsbStoragePaths.Trimmed(paths[0]))!;
        if (!await _host.ConfirmAsync(Str.Get("StorageTools.Delete.Title"), Str.Format("StorageTools.Delete.Message", Phrase(paths), parent),
                CancellationToken.None, Str.Get("StorageTools.Delete.Confirm")))
            return;

        var failures = new List<string>();
        await Task.Run(() =>
        {
            foreach (var path in paths)
            {
                try
                {
                    if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else
                    {
                        File.SetAttributes(path, FileAttributes.Normal);
                        File.Delete(path);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    lock (failures) failures.Add($"{Path.GetFileName(path)}: {e.Message}");
                }
            }
        });
        string said = failures.Count == 0
            ? Str.Format("StorageTools.Result.Deleted", Phrase(paths))
            : Str.Format("StorageTools.Result.DeleteFailed", failures.Count, string.Join("; ", failures));
        LastResult = said;
        _host.Log(this, said);
        await AfterChangeAsync(Browser.Folder is { } open && paths.Any(p => UsbStoragePaths.Within(open, p)) ? parent : Browser.Folder, null);
    }

    bool CanCopyPath(object? target) => PathTextOf(target) is not null;

    [RelayCommand(CanExecute = nameof(CanCopyPath))]
    private void CopyPath(object? target)
    {
        if (PathTextOf(target) is not { } text) return;
        try { Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException) { LastResult = Str.Get("StorageTools.Result.ClipboardBusy"); }
    }

    /// <summary>Makes one change on the device, says how it went, and shows the folder it happened in.</summary>
    async Task ChangeAsync(Action change, string said, string show, string? select)
    {
        try
        {
            await Task.Run(change);
            LastResult = said;
            _host.Log(this, said);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LastResult = e.Message;
            _host.Log(this, e.Message);
        }
        await AfterChangeAsync(show, select);
    }

    /// <summary>After a write: free space read again, the tree's open branches and <paramref name="folder"/> listed afresh.</summary>
    async Task AfterChangeAsync(string? folder, string? select)
    {
        await RefreshAsync();
        if (folder is not null && Browser.OnDevice(folder) && Directory.Exists(folder)) await Browser.NavigateAsync(folder, select);
    }

    bool ValidName(string name)
    {
        name = name.Trim();
        if (name.Length > 0 && name is not ("." or "..") && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) return true;
        LastResult = Str.Format("StorageTools.Result.BadName", name);
        return false;
    }

    static string UniqueName(string folder, string name)
    {
        string candidate = name;
        for (int n = 2; Directory.Exists(Path.Combine(folder, candidate)) || File.Exists(Path.Combine(folder, candidate)); n++)
            candidate = $"{name} ({n})";
        return candidate;
    }

    static string Phrase(IReadOnlyList<string> paths) =>
        paths.Count == 1 ? $"“{Path.GetFileName(UsbStoragePaths.Trimmed(paths[0]))}”" : Str.Format("StorageTools.Confirm.Items", paths.Count);

    // ── The agent ──────────────────────────────────────────────────────────────────────────────────────────

    public string Describe()
    {
        var sb = new StringBuilder();
        if (!_read) return "Not read yet: open the tile or run list_volumes.";
        if (Volumes.Count == 0) sb.AppendLine(Note ?? "No volumes.");
        foreach (var v in Volumes)
            sb.AppendLine($"Volume {v.Name} ({v.FileSystem}) at {v.Root}: {v.Space}{(v.IsReadOnly ? ", read-only" : "")}");
        if (Browser.Folder is { } folder)
            sb.AppendLine($"Browsing {folder}: {Browser.Entries.Count} item(s), {Browser.SelectedRows.Count} selected.");
        if (IsCopying) sb.AppendLine($"Copying: {ProgressText}");
        if (LastResult is not null) sb.AppendLine($"Last change: {LastResult}");
        return sb.ToString();
    }

    public IReadOnlyList<UsbToolOperation> Operations =>
    [
        new("list_volumes", "Reads the device's disks and volumes again: drive letters, labels, file systems, free space.", false, [],
            async (_, _) => { await RefreshAsync(); return Describe(); }),
        new("list_folder", "Lists a folder on the device: name, size and date of each entry, folders first.", false,
            [new UsbToolArgument("path", "A folder on one of the device's volumes, e.g. 'E:\\Photos'. Defaults to the folder being browsed.", Required: false)],
            (args, _) => Task.FromResult(ListFolder(args.GetValueOrDefault("path") ?? Browser.Folder))),
        new("copy_from_device", "Copies files or folders from the device to a folder on this computer, each read back and checked by SHA-256.", true,
            [new UsbToolArgument("paths", "What to copy: a JSON array of paths on the device, or paths separated by ';'."),
             new UsbToolArgument("destination", "A folder on this computer.")],
            (args, ct) => CopyFromDeviceAsync(args, ct)),
        new("copy_to_device", "Copies files or folders from this computer onto the device, each read back from the device and checked by SHA-256.", true,
            [new UsbToolArgument("paths", "What to copy: a JSON array of paths on this computer, or paths separated by ';'."),
             new UsbToolArgument("folder", "The folder on the device to copy into, e.g. 'E:\\Backups'.")],
            (args, ct) => CopyToDeviceAsync(args, ct)),
    ];

    string ListFolder(string? path)
    {
        if (path is null) return "No folder is open; give a path on one of the device's volumes.";
        if (!Browser.OnDevice(path)) return $"'{path}' is not on this device. Its volumes: {string.Join(", ", Volumes.Select(v => v.Root))}.";
        var rows = UsbStorageBrowser.List(path).OrderByDescending(r => r.IsFolder).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        var sb = new StringBuilder($"{path}: {rows.Count} item(s)\n");
        foreach (var row in rows.Take(ListedEntries))
            sb.AppendLine(row.IsFolder ? $"  [folder] {row.Name}" : $"  {row.Name} — {row.SizeText}, {row.ModifiedText}");
        if (rows.Count > ListedEntries) sb.AppendLine($"  … {rows.Count - ListedEntries} more");
        return sb.ToString();
    }

    async Task<string> CopyFromDeviceAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        var paths = Paths(args["paths"]);
        if (paths.FirstOrDefault(p => !Browser.OnDevice(p)) is { } stray) return $"'{stray}' is not on this device.";
        return await CopyAsync(paths, args["destination"], ct);
    }

    async Task<string> CopyToDeviceAsync(IReadOnlyDictionary<string, string> args, CancellationToken ct)
    {
        var folder = args["folder"];
        if (!Browser.OnDevice(folder)) return $"'{folder}' is not on this device. Its volumes: {string.Join(", ", Volumes.Select(v => v.Root))}.";
        return await CopyAsync(Paths(args["paths"]), folder, ct);
    }

    static IReadOnlyList<string> Paths(string value) =>
        value.TrimStart().StartsWith('[')
            ? JsonSerializer.Deserialize<List<string>>(value) ?? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void Dispose()
    {
        _disposed = true;
        _copying?.Cancel();
        Browser.SelectionChanged -= NotifyCommands;
        Browser.PropertyChanged -= OnBrowserChanged;
    }
}
