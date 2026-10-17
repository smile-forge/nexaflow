using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nexaflow.IO.Usb.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Icons;
using Nexaflow.IO.Common.Stores;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Storage;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Stores;

namespace Nexaflow.Features.UsbProbe.StorageTools;

/// <summary>
/// H1, mass storage: the device's mounted volumes in the shared file browser (<see cref="StoreBrowser"/> over a
/// <see cref="DiskStore"/>), with copies both ways — each read back past the cache and checked by SHA-256 — new
/// folders, renames and deletes, every change asked for first.
/// </summary>
public sealed partial class UsbStorageTool : ObservableObject, IUsbTool, IUsbToolDebugDetails, IUsbToolShownFolders
{
    const int ListedEntries = 200;

    readonly UsbFunctionNode _function;
    readonly IUsbStorageLocator _locator;

    readonly DiskStore _store;
    bool _read;

    public UsbStorageTool(UsbFunctionNode function, IUsbStorageLocator locator, IUsbToolHost host)
    {
        _function = function;
        _locator = locator;
        _status = Str.Get("StorageTools.Status.NotRead");
        _store = new DiskStore(Str.Get("StorageTools.Title"), ReadVolumesAsync);
        Browser = new StoreBrowser(_store, new UsbToolStoreHost(this, host), IconRef.Fluent("usb_stick"));
        Browser.RootsRead += OnRootsRead;
        Browser.PropertyChanged += OnBrowserChanged;
    }

    public string ToolId => "usb.storage";
    public string Key => $"{ToolId}:{_function.InstanceId}";
    public string Title => Str.Get("StorageTools.Title");
    public IconRef Icon => IconRef.Fluent("usb_stick");
    public string Description => Str.Get("StorageTools.Description");
    public string? FunctionInstanceId => _function.InstanceId;
    public UsbToolSize Size => UsbToolSize.Wide;
    public bool IsBusy => Browser.IsCopying;

    /// <summary>The device's volumes, browsed and changed.</summary>
    public StoreBrowser Browser { get; }

    /// <summary>The volumes the browser shows, each a folder on this PC shown whole.</summary>
    public IReadOnlyList<string> ShownFolders => [.. Browser.RootInfo.Select(root => _store.LocalPathOf(root.Id)).OfType<string>()];

    [ObservableProperty] private string _status;

    public async Task ActivateAsync(CancellationToken ct)
    {
        if (_read) return;
        _read = true;
        await Browser.RefreshAsync();
    }

    /// <summary>Nothing here is live: a copy in progress carries on with the tile shut.</summary>
    public void Deactivate() { }

    /// <summary>The volumes the storage stack has mounted for this function, read afresh each time the browser asks.</summary>
    async Task<IReadOnlyList<StoreRoot>> ReadVolumesAsync(CancellationToken ct)
    {
        var disks = await Task.Run(() => _locator.DisksOf(_function), ct);
        var volumes = disks.SelectMany(d => d.Volumes).ToList();
        var errors = disks.Where(d => d.Error is not null).Select(d => d.Error!).ToList();
        Browser.EmptyNote = volumes.Count > 0 ? null
            : errors.Count > 0 ? string.Join(" ", errors)
            : disks.Count > 0 ? Str.Get("StorageTools.Note.NoVolume") : Str.Get("StorageTools.Note.NoDisk");
        return [.. volumes.Select(v => new StoreRoot(v.Root, v.DisplayName, v.FileSystem, v.Capacity, v.Free, v.IsReadOnly))];
    }

    void OnRootsRead() =>
        Status = Browser.RootInfo.FirstOrDefault() is { } first ? StoreBrowser.DescribeRoot(first) : Browser.EmptyNote ?? "";

    void OnBrowserChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StoreBrowser.IsCopying)) OnPropertyChanged(nameof(IsBusy));
    }

    // ── The agent ──────────────────────────────────────────────────────────────────────────────────────────

    public string Describe() => _read ? Browser.Describe() : "Not read yet: open the tile or run list_volumes.";

    public IReadOnlyList<UsbToolOperation> Operations =>
    [
        new("list_volumes", "Reads the device's volumes again: drive letters, labels, file systems, free space.", false, [],
            async (_, _) => { _read = true; await Browser.RefreshAsync(); return Describe(); }),
        new("list_folder", "Lists a folder on the device: name, size and date of each entry, folders first.", false,
            [new UsbToolArgument("path", "A folder on one of the device's volumes, e.g. 'E:\\Photos'. Defaults to the folder being browsed.", Required: false)],
            (args, ct) => Browser.ListTextAsync(args.GetValueOrDefault("path"), ListedEntries, ct)),
        new("copy_from_device", "Copies files or folders from the device to a folder on this computer, each read back and checked by SHA-256.", true,
            [new UsbToolArgument("paths", "What to copy: a JSON array of paths on the device, or paths separated by ';'."),
             new UsbToolArgument("destination", "A folder on this computer.")],
            (args, ct) => Browser.CopyOutAsync(Paths(args["paths"]), args["destination"], ct)),
        new("copy_to_device", "Copies files or folders from this computer onto the device, each read back from the device and checked by SHA-256.", true,
            [new UsbToolArgument("paths", "What to copy: a JSON array of paths on this computer, or paths separated by ';'."),
             new UsbToolArgument("folder", "The folder on the device to copy into, e.g. 'E:\\Backups'.")],
            (args, ct) => Browser.CopyInAsync(Paths(args["paths"]), args["folder"], ct)),
    ];

    static IReadOnlyList<string> Paths(string value) =>
        value.TrimStart().StartsWith('[')
            ? JsonSerializer.Deserialize<List<string>>(value) ?? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The disks the storage stack made of the function and what was mounted on them — labels hashed, and of
    /// folder mount points only how many, since their paths are the user's.</summary>
    public async Task<JsonObject> DebugDetailsAsync(UsbDebugMask mask, CancellationToken ct)
    {
        var disks = await Task.Run(() => _locator.DisksOf(_function), ct);
        return new JsonObject
        {
            ["disks"] = new JsonArray([.. disks.Select(d => (JsonNode)new JsonObject
            {
                ["instanceId"] = mask.Text(d.InstanceId),
                ["name"] = mask.Text(d.Name),
                ["diskNumber"] = d.DiskNumber,
                ["size"] = d.Size,
                ["error"] = d.Error,
                ["volumes"] = new JsonArray([.. d.Volumes.Select(v => (JsonNode)new JsonObject
                {
                    ["driveRoots"] = new JsonArray([.. v.MountPoints.Where(p => p.Length <= 3).Select(p => (JsonNode)p)]),
                    ["folderMounts"] = v.MountPoints.Count(p => p.Length > 3),
                    ["label"] = UsbDebugMask.Name(v.Label),
                    ["fileSystem"] = v.FileSystem,
                    ["capacity"] = v.Capacity,
                    ["free"] = v.Free,
                    ["readOnly"] = v.IsReadOnly,
                    ["error"] = v.Error,
                })]),
            })]),
        };
    }

    public void Dispose()
    {
        Browser.RootsRead -= OnRootsRead;
        Browser.PropertyChanged -= OnBrowserChanged;
        Browser.Dispose();
    }
}
