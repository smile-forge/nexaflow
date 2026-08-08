using System.Management;
using System.Runtime.Versioning;

namespace Nexaflow.Features.Executable.Services;

/// <param name="Name">The product's display name, e.g. "Windows Defender".</param>
/// <param name="IsEnabled">Real-time protection is on.</param>
/// <param name="IsUpToDate">Definitions are current.</param>
public sealed record AntivirusProduct(string Name, bool IsEnabled, bool IsUpToDate)
{
    public string Status => (IsEnabled, IsUpToDate) switch
    {
        (true,  true)  => "enabled, definitions up to date",
        (true,  false) => "enabled, definitions out of date",
        (false, _)     => "disabled",
    };
}

/// <summary>
/// Enumerates the antivirus products Windows Security Center knows about, so the Analysis tab can
/// name whichever engine an AMSI scan will actually reach rather than saying "the registered
/// antivirus" and hoping.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AntivirusProducts
{
    /// <summary>
    /// Queries <c>root\SecurityCenter2</c>. Returns an empty list rather than throwing on a system
    /// where Security Center is absent (Server SKUs) or WMI is blocked. Blocking — call it from a
    /// background task.
    /// </summary>
    public static IReadOnlyList<AntivirusProduct> Enumerate()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct");

            var products = new List<AntivirusProduct>();
            foreach (var item in searcher.Get())
            {
                using var product = (ManagementObject)item;
                string name = product["displayName"] as string ?? "(unnamed)";
                uint   state = product["productState"] is { } raw ? Convert.ToUInt32(raw) : 0;
                var (enabled, upToDate) = DecodeState(state);
                products.Add(new AntivirusProduct(name, enabled, upToDate));
            }
            return products;
        }
        catch (Exception)
        {
            // Security Center is missing, WMI is disabled, or the namespace is not accessible.
            return [];
        }
    }

    /// <summary>
    /// <c>productState</c> is an undocumented bit-packed DWORD, read as three bytes:
    /// <list type="bullet">
    /// <item>bits 16-23 — the <em>provider</em> (antivirus, antispyware, firewall);</item>
    /// <item>bits 8-15 — real-time protection: <c>0x10</c>/<c>0x11</c> on, <c>0x00</c> off;</item>
    /// <item>bits 0-7 — definitions: <c>0x00</c> current, <c>0x10</c> out of date.</item>
    /// </list>
    /// The protection byte is the middle one. Reading the provider byte instead makes an enabled
    /// Defender report as disabled, because its provider nibble and its protection nibble differ.
    /// </summary>
    internal static (bool Enabled, bool UpToDate) DecodeState(uint state)
    {
        uint protection  = (state >> 8) & 0xFF;
        uint definitions = state & 0xFF;
        return ((protection & 0x10) != 0, definitions == 0);
    }
}
