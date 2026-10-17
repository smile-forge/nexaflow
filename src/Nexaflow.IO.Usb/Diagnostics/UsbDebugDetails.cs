using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nexaflow.IO.Usb.Browsing;
using Nexaflow.IO.Usb.Descriptors;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.IO.Usb.Windows;

namespace Nexaflow.IO.Usb.Diagnostics;

/// <summary>
/// What a device's debug details hide: its serial numbers, wherever they appear (a serial is often part of an instance
/// id), and personal names such as a volume's label. Each becomes a token made from its hash, so the same device dumped
/// twice still reads as the same device.
/// </summary>
public sealed class UsbDebugMask
{
    readonly List<string> _serials;

    public UsbDebugMask(IEnumerable<string?> serials) =>
        _serials = [.. serials.OfType<string>().Where(s => s.Length >= 4).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(s => s.Length)];

    /// <summary>The device's serials: the string it reports, and the last part of its instance id when that is one
    /// (Windows names a device with a serial by it; one without gets a generated id with '&amp;' in it).</summary>
    public static UsbDebugMask For(UsbDeviceNode device)
    {
        string tail = device.InstanceId[(device.InstanceId.LastIndexOf('\\') + 1)..];
        return new UsbDebugMask([device.Strings.SerialNumber, tail.Contains('&') ? null : tail]);
    }

    /// <summary><paramref name="text"/> with every serial in it replaced by its token.</summary>
    public string? Text(string? text)
    {
        if (text is null) return null;
        foreach (var serial in _serials)
            text = text.Replace(serial, Token("SERIAL", serial), StringComparison.OrdinalIgnoreCase);
        return text;
    }

    /// <summary>A name a person gave something — a volume's label — as a token.</summary>
    public static string? Name(string? name) => string.IsNullOrEmpty(name) ? name : Token("NAME", name);

    static string Token(string kind, string value) =>
        $"{kind}-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant())))[..8]}";
}

/// <summary>What one live tool added to the debug details, or why it could not.</summary>
public sealed record UsbDebugTool(string ToolId, string Title, string? FunctionInstanceId, string Status, JsonObject? Details, string? Error);

/// <summary>
/// A device's debug details: not a report for the person using the app but a dump for the people extending it, so a
/// device they do not have can be supported from what a tester sends — everything the device says about itself, raw;
/// everything Windows made of it; what each live tool saw and what failed. The descriptors are written as the test
/// fixtures hold them ("12 01 00 02 …"), so a dump becomes a test.
/// </summary>
public static class UsbDebugDetails
{
    /// <summary>The layout's version, for whoever reads a dump later.</summary>
    public const int Format = 1;

    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string ToText(JsonObject details) => details.ToJsonString(Indented);

    public static JsonObject Build(UsbDeviceNode device, UsbDebugMask mask, string appVersion, string? usbIdsVersion,
        IReadOnlyList<UsbDebugTool> tools, IReadOnlyList<string> notCovered, IReadOnlyList<string> log,
        IReadOnlyList<UsbHidReportDescriptor> hidReports) => new()
    {
        ["nexaflowUsbDebug"] = Format,
        ["created"] = DateTimeOffset.Now.ToString("O"),
        ["app"] = appVersion,
        ["windows"] = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
        ["usbIds"] = usbIdsVersion,
        ["summary"] = Array(Summary(device, tools, notCovered)),
        ["device"] = Device(device, mask),
        ["hidReportDescriptors"] = new JsonArray([.. hidReports.Select(r => new JsonObject
        {
            ["interface"] = r.Interface,
            ["bytes"] = r.Bytes is { } bytes ? UsbHex.Format(bytes) : null,
            ["problem"] = r.Problem,
        })]),
        ["tools"] = new JsonArray([.. tools.Select(t => new JsonObject
        {
            ["tool"] = t.ToolId,
            ["title"] = t.Title,
            ["function"] = mask.Text(t.FunctionInstanceId),
            ["status"] = mask.Text(t.Status),
            ["details"] = t.Details,
            ["error"] = mask.Text(t.Error),
        })]),
        ["notCovered"] = Array(notCovered.Select(mask.Text)),
        ["log"] = Array(log.Select(mask.Text)),
    };

    /// <summary>The lines a person reads first: what it is, what Windows made of it, what was offered and what failed.</summary>
    static IEnumerable<string> Summary(UsbDeviceNode device, IReadOnlyList<UsbDebugTool> tools, IReadOnlyList<string> notCovered)
    {
        yield return $"{device.DisplayName} — {device.VendorId:X4}:{device.ProductId:X4} at {device.Location.PortPath}, {device.Speed}" +
                     (device.IsEjected ? ", ejected" : "") + (device.Driver.ProblemCode != 0 ? $", problem {device.Driver.ProblemCode}" : "");
        foreach (var function in device.Functions)
            yield return $"function {(function.FunctionInterface is { } mi ? $"interface {mi}" : "(whole device)")}: {device.ClassOf(function).Name}, " +
                         $"{function.Mode} via {function.Driver.Service ?? "no driver"}" +
                         (function.Driver.ProblemCode != 0 ? $", problem {function.Driver.ProblemCode}" : "") +
                         (function.Children.Count > 0 ? $"; Windows made: {string.Join(", ", function.Children.Select(c => c.ClassName ?? "?"))}" : "");
        yield return tools.Count == 0 ? "live tools: none offered" : $"live tools: {string.Join(", ", tools.Select(t => t.Error is null ? t.ToolId : $"{t.ToolId} (failed)"))}";
        foreach (var uncovered in notCovered) yield return $"not covered: {uncovered}";
    }

    static JsonObject Device(UsbDeviceNode d, UsbDebugMask mask) => new()
    {
        ["instanceId"] = mask.Text(d.InstanceId),
        ["portPath"] = d.Location.PortPath,
        ["hub"] = mask.Text(d.Location.HubInstanceId),
        ["port"] = d.Location.Port,
        ["userConnectable"] = d.Location.UserConnectable,
        ["typeC"] = d.Location.TypeC,
        ["speed"] = d.Speed.ToString(),
        ["superSpeedCapable"] = d.SuperSpeedCapable,
        ["isHub"] = d.IsHub,
        ["power"] = d.Power.ToString(),
        ["ejected"] = d.IsEjected,
        ["names"] = new JsonObject
        {
            ["display"] = mask.Text(d.DisplayName),
            ["usbIdsVendor"] = d.VendorName,
            ["usbIdsProduct"] = d.ProductName,
            ["busReported"] = mask.Text(d.BusReportedName),
        },
        ["strings"] = new JsonObject
        {
            ["complete"] = d.Strings.Complete,
            ["languages"] = Array(d.Strings.Languages.Select(l => $"{l:X4}")),
            ["byIndex"] = new JsonObject([.. d.Strings.ByIndex.Select(s => KeyValuePair.Create(s.Key.ToString(), (JsonNode?)mask.Text(s.Value)))]),
        },
        ["descriptors"] = new JsonObject
        {
            ["device"] = UsbHex.Format(d.RawDeviceDescriptor),
            ["activeConfiguration"] = d.ActiveConfigurationValue,
            ["configurations"] = Array(d.Configurations.Select(c => UsbHex.Format(c.Raw))),
            ["bos"] = d.Bos is { } bos ? UsbHex.Format(bos.Raw) : null,
            ["problems"] = Array(d.Configurations.SelectMany(c => c.Problems)),
        },
        ["msOs"] = new JsonObject
        {
            ["os10"] = d.MsOs.Os10,
            ["os10VendorCode"] = d.MsOs.Os10VendorCode is { } code ? $"{code:X2}" : null,
            ["os20"] = d.MsOs.Os20 is not null,
            ["webUsb"] = d.MsOs.WebUsb is not null,
        },
        ["driver"] = Driver(d.Driver, mask),
        ["functions"] = new JsonArray([.. d.Functions.Select(f => (JsonNode)new JsonObject
        {
            ["instanceId"] = mask.Text(f.InstanceId),
            ["interface"] = f.FunctionInterface,
            ["interfaces"] = new JsonArray([.. f.InterfaceNumbers.Select(n => (JsonNode)n)]),
            ["class"] = d.ClassOf(f).Name,
            ["name"] = mask.Text(f.Name),
            ["mode"] = f.Mode.ToString(),
            ["power"] = f.Power.ToString(),
            ["comPort"] = f.ComPort,
            ["deviceInterfaceGuids"] = Array(f.DeviceInterfaceGuids.Select(g => g.ToString())),
            ["driver"] = Driver(f.Driver, mask),
            ["children"] = new JsonArray([.. f.Children.Select(c => (JsonNode)Child(c, mask))]),
        })]),
        ["auxiliary"] = new JsonArray([.. d.Auxiliary.Select(c => (JsonNode)Child(c, mask))]),
        ["diagnostics"] = Array(d.Diagnostics.Select(mask.Text)),
    };

    static JsonObject Driver(UsbDevNodeDriver driver, UsbDebugMask mask) => new()
    {
        ["service"] = driver.Service,
        ["class"] = driver.ClassName,
        ["classGuid"] = driver.ClassGuid?.ToString(),
        ["inf"] = driver.InfPath,
        ["section"] = driver.InfSection,
        ["version"] = driver.Version,
        ["provider"] = driver.Provider,
        ["matchingId"] = mask.Text(driver.MatchingDeviceId),
        ["problem"] = driver.ProblemCode,
        ["problemText"] = UsbProblemCodes.Describe(driver.ProblemCode),
        ["hardwareIds"] = Array(driver.HardwareIds.Select(mask.Text)),
        ["compatibleIds"] = Array(driver.CompatibleIds.Select(mask.Text)),
    };

    static JsonObject Child(UsbChildNode child, UsbDebugMask mask) => new()
    {
        ["instanceId"] = mask.Text(child.InstanceId),
        ["class"] = child.ClassName,
        ["service"] = child.Service,
        ["name"] = mask.Text(child.Name),
    };

    static JsonArray Array(IEnumerable<string?> items) => new([.. items.Select(i => (JsonNode?)i)]);
}
