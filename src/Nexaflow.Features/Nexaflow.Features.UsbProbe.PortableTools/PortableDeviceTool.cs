using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nexaflow.IO.Usb.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Icons;
using Nexaflow.IO.Common.Portable;
using Nexaflow.IO.Common.Stores;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Plugins;
using Nexaflow.Visuals.Common.Localization;
using Nexaflow.Visuals.Stores;

namespace Nexaflow.Features.UsbProbe.PortableTools;

/// <summary>
/// Offers a files tool for every function a phone or camera shares its storage through: MTP (or PTP, which MTP extends)
/// — the still-image class, Windows' MTP drivers, or a portable device Windows made of the function.
/// </summary>
[Subfeature("usbprobe", "portable",
    DisplayName = "Phone and camera files",
    Description = "Browses a phone's or camera's storage over MTP and copies files to and from it, reading every copy back to check it.",
    Order = 1)]
public sealed class PortableToolProvider : IUsbToolProvider
{
    static readonly HashSet<string> MtpServices = new(StringComparer.OrdinalIgnoreCase) { "WUDFWpdMtp", "WpdUsb", "WUDFWpdFs" };

    readonly Func<string, IReadOnlyList<string>> _devicePaths;
    readonly Func<string, IFileStore> _open;

    public PortableToolProvider() : this(WpdStore.DevicePathsOf, path => WpdStore.Open(path)) { }

    /// <param name="devicePaths">The portable-device paths Windows gives a devnode.</param>
    /// <param name="open">Opens one of them as a store.</param>
    internal PortableToolProvider(Func<string, IReadOnlyList<string>> devicePaths, Func<string, IFileStore> open)
    {
        _devicePaths = devicePaths;
        _open = open;
    }

    public Uri Templates { get; } = new("/Nexaflow.Features.UsbProbe.PortableTools;component/Views/PortableTemplates.xaml", UriKind.Relative);

    public IReadOnlyList<IUsbTool> Offer(UsbDeviceNode device, IUsbToolHost host) =>
    [
        .. device.Functions
            .Where(f => f.Mode != UsbFunctionMode.CompositeParent)
            .Where(f => MtpServices.Contains(f.Driver.Service ?? "")
                     || device.Speaks(f, UsbClassCodes.Image)
                     || f.Children.Any(c => string.Equals(c.ClassName, "WPD", StringComparison.OrdinalIgnoreCase)))
            .Select(f => new PortableDeviceTool(device, f, host, _devicePaths, _open)),
    ];
}

/// <summary>
/// A phone's or camera's files over MTP, in the shared file browser: its storages (internal, SD card) as the tree's
/// roots, copies both ways read back and checked, new folders, renames and deletes, every change asked for first.
/// </summary>
/// <remarks>
/// A phone shares nothing while it is locked or set to charge only, and its USB device does not change when it is
/// unlocked — only what it shares over MTP does. So the device is opened when the tile is, and the store's own events
/// (a storage shared on unlocking) bring the browser back to read it again.
/// </remarks>
public sealed partial class PortableDeviceTool : ObservableObject, IUsbTool, IUsbToolDebugDetails
{
    const int ListedEntries = 200;

    readonly UsbDeviceNode _device;
    readonly UsbFunctionNode _function;
    readonly IUsbToolHost _host;
    readonly Func<string, IReadOnlyList<string>> _devicePaths;
    readonly Func<string, IFileStore> _open;

    IFileStore? _store;
    bool _disposed;

    internal PortableDeviceTool(UsbDeviceNode device, UsbFunctionNode function, IUsbToolHost host,
        Func<string, IReadOnlyList<string>> devicePaths, Func<string, IFileStore> open)
    {
        _device = device;
        _function = function;
        _host = host;
        _devicePaths = devicePaths;
        _open = open;
        _status = Str.Get("PortableTools.Status.NotRead");
    }

    public string ToolId => "usb.portable";
    public string Key => $"{ToolId}:{_function.InstanceId}";
    public string Title => Str.Get("PortableTools.Title");
    public IconRef Icon => IconRef.Fluent("phone");
    public string Description => Str.Get("PortableTools.Description");
    public string? FunctionInstanceId => _function.InstanceId;
    public UsbToolSize Size => UsbToolSize.Wide;
    public bool IsBusy => Browser?.IsCopying == true;

    /// <summary>The device's storage, once opened; null until the tile is, or when it cannot be.</summary>
    [ObservableProperty] private StoreBrowser? _browser;

    /// <summary>Why there is no browser: the device could not be opened, or Windows has not made a portable device of it.</summary>
    [ObservableProperty] private string? _note;

    [ObservableProperty] private string _status;

    /// <summary>The devnodes a portable device may hang from: the function, and the WPD devnodes Windows made of it.</summary>
    IEnumerable<string> SharingDevnodes() =>
        new[] { _function.InstanceId }.Concat(_function.Children.Where(c => c.ClassName is "WPD").Select(c => c.InstanceId));

    public async Task ActivateAsync(CancellationToken ct)
    {
        if (Browser is not null || _disposed) return;
        string? path = SharingDevnodes().SelectMany(_devicePaths).FirstOrDefault();
        if (path is null)
        {
            Status = Note = Str.Get("PortableTools.Note.NoDevice");
            return;
        }

        IFileStore store;
        try { store = await Task.Run(() => _open(path), ct); }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            Status = Note = Str.Format("PortableTools.Note.OpenFailed", e.Message);
            return;
        }
        if (_disposed) { store.Dispose(); return; }

        Note = null;
        _store = store;
        var browser = new StoreBrowser(store, new UsbToolStoreHost(this, _host), IconRef.Fluent("phone"))
        {
            EmptyNote = Str.Get("PortableTools.Note.NothingShared"),
        };
        browser.RootsRead += OnRootsRead;
        browser.PropertyChanged += OnBrowserChanged;
        Browser = browser;
        await browser.RefreshAsync();
    }

    /// <summary>Nothing here is live: a copy in progress carries on with the tile shut.</summary>
    public void Deactivate() { }

    void OnRootsRead()
    {
        if (Browser is not { } browser) return;
        Status = browser.RootInfo.Count == 0 ? browser.EmptyNote ?? ""
            : $"{browser.StoreName} · {string.Join(" · ", browser.RootInfo.Select(StoreBrowser.DescribeRoot))}";
    }

    void OnBrowserChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StoreBrowser.IsCopying)) OnPropertyChanged(nameof(IsBusy));
    }

    // ── The agent ──────────────────────────────────────────────────────────────────────────────────────────

    public string Describe() => Browser?.Describe() ?? Note ?? "Not opened yet: open the tile or run list_storage.";

    public IReadOnlyList<UsbToolOperation> Operations =>
    [
        new("list_storage", "Opens the device and reads what storage it shares (internal storage, an SD card) and how much room is left.", false, [],
            async (_, ct) =>
            {
                await ActivateAsync(ct);
                if (Browser is { } browser) await browser.RefreshAsync();
                return Describe();
            }),
        new("list_folder", "Lists a folder on the device: name, size and date of each entry, folders first.", false,
            [new UsbToolArgument("path", "A folder as Copy path gives it, e.g. 'Galaxy S21\\Internal storage\\DCIM'. Defaults to the folder being browsed.", Required: false)],
            async (args, ct) => Browser is { } browser ? await browser.ListTextAsync(args.GetValueOrDefault("path"), ListedEntries, ct) : Describe()),
        new("copy_from_device", "Copies files or folders from the device to a folder on this computer, each read back and checked by SHA-256.", true,
            [new UsbToolArgument("paths", "What to copy: a JSON array of paths on the device as Copy path gives them, or paths separated by ';'."),
             new UsbToolArgument("destination", "A folder on this computer.")],
            async (args, ct) => Browser is { } browser ? await browser.CopyOutAsync(Paths(args["paths"]), args["destination"], ct) : Describe()),
        new("copy_to_device", "Copies files or folders from this computer onto the device, each read back from the device and checked by SHA-256.", true,
            [new UsbToolArgument("paths", "What to copy: a JSON array of paths on this computer, or paths separated by ';'."),
             new UsbToolArgument("folder", "The folder on the device to copy into, as Copy path gives it.")],
            async (args, ct) => Browser is { } browser ? await browser.CopyInAsync(Paths(args["paths"]), args["folder"], ct) : Describe()),
    ];

    static IReadOnlyList<string> Paths(string value) =>
        value.TrimStart().StartsWith('[')
            ? JsonSerializer.Deserialize<List<string>>(value) ?? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>The portable-device paths Windows gave the function, what the device says about itself, and the storages
    /// it shares — through the store the tile has open, or one opened for the purpose and closed after.</summary>
    public async Task<JsonObject> DebugDetailsAsync(UsbDebugMask mask, CancellationToken ct)
    {
        var paths = SharingDevnodes().SelectMany(_devicePaths).ToList();
        var details = new JsonObject
        {
            ["devicePaths"] = new JsonArray([.. paths.Select(p => (JsonNode?)mask.Text(p))]),
            ["openedBy"] = _store is not null ? "tile" : paths.Count > 0 ? "debug details" : null,
        };
        if (_store is { } open)
            await AddStoreAsync(details, open, ct);
        else if (paths.Count > 0)
        {
            try
            {
                using var store = await Task.Run(() => _open(paths[0]), ct);
                await AddStoreAsync(details, store, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
            {
                details["openError"] = $"0x{e.HResult:X8} {e.Message}";
            }
        }
        return details;
    }

    static async Task AddStoreAsync(JsonObject details, IFileStore store, CancellationToken ct)
    {
        details["facts"] = new JsonObject([.. store.Facts.Select(f => KeyValuePair.Create(f.Key, (JsonNode?)f.Value))]);
        try
        {
            var roots = await store.RootsAsync(ct);
            details["storages"] = new JsonArray([.. roots.Select(r => (JsonNode)new JsonObject
            {
                ["name"] = r.Name,
                ["fileSystem"] = r.FileSystem,
                ["capacity"] = r.Capacity,
                ["free"] = r.Free,
                ["readOnly"] = r.IsReadOnly,
            })]);
        }
        catch (Exception e) when (e is not OperationCanceledException and not OutOfMemoryException)
        {
            details["storagesError"] = $"0x{e.HResult:X8} {e.Message}";
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (Browser is not { } browser) return;
        browser.RootsRead -= OnRootsRead;
        browser.PropertyChanged -= OnBrowserChanged;
        browser.Dispose();
    }
}
