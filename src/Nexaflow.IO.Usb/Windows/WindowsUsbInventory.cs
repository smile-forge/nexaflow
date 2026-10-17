using Microsoft.Win32;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;
using Nexaflow.IO.Usb.Windows.Native;

namespace Nexaflow.IO.Usb.Windows;

/// <summary>
/// <see cref="IUsbDeviceInventory"/> on Windows: hub IOCTLs for what each device says, CfgMgr32 for what Windows
/// did with it, CM_Register_Notification to follow changes. Passes are serialised; a burst of PnP events (a
/// composite device arriving is a dozen) settles into one pass. Descriptors are read once per device and reused,
/// so a warm pass costs the IOCTLs and property reads only — and a suspended device is never woken to answer.
/// </summary>
public sealed class WindowsUsbInventory : IUsbDeviceInventory
{
    static readonly TimeSpan DefaultSettle = TimeSpan.FromMilliseconds(250);

    volatile UsbIdsDatabase? _names;
    readonly TimeSpan _settle;
    readonly SemaphoreSlim _passGate = new(1, 1);
    readonly Dictionary<string, CachedDescriptors> _cache = new(StringComparer.OrdinalIgnoreCase);
    readonly Timer _settleTimer;
    UsbPnpWatcher? _watcher;

    /// <summary>USB devnodes enumerated or removed since the last pass: re-enumerated, so their cached descriptors may
    /// describe the device as it was before — a phone switching into file transfer keeps its id and changes its
    /// configuration.</summary>
    readonly HashSet<string> _reenumerated = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The non-USB devnodes the last pass found beneath USB devices, so their removal is recognised without
    /// asking Windows about a devnode that has gone.</summary>
    volatile HashSet<string> _offspring = new(StringComparer.OrdinalIgnoreCase);
    volatile UsbInventorySnapshot _snapshot = UsbInventorySnapshot.Empty;
    bool _disposed;

    public WindowsUsbInventory(UsbIdsDatabase? names, TimeSpan? settle = null)
    {
        _names = names;
        _settle = settle ?? DefaultSettle;
        _settleTimer = new Timer(_ => _ = PassAfterEventsAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public UsbInventorySnapshot Snapshot => _snapshot;

    public event EventHandler<UsbInventoryChangedEventArgs>? Changed;

    public async Task StartAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await RefreshAsync(ct).ConfigureAwait(false);
        _watcher ??= new UsbPnpWatcher(OnDevNode);
    }

    /// <summary>
    /// A devnode came, started or went. A USB one always calls for a pass; so does one the last pass found beneath a USB
    /// device; any other is looked up — off the notification thread, which must not block — and calls for a pass only when
    /// it hangs beneath a USB device.
    /// </summary>
    void OnDevNode(string instanceId, bool enumeratedOrRemoved)
    {
        if (DevNodes.IsUsb(instanceId))
        {
            if (enumeratedOrRemoved) lock (_reenumerated) _reenumerated.Add(instanceId);
            Settle();
        }
        else if (_offspring.Contains(instanceId))
        {
            Settle();
        }
        else
        {
            ThreadPool.UnsafeQueueUserWorkItem(static state =>
            {
                var (self, id) = state;
                if (!self._disposed && DevNodes.IsUsbOrBeneathUsb(id)) self.Settle();
            }, (this, instanceId), preferLocal: false);
        }
    }

    /// <summary>Runs a pass once things have been quiet for the settle time.</summary>
    void Settle()
    {
        if (_disposed) return;
        try { _settleTimer.Change(_settle, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }
    }

    bool TakeReenumerated(string instanceId)
    {
        lock (_reenumerated) return _reenumerated.Remove(instanceId);
    }

    public Task<UsbInventorySnapshot> RefreshAsync(CancellationToken ct) => PassAsync(forceWireFor: null, ct);

    public async Task<UsbDeviceNode?> RefreshDeviceAsync(string instanceId, CancellationToken ct)
    {
        var owner = _snapshot.FindOwner(instanceId)?.InstanceId ?? instanceId;
        var snapshot = await PassAsync(owner, ct).ConfigureAwait(false);
        return snapshot.Find(owner);
    }

    public Task<UsbInventorySnapshot> UseNamesAsync(UsbIdsDatabase? names, CancellationToken ct)
    {
        _names = names;
        return RefreshAsync(ct);
    }

    async Task PassAfterEventsAsync()
    {
        try
        {
            await PassAsync(null, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception) when (_disposed)
        {
            // disposed while a settle pass was queued
        }
    }

    async Task<UsbInventorySnapshot> PassAsync(string? forceWireFor, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        UsbInventorySnapshot next;
        IReadOnlyList<UsbInventoryChange> changes;
        await _passGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var previous = _snapshot;
            next = await Task.Run(() => Capture(forceWireFor), ct).ConfigureAwait(false);
            changes = UsbInventoryDiff.Between(previous, next);
            _snapshot = next;
        }
        finally
        {
            _passGate.Release();
        }
        // Raised outside the gate so a handler may call back into the inventory.
        if (changes.Count > 0) Changed?.Invoke(this, new UsbInventoryChangedEventArgs(next, changes));
        return next;
    }

    UsbInventorySnapshot Capture(string? forceWireFor)
    {
        var diagnostics = new List<string>();
        var scans = UsbHubTopology.Scan();

        var byDriverKey = new Dictionary<string, (string Id, uint DevInst)>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in DevNodes.PresentDeviceIds("USB"))
            if (DevNodes.Locate(id) is { } devInst && DevNodes.String(devInst, DevPropKeys.DriverKey) is { } key)
                byDriverKey[key] = (id, devInst);

        var placement = new Dictionary<string, (string Hub, int Port)>(StringComparer.OrdinalIgnoreCase);
        foreach (var scan in scans)
            foreach (var port in scan.Ports)
                if (port.DriverKey is { } key && byDriverKey.TryGetValue(key, out var node))
                    placement[node.Id] = (scan.InstanceId, port.Port);
        var buses = BusNumbers(scans);

        var devices = new List<UsbDeviceNode>();
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scan in scans)
        {
            if (scan.OpenError is not null) diagnostics.Add($"{scan.InstanceId}: {scan.OpenError}");
            foreach (var port in scan.Ports)
            {
                if (port.DriverKey is null || !byDriverKey.TryGetValue(port.DriverKey, out var node))
                {
                    diagnostics.Add($"{scan.InstanceId} port {port.Port}: a device is connected but has no devnode yet");
                    continue;
                }
                if (!UsbDeviceDescriptor.TryParse(port.DeviceDescriptor, out var descriptor))
                {
                    diagnostics.Add($"{node.Id}: the hub returned no valid device descriptor");
                    continue;
                }
                present.Add(node.Id);
                var location = Locate(node.Id, scan, port, placement, buses);
                bool force = string.Equals(node.Id, forceWireFor, StringComparison.OrdinalIgnoreCase);
                devices.Add(Build(node.Id, node.DevInst, scan, port, descriptor, location, force, fresh: TakeReenumerated(node.Id)));
            }
        }

        foreach (var stale in _cache.Keys.Where(k => !present.Contains(k)).ToList()) _cache.Remove(stale);
        _offspring = new HashSet<string>(
            devices.SelectMany(d => d.Functions.SelectMany(f => f.Children).Concat(d.Auxiliary)).Select(c => c.InstanceId),
            StringComparer.OrdinalIgnoreCase);
        devices.Sort((a, b) => string.CompareOrdinal(a.Location.PortPath, b.Location.PortPath));
        return new UsbInventorySnapshot(DateTimeOffset.Now, devices, Hubs(scans, placement, buses), diagnostics);
    }

    /// <summary>
    /// Each hub with its name, its bus and, for a root hub, the controller above it — what a tree needs to show a
    /// controller › hub › port path without asking the configuration manager again.
    /// </summary>
    static List<UsbHubInfo> Hubs(List<UsbHubScan> scans, Dictionary<string, (string Hub, int Port)> placement, Dictionary<string, int> buses)
    {
        var hubs = new List<UsbHubInfo>(scans.Count);
        foreach (var scan in scans)
        {
            string root = scan.InstanceId;
            while (placement.TryGetValue(root, out var at)) root = at.Hub;

            uint? devInst = DevNodes.Locate(scan.InstanceId);
            string? name = devInst is { } d ? DevNodes.String(d, DevPropKeys.FriendlyName) ?? DevNodes.String(d, DevPropKeys.DeviceDesc) : null;
            hubs.Add(new UsbHubInfo(scan.InstanceId, scan.Kind, name, buses.GetValueOrDefault(root), scan.PortCount, scan.AllPorts,
                buses.ContainsKey(scan.InstanceId) && devInst is { } r ? Controller(r) : null, scan.OpenError));
        }
        return hubs;
    }

    static UsbHostController? Controller(uint rootHub)
    {
        if (DevNodes.String(rootHub, DevPropKeys.Parent) is not { } parentId || DevNodes.Locate(parentId) is not { } parent) return null;
        string name = DevNodes.String(parent, DevPropKeys.FriendlyName) ?? DevNodes.String(parent, DevPropKeys.DeviceDesc) ?? parentId;
        return new UsbHostController(parentId, name, DevNodes.Driver(parent));
    }

    /// <param name="fresh">The device re-enumerated since the last pass: read its descriptors again rather than trust the cache.</param>
    UsbDeviceNode Build(string instanceId, uint devInst, UsbHubScan scan, UsbPortConnection port, UsbDeviceDescriptor descriptor, UsbPortLocation location, bool forceWire, bool fresh)
    {
        var power = DevNodes.Power(devInst);
        bool asleep = power is UsbPowerState.D2 or UsbPowerState.D3;
        var wire = Descriptors(instanceId, scan.InterfacePath, port, descriptor, readWire: forceWire || !asleep, force: forceWire || fresh);

        var diagnostics = new List<string>(wire.Problems);
        if (!wire.WireRead) diagnostics.Add($"suspended ({power}): strings and BOS were not requested, so the device was not woken");

        var configurations = wire.Configurations.Select(c => UsbDescriptorParser.ParseConfiguration(c, port.Speed)).ToList();
        foreach (var configuration in configurations)
            diagnostics.AddRange(configuration.Problems.Select(p => $"configuration {configuration.Value}: {p}"));
        var bos = wire.Bos is { } bosBytes ? UsbDescriptorParser.ParseBos(bosBytes) : null;
        var strings = new UsbDeviceStrings(
            Text(wire, descriptor.ManufacturerIndex), Text(wire, descriptor.ProductIndex), Text(wire, descriptor.SerialNumberIndex),
            wire.Strings, wire.Languages, wire.WireRead && !wire.Problems.Any(p => p.StartsWith("string", StringComparison.Ordinal)));

        var driver = DevNodes.Driver(devInst);
        var active = configurations.FirstOrDefault(c => c.Value == port.CurrentConfiguration) ?? configurations.FirstOrDefault();
        var (functions, auxiliary) = Functions(instanceId, devInst, driver, active);

        return new UsbDeviceNode(instanceId, location, descriptor, port.DeviceDescriptor, configurations, port.CurrentConfiguration,
            bos, strings, port.Speed, port.SuperSpeedCapable, port.IsHub, power, driver, functions, auxiliary,
            MsOs(descriptor, bos), DevNodes.String(devInst, DevPropKeys.BusReportedDeviceDesc), diagnostics)
        {
            VendorName = _names?.VendorName(descriptor.VendorId),
            ProductName = _names?.ProductName(descriptor.VendorId, descriptor.ProductId),
        };
    }

    UsbWireDescriptors Descriptors(string instanceId, string hubPath, UsbPortConnection port, UsbDeviceDescriptor descriptor, bool readWire, bool force)
    {
        if (!force && _cache.TryGetValue(instanceId, out var cached)
            && cached.Matches(port.DeviceDescriptor, port.CurrentConfiguration) && (cached.Wire.WireRead || !readWire))
            return cached.Wire;
        var wire = UsbHubDescriptorReader.Read(hubPath, port.Port, descriptor, readWire);
        _cache[instanceId] = new CachedDescriptors(port.DeviceDescriptor, port.CurrentConfiguration, wire);
        return wire;
    }

    static string? Text(UsbWireDescriptors wire, byte index) => index == 0 ? null : wire.Strings.GetValueOrDefault(index);

    static (List<UsbFunctionNode>, List<UsbChildNode>) Functions(string instanceId, uint devInst, UsbDevNodeDriver driver, UsbConfiguration? active)
    {
        if (UsbFunctionLayout.Classify(driver.Service, driver.InfPath) != UsbFunctionMode.CompositeParent)
            return ([Function(instanceId, devInst, driver, null, active)], []);

        var functions = new List<UsbFunctionNode>();
        var auxiliary = new List<UsbChildNode>();
        foreach (var childId in DevNodes.Strings(devInst, DevPropKeys.Children))
        {
            if (DevNodes.Locate(childId) is not { } child) continue;
            if (UsbFunctionLayout.FunctionInterfaceOf(childId) is { } mi) functions.Add(Function(childId, child, DevNodes.Driver(child), mi, active));
            else auxiliary.Add(Child(childId, child));
        }
        functions.Sort((a, b) => Nullable.Compare(a.FunctionInterface, b.FunctionInterface));
        return (functions, auxiliary);
    }

    static UsbFunctionNode Function(string instanceId, uint devInst, UsbDevNodeDriver driver, byte? mi, UsbConfiguration? active)
    {
        using var parameters = DevNodes.HardwareKey(devInst, writable: false);
        var guids = (parameters?.GetValue("DeviceInterfaceGUIDs") as string[] ?? (parameters?.GetValue("DeviceInterfaceGUID") is string one ? [one] : []))
            .Select(g => System.Guid.TryParse(g, out var parsed) ? parsed : (Guid?)null).OfType<Guid>().ToList();
        var children = DevNodes.Strings(devInst, DevPropKeys.Children).Select(id => DevNodes.Locate(id) is { } c ? Child(id, c) : new UsbChildNode(id, null, null, null)).ToList();

        return new UsbFunctionNode(instanceId, UsbFunctionLayout.InterfacesOf(mi, active),
            DevNodes.String(devInst, DevPropKeys.BusReportedDeviceDesc) ?? DevNodes.String(devInst, DevPropKeys.FriendlyName),
            driver, UsbFunctionLayout.Classify(driver.Service, driver.InfPath), DevNodes.Power(devInst), guids,
            parameters?.GetValue("PortName") as string, children)
        { FunctionInterface = mi };
    }

    static UsbChildNode Child(string instanceId, uint devInst) =>
        new(instanceId, DevNodes.String(devInst, DevPropKeys.Class), DevNodes.String(devInst, DevPropKeys.Service),
            DevNodes.String(devInst, DevPropKeys.FriendlyName) ?? DevNodes.String(devInst, DevPropKeys.DeviceDesc));

    /// <summary>
    /// Windows asks a new device for string 0xEE once and caches the answer under usbflags, so MS OS 1.0 support
    /// is known without sending anything; MS OS 2.0 and WebUSB come from the BOS.
    /// </summary>
    static UsbMsOsSupport MsOs(UsbDeviceDescriptor d, UsbBos? bos)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\usbflags\{d.VendorId:X4}{d.ProductId:X4}{d.DeviceVersion.Raw:X4}");
        bool? os10 = null;
        byte? vendorCode = null;
        if (key?.GetValue("osvc") is byte[] { Length: >= 2 } osvc)
        {
            os10 = osvc[0] != 0;
            if (os10 == true) vendorCode = osvc[1];
        }
        return new UsbMsOsSupport(os10, vendorCode, bos?.MsOs20, bos?.WebUsb);
    }

    static UsbPortLocation Locate(string instanceId, UsbHubScan scan, UsbPortConnection port, Dictionary<string, (string Hub, int Port)> placement, Dictionary<string, int> buses)
    {
        var chain = new List<int>();
        string current = instanceId;
        while (placement.TryGetValue(current, out var at))
        {
            chain.Insert(0, at.Port);
            current = at.Hub;
        }
        return new UsbPortLocation(scan.InstanceId, port.Port, buses.GetValueOrDefault(current), chain, port.UserConnectable, port.TypeC);
    }

    /// <summary>Root hubs numbered 1… in the order of their PCI location paths — stable across boots and replugs.</summary>
    static Dictionary<string, int> BusNumbers(IEnumerable<UsbHubScan> scans) =>
        scans.Where(s => s.InstanceId.StartsWith(@"USB\ROOT_HUB", StringComparison.OrdinalIgnoreCase))
            .Select(s => (s.InstanceId, Path: DevNodes.Locate(s.InstanceId) is { } d ? DevNodes.Strings(d, DevPropKeys.LocationPaths).FirstOrDefault() ?? s.InstanceId : s.InstanceId))
            .OrderBy(r => r.Path, StringComparer.Ordinal)
            .Select((r, i) => (r.InstanceId, Bus: i + 1))
            .ToDictionary(r => r.InstanceId, r => r.Bus, StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher?.Dispose();
        _settleTimer.Dispose();
        _passGate.Dispose();
    }

    sealed record CachedDescriptors(byte[] DeviceDescriptor, byte Configuration, UsbWireDescriptors Wire)
    {
        public bool Matches(byte[] deviceDescriptor, byte configuration) => configuration == Configuration && deviceDescriptor.AsSpan().SequenceEqual(DeviceDescriptor);
    }
}
