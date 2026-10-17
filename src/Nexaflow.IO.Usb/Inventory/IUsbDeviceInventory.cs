using Nexaflow.IO.Usb.Naming;

namespace Nexaflow.IO.Usb.Inventory;

/// <summary>One downstream port of a hub, occupied or not, with what the port itself says about its connector.</summary>
public sealed record UsbHubPort(int Number, bool Connected, bool? UserConnectable, bool? TypeC);

/// <summary>The host controller a root hub belongs to: the PCI (or platform) device the bus hangs from.</summary>
public sealed record UsbHostController(string InstanceId, string Name, UsbDevNodeDriver Driver);

/// <summary>
/// A hub — root or external — as the topology scan saw it. A root hub carries its <see cref="Controller"/>; an
/// external hub is also a <see cref="UsbDeviceNode"/> with the same instance id.
/// </summary>
public sealed record UsbHubInfo(
    string InstanceId, string Kind, string? Name, int Bus, int PortCount, IReadOnlyList<UsbHubPort> Ports,
    UsbHostController? Controller, string? OpenError)
{
    public bool IsRoot => Controller is not null;
}

/// <summary>One complete, immutable inventory pass.</summary>
public sealed class UsbInventorySnapshot(DateTimeOffset capturedAt, IReadOnlyList<UsbDeviceNode> devices, IReadOnlyList<UsbHubInfo> hubs, IReadOnlyList<string> diagnostics)
{
    public static readonly UsbInventorySnapshot Empty = new(DateTimeOffset.MinValue, [], [], []);

    public DateTimeOffset CapturedAt => capturedAt;
    public IReadOnlyList<UsbDeviceNode> Devices => devices;
    public IReadOnlyList<UsbHubInfo> Hubs => hubs;
    /// <summary>Topology problems that belong to no one device: a hub that would not open, a port with no devnode.</summary>
    public IReadOnlyList<string> Diagnostics => diagnostics;

    public UsbDeviceNode? Find(string instanceId) =>
        devices.FirstOrDefault(d => string.Equals(d.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The device owning a function devnode, or the device itself.</summary>
    public UsbDeviceNode? FindOwner(string instanceId) => Find(instanceId) ?? devices.FirstOrDefault(d => d.Function(instanceId) is not null);

    public UsbHubInfo? Hub(string instanceId) =>
        hubs.FirstOrDefault(h => string.Equals(h.InstanceId, instanceId, StringComparison.OrdinalIgnoreCase));
}

public enum UsbInventoryChangeKind
{
    Arrived,
    Removed,
    /// <summary>A function's service, mode or problem code changed — a take-over, a restore, a driver update.</summary>
    DriverChanged,
    /// <summary>Anything else about a present device changed, such as strings that could not be read while it slept.</summary>
    Updated,
}

public sealed record UsbInventoryChange(UsbInventoryChangeKind Kind, string InstanceId, UsbDeviceNode? Device);

public sealed class UsbInventoryChangedEventArgs(UsbInventorySnapshot snapshot, IReadOnlyList<UsbInventoryChange> changes) : EventArgs
{
    public UsbInventorySnapshot Snapshot => snapshot;
    public IReadOnlyList<UsbInventoryChange> Changes => changes;
}

/// <summary>
/// The live USB device tree. Reading it needs no privilege and changes nothing: descriptors come through the hubs,
/// driver state through the configuration manager. <see cref="Changed"/> is raised on a background thread.
/// </summary>
public interface IUsbDeviceInventory : IDisposable
{
    UsbInventorySnapshot Snapshot { get; }

    event EventHandler<UsbInventoryChangedEventArgs>? Changed;

    /// <summary>Takes the first snapshot and starts following arrivals, removals and driver changes.</summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>A full pass now; descriptors already read for a device are reused.</summary>
    Task<UsbInventorySnapshot> RefreshAsync(CancellationToken ct);

    /// <summary>Re-reads one device's descriptors from the wire even if it was asleep before — the explicit "look again".</summary>
    Task<UsbDeviceNode?> RefreshDeviceAsync(string instanceId, CancellationToken ct);

    /// <summary>
    /// Names devices from <paramref name="names"/> from now on, and runs a pass so the snapshot carries them — after
    /// a newer usb.ids was downloaded. Devices whose names changed are reported as <see cref="UsbInventoryChangeKind.Updated"/>.
    /// </summary>
    Task<UsbInventorySnapshot> UseNamesAsync(UsbIdsDatabase? names, CancellationToken ct);
}
