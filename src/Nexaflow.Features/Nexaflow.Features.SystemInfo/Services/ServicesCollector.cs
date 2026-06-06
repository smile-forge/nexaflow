using Microsoft.Win32;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.SystemInfo.Models;

namespace Nexaflow.Features.SystemInfo.Services;

/// <summary>
/// Reads the Windows service list (and single-row refreshes) from WMI <c>Win32_Service</c>. Pure and
/// read-only — no elevation; mutations go through the privilege bridge. Synchronous: call off the UI thread.
/// </summary>
public sealed class ServicesCollector
{
    public IReadOnlyList<ServiceRow> Collect()
    {
        var rows = new List<ServiceRow>();
        foreach (var s in Wmi.Query("Win32_Service"))
        {
            if (s.Str("Name") is not { } name) continue;
            rows.Add(new ServiceRow(
                name,
                s.Str("DisplayName") ?? name,
                s.Str("Description") ?? "",
                s.Val<bool>("AcceptPause") ?? false,
                s.Str("State") ?? "Unknown",
                ReadStartMode(s, name)));
        }
        return rows.OrderBy(r => r.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Re-reads live status + startup mode for one service, or null if it no longer exists.</summary>
    public (string Status, string StartMode)? ReadState(string name)
    {
        var s = Wmi.Query(@"root\cimv2", $"SELECT * FROM Win32_Service WHERE Name='{EscapeWql(name)}'")
                   .FirstOrDefault();
        return s is null ? null : (s.Str("State") ?? "Unknown", ReadStartMode(s, name));
    }

    // WMI reports "Auto" without the delayed flag; that lives in the service's registry key.
    private static string ReadStartMode(System.Management.ManagementObject s, string name) =>
        s.Str("StartMode") switch
        {
            "Auto"     => IsDelayedAutostart(name) ? ServiceStartModes.AutomaticDelayed : ServiceStartModes.Automatic,
            "Manual"   => ServiceStartModes.Manual,
            "Disabled" => ServiceStartModes.Disabled,
            { } other  => other,   // Boot / System (drivers)
            null       => "Unknown",
        };

    private static bool IsDelayedAutostart(string name)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
            return key?.GetValue("DelayedAutostart") is int v && v == 1;
        }
        catch { return false; }
    }

    // Escape a value for a WQL string literal (Name='...').
    private static string EscapeWql(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
