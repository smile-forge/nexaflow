using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Descriptors.Layout;
using Nexaflow.IO.Usb.Inventory;


namespace Nexaflow.IO.Usb.Browsing;

public sealed record UsbFact(string Label, string Value);

/// <summary>What a group of facts is about, so a page that shows some of it elsewhere can leave the group out.</summary>
public enum UsbFactTopic
{
    General,
    /// <summary>The plain-language verdict: what the device is and what can be done with it.</summary>
    Summary,
    /// <summary>The functions Windows made of the device — what the device page's Interfaces tab shows in full.</summary>
    Functions,
    /// <summary>What went wrong reading the device — what the device page's Diagnostics tab shows in full.</summary>
    Diagnostics,
}

public sealed record UsbFactGroup(string Title, IReadOnlyList<UsbFact> Facts)
{
    public UsbFactTopic Topic { get; init; } = UsbFactTopic.General;
}

/// <summary>Short words for the states the inventory reports, shared by every place that names them.</summary>
public static class UsbLocationText
{
    public static string Mode(UsbFunctionMode mode, string? service) => mode switch
    {
        UsbFunctionMode.WindowsClass => $"Windows driver ({service})",
        UsbFunctionMode.GenericRaw => $"raw access ({service})",
        UsbFunctionMode.VendorDriver => $"vendor driver ({service})",
        UsbFunctionMode.NoDriver => "no driver",
        UsbFunctionMode.CompositeParent => "composite",
        _ => mode.ToString(),
    };

    /// <summary>Each function's mode, without repeats — "Windows driver (HidUsb), raw access (WINUSB)".</summary>
    public static string Modes(UsbDeviceNode device) =>
        string.Join(", ", device.Functions.Select(f => Mode(f.Mode, f.Driver.Service)).Distinct());

    public static string Power(UsbPowerState power) => power switch
    {
        UsbPowerState.D0 => "on (D0)",
        UsbPowerState.D1 or UsbPowerState.D2 => $"low power ({power})",
        UsbPowerState.D3 => "suspended (D3)",
        _ => "unknown",
    };

    /// <summary>A descriptor's name in a listing: "Interface 2, alt 1", "Endpoint 0x81", or its kind.</summary>
    public static string EntryName(UsbDescriptorMap map, UsbDescriptorEntry entry)
    {
        byte At(int i) => entry.Length > i ? map.Bytes[entry.Offset + i] : (byte)0;
        return entry.Type switch
        {
            UsbDescriptorType.Interface when entry.Length >= 4 => $"Interface {At(2)}, alt {At(3)}",
            UsbDescriptorType.Endpoint when entry.Length >= 3 => $"Endpoint 0x{At(2):X2}",
            UsbDescriptorType.InterfaceAssociation when entry.Length >= 4 =>
                At(3) <= 1 ? $"Function (interface {At(2)})" : $"Function (interfaces {At(2)}–{At(2) + At(3) - 1})",
            _ => entry.Kind,
        };
    }
}

/// <summary>
/// What a location says about itself beyond its descriptor fields: who it is, where it is plugged in, what
/// Windows did with it, and what that makes possible — the leaf information a listing has no room for.
/// </summary>
public static class UsbLocationFacts
{
    public static IReadOnlyList<UsbFactGroup> Of(UsbLocation location) => location.Kind switch
    {
        UsbLocationKind.Root => [],
        UsbLocationKind.Bus => Bus(location),
        UsbLocationKind.Port => Port(location),
        UsbLocationKind.Device => Device(location),
        UsbLocationKind.Configuration => Configuration(location),
        _ => location.Function is { } function && IsInterfaceLevel(location) ? [Function(function)] : [],
    };

    static bool IsInterfaceLevel(UsbLocation location) =>
        location.Entry?.Type is UsbDescriptorType.Interface or UsbDescriptorType.InterfaceAssociation;

    static IReadOnlyList<UsbFactGroup> Bus(UsbLocation location)
    {
        var hub = location.Hub!;
        var groups = new List<UsbFactGroup>();
        if (hub.Controller is { } controller)
            groups.Add(new("Host controller", Facts(
                ("Name", controller.Name), ("Instance ID", controller.InstanceId),
                ("Service", controller.Driver.Service), ("Driver version", controller.Driver.Version),
                ("Provider", controller.Driver.Provider), ("Problem", Problem(controller.Driver.ProblemCode)))));
        groups.Add(new("Root hub", Facts(
            ("Name", hub.Name), ("Instance ID", hub.InstanceId), ("Kind", hub.Kind), ("Bus", hub.Bus.ToString()),
            ("Ports", $"{hub.PortCount}, {hub.Ports.Count(p => p.Connected)} in use"), ("Error", hub.OpenError))));
        return groups;
    }

    static IReadOnlyList<UsbFactGroup> Port(UsbLocation location) =>
    [
        new("Port", Facts(
            ("Number", location.Port!.Number.ToString()), ("Hub", location.Hub?.Name ?? location.Hub?.InstanceId),
            ("Connector", Connector(location.Port)), ("State", "nothing plugged in"))),
    ];

    static IReadOnlyList<UsbFactGroup> Device(UsbLocation location)
    {
        var d = location.Device!;
        var active = d.ActiveConfiguration;
        var groups = new List<UsbFactGroup>
        {
            new("Identity", Facts(
                ("Name", d.DisplayName),
                ("Manufacturer", d.Strings.Manufacturer), ("Product", d.Strings.Product), ("Serial number", d.Strings.SerialNumber),
                ("Vendor (usb.ids)", d.VendorName), ("Product (usb.ids)", d.ProductName),
                ("VID:PID", $"{d.VendorId:X4}:{d.ProductId:X4}"),
                ("USB version", d.Descriptor.UsbVersion.ToString()), ("Device version", d.Descriptor.DeviceVersion.ToString()),
                ("Class", d.Descriptor.Class == UsbClassCodes.PerInterface ? "per interface" : UsbClassCodes.Name(d.Descriptor.Class, d.Descriptor.SubClass, d.Descriptor.Protocol)),
                ("Strings", d.Strings.Complete ? null : "not read — the device was asleep or refused"))),
            new("Connection", Facts(
                ("Port path", d.Location.PortPath), ("Hub", location.Hub?.Name ?? d.Location.HubInstanceId), ("Port", d.Location.Port.ToString()),
                ("Connector", location.Port is { } port ? Connector(port) : null),
                ("Speed", d.Speed.ToString()), ("SuperSpeed capable", d.SuperSpeedCapable is { } ss ? (ss ? "yes" : "no") : null),
                ("Power state", UsbLocationText.Power(d.Power)),
                ("Max power", active is null ? null : $"{active.MaxPowerMilliamps} mA"),
                ("Powered", active is null ? null : active.SelfPowered ? "self-powered" : "bus-powered"),
                ("Remote wakeup", active is null ? null : active.RemoteWakeup ? "supported" : "no"))),
        };

        var capabilities = UsbCapabilitySummary.Describe(d);
        if (capabilities.Count > 0)
            groups.Add(new("What it is", [.. capabilities.Select(line => new UsbFact("", line))]) { Topic = UsbFactTopic.Summary });

        groups.Add(new("Microsoft OS descriptors", Facts(
            ("WinUSB-ready", d.MsOs.DeclaresWinUsbReadiness ? "yes — raw access needs no driver change" : "no"),
            ("MS OS 1.0", d.MsOs.Os10 switch { true => $"yes, vendor code 0x{d.MsOs.Os10VendorCode:X2}", false => "no", null => "not known" }),
            ("MS OS 2.0", d.MsOs.Os20 is { } os20 ? $"set of {os20.DescriptorSetLength} bytes, vendor code 0x{os20.VendorCode:X2}" : "no"),
            ("WebUSB", d.MsOs.WebUsb is { } web ? $"version {web.Version}, vendor code 0x{web.VendorCode:X2}" : "no"))));

        groups.Add(new("Driver", DriverFacts(d.InstanceId, d.Driver, null)));
        if (d.Functions.Count > 1 || d.Functions.Count == 1 && d.Functions[0].FunctionInterface is not null)
            groups.Add(new("Functions", [.. d.Functions.Select(f => new UsbFact(
                f.FunctionInterface is { } mi ? $"Interface {mi}" : "Device",
                $"{f.Name ?? f.InstanceId} — {UsbLocationText.Mode(f.Mode, f.Driver.Service)}{(f.ComPort is { } com ? $", {com}" : "")}"))]) { Topic = UsbFactTopic.Functions });
        if (d.Auxiliary.Count > 0)
            groups.Add(new("Also exposes", [.. d.Auxiliary.Select(a => new UsbFact(a.ClassName ?? "device", a.Name ?? a.InstanceId))]));
        if (d.Diagnostics.Count > 0)
            groups.Add(new("Diagnostics", [.. d.Diagnostics.Select(line => new UsbFact("", line))]) { Topic = UsbFactTopic.Diagnostics });
        return groups;
    }

    static IReadOnlyList<UsbFactGroup> Configuration(UsbLocation location)
    {
        var d = location.Device!;
        var map = location.Map!;
        byte value = map.Bytes.Length > 5 ? map.Bytes[5] : (byte)0;
        var configuration = d.Configurations.FirstOrDefault(c => c.Value == value);
        if (configuration is null) return [];
        var groups = new List<UsbFactGroup>
        {
            new("Configuration", Facts(
                ("Value", configuration.Value.ToString()), ("Active", configuration.Value == d.ActiveConfigurationValue ? "yes" : "no"),
                ("Name", d.Strings[configuration.NameIndex]),
                ("Interfaces", $"{configuration.DefaultSettings.Count()} ({configuration.Interfaces.Count} setting(s) with alternates)"),
                ("Functions", configuration.Associations.Count == 0 ? null : $"{configuration.Associations.Count} interface association(s)"),
                ("Max power", $"{configuration.MaxPowerMilliamps} mA"),
                ("Powered", configuration.SelfPowered ? "self-powered" : "bus-powered"),
                ("Remote wakeup", configuration.RemoteWakeup ? "supported" : "no"))),
        };
        var problems = configuration.Problems.Concat(map.Problems).Distinct().ToList();
        if (problems.Count > 0) groups.Add(new("Problems", [.. problems.Select(p => new UsbFact("", p))]));
        return groups;
    }

    static UsbFactGroup Function(UsbFunctionNode function) => new("Windows function", DriverFacts(function.InstanceId, function.Driver, function));

    static IReadOnlyList<UsbFact> DriverFacts(string instanceId, UsbDevNodeDriver driver, UsbFunctionNode? function) => Facts(
        ("Instance ID", instanceId),
        ("Name", function?.Name),
        ("Mode", function is null ? null : UsbLocationText.Mode(function.Mode, driver.Service)),
        ("Service", driver.Service ?? "none"),
        ("Class", driver.ClassName),
        ("INF", driver.InfPath is null ? null : driver.InfSection is null ? driver.InfPath : $"{driver.InfPath} [{driver.InfSection}]"),
        ("Version", driver.Version), ("Provider", driver.Provider), ("Matched on", driver.MatchingDeviceId),
        ("Problem", Problem(driver.ProblemCode)),
        ("Power state", function is null ? null : UsbLocationText.Power(function.Power)),
        ("COM port", function?.ComPort),
        ("Interface GUIDs", function is { DeviceInterfaceGuids.Count: > 0 } ? string.Join(", ", function.DeviceInterfaceGuids.Select(g => g.ToString("B"))) : null),
        ("Child devices", function is { Children.Count: > 0 } ? string.Join(", ", function.Children.Select(c => $"{c.Name ?? c.InstanceId} ({c.ClassName ?? "no class"})")) : null),
        ("Hardware IDs", driver.HardwareIds.Count == 0 ? null : string.Join("\n", driver.HardwareIds)),
        ("Compatible IDs", driver.CompatibleIds.Count == 0 ? null : string.Join("\n", driver.CompatibleIds)));

    static string? Problem(uint code) => UsbProblemCodes.Describe(code);

    static string Connector(UsbHubPort port)
    {
        var parts = new List<string> { port.UserConnectable switch { true => "user-connectable", false => "internal", null => "not reported" } };
        if (port.TypeC == true) parts.Add("USB-C");
        return string.Join(", ", parts);
    }

    /// <summary>The facts that have a value — an unknown is left out rather than shown as a blank.</summary>
    static IReadOnlyList<UsbFact> Facts(params (string Label, string? Value)[] facts) =>
        [.. facts.Where(f => !string.IsNullOrEmpty(f.Value)).Select(f => new UsbFact(f.Label, f.Value!))];
}
