using System.Buffers.Binary;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Nexaflow.Spikes.UsbDriverBinding.Native;

namespace Nexaflow.Spikes.UsbDriverBinding.Inventory;

public enum InterfaceMode { WindowsClass, GenericRaw, VendorDriver, NoDriver, CompositeParent }

public sealed record DriverInfo(
    string? Service, string? Class, string? ClassGuid, string? InfPath, string? InfSection, string? Version, string? Provider,
    string? MatchingDeviceId, uint Problem, string[] HardwareIds, string[] CompatibleIds);

public sealed record ChildNode(string InstanceId, string? Class, string? Service, string? Name);

public sealed record FunctionNode(
    string InstanceId, int? InterfaceNumber, string? Name, DriverInfo Driver, InterfaceMode Mode,
    string[] DeviceInterfaceGuids, string? ComPort, List<ChildNode> Children)
{
    public string? PowerState { get; init; }
}

public sealed record MsOsDescriptors(bool? Os10FromRegistry, string? Os10VendorCode, string? Os20, string? WebUsb);

public sealed record DeviceReport(
    string InstanceId, string? ParentHubInstanceId, string PortPath, string Vid, string Pid, string? VendorName, string? ProductName,
    Dictionary<string, string?> Strings, DeviceDescriptorInfo Descriptor, string DeviceClassName, string Speed, bool? SuperSpeedCapable,
    bool? UserConnectablePort, bool? TypeCPort, bool IsHub, DriverInfo DeviceDriver, List<FunctionNode> Functions,
    List<ConfigurationInfo> Configurations, List<BosCapability> Bos, MsOsDescriptors MsOs, List<HidReportProbe> HidReports,
    List<string> Summary, Dictionary<string, string> RawDescriptorsHex, List<string> Errors, double DescriptorReadMs)
{
    public string? PowerState { get; init; }
}

public sealed record HubReport(string InstanceId, string Type, int Ports, string? OpenError);

public sealed record InventoryReport(string Schema, DateTimeOffset CapturedAt, List<HubReport> Hubs, List<DeviceReport> Devices, List<string> Unmatched);

/// <summary>Joins the hub walk (what the device says) with CfgMgr32 (what Windows did about it).</summary>
public static class DeviceInventory
{
    static readonly Regex InterfaceSuffix = new(@"&MI_([0-9A-F]{2})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static readonly HashSet<string> GenericServices = new(StringComparer.OrdinalIgnoreCase) { "WINUSB", "libusbK", "libusb0", "UsbDk" };
    static readonly HashSet<string> ClassServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "HidUsb", "usbser", "USBSTOR", "UASPStor", "usbaudio", "usbaudio2", "usbvideo", "BTHUSB", "usbprint", "usbccid",
        "WUDFRd", "UsbNcm", "usb8023", "RNDISMP", "WpdUsb", "USBHUB3", "usbhub",
    };

    public static InventoryReport Capture(UsbIds? names)
    {
        var scans = HubWalker.Scan(readDescriptors: true);
        var byDriverKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in DevNodes.PresentDeviceIds("USB"))
            if (DevNodes.Locate(id) is { } devInst && DevNodes.String(devInst, DevPropKeys.DriverKey) is { } key)
                byDriverKey[key] = id;

        // instance id → (parent hub instance id, port) so a port path can be built from the root down
        var placement = new Dictionary<string, (string Hub, int Port)>(StringComparer.OrdinalIgnoreCase);
        foreach (var scan in scans)
            foreach (var port in scan.Ports)
                if (port.DriverKey is { } key && byDriverKey.TryGetValue(key, out var id))
                    placement[id] = (scan.HubInstanceId, port.Port);

        var devices = new List<DeviceReport>();
        var unmatched = new List<string>();
        foreach (var scan in scans)
            foreach (var port in scan.Ports)
            {
                if (port.DriverKey is null || !byDriverKey.TryGetValue(port.DriverKey, out var instanceId))
                {
                    unmatched.Add($"{scan.HubInstanceId} port {port.Port}: driver key {port.DriverKey ?? "(none)"} has no devnode");
                    continue;
                }
                devices.Add(Build(instanceId, scan, port, PortPath(instanceId, placement), names));
            }

        devices.Sort((a, b) => string.CompareOrdinal(a.PortPath, b.PortPath));
        return new InventoryReport("nexaflow.usb.spike-inventory/1", DateTimeOffset.Now,
            scans.Select(s => new HubReport(s.HubInstanceId, s.HubType, s.PortCount, s.OpenError)).ToList(), devices, unmatched);
    }

    static string PortPath(string instanceId, Dictionary<string, (string Hub, int Port)> placement)
    {
        var ports = new Stack<int>();
        string current = instanceId;
        while (placement.TryGetValue(current, out var at))
        {
            ports.Push(at.Port);
            current = at.Hub;
        }
        return $"{RootLabel(current)}-{string.Join('.', ports)}";
    }

    static string RootLabel(string rootHubInstanceId) => rootHubInstanceId.Split('\\').LastOrDefault() ?? rootHubInstanceId;

    static DeviceReport Build(string instanceId, HubScan scan, HubPort port, string portPath, UsbIds? names)
    {
        var errors = new List<string>();
        var descriptor = DeviceDescriptorInfo.Parse(port.DeviceDescriptor);
        var descriptors = port.Descriptors;
        if (descriptors is not null) errors.AddRange(descriptors.Errors);

        bool superSpeed = port.Speed.StartsWith("Super", StringComparison.Ordinal);
        var configurations = descriptors?.Configurations.Select(c => DescriptorParser.ParseConfiguration(c, superSpeed)).ToList() ?? [];
        var strings = descriptors?.Strings ?? [];
        foreach (var iface in configurations.SelectMany(c => c.Interfaces))
            iface.Name = strings.GetValueOrDefault(iface.NameIndex);

        var bos = descriptors?.Bos is { } bosBytes ? DescriptorParser.ParseBos(bosBytes) : [];
        var devInst = DevNodes.Locate(instanceId);
        var deviceDriver = Driver(devInst);
        var functions = Functions(instanceId, devInst, deviceDriver);

        var raw = new Dictionary<string, string> { ["device"] = DescriptorParser.Hex(port.DeviceDescriptor) };
        for (int i = 0; i < (descriptors?.Configurations.Count ?? 0); i++) raw[$"configuration{i}"] = DescriptorParser.Hex(descriptors!.Configurations[i]);
        if (descriptors?.Bos is { } b) raw["bos"] = DescriptorParser.Hex(b);

        var msOs = new MsOsDescriptors(
            Os10FromRegistry(descriptor, out var vendorCode), vendorCode,
            bos.FirstOrDefault(c => c.Name == "MS OS 2.0")?.Detail,
            bos.FirstOrDefault(c => c.Name == "WebUSB")?.Detail);

        var report = new DeviceReport(
            instanceId, scan.HubInstanceId, portPath, $"0x{descriptor.VendorId:X4}", $"0x{descriptor.ProductId:X4}",
            names?.Vendor(descriptor.VendorId), names?.Product(descriptor.VendorId, descriptor.ProductId),
            new()
            {
                ["manufacturer"] = strings.GetValueOrDefault(descriptor.ManufacturerIndex),
                ["product"] = strings.GetValueOrDefault(descriptor.ProductIndex),
                ["serial"] = strings.GetValueOrDefault(descriptor.SerialIndex),
            },
            descriptor, UsbClassNames.Interface(descriptor.Class, descriptor.SubClass, descriptor.Protocol), port.Speed, port.SuperSpeedCapable,
            port.UserConnectable, port.TypeC, port.IsHub, deviceDriver, functions, configurations, bos, msOs,
            descriptors?.HidReports ?? [], [], raw, errors, descriptors?.ElapsedMs ?? 0) { PowerState = devInst is { } p ? DevNodes.PowerState(p) : null };
        report.Summary.AddRange(CapabilitySummary.Describe(report));
        return report;
    }

    static List<FunctionNode> Functions(string instanceId, uint? devInst, DriverInfo deviceDriver)
    {
        if (devInst is null) return [];
        if (!string.Equals(deviceDriver.Service, "usbccgp", StringComparison.OrdinalIgnoreCase))
            return [Function(instanceId, devInst.Value, null)];
        return DevNodes.Strings(devInst.Value, DevPropKeys.Children)
            .Select(child => (child, inst: DevNodes.Locate(child)))
            .Where(x => x.inst is not null)
            .Select(x => Function(x.child, x.inst!.Value, InterfaceSuffix.Match(x.child) is { Success: true } m ? Convert.ToInt32(m.Groups[1].Value, 16) : null))
            .ToList();
    }

    static FunctionNode Function(string instanceId, uint devInst, int? interfaceNumber)
    {
        var driver = Driver(devInst);
        var children = DevNodes.Strings(devInst, DevPropKeys.Children)
            .Select(id => DevNodes.Locate(id) is { } c
                ? new ChildNode(id, DevNodes.String(c, DevPropKeys.Class), DevNodes.String(c, DevPropKeys.Service), DevNodes.String(c, DevPropKeys.FriendlyName) ?? DevNodes.String(c, DevPropKeys.DeviceDesc))
                : new ChildNode(id, null, null, null))
            .ToList();
        using var parameters = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{instanceId}\Device Parameters");
        var guids = parameters?.GetValue("DeviceInterfaceGUIDs") as string[]
            ?? (parameters?.GetValue("DeviceInterfaceGUID") is string single ? [single] : []);
        return new FunctionNode(instanceId, interfaceNumber,
            DevNodes.String(devInst, DevPropKeys.BusReportedDeviceDesc) ?? DevNodes.String(devInst, DevPropKeys.FriendlyName),
            driver, Classify(driver), guids, parameters?.GetValue("PortName") as string, children) { PowerState = DevNodes.PowerState(devInst) };
    }

    static DriverInfo Driver(uint? devInst)
    {
        if (devInst is not { } d) return new DriverInfo(null, null, null, null, null, null, null, null, 0, [], []);
        return new DriverInfo(
            DevNodes.String(d, DevPropKeys.Service), DevNodes.String(d, DevPropKeys.Class), DevNodes.Guid(d, DevPropKeys.ClassGuid)?.ToString(),
            DevNodes.String(d, DevPropKeys.DriverInfPath), DevNodes.String(d, DevPropKeys.DriverInfSection),
            DevNodes.String(d, DevPropKeys.DriverVersion), DevNodes.String(d, DevPropKeys.DriverProvider),
            DevNodes.String(d, DevPropKeys.MatchingDeviceId), DevNodes.Status(d)?.Problem ?? 0,
            DevNodes.Strings(d, DevPropKeys.HardwareIds), DevNodes.Strings(d, DevPropKeys.CompatibleIds));
    }

    static InterfaceMode Classify(DriverInfo driver) => driver.Service switch
    {
        null or "" => InterfaceMode.NoDriver,
        var s when s.Equals("usbccgp", StringComparison.OrdinalIgnoreCase) => InterfaceMode.CompositeParent,
        var s when GenericServices.Contains(s) => InterfaceMode.GenericRaw,
        var s when ClassServices.Contains(s) => InterfaceMode.WindowsClass,
        _ => InterfaceMode.VendorDriver,
    };

    /// <summary>
    /// Windows asks every new device for string 0xEE once and caches the answer under usbflags, so the MS OS 1.0
    /// question is answered without sending anything to the device.
    /// </summary>
    static bool? Os10FromRegistry(DeviceDescriptorInfo d, out string? vendorCode)
    {
        vendorCode = null;
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\usbflags\{d.VendorId:X4}{d.ProductId:X4}{d.BcdDeviceRaw:X4}");
        if (key?.GetValue("osvc") is not byte[] { Length: >= 2 } osvc) return null;
        if (osvc[0] != 0) vendorCode = $"0x{osvc[1]:X2}";
        return osvc[0] != 0;
    }
}
