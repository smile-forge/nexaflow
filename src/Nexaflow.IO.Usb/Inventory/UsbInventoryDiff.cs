namespace Nexaflow.IO.Usb.Inventory;

/// <summary>What changed between two passes, by device instance id.</summary>
public static class UsbInventoryDiff
{
    public static IReadOnlyList<UsbInventoryChange> Between(UsbInventorySnapshot before, UsbInventorySnapshot after)
    {
        var changes = new List<UsbInventoryChange>();
        var old = before.Devices.ToDictionary(d => d.InstanceId, StringComparer.OrdinalIgnoreCase);
        foreach (var device in after.Devices)
        {
            if (!old.Remove(device.InstanceId, out var previous)) changes.Add(new(UsbInventoryChangeKind.Arrived, device.InstanceId, device));
            else if (!SameDrivers(previous, device)) changes.Add(new(UsbInventoryChangeKind.DriverChanged, device.InstanceId, device));
            else if (!SameDescription(previous, device)) changes.Add(new(UsbInventoryChangeKind.Updated, device.InstanceId, device));
        }
        foreach (var gone in old.Values) changes.Add(new(UsbInventoryChangeKind.Removed, gone.InstanceId, null));
        return changes;
    }

    static bool SameDrivers(UsbDeviceNode a, UsbDeviceNode b) =>
        Same(a.Driver, b.Driver) && a.Functions.Count == b.Functions.Count
        && a.Functions.Zip(b.Functions).All(p => string.Equals(p.First.InstanceId, p.Second.InstanceId, StringComparison.OrdinalIgnoreCase)
                                              && p.First.Mode == p.Second.Mode && Same(p.First.Driver, p.Second.Driver));

    static bool Same(UsbDevNodeDriver a, UsbDevNodeDriver b) =>
        string.Equals(a.Service, b.Service, StringComparison.OrdinalIgnoreCase) && a.ProblemCode == b.ProblemCode
        && string.Equals(a.InfPath, b.InfPath, StringComparison.OrdinalIgnoreCase);

    // Power state is left out: it moves on its own between passes and is not a change anyone acts on.
    // What Windows made of each function is in: a phone unlocked into file transfer keeps its USB devnode and gains a
    // portable-device child, and a serial adapter can be given another COM port.
    static bool SameDescription(UsbDeviceNode a, UsbDeviceNode b) =>
        SameOffspring(a, b)
        && a.RawDeviceDescriptor.AsSpan().SequenceEqual(b.RawDeviceDescriptor)
        && a.ActiveConfigurationValue == b.ActiveConfigurationValue
        && a.Configurations.Count == b.Configurations.Count
        && a.Strings.Complete == b.Strings.Complete && a.Strings.ByIndex.Count == b.Strings.ByIndex.Count
        && (a.Bos is null) == (b.Bos is null)
        && a.Location.PortPath == b.Location.PortPath
        && a.VendorName == b.VendorName && a.ProductName == b.ProductName;

    static bool SameOffspring(UsbDeviceNode a, UsbDeviceNode b) =>
        Ids(a.Auxiliary).SetEquals(Ids(b.Auxiliary))
        && a.Functions.Zip(b.Functions).All(p => p.First.ComPort == p.Second.ComPort && Ids(p.First.Children).SetEquals(Ids(p.Second.Children)));

    static HashSet<string> Ids(IEnumerable<UsbChildNode> nodes) => new(nodes.Select(n => n.InstanceId), StringComparer.OrdinalIgnoreCase);
}
