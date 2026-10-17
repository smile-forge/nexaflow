using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common;
using Nexaflow.IO.Usb.Browsing;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.Visuals.Common.Collections;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.UsbProbe.ViewModels;

/// <summary>What a Copy item on the USB page copies.</summary>
public enum UsbCopyKind
{
    /// <summary>The place's USB path: <c>3-10.2</c>, what the page, the device page and the agent all take.</summary>
    Path,
    /// <summary>A device's Windows instance ID, as Device Manager and <c>pnputil</c> name it.</summary>
    InstanceId,
    /// <summary>A device's vendor and product IDs: <c>046D:C332</c>.</summary>
    Ids,
}

/// <summary>
/// The USB page: the machine's buses, hubs, ports and devices as places to go, as the file browser has folders —
/// a tree of the places that hold something, a listing of what the open place holds, and below it everything
/// about the selected item, down to a descriptor field and its bytes.
/// </summary>
/// <remarks>
/// <para>
/// It is live. The inventory reports arrivals, removals and driver changes from a background thread; each is
/// marshalled here and folded into what is on screen in place — the open location, the selection, the tree's
/// expansion and the listing's rows all survive — and a device that arrives while the page is open is marked
/// new until it is looked at. When the open location's device leaves, the page moves to the nearest place that
/// still exists and says what went.
/// </para>
/// <para>
/// Every location is a <see cref="UsbBrowser"/> path, so the user (breadcrumbs, <c>&gt;</c> in the AI bar) and
/// the agent (its tools) move around the same tree by the same names.
/// </para>
/// </remarks>
public sealed partial class UsbProbeViewModel : ObservableObject, IPageViewModel, IDisposable
{
    readonly IUsbDeviceInventory _inventory;
    readonly UsbIdsStore _names;
    readonly UsbProbeConfig _config;
    readonly IShellServices _shell;
    readonly CancellationTokenSource _lifetime = new();

    /// <summary>Devices plugged in while the page was open and not looked at since.</summary>
    readonly HashSet<string> _arrived = new(StringComparer.OrdinalIgnoreCase);

    UsbDescriptorMapCache _maps;
    UsbBrowser _browser;
    bool _started;
    bool _navigating;
    bool _disposed;

    public UsbProbeViewModel(IUsbDeviceInventory inventory, UsbIdsStore names, UsbProbeConfig config, IShellServices shell, string? location)
    {
        _inventory = inventory;
        _names = names;
        _config = config;
        _shell = shell;
        _maps = new UsbDescriptorMapCache(names.Current);
        _browser = new UsbBrowser(UsbInventorySnapshot.Empty, _maps);
        _currentPath = UsbLocationPath.Normalize(location);

        Root = new UsbTreeNode("", Expand) { IsLoaded = true };
        Root.Describe(_browser.Root, false);
        TreeRoots.Add(Root);
        _names.Changed += OnNamesChanged;
    }

    /// <summary>Raised after every move, with the crumbs from the root down — the registration turns them into the tab's breadcrumbs.</summary>
    public event Action<IReadOnlyList<(string Label, string Path)>>? NavigationChanged;

    public ObservableCollection<UsbTreeNode> TreeRoots { get; } = [];
    internal UsbTreeNode Root { get; }

    public RangeObservableCollection<UsbListRow> Rows { get; } = [];

    /// <summary>The snapshot the page is showing, as places.</summary>
    public UsbBrowser Browser => _browser;

    [ObservableProperty] private string _currentPath;
    [ObservableProperty] private UsbLocation? _current;
    [ObservableProperty] private UsbListRow? _selectedRow;
    [ObservableProperty] private UsbDetailViewModel? _detail;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;

    /// <summary>True where the listing holds ports and devices (the columns a device has); false inside a device.</summary>
    [ObservableProperty] private bool _isTopologyListing = true;

    /// <summary>The detail pane's tab, kept as the selection moves so a user reading descriptors keeps reading descriptors.</summary>
    [ObservableProperty] private int _detailTab;

    public bool IsContextReady => _started && !IsBusy;

    /// <summary>The first look: reads the tree, opens the location the tab was given, and starts following changes.</summary>
    public async Task StartAsync()
    {
        if (_started || _disposed) return;
        _started = true;
        IsBusy = true;
        Status = Str.Get("UsbProbe.Status.Reading");
        try
        {
            await _inventory.StartAsync(_lifetime.Token);
            // Subscribed after the first pass, so the devices already plugged in are not announced as arrivals.
            _inventory.Changed += OnInventoryChanged;
            Show(_inventory.Snapshot, []);
            Go(_browser.Resolve(CurrentPath) ?? _browser.NearestExisting(CurrentPath));
            Status = Summary();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            return;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Status = Str.Format("UsbProbe.Status.Failed", e.Message);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsContextReady));
        }
        CheckNamesInBackground();
    }

    // ── Moving around ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens <paramref name="target"/>: a location path, or — when nothing has that path — the first device whose
    /// name, VID:PID, serial or port path matches it. False when nothing does.
    /// </summary>
    public bool Navigate(string? target)
    {
        var location = _browser.Resolve(target) ?? _browser.Find(target ?? "").FirstOrDefault();
        if (location is null) return false;
        Go(location);
        return true;
    }

    /// <summary>Opens <paramref name="location"/> now, or — before the first read — makes it where the page opens.</summary>
    public void OpenWhenReady(string? location)
    {
        if (_started && Current is not null) Navigate(location);
        else CurrentPath = UsbLocationPath.Normalize(location);
    }

    /// <summary>Up is offered below the top: every bus has a place above it, the top has none.</summary>
    private bool CanGoUp() => Current is { } current && _browser.Parent(current) is not null;

    [RelayCommand(CanExecute = nameof(CanGoUp))]
    private void Up()
    {
        if (Current is { } current && _browser.Parent(current) is { } parent) Go(parent, select: current.Path);
    }

    /// <summary>Opens a row or tree node that has something inside it; a leaf is already shown in full by selecting it.</summary>
    [RelayCommand]
    private void Open(object? target)
    {
        if (LocationOf(target) is { } location && (target is UsbTreeNode || _browser.Children(location).Count > 0)) Go(location);
    }

    /// <summary>Reads the tree again; descriptors already read are kept.</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_started) return;
        IsBusy = true;
        try
        {
            Show(await _inventory.RefreshAsync(_lifetime.Token), []);
            Status = Summary();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { IsBusy = false; }
    }

    /// <summary>
    /// Asks a device for its strings and BOS again, waking it if it was asleep — the only thing this page sends a device,
    /// and only standard GET_DESCRIPTOR requests. The device is the one the menu was opened on, or else the one in view.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanReRead))]
    private async Task ReReadAsync(object? target)
    {
        if (DeviceOf(target) is not { } device) return;
        try
        {
            await ReReadDeviceAsync(device.InstanceId, CancellationToken.None);
            Status = Str.Format("UsbProbe.Status.ReRead", device.DisplayName);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    /// <summary>The re-read behind the button and the agent's <c>usb_reread</c>; the device as it now reads, or null once it has gone.</summary>
    internal async Task<UsbDeviceNode?> ReReadDeviceAsync(string instanceId, CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        IsBusy = true;
        try
        {
            var device = await _inventory.RefreshDeviceAsync(instanceId, linked.Token);
            Show(_inventory.Snapshot, []);
            return device;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanReRead(object? target) => DeviceOf(target) is not null;

    /// <summary>Opens a device on its own page, with its live tools.</summary>
    [RelayCommand(CanExecute = nameof(CanReRead))]
    private void OpenDevice(object? target)
    {
        if (DeviceOf(target) is { } device) OpenDevicePage(device);
    }

    /// <summary>The place a command acts on: the tree node or row its menu was opened on, or — given none — the row
    /// selected, then the place open.</summary>
    internal UsbLocation? LocationOf(object? target) => target switch
    {
        UsbListRow row => row.Location,
        UsbTreeNode { IsPlaceholder: false } node => _browser.Resolve(node.Path),
        _ => SelectedRow?.Location ?? Current,
    };

    UsbDeviceNode? DeviceOf(object? target) => LocationOf(target) is { Kind: UsbLocationKind.Device } location ? location.Device : null;

    /// <summary>What a Copy item puts on the clipboard for the place its menu was opened on; null where the place has
    /// none — a bus has no instance ID, the top no path.</summary>
    internal string? CopyTextOf(object? target, UsbCopyKind kind) => LocationOf(target) is not { } location ? null : kind switch
    {
        UsbCopyKind.Path => location.Path.Length > 0 ? location.Path : null,
        UsbCopyKind.InstanceId => DeviceOf(target)?.InstanceId,
        _ => DeviceOf(target) is { } device ? $"{device.VendorId:X4}:{device.ProductId:X4}" : null,
    };

    bool CanCopyPath(object? target) => CopyTextOf(target, UsbCopyKind.Path) is not null;
    bool CanCopyInstanceId(object? target) => CopyTextOf(target, UsbCopyKind.InstanceId) is not null;
    bool CanCopyIds(object? target) => CopyTextOf(target, UsbCopyKind.Ids) is not null;

    [RelayCommand(CanExecute = nameof(CanCopyPath))]
    private void CopyPath(object? target) => ToClipboard(CopyTextOf(target, UsbCopyKind.Path));

    [RelayCommand(CanExecute = nameof(CanCopyInstanceId))]
    private void CopyInstanceId(object? target) => ToClipboard(CopyTextOf(target, UsbCopyKind.InstanceId));

    [RelayCommand(CanExecute = nameof(CanCopyIds))]
    private void CopyIds(object? target) => ToClipboard(CopyTextOf(target, UsbCopyKind.Ids));

    void ToClipboard(string? text)
    {
        if (text is null) return;
        try { System.Windows.Clipboard.SetText(text); }
        catch (System.Runtime.InteropServices.ExternalException) { Status = Str.Get("UsbProbe.Status.ClipboardBusy"); }
    }

    internal void OpenDevicePage(UsbDeviceNode device) =>
        _shell.OpenTab(UsbDevicePageRegistration.StaticPageKind, new() { [UsbDevicePageRegistration.DeviceParam] = device.Location.PortPath });

    internal UsbDeviceNode? DeviceInView() => (SelectedRow?.Location ?? Current)?.Device;

    /// <summary>Called by the tree when the user picks a node.</summary>
    internal void TreeSelected(UsbTreeNode node)
    {
        if (_navigating || node.IsPlaceholder || node.Path == CurrentPath) return;
        if (_browser.Resolve(node.Path) is { } location) Go(location);
    }

    void Go(UsbLocation location, string? select = null)
    {
        if (_navigating) return;
        _navigating = true;
        try
        {
            CurrentPath = location.Path;
            Current = location;
            Visited(location);
            IsTopologyListing = IsTopology(location);
            Rows.ReplaceAll(_browser.Children(location).Select(c => new UsbListRow(c, IsNew(c))));
            SelectedRow = select is null ? null : Rows.FirstOrDefault(r => r.Path == select);
            Detail = new UsbDetailViewModel(SelectedRow?.Location ?? location);
            RevealInTree(location);
            Announce();
        }
        finally
        {
            _navigating = false;
        }
    }

    static bool IsTopology(UsbLocation location) =>
        location.Kind is UsbLocationKind.Root or UsbLocationKind.Bus || location is { Kind: UsbLocationKind.Device, Device.IsHub: true };

    partial void OnSelectedRowChanged(UsbListRow? value)
    {
        if (_navigating) return;
        if (value is not null) Visited(value.Location);
        Detail = Current is null ? null : new UsbDetailViewModel(value?.Location ?? Current);
        ReReadCommand.NotifyCanExecuteChanged();
        OpenDeviceCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        CopyInstanceIdCommand.NotifyCanExecuteChanged();
        CopyIdsCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsContextReady));

    const int OverviewTab = 0, DescriptorTab = 1;
    int _preferredTab;
    bool _choosingTab;

    /// <summary>The tab the user last chose, where the new item has it; otherwise the one it does have.</summary>
    partial void OnDetailChanged(UsbDetailViewModel? value)
    {
        _choosingTab = true;
        DetailTab = value is null ? OverviewTab
            : !value.HasFacts || (_preferredTab == DescriptorTab && value.HasDescriptor) ? DescriptorTab : OverviewTab;
        _choosingTab = false;
    }

    partial void OnDetailTabChanged(int value)
    {
        if (!_choosingTab) _preferredTab = value;
    }

    void Visited(UsbLocation location)
    {
        if (location.Device is { } device && _arrived.Remove(device.InstanceId))
        {
            foreach (var row in Rows.Where(r => r.Device?.InstanceId == device.InstanceId)) row.IsNew = false;
            SyncTree(Root);
        }
    }

    bool IsNew(UsbLocation location) => location.Kind == UsbLocationKind.Device && _arrived.Contains(location.Device!.InstanceId);

    void Announce()
    {
        var crumbs = UsbLocationPath.Lineage(CurrentPath)
            .Select(path => (_browser.Resolve(path)?.Name ?? path, path))
            .ToList();
        NavigationChanged?.Invoke(crumbs);
        ReReadCommand.NotifyCanExecuteChanged();
        OpenDeviceCommand.NotifyCanExecuteChanged();
        UpCommand.NotifyCanExecuteChanged();
        CopyPathCommand.NotifyCanExecuteChanged();
        CopyInstanceIdCommand.NotifyCanExecuteChanged();
        CopyIdsCommand.NotifyCanExecuteChanged();
    }

    // ── The tree ───────────────────────────────────────────────────────────────────────────────────────────

    void Expand(UsbTreeNode node)
    {
        node.IsLoaded = true;
        SyncTree(node);
    }

    /// <summary>Opens the tree down to and including the place in view and selects it — or, for a leaf, the place that holds it.</summary>
    void RevealInTree(UsbLocation location)
    {
        var node = Root;
        foreach (var path in UsbLocationPath.Lineage(location.Path).Skip(1))
        {
            if (!node.IsLoaded) Expand(node);
            node.IsExpanded = true;
            var next = node.Children.FirstOrDefault(c => c.Path == path);
            if (next is null) break;
            node = next;
        }
        // The place in view opens too, as the file browser's folder does, so what arrives inside it shows.
        if (node.Path == location.Path)
        {
            if (!node.IsLoaded) Expand(node);
            node.IsExpanded = true;
        }
        node.IsSelected = true;
    }

    /// <summary>
    /// Opens the tree down to a device just plugged in, so it shows — with its NEW badge — without being selected: what
    /// the reader was looking at stays in view.
    /// </summary>
    void RevealArrival(string path)
    {
        var node = Root;
        foreach (var step in UsbLocationPath.Lineage(path).Skip(1))
        {
            if (step == path) break;
            if (!node.IsLoaded) Expand(node);
            node.IsExpanded = true;
            if (node.Children.FirstOrDefault(c => c.Path == step) is not { } next) return;
            node = next;
        }
        if (!node.IsLoaded) Expand(node);
        node.IsExpanded = true;
    }

    /// <summary>
    /// What the tree shows beneath a place: buses and the devices plugged in, so a hub opens onto what hangs off it. What a
    /// device is made of — its configurations, interfaces and endpoints — is the listing's and the device page's, not the
    /// tree's; empty ports are listing rows too.
    /// </summary>
    List<UsbLocation> TreePlaces(UsbLocation location) =>
        [.. _browser.Children(location).Where(c => c.Kind is UsbLocationKind.Bus or UsbLocationKind.Device)];

    /// <summary>Brings a node, and every loaded node beneath it, up to the current snapshot.</summary>
    void SyncTree(UsbTreeNode node)
    {
        if (_browser.Resolve(node.Path) is not { } location) return;
        node.Describe(location, IsNew(location));

        var containers = TreePlaces(location);
        if (!node.IsLoaded)
        {
            bool hasPlaceholder = node.Children.Count == 1 && node.Children[0].IsPlaceholder;
            if (containers.Count > 0 && !hasPlaceholder)
            {
                node.Children.Clear();
                node.Children.Add(new UsbTreeNode(UsbTreeNode.PlaceholderPath, Expand));
            }
            else if (containers.Count == 0 && node.Children.Count > 0) node.Children.Clear();
            return;
        }

        var wanted = containers.Select(c => c.Path).ToList();
        for (int i = node.Children.Count - 1; i >= 0; i--)
            if (!wanted.Contains(node.Children[i].Path)) node.Children.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            int at = IndexOf(node.Children, wanted[i]);
            if (at < 0) node.Children.Insert(i, new UsbTreeNode(wanted[i], Expand));
            else if (at != i) node.Children.Move(at, i);
            SyncTree(node.Children[i]);
        }
    }

    static int IndexOf(ObservableCollection<UsbTreeNode> nodes, string path)
    {
        for (int i = 0; i < nodes.Count; i++)
            if (nodes[i].Path == path) return i;
        return -1;
    }

    // ── Following the machine ──────────────────────────────────────────────────────────────────────────────

    void OnInventoryChanged(object? sender, UsbInventoryChangedEventArgs e)
    {
        if (_disposed) return;
        _ = _shell.RunOnUiAsync(() =>
        {
            if (!_disposed) Show(e.Snapshot, e.Changes);
        });
    }

    /// <summary>Folds a newer snapshot into the page without losing the user's place.</summary>
    internal void Show(UsbInventorySnapshot snapshot, IReadOnlyList<UsbInventoryChange> changes)
    {
        string? leftName = null;
        foreach (var change in changes)
        {
            if (change.Kind == UsbInventoryChangeKind.Arrived) _arrived.Add(change.InstanceId);
            else if (change.Kind == UsbInventoryChangeKind.Removed)
            {
                _arrived.Remove(change.InstanceId);
                if (Current?.Device?.InstanceId is { } id && string.Equals(id, change.InstanceId, StringComparison.OrdinalIgnoreCase))
                    leftName = Current.Device.DisplayName;
            }
        }

        _browser = new UsbBrowser(snapshot, _maps);
        SyncTree(Root);
        foreach (var arrived in changes.Where(c => c.Kind == UsbInventoryChangeKind.Arrived && c.Device is not null))
            RevealArrival(arrived.Device!.Location.PortPath);
        if (!_started || Current is null) return;

        if (_browser.Resolve(CurrentPath) is not { } current)
        {
            leftName ??= Current.Name;
            Go(_browser.NearestExisting(CurrentPath));
            Status = Str.Format("UsbProbe.Status.Disconnected", leftName);
            return;
        }

        Current = current;
        IsTopologyListing = IsTopology(current);
        SyncRows(current);
        Detail = new UsbDetailViewModel(SelectedRow?.Location ?? current);
        Announce();
        if (changes.Count > 0) Status = Describe(changes);
    }

    /// <summary>Updates the listing row by row, so the selection and scroll position stay where they were.</summary>
    void SyncRows(UsbLocation location)
    {
        var children = _browser.Children(location);
        var existing = Rows.ToDictionary(r => r.Path);
        var wanted = new List<UsbListRow>(children.Count);
        foreach (var child in children)
        {
            if (existing.TryGetValue(child.Path, out var row)) row.Update(child, IsNew(child));
            else row = new UsbListRow(child, IsNew(child));
            wanted.Add(row);
        }

        var keep = wanted.ToHashSet();
        for (int i = Rows.Count - 1; i >= 0; i--)
            if (!keep.Contains(Rows[i])) Rows.RemoveAt(i);
        for (int i = 0; i < wanted.Count; i++)
        {
            int at = Rows.IndexOf(wanted[i]);
            if (at < 0) Rows.Insert(i, wanted[i]);
            else if (at != i) Rows.Move(at, i);
        }
        if (SelectedRow is not null && !keep.Contains(SelectedRow)) SelectedRow = null;
    }

    string Describe(IReadOnlyList<UsbInventoryChange> changes)
    {
        var parts = new List<string>();
        foreach (var change in changes)
        {
            string name = change.Device?.DisplayName ?? change.InstanceId;
            switch (change.Kind)
            {
                case UsbInventoryChangeKind.Arrived: parts.Add(Str.Format("UsbProbe.Status.Arrived", name)); break;
                case UsbInventoryChangeKind.Removed: parts.Add(Str.Format("UsbProbe.Status.Removed", name)); break;
                case UsbInventoryChangeKind.DriverChanged: parts.Add(Str.Format("UsbProbe.Status.DriverChanged", name)); break;
            }
        }
        return parts.Count == 0 ? Status : string.Join(" · ", parts);
    }

    string Summary()
    {
        var root = _browser.Root;
        return _names.Current?.Version is { } version ? Str.Format("UsbProbe.Status.SummaryWithNames", root.Summary, version) : root.Summary;
    }

    // ── Device names ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The monthly look for a newer usb.ids, when Options allows it — off the UI thread, and silent unless it fails.</summary>
    void CheckNamesInBackground()
    {
        if (!_config.CheckDeviceNamesMonthly || !_names.IsCheckDue()) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var result = await _names.UpdateAsync(_lifetime.Token).ConfigureAwait(false);
                if (result.Outcome == UsbIdsUpdateOutcome.Failed)
                    await _shell.RunOnUiAsync(() => Status = Str.Format("UsbProbe.Status.NamesCheckFailed", result.Message)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        });
    }

    void OnNamesChanged(object? sender, EventArgs e) => _ = ApplyNamesAsync();

    /// <summary>A newer usb.ids arrived (here or from Options): name everything from it.</summary>
    async Task ApplyNamesAsync()
    {
        try
        {
            var names = _names.Current;
            var snapshot = await _inventory.UseNamesAsync(names, _lifetime.Token).ConfigureAwait(false);
            await _shell.RunOnUiAsync(() =>
            {
                if (_disposed) return;
                _maps = new UsbDescriptorMapCache(names);
                Show(snapshot, []);
                Status = Str.Format("UsbProbe.Status.NamesUpdated", names?.Version ?? "?");
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _names.Changed -= OnNamesChanged;
        _inventory.Changed -= OnInventoryChanged;
        _lifetime.Cancel();
        _inventory.Dispose();
        _lifetime.Dispose();
    }
}
