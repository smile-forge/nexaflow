using System.Globalization;
using System.Runtime.CompilerServices;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Descriptors.Layout;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;

namespace Nexaflow.IO.Usb.Browsing;

/// <summary>
/// Descriptor maps for the devices of successive snapshots, built once per blob. The inventory hands the same
/// byte arrays on while a device's descriptors are unchanged, so a map is keyed on its blob and rebuilt only
/// when the blob or the device's strings (which name its string indices) change.
/// </summary>
public sealed class UsbDescriptorMapCache(UsbIdsDatabase? names, IReadOnlyList<IUsbDescriptorDecoder>? decoders = null)
{
    sealed record Built(UsbDeviceStrings Strings, UsbDescriptorMap Map);

    readonly ConditionalWeakTable<byte[], Built> _maps = new();
    readonly IReadOnlyList<IUsbDescriptorDecoder> _decoders = decoders ?? UsbDescriptorMaps.Default;

    public UsbIdsDatabase? Names => names;

    public UsbDescriptorMap Device(UsbDeviceNode device) => Get(device, device.RawDeviceDescriptor, UsbDescriptorBlobKind.Device);

    public UsbDescriptorMap Configuration(UsbDeviceNode device, UsbConfiguration configuration) =>
        Get(device, configuration.Raw, UsbDescriptorBlobKind.Configuration);

    public UsbDescriptorMap? Bos(UsbDeviceNode device) => device.Bos is { } bos ? Get(device, bos.Raw, UsbDescriptorBlobKind.Bos) : null;

    UsbDescriptorMap Get(UsbDeviceNode device, byte[] blob, UsbDescriptorBlobKind kind)
    {
        if (_maps.TryGetValue(blob, out var built) && ReferenceEquals(built.Strings, device.Strings)) return built.Map;
        var context = new UsbDescriptorContext(device.Speed, i => device.Strings[i], names);
        var map = kind switch
        {
            UsbDescriptorBlobKind.Device => UsbDescriptorMaps.Device(blob, context, _decoders),
            UsbDescriptorBlobKind.Configuration => UsbDescriptorMaps.Configuration(blob, context, _decoders),
            _ => UsbDescriptorMaps.Bos(blob, context, _decoders),
        };
        _maps.AddOrUpdate(blob, new Built(device.Strings, map));
        return map;
    }
}

/// <summary>The grammar of location paths — see <see cref="UsbLocation"/>.</summary>
public static class UsbLocationPath
{
    public const string Configuration = "config";
    public const string Bos = "bos";

    /// <summary>Trims, drops a leading "usb:" or slash, and folds case, so a path typed by hand resolves.</summary>
    public static string Normalize(string? path)
    {
        var p = (path ?? "").Trim().Replace('\\', '/');
        if (p.StartsWith("usb:", StringComparison.OrdinalIgnoreCase)) p = p[4..];
        return p.Trim('/').ToLowerInvariant();
    }

    /// <summary>
    /// The path one level up: an inner segment is dropped; a port path loses its last port (3-10.2 → 3-10), and a
    /// port on a root hub climbs to its bus (3-10 → 3); a bus climbs to the root.
    /// </summary>
    public static string? Parent(string path)
    {
        path = Normalize(path);
        if (path.Length == 0) return null;
        int slash = path.LastIndexOf('/');
        if (slash >= 0) return path[..slash];
        int dot = path.LastIndexOf('.');
        if (dot >= 0) return path[..dot];
        int dash = path.IndexOf('-');
        return dash >= 0 ? path[..dash] : "";
    }

    /// <summary>The paths from the root down to <paramref name="path"/>, root first.</summary>
    public static IReadOnlyList<string> Lineage(string path)
    {
        var chain = new List<string>();
        for (string? at = Normalize(path); at is not null; at = Parent(at)) chain.Insert(0, at);
        return chain;
    }
}

/// <summary>
/// The USB tree of one snapshot as places to go: what is at a path, what is under it, and what matches a search.
/// It reads only what the inventory already holds — browsing never touches a device.
/// </summary>
public sealed class UsbBrowser(UsbInventorySnapshot snapshot, UsbDescriptorMapCache maps)
{
    public UsbInventorySnapshot Snapshot => snapshot;
    public UsbDescriptorMapCache Maps => maps;

    public UsbLocation Root
    {
        get
        {
            int buses = RootHubs().Count();
            return new UsbLocation("", UsbLocationKind.Root, "USB", $"{buses} bus{(buses == 1 ? "" : "es")} · {snapshot.Devices.Count} device(s)");
        }
    }

    public UsbLocation? Resolve(string? path)
    {
        var normalized = UsbLocationPath.Normalize(path);
        if (normalized.Length == 0) return Root;
        var segments = normalized.Split('/');
        var current = Top(segments[0]);
        for (int i = 1; current is not null && i < segments.Length; i++) current = Child(current, segments[i]);
        return current;
    }

    /// <summary>The deepest location on <paramref name="path"/> that still exists — where a page goes when its device left.</summary>
    public UsbLocation NearestExisting(string? path)
    {
        foreach (var candidate in UsbLocationPath.Lineage(path ?? "").Reverse())
            if (Resolve(candidate) is { } found) return found;
        return Root;
    }

    public UsbLocation? Parent(UsbLocation location) => UsbLocationPath.Parent(location.Path) is { } parent ? Resolve(parent) : null;

    public IReadOnlyList<UsbLocation> Children(UsbLocation location) => location.Kind switch
    {
        UsbLocationKind.Root => [.. RootHubs().Select(Bus)],
        UsbLocationKind.Bus => Ports(location.Hub!, location.Hub!.Bus.ToString(CultureInfo.InvariantCulture), '-'),
        UsbLocationKind.Device => DeviceChildren(location),
        UsbLocationKind.Configuration or UsbLocationKind.Bos or UsbLocationKind.Descriptor => DescriptorChildren(location),
        _ => [],
    };

    /// <summary>
    /// Devices whose name, vendor, product, strings, VID:PID, port path, instance id, COM port, class or driver
    /// contains every word of <paramref name="query"/>; a path that resolves is returned as it is.
    /// </summary>
    public IReadOnlyList<UsbLocation> Find(string query)
    {
        if (Resolve(query) is { Kind: not UsbLocationKind.Root } exact) return [exact];
        var words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];
        return [.. snapshot.Devices
            .Where(d => words.All(w => SearchText(d).Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Select(DeviceAt)];
    }

    IEnumerable<UsbHubInfo> RootHubs() => snapshot.Hubs.Where(h => h.IsRoot).OrderBy(h => h.Bus);

    UsbLocation? Top(string segment)
    {
        if (int.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out int bus))
            return RootHubs().FirstOrDefault(h => h.Bus == bus) is { } root ? Bus(root) : null;

        if (snapshot.Devices.FirstOrDefault(d => d.Location.PortPath == segment) is { } device) return DeviceAt(device);

        // Not a device: an empty port, if the hub above it has one by that number.
        if (UsbLocationPath.Parent(segment) is not { Length: > 0 } hubPath) return null;
        int separator = segment.LastIndexOfAny(['.', '-']);
        if (!int.TryParse(segment.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int number)) return null;
        var hub = HubAt(hubPath);
        return hub?.Ports.FirstOrDefault(p => p.Number == number) is { } port ? EmptyPort(segment, hub, port) : null;
    }

    UsbHubInfo? HubAt(string path)
    {
        if (int.TryParse(path, NumberStyles.None, CultureInfo.InvariantCulture, out int bus)) return RootHubs().FirstOrDefault(h => h.Bus == bus);
        return snapshot.Devices.FirstOrDefault(d => d.IsHub && d.Location.PortPath == path) is { } device ? snapshot.Hub(device.InstanceId) : null;
    }

    UsbLocation Bus(UsbHubInfo root)
    {
        int inUse = root.Ports.Count(p => p.Connected);
        string name = root.Controller?.Name ?? root.Name ?? $"Bus {root.Bus}";
        return new UsbLocation(root.Bus.ToString(CultureInfo.InvariantCulture), UsbLocationKind.Bus, name,
            $"Bus {root.Bus} · {root.PortCount} port(s), {inUse} in use{(root.OpenError is { } e ? $" · {e}" : "")}")
        { Hub = root };
    }

    List<UsbLocation> Ports(UsbHubInfo hub, string prefix, char separator)
    {
        var ports = new List<UsbLocation>(hub.Ports.Count);
        foreach (var port in hub.Ports)
        {
            string path = $"{prefix}{separator}{port.Number}";
            ports.Add(snapshot.Devices.FirstOrDefault(d => d.Location.PortPath == path) is { } device ? DeviceAt(device) : EmptyPort(path, hub, port));
        }
        return ports;
    }

    static UsbLocation EmptyPort(string path, UsbHubInfo hub, UsbHubPort port)
    {
        var notes = new List<string> { "empty" };
        if (port.TypeC == true) notes.Add("USB-C");
        if (port.UserConnectable == false) notes.Add("internal");
        return new UsbLocation(path, UsbLocationKind.Port, $"Port {port.Number}", string.Join(" · ", notes)) { Hub = hub, Port = port };
    }

    UsbLocation DeviceAt(UsbDeviceNode device)
    {
        var hub = snapshot.Hub(device.Location.HubInstanceId);
        var port = hub?.Ports.FirstOrDefault(p => p.Number == device.Location.Port);
        return new UsbLocation(device.Location.PortPath, UsbLocationKind.Device, device.DisplayName, DeviceSummary(device))
        {
            Hub = hub, Port = port, Device = device, Map = maps.Device(device), Function = device.Functions.Count == 1 ? device.Functions[0] : null,
        };
    }

    string DeviceSummary(UsbDeviceNode device)
    {
        if (device.IsEjected) return $"{device.VendorId:X4}:{device.ProductId:X4} · ejected — safe to unplug";
        var parts = new List<string> { $"{device.VendorId:X4}:{device.ProductId:X4}", DeviceClass(device), device.Speed.ToString() };
        if (UsbLocationText.Modes(device) is { Length: > 0 } modes) parts.Add(modes);
        if (device.Power is UsbPowerState.D2 or UsbPowerState.D3) parts.Add("asleep");
        return string.Join(" · ", parts);
    }

    string DeviceClass(UsbDeviceNode device)
    {
        var d = device.Descriptor;
        if (d.Class != UsbClassCodes.PerInterface && d.Class != UsbClassCodes.Miscellaneous) return UsbClassCodes.Name(d.Class, d.SubClass, d.Protocol);
        var classes = (device.ActiveConfiguration?.DefaultSettings ?? [])
            .Select(i => UsbClassCodes.Name(i.Class, i.SubClass, i.Protocol)).Distinct().ToList();
        return classes.Count == 0 ? "no interfaces" : string.Join(" + ", classes);
    }

    List<UsbLocation> DeviceChildren(UsbLocation location)
    {
        var device = location.Device!;
        var children = new List<UsbLocation>();
        if (device.IsHub && snapshot.Hub(device.InstanceId) is { } hub) children.AddRange(Ports(hub, device.Location.PortPath, '.'));

        foreach (var configuration in device.Configurations)
        {
            var map = maps.Configuration(device, configuration);
            bool active = configuration.Value == device.ActiveConfigurationValue;
            children.Add(new UsbLocation($"{location.Path}/{UsbLocationPath.Configuration}{configuration.Value}", UsbLocationKind.Configuration,
                $"Configuration {configuration.Value}{(active ? " (active)" : "")}", map.Entries.Count > 0 ? map.Entries[0].Summary : "empty")
            { Device = device, Map = map, Entry = map.Entries.Count > 0 ? map.Entries[0] : null });
        }

        if (maps.Bos(device) is { } bos)
            children.Add(new UsbLocation($"{location.Path}/{UsbLocationPath.Bos}", UsbLocationKind.Bos, "BOS",
                bos.Entries.Count > 0 ? bos.Entries[0].Summary : "empty")
            { Device = device, Map = bos, Entry = bos.Entries.Count > 0 ? bos.Entries[0] : null });
        return children;
    }

    List<UsbLocation> DescriptorChildren(UsbLocation location)
    {
        if (location.Map is not { } map || location.Entry is not { } entry) return [];
        return [.. map.ChildrenOf(entry.Index).Select(child => DescriptorAt(location.Path, location.Device!, map, child))];
    }

    UsbLocation? Child(UsbLocation location, string segment)
    {
        if (location.Kind is UsbLocationKind.Device)
            return DeviceChildren(location).FirstOrDefault(c => LastSegment(c.Path) == segment);
        if (location.Map is not { } map || location.Entry is not { } entry) return null;

        // Exact child first; then anywhere beneath, so "config1/if2.0" finds an interface an association holds.
        var direct = map.ChildrenOf(entry.Index).FirstOrDefault(c => Key(map, c) == segment);
        if (direct is not null) return DescriptorAt(location.Path, location.Device!, map, direct);
        var nested = Descendants(map, entry.Index).FirstOrDefault(c => Key(map, c) == segment);
        return nested is null ? null : DescriptorAt(PathOf(map, entry, nested, location.Path), location.Device!, map, nested);
    }

    static IEnumerable<UsbDescriptorEntry> Descendants(UsbDescriptorMap map, int index)
    {
        foreach (var child in map.ChildrenOf(index))
        {
            yield return child;
            foreach (var deeper in Descendants(map, child.Index)) yield return deeper;
        }
    }

    /// <summary>The parent path of <paramref name="target"/> when it was found below <paramref name="from"/>.</summary>
    static string PathOf(UsbDescriptorMap map, UsbDescriptorEntry from, UsbDescriptorEntry target, string fromPath)
    {
        var chain = new List<string>();
        for (var at = target.Parent is { } p ? map.Entries[p] : null; at is not null && at.Index != from.Index; at = at.Parent is { } q ? map.Entries[q] : null)
            chain.Insert(0, Key(map, at));
        return chain.Count == 0 ? fromPath : $"{fromPath}/{string.Join('/', chain)}";
    }

    UsbLocation DescriptorAt(string parentPath, UsbDeviceNode device, UsbDescriptorMap map, UsbDescriptorEntry entry) =>
        new($"{parentPath}/{Key(map, entry)}", UsbLocationKind.Descriptor, UsbLocationText.EntryName(map, entry), entry.Summary)
        {
            Device = device, Map = map, Entry = entry, Function = FunctionOf(device, map, entry),
        };

    /// <summary>The segment naming a descriptor among its siblings: if0.1, ep0x81, iad2, or d7 for anything else.</summary>
    public static string Key(UsbDescriptorMap map, UsbDescriptorEntry entry)
    {
        byte At(int i) => entry.Length > i ? map.Bytes[entry.Offset + i] : (byte)0;
        return entry.Type switch
        {
            UsbDescriptorType.Interface when entry.Length >= 4 => $"if{At(2)}.{At(3)}",
            UsbDescriptorType.Endpoint when entry.Length >= 3 => $"ep0x{At(2):x2}",
            UsbDescriptorType.InterfaceAssociation when entry.Length >= 3 => $"iad{At(2)}",
            _ => $"d{entry.Index}",
        };
    }

    static string LastSegment(string path) => path[(path.LastIndexOf('/') + 1)..];

    static UsbFunctionNode? FunctionOf(UsbDeviceNode device, UsbDescriptorMap map, UsbDescriptorEntry entry)
    {
        for (var at = entry; at is not null; at = at.Parent is { } p ? map.Entries[p] : null)
        {
            if (at.Type is not (UsbDescriptorType.Interface or UsbDescriptorType.InterfaceAssociation)) continue;
            if (map.Bytes.Length <= at.Offset + 2) return null;
            byte number = map.Bytes[at.Offset + 2];
            return device.Functions.FirstOrDefault(f => f.InterfaceNumbers.Contains(number))
                ?? device.Functions.FirstOrDefault(f => f.FunctionInterface is null);
        }
        return null;
    }

    static string SearchText(UsbDeviceNode d) => string.Join('\n', new[]
    {
        d.DisplayName, d.VendorName, d.ProductName, d.Strings.Manufacturer, d.Strings.Product, d.Strings.SerialNumber, d.BusReportedName,
        $"{d.VendorId:x4}:{d.ProductId:x4}", d.Location.PortPath, d.InstanceId, d.Driver.Service,
    }.Concat(d.Functions.SelectMany(f => new[] { f.Name, f.ComPort, f.Driver.Service, f.Driver.ClassName }))
     .Concat((d.ActiveConfiguration?.DefaultSettings ?? []).Select(i => UsbClassCodes.Name(i.Class, i.SubClass, i.Protocol)))
     .OfType<string>());
}
