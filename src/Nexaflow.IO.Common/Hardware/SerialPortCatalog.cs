using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Nexaflow.IO.Common.Hardware;

/// <summary>A COM port: its name, what Windows calls the device behind it, and that device's instance id.</summary>
public sealed record SerialPortInfo(string Name, string? Description, string? InstanceId)
{
    /// <summary>"COM5 — USB Serial Device", or the name alone.</summary>
    public string DisplayName => Description is null ? Name : $"{Name} — {Description}";
}

/// <summary>
/// The machine's COM ports. The serial driver map (<c>HARDWARE\DEVICEMAP\SERIALCOMM</c>) is the list of ports that
/// exist now, virtual ones included; the COM-port interface class names the devices behind the ones that have one.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SerialPortCatalog
{
    static readonly Guid ComPortInterface = new("86E0D1E0-8089-11D0-9CE4-08003E301F73");

    public static IReadOnlyList<SerialPortInfo> List()
    {
        var devices = Devices();
        using var map = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
        var names = map?.GetValueNames().Select(v => map.GetValue(v) as string).OfType<string>() ?? [];
        return [.. names.Concat(devices.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(name => devices.TryGetValue(name, out var d) ? new SerialPortInfo(name, d.Description, d.InstanceId) : new SerialPortInfo(name, null, null))
            .OrderBy(p => Number(p.Name)).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Port name → the device behind it, from each COM-port interface's devnode.</summary>
    static Dictionary<string, (string? Description, string InstanceId)> Devices()
    {
        var found = new Dictionary<string, (string?, string)>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in DeviceInterfaces.Of(ComPortInterface))
        {
            if (DeviceInterfaces.InstanceIdOf(path) is not { } id) continue;
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{id}");
            using var parameters = key?.OpenSubKey("Device Parameters");
            if (parameters?.GetValue("PortName") is not string port) continue;
            found[port] = (Describe(key), id);
        }
        return found;
    }

    /// <summary>The device's friendly name without its "(COM5)", or its description without the INF reference.</summary>
    static string? Describe(RegistryKey? key)
    {
        if (key?.GetValue("FriendlyName") is string friendly)
        {
            int at = friendly.LastIndexOf(" (COM", StringComparison.OrdinalIgnoreCase);
            return at > 0 ? friendly[..at] : friendly;
        }
        return key?.GetValue("DeviceDesc") is string desc ? desc[(desc.LastIndexOf(';') + 1)..] : null;
    }

    static int Number(string name) =>
        name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(3), out int n) ? n : int.MaxValue;
}
