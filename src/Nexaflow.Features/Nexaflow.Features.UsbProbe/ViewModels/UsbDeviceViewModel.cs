using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.Icons;
using Nexaflow.IO.Common.Stores;
using Nexaflow.IO.Usb.Binding;
using Nexaflow.IO.Usb.Browsing;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Plugins;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe.ViewModels;

/// <summary>
/// One device, opened from the USB page: what it is (Overview), what can be done with it now (Live tools), what it is
/// made of (Interfaces) and what is wrong with it (Diagnostics).
/// </summary>
/// <remarks>
/// <para>
/// The page follows the device rather than the port: it locks onto the instance id it first finds, so the device can
/// be unplugged — the page says so and its tools stop — and plugged back in, into the same port or another, and the
/// page picks it up again.
/// </para>
/// <para>
/// Live tools come from <see cref="IUsbToolProvider"/> plugins, offered the device whenever its functions change. The
/// page knows none of them: each tool is a headless view-model drawn by the template its provider ships, and the page
/// adds the tile around it. Whatever on the device no tool covers gets a last tile saying what it is and why.
/// </para>
/// </remarks>
public sealed partial class UsbDeviceViewModel : ObservableObject, IPageViewModel, IDisposable
{
    public const int OverviewTab = 0, LiveToolsTab = 1, InterfacesTab = 2, DiagnosticsTab = 3;

    /// <summary>Lines the Diagnostics tab's log keeps.</summary>
    const int LogLength = 500;

    readonly IUsbDeviceInventory _inventory;
    readonly UsbIdsStore _names;
    readonly IReadOnlyList<ISubfeatureHandle<IUsbToolProvider>> _providers;
    readonly IShellServices _shell;
    readonly IUsbDeviceBroker _broker;
    readonly CancellationTokenSource _lifetime = new();
    readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    readonly HashSet<Uri> _templates = [];
    readonly ToolHost _host;

    readonly UsbDebugCollector _debug;

    UsbDescriptorMapCache _maps;
    UsbBrowser _browser;
    string _target;
    string? _instanceId;
    string? _offeredFor;
    int _offerGeneration;
    bool _started, _pageShown, _disposed;

    public UsbDeviceViewModel(IUsbDeviceInventory inventory, UsbIdsStore names,
        IReadOnlyList<ISubfeatureHandle<IUsbToolProvider>> providers, IShellServices shell, IUsbDeviceBroker broker, string target)
    {
        _inventory = inventory;
        _names = names;
        _providers = providers;
        _shell = shell;
        _broker = broker;
        _target = UsbLocationPath.Normalize(target);
        _maps = new UsbDescriptorMapCache(names.Current);
        _browser = new UsbBrowser(UsbInventorySnapshot.Empty, _maps);
        _host = new ToolHost(this);
        _debug = new UsbDebugCollector(providers, shell, names);
        _names.Changed += OnNamesChanged;
    }

    /// <summary>A provider's templates are needed before its first tile is shown; the view merges them.</summary>
    public event Action<Uri>? TemplatesNeeded;

    /// <summary>The device was found, renamed or moved port — the registration re-titles the tab.</summary>
    public event Action? Moved;

    [ObservableProperty] private UsbDeviceNode? _device;
    [ObservableProperty] private UsbLocation? _location;
    [ObservableProperty] private bool _isPresent;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private int _selectedTab;

    public string Title => Device?.DisplayName ?? _target;

    /// <summary>The device's port path — what the USB page and the agent's tools call it.</summary>
    public string Path => Device?.Location.PortPath ?? _target;

    public string Ids => Device is { } d ? $"{d.VendorId:X4}:{d.ProductId:X4}" : "";
    public IconRef Icon => Location is { } l ? UsbIcons.Of(l) : IconRef.Fluent("usb_plug");
    public string Subtitle => Location?.Summary ?? "";

    public bool IsContextReady => _started && !IsBusy;

    // ── Overview ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>What it is first, then its identity, connection and driver — not its functions or its problems, which
    /// the Interfaces and Diagnostics tabs show in full.</summary>
    public ObservableCollection<UsbFactGroup> FactGroups { get; } = [];

    // ── Live tools ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary><see cref="UsbToolTile"/>s in provider order, then a <see cref="UsbUncoveredTile"/> when anything is left.</summary>
    public ObservableCollection<object> Tiles { get; } = [];

    /// <summary>Why the Live tools tab is empty, when it is; null while it has tiles.</summary>
    [ObservableProperty] private string? _noTools;

    // ── Interfaces ─────────────────────────────────────────────────────────────────────────────────────────

    public ObservableCollection<UsbStructureRow> Structure { get; } = [];
    [ObservableProperty] private UsbStructureRow? _selectedStructure;
    [ObservableProperty] private UsbDetailViewModel? _structureDetail;
    [ObservableProperty] private int _structureDetailTab;

    // ── Diagnostics ────────────────────────────────────────────────────────────────────────────────────────

    public ObservableCollection<string> Problems { get; } = [];
    [ObservableProperty] private bool _hasProblems;
    public ObservableCollection<UsbToolLogEntry> Log { get; } = [];

    // ── Life ───────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The first look: reads the tree (or finds it already read by the USB page) and starts following it.</summary>
    public async Task StartAsync()
    {
        if (_started || _disposed) return;
        _started = true;
        IsBusy = true;
        Status = Str.Get("UsbProbe.Device.Status.Reading");
        try
        {
            await _inventory.StartAsync(_lifetime.Token);
            _inventory.Changed += OnInventoryChanged;
            Show(_inventory.Snapshot);
            Status = Device is null ? Str.Format("UsbProbe.Device.Status.NotFound", _target) : "";
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Status = Str.Format("UsbProbe.Device.Status.Failed", e.Message);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsContextReady));
        }
    }

    /// <summary>The view is on screen or not: tools only run where they can be seen.</summary>
    public void SetPageShown(bool shown)
    {
        _pageShown = shown;
        SyncInView();
    }

    /// <summary>The shell re-pointing the tab at another device.</summary>
    public void Retarget(string target)
    {
        var normalized = UsbLocationPath.Normalize(target);
        if (normalized == Path) return;
        _target = normalized;
        _instanceId = null;
        _offeredFor = null;
        ClearTools();
        if (_started) Show(_inventory.Snapshot);
    }

    partial void OnSelectedTabChanged(int value) => SyncInView();

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsContextReady));

    partial void OnSelectedStructureChanged(UsbStructureRow? value) =>
        StructureDetail = value is null ? null : new UsbDetailViewModel(value.Location);

    void SyncInView()
    {
        bool inView = _pageShown && SelectedTab == LiveToolsTab && IsPresent;
        foreach (var tile in Tiles.OfType<UsbToolTile>()) tile.SetInView(inView);
    }

    // ── Following the device ───────────────────────────────────────────────────────────────────────────────

    void OnInventoryChanged(object? sender, UsbInventoryChangedEventArgs e)
    {
        if (_disposed) return;
        _ = _shell.RunOnUiAsync(() =>
        {
            if (!_disposed) Show(e.Snapshot);
        });
    }

    UsbDeviceNode? Locate(UsbInventorySnapshot snapshot)
    {
        if (_instanceId is not null) return snapshot.Find(_instanceId);
        var location = _browser.Resolve(_target) ?? _browser.Find(_target).FirstOrDefault();
        return location?.Device;
    }

    /// <summary>Folds a newer snapshot in: the device as it now is, or the news that it has gone.</summary>
    internal void Show(UsbInventorySnapshot snapshot)
    {
        _browser = new UsbBrowser(snapshot, _maps);
        if (Locate(snapshot) is not { } device)
        {
            if (IsPresent)
            {
                IsPresent = false;
                Status = Str.Format("UsbProbe.Device.Status.Unplugged", Title);
                ClearTools();
                _offeredFor = null;
                SyncInView();
            }
            return;
        }

        bool returned = !IsPresent && _instanceId is not null;
        _instanceId = device.InstanceId;
        _target = device.Location.PortPath;
        Device = device;
        Location = _browser.Resolve(device.Location.PortPath);
        IsPresent = true;

        RefreshOverview(device);
        RefreshStructure();
        RefreshProblems(device);
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Path));
        OnPropertyChanged(nameof(Ids));
        OnPropertyChanged(nameof(Icon));
        OnPropertyChanged(nameof(Subtitle));
        Moved?.Invoke();
        if (returned) Status = Str.Format("UsbProbe.Device.Status.Returned", Title);

        var signature = Signature(device);
        if (signature != _offeredFor)
        {
            _offeredFor = signature;
            Offered = OfferAsync(device);
        }
        SyncInView();
    }

    /// <summary>What the tools depend on: the functions, their drivers and what Windows made of them. A change here
    /// is a reason to offer the device to the providers again; a renamed string is not.</summary>
    static string Signature(UsbDeviceNode device) => $"{device.Driver.ProblemCode}#" + string.Join("|", device.Functions.Select(f =>
        $"{f.InstanceId};{f.Driver.Service};{f.Driver.ProblemCode};{f.Mode};{f.ComPort};{string.Join(",", f.Children.Select(c => c.InstanceId))}"));

    void RefreshOverview(UsbDeviceNode device)
    {
        FactGroups.Clear();
        if (Location is not { } location) return;
        var shown = UsbLocationFacts.Of(location)
            .Where(g => g.Topic is not (UsbFactTopic.Functions or UsbFactTopic.Diagnostics))
            .OrderBy(g => g.Topic == UsbFactTopic.Summary ? 0 : 1);
        foreach (var group in shown) FactGroups.Add(group);
    }

    /// <summary>Each configuration with its associations, interfaces and endpoints, indented under what holds them.</summary>
    void RefreshStructure()
    {
        string? keep = SelectedStructure?.Path;
        Structure.Clear();
        if (Location is not { } device) return;
        foreach (var configuration in _browser.Children(device).Where(c => c.Kind == UsbLocationKind.Configuration))
        {
            Structure.Add(new UsbStructureRow(configuration, 0));
            Walk(configuration, 1);
        }
        SelectedStructure = Structure.FirstOrDefault(r => r.Path == keep)
            ?? Structure.FirstOrDefault(r => r.Location.Entry?.Type == UsbDescriptorType.Interface)
            ?? Structure.FirstOrDefault();

        void Walk(UsbLocation parent, int depth)
        {
            foreach (var child in _browser.Children(parent))
            {
                if (child.Entry?.Type is not (UsbDescriptorType.InterfaceAssociation or UsbDescriptorType.Interface or UsbDescriptorType.Endpoint)) continue;
                Structure.Add(new UsbStructureRow(child, depth));
                Walk(child, depth + 1);
            }
        }
    }

    void RefreshProblems(UsbDeviceNode device)
    {
        Problems.Clear();
        foreach (var line in device.Diagnostics) Problems.Add(line);
        if (UsbProblemCodes.Describe(device.Driver.ProblemCode) is { } own) Problems.Add(Str.Format("UsbProbe.Device.Problem.Device", own));
        foreach (var function in device.Functions)
            if (function.InstanceId != device.InstanceId && UsbProblemCodes.Describe(function.Driver.ProblemCode) is { } problem)
                Problems.Add(Str.Format("UsbProbe.Device.Problem.Function", FunctionLabel(device, function.InstanceId), problem));
        if (Location is { } location)
            foreach (var configuration in _browser.Children(location).Where(c => c.Kind == UsbLocationKind.Configuration))
                foreach (var problem in configuration.Map?.Problems ?? [])
                    Problems.Add(problem);
        if (!device.Strings.Complete) Problems.Add(Str.Get("UsbProbe.Device.Problem.Strings"));
        if (device.Power is UsbPowerState.D2 or UsbPowerState.D3) Problems.Add(Str.Get("UsbProbe.Device.Problem.Asleep"));
        HasProblems = Problems.Count > 0;
    }

    /// <summary>"Speakers · interfaces 1, 2" — a function as the Interfaces tab would point at it.</summary>
    internal static string? FunctionLabel(UsbDeviceNode device, string? functionInstanceId)
    {
        if (functionInstanceId is null || device.Function(functionInstanceId) is not { } function) return null;
        string name = function.Name ?? device.ClassOf(function).Name;
        return function.FunctionInterface is null
            ? name
            : Str.Format("UsbProbe.Device.Function.Interfaces", name, string.Join(", ", function.InterfaceNumbers));
    }

    /// <summary>
    /// Which part of the device a tile works through — said only where the device has several, so two tiles of one kind
    /// can be told apart. A device that is one function is all the tile could mean, and its title says what that is.
    /// </summary>
    internal static string? TileFunctionLabel(UsbDeviceNode device, string? functionInstanceId) =>
        functionInstanceId is not null && device.Function(functionInstanceId) is { FunctionInterface: not null }
            ? FunctionLabel(device, functionInstanceId)
            : null;

    // ── Live tools ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The latest offer to the providers, done when its tiles are in place — what a test waits on.</summary>
    internal Task Offered { get; private set; } = Task.CompletedTask;

    async Task OfferAsync(UsbDeviceNode device)
    {
        int generation = ++_offerGeneration;
        var offered = await Task.Run(() => Collect(device));
        if (_disposed || generation != _offerGeneration || !IsPresent)
        {
            foreach (var (_, tool) in offered) tool.Dispose();
            return;
        }
        Reconcile(device, offered);
    }

    /// <summary>Every provider's tools for the device; a provider that throws is logged and skipped.</summary>
    List<(IUsbToolProvider Provider, IUsbTool Tool)> Collect(UsbDeviceNode device) =>
        UsbToolOffers.From(_providers, device, _host,
            (provider, e) => _ = _shell.RunOnUiAsync(() => AddLog(provider, Str.Format("UsbProbe.Device.Log.ProviderFailed", e.Message))));

    /// <summary>Replaces the tiles with the newly offered tools, keeping each tool's open or shut state by its key.</summary>
    void Reconcile(UsbDeviceNode device, List<(IUsbToolProvider Provider, IUsbTool Tool)> offered)
    {
        ClearTools();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (provider, tool) in offered)
        {
            if (!seen.Add(tool.Key)) { tool.Dispose(); continue; }
            if (_templates.Add(provider.Templates)) TemplatesNeeded?.Invoke(provider.Templates);
            var tile = new UsbToolTile(tool, TileFunctionLabel(device, tool.FunctionInstanceId), !_collapsed.Contains(tool.Key));
            tile.PropertyChanged += OnTileChanged;
            Tiles.Add(tile);
        }

        var uncovered = device.IsEjected ? [] : Uncovered(device, offered.Select(o => o.Tool));
        if (uncovered.Count > 0) Tiles.Add(new UsbUncoveredTile(uncovered));

        NoTools = Tiles.Count > 0 ? null
            : device.IsEjected ? Str.Get("UsbProbe.Device.Tools.Ejected")
            : device.IsHub ? Str.Get("UsbProbe.Device.Tools.Hub")
            : Str.Get("UsbProbe.Device.Tools.None");
        SyncInView();
    }

    /// <summary>The functions no tool works with — hubs and usbccgp's split aside, which are plumbing, and driver tools
    /// aside, which change how a function is reached but use it no more than before.</summary>
    internal static IReadOnlyList<UsbUncoveredFunction> Uncovered(UsbDeviceNode device, IEnumerable<IUsbTool> tools)
    {
        var claimed = tools.Where(t => t is not IUsbDriverTool).Select(t => t.FunctionInstanceId).ToList();
        bool wholeDevice = claimed.Contains(null);
        var set = claimed.OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var uncovered = new List<UsbUncoveredFunction>();
        foreach (var function in device.Functions)
        {
            if (function.Mode == UsbFunctionMode.CompositeParent || set.Contains(function.InstanceId)) continue;
            if (wholeDevice && device.Functions.Count == 1) continue;
            var cls = device.ClassOf(function);
            if (cls.Class == UsbClassCodes.Hub) continue;
            uncovered.Add(new UsbUncoveredFunction(FunctionLabel(device, function.InstanceId) ?? cls.Name, cls.Name, UsbClassGuidance.For(cls, function.Mode, function.Driver.Service)));
        }
        return uncovered;
    }

    void OnTileChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not UsbToolTile tile || e.PropertyName != nameof(UsbToolTile.IsExpanded)) return;
        if (tile.IsExpanded) _collapsed.Remove(tile.Key);
        else _collapsed.Add(tile.Key);
    }

    void ClearTools()
    {
        foreach (var tile in Tiles.OfType<UsbToolTile>())
        {
            tile.PropertyChanged -= OnTileChanged;
            tile.Dispose();
        }
        Tiles.Clear();
        NoTools = null;
    }

    internal void AddLog(string tool, string message)
    {
        Log.Insert(0, new UsbToolLogEntry(DateTimeOffset.Now, tool, message));
        while (Log.Count > LogLength) Log.RemoveAt(Log.Count - 1);
    }

    /// <summary>The tile whose tool has this key or id — how the agent names one.</summary>
    internal UsbToolTile? Tile(string keyOrId) =>
        Tiles.OfType<UsbToolTile>().FirstOrDefault(t => t.Key == keyOrId)
        ?? Tiles.OfType<UsbToolTile>().FirstOrDefault(t => t.Tool.ToolId == keyOrId);

    // ── Commands ───────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Asks the device for its descriptors and strings again, waking it — standard GET_DESCRIPTOR requests only.</summary>
    [RelayCommand]
    private async Task ReReadAsync()
    {
        if (_instanceId is not { } id || !IsPresent) return;
        IsBusy = true;
        try
        {
            await _inventory.RefreshDeviceAsync(id, _lifetime.Token);
            Show(_inventory.Snapshot);
            Status = Str.Format("UsbProbe.Device.Status.ReRead", Title);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { IsBusy = false; }
    }

    /// <summary>Shows the device in the USB page's tree.</summary>
    [RelayCommand]
    private void OpenInExplorer() =>
        _shell.OpenTab(UsbProbePageRegistration.StaticPageKind, new() { [UsbProbePageRegistration.LocationParam] = Path });

    /// <summary>Copies the device's debug details — for the developers, see <see cref="UsbDebugCollector"/> — with what
    /// each tool open here sees and what they did.</summary>
    [RelayCommand]
    private Task CopyDebugDetailsAsync() =>
        TakeDebugDetailsAsync(device => _debug.CopyAsync(device, LiveTools(), LogLines(), _lifetime.Token));

    /// <summary>Saves the device's debug details as a .json, to send.</summary>
    [RelayCommand]
    private async Task SaveDebugDetailsAsync()
    {
        if (Device is not { } device || await _debug.PickSaveFileAsync(device) is not { } path) return;
        await TakeDebugDetailsAsync(d => _debug.SaveAsync(path, d, LiveTools(), LogLines(), _lifetime.Token));
    }

    async Task TakeDebugDetailsAsync(Func<UsbDeviceNode, Task<string>> take)
    {
        if (Device is not { } device) return;
        IsBusy = true;
        Status = Str.Get("UsbProbe.Status.CollectingDebug");
        try { Status = await take(device); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = Str.Format("UsbProbe.Status.DebugFailed", e.Message); }
        finally { IsBusy = false; }
    }

    IReadOnlyList<IUsbTool> LiveTools() => [.. Tiles.OfType<UsbToolTile>().Select(t => t.Tool)];

    /// <summary>The activity log, oldest first.</summary>
    IReadOnlyList<string> LogLines() => [.. Log.Reverse().Select(e => $"{e.At:O} {e.Tool}: {e.Message}")];

    // ── Names ──────────────────────────────────────────────────────────────────────────────────────────────

    void OnNamesChanged(object? sender, EventArgs e) => _ = _shell.RunOnUiAsync(() =>
    {
        if (_disposed) return;
        _maps = new UsbDescriptorMapCache(_names.Current);
        if (_started) Show(_inventory.Snapshot);
    });

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _names.Changed -= OnNamesChanged;
        _inventory.Changed -= OnInventoryChanged;
        _lifetime.Cancel();
        ClearTools();
        _inventory.Dispose();
        _lifetime.Dispose();
    }

    /// <summary>What a tool on this page may do outside itself.</summary>
    sealed class ToolHost(UsbDeviceViewModel page) : IUsbToolHost
    {
        public UsbIdsDatabase? Names => page._names.Current;

        public IUsbDeviceBroker DriverBroker => page._broker;

        public Task RunOnUiAsync(Action action) => page._shell.RunOnUiAsync(action);

        public Task<bool> ConfirmAsync(string title, string message, CancellationToken ct, string? confirmLabel = null) =>
            page._shell.ConfirmAsync(title, message, confirmLabel, null, ct);

        public Task<string?> PromptAsync(string title, string label, string initialValue)
        {
            var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            page._shell.ShowPrompt(title, label, initialValue, text => answer.TrySetResult(text), () => answer.TrySetResult(null));
            return answer.Task;
        }

        public bool OpenFile(string path) => page._shell.HandleObject(path);

        public string FetchedFilesFolder => IStoreBrowserHost.DefaultFetchedFilesFolder;

        public Task<string?> PickOpenFileAsync(IReadOnlyList<string>? extensions) => page._shell.PickOpenFileAsync(extensions);

        public Task<IReadOnlyList<string>> PickOpenFilesAsync(IReadOnlyList<string>? extensions) => page._shell.PickOpenFilesAsync(extensions);

        public Task<string?> PickFolderAsync() => page._shell.PickFolderAsync();

        public Task<string?> PickSaveFileAsync(string defaultFileName, IReadOnlyList<string>? extensions) =>
            page._shell.PickSaveFileAsync(defaultFileName, extensions);

        public void OpenPage(string pageKind, IReadOnlyDictionary<string, string> parameters) =>
            page._shell.OpenTab(pageKind, new Dictionary<string, string>(parameters));

        public void Log(IUsbTool tool, string message)
        {
            string title = tool.Title;
            _ = page._shell.RunOnUiAsync(() =>
            {
                if (!page._disposed) page.AddLog(title, message);
            });
        }
    }
}
