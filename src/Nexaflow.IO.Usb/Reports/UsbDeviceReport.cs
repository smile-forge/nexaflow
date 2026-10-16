using System.Text.Json;
using System.Text.Json.Serialization;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Naming;

namespace Nexaflow.IO.Usb.Reports;

/// <summary>
/// <c>nexaflow.usb.report/1</c> (usb-probe-spec §10): one device as structured context — exported as JSON, pasted
/// as text, handed to the AI, and the seed of a future device profile. Hex values are strings so the report reads
/// the way the descriptors are written.
/// </summary>
public sealed record UsbDeviceReport(
    string Schema, DateTimeOffset CapturedAt, UsbReportDevice Device, IReadOnlyList<UsbReportConfiguration> Configurations,
    UsbReportMsOs MsOsDescriptors, IReadOnlyList<UsbReportCapability> Bos, IReadOnlyList<UsbReportChild> Auxiliary,
    IReadOnlyList<string> Summary, IReadOnlyList<string> Diagnostics, IReadOnlyDictionary<string, string> RawDescriptorsHex)
{
    public const string SchemaId = "nexaflow.usb.report/1";

    public string ToJson() => JsonSerializer.Serialize(this, UsbReportJsonContext.Default.UsbDeviceReport);

    public static UsbDeviceReport? FromJson(string json) => JsonSerializer.Deserialize(json, UsbReportJsonContext.Default.UsbDeviceReport);
}

public sealed record UsbReportStrings(string? Manufacturer, string? Product, string? Serial, bool Complete);

public sealed record UsbReportDevice(
    string InstanceId, string Vid, string Pid, string? VendorName, string? ProductName, UsbReportStrings Strings,
    string Usb, string DeviceVersion, string Class, string ClassName, string Speed, bool? SuperSpeedCapable,
    int? MaxPowerMa, bool? SelfPowered, bool? RemoteWakeup, string PortPath, bool? TypeCPort, string Power, bool IsHub, UsbReportDriver Driver);

public sealed record UsbReportDriver(string? Service, string? Class, string? Inf, string? Provider, string? Version, string? MatchingId, uint ProblemCode, string? ComPort);

public sealed record UsbReportEndpoint(string Address, string Type, string Dir, int MaxPacket, int Interval);

public sealed record UsbReportExtra(string Kind, string Hex);

public sealed record UsbReportInterface(
    int Number, int Alternate, string Class, string ClassName, string SubClass, string Protocol, string? Name,
    string? FunctionInstanceId, string? Mode, UsbReportDriver? Driver, IReadOnlyList<UsbReportEndpoint> Endpoints, IReadOnlyList<UsbReportExtra> ClassSpecific);

public sealed record UsbReportConfiguration(int Value, string? Name, bool Active, IReadOnlyList<UsbReportInterface> Interfaces, IReadOnlyList<string> Problems);

public sealed record UsbReportMsOs(bool Present, bool? Os10, string? Os10VendorCode, string? Os20, string? WebUsb);

public sealed record UsbReportCapability(string Name, string Detail);

public sealed record UsbReportChild(string InstanceId, string? Class, string? Service, string? Name);

public static class UsbDeviceReports
{
    public static UsbDeviceReport Build(UsbDeviceNode device, DateTimeOffset capturedAt, UsbIdsDatabase? names = null)
    {
        var d = device.Descriptor;
        var active = device.ActiveConfiguration;
        var raw = new Dictionary<string, string> { ["device"] = Hex(device.RawDeviceDescriptor) };
        foreach (var c in device.Configurations) raw[$"configuration{c.Value}"] = Hex(c.Raw);
        if (device.Bos is { } bos) raw["bos"] = Hex(bos.Raw);

        var report = new UsbReportDevice(
            device.InstanceId, $"0x{d.VendorId:X4}", $"0x{d.ProductId:X4}", device.VendorName, device.ProductName,
            new UsbReportStrings(device.Strings.Manufacturer, device.Strings.Product, device.Strings.SerialNumber, device.Strings.Complete),
            d.UsbVersion.ToString(), d.DeviceVersion.ToString(), $"0x{d.Class:X2}", ClassName(d.Class, d.SubClass, d.Protocol, names),
            device.Speed.ToString(), device.SuperSpeedCapable, active?.MaxPowerMilliamps, active?.SelfPowered, active?.RemoteWakeup,
            device.Location.PortPath, device.Location.TypeC, device.Power.ToString(), device.IsHub, Driver(device.Driver, null));

        return new UsbDeviceReport(
            UsbDeviceReport.SchemaId, capturedAt, report,
            device.Configurations.Select(c => Configuration(device, c, c == active, names)).ToList(),
            new UsbReportMsOs(device.MsOs.DeclaresWinUsbReadiness || device.MsOs.WebUsb is not null, device.MsOs.Os10,
                device.MsOs.Os10VendorCode is { } vc ? $"0x{vc:X2}" : null,
                device.Bos?.Capabilities.FirstOrDefault(c => c.Name == "Microsoft OS 2.0")?.Detail,
                device.Bos?.Capabilities.FirstOrDefault(c => c.Name == "WebUSB")?.Detail),
            device.Bos?.Capabilities.Select(c => new UsbReportCapability(c.Name, c.Detail)).ToList() ?? [],
            device.Auxiliary.Select(a => new UsbReportChild(a.InstanceId, a.ClassName, a.Service, a.Name)).ToList(),
            UsbCapabilitySummary.Describe(device), device.Diagnostics, raw);
    }

    static UsbReportConfiguration Configuration(UsbDeviceNode device, UsbConfiguration configuration, bool active, UsbIdsDatabase? names) =>
        new(configuration.Value, device.Strings[configuration.NameIndex], active,
            configuration.Interfaces.Select(i =>
            {
                var function = active ? device.Functions.FirstOrDefault(f => f.InterfaceNumbers.Contains(i.Number)) : null;
                return new UsbReportInterface(i.Number, i.AlternateSetting, $"0x{i.Class:X2}", ClassName(i.Class, i.SubClass, i.Protocol, names),
                    $"0x{i.SubClass:X2}", $"0x{i.Protocol:X2}", device.Strings[i.NameIndex], function?.InstanceId, function?.Mode.ToString(),
                    function is null ? null : Driver(function.Driver, function.ComPort),
                    i.Endpoints.Select(e => new UsbReportEndpoint($"0x{e.Address:X2}", e.Type.ToString(), e.Direction.ToString(), e.MaxPacketSize * (e.AdditionalTransactions + 1), e.Interval)).ToList(),
                    i.ClassSpecific.Select(x => new UsbReportExtra(x.Kind, Hex(x.Raw))).ToList());
            }).ToList(),
            configuration.Problems);

    static UsbReportDriver Driver(UsbDevNodeDriver driver, string? comPort) =>
        new(driver.Service, driver.ClassName, driver.InfPath, driver.Provider, driver.Version, driver.MatchingDeviceId, driver.ProblemCode, comPort);

    /// <summary>The standard's own name for a defined class; usb.ids only for codes the standard leaves open.</summary>
    static string ClassName(byte cls, byte sub, byte protocol, UsbIdsDatabase? names)
    {
        var standard = UsbClassCodes.Name(cls, sub, protocol);
        return standard.StartsWith("Class 0x", StringComparison.Ordinal) ? names?.DescribeClass(cls, sub, protocol) ?? standard : standard;
    }

    public static string Hex(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty) return "";
        return string.Create(data.Length * 3 - 1, data.ToArray(), static (span, bytes) =>
        {
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i > 0) span[i * 3 - 1] = ' ';
                bytes[i].TryFormat(span[(i * 3)..], out _, "X2");
            }
        });
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(UsbDeviceReport))]
internal sealed partial class UsbReportJsonContext : JsonSerializerContext;
