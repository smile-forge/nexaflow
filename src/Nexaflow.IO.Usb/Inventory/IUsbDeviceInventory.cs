namespace Nexaflow.IO.Usb.Inventory;

public sealed record UsbHubInfo(string InstanceId, string Kind, int PortCount, string? OpenError);

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
}
