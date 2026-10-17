using System.Security.Principal;
using Microsoft.Win32;
using Nexaflow.Spikes.UsbDriverBinding.Native;

namespace Nexaflow.Spikes.UsbDriverBinding;

public sealed record EnvironmentReport(
    string Windows, bool Elevated, string TokenElevationType, bool? SecureBoot, bool? MemoryIntegrity, string SmartAppControl,
    Dictionary<string, string> GenericDriverServices, bool InboxWinUsbInf);

/// <summary>The W1–W4 axes of the test matrix, read without privilege.</summary>
public static class EnvironmentProbe
{
    public static EnvironmentReport Capture()
    {
        using var nt = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        string windows = $"{nt?.GetValue("ProductName")} {nt?.GetValue("DisplayVersion")} build {nt?.GetValue("CurrentBuild")}.{nt?.GetValue("UBR")}";

        using var identity = WindowsIdentity.GetCurrent();
        bool elevated = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        string elevationType = Advapi32.GetTokenInformation(identity.Token, 18, out int type, sizeof(int), out _)
            ? type switch { 1 => "Default (UAC off or standard user)", 2 => "Full (elevated)", 3 => "Limited (admin, filtered by UAC)", _ => $"{type}" }
            : "unknown";

        var services = new Dictionary<string, string>();
        foreach (var name in new[] { "WINUSB", "libusbK", "libusb0", "UsbDk" })
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
            services[name] = key is null ? "absent" : $"present ({key.GetValue("ImagePath")})";
        }

        return new EnvironmentReport(windows, elevated, elevationType,
            ReadFlag(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled"),
            ReadFlag(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled"),
            (Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy")?.GetValue("VerifiedAndReputablePolicyState") as int?) switch
            {
                0 => "off", 1 => "enforced", 2 => "evaluation", null => "unknown", var v => $"{v}",
            },
            services, File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF", "winusb.inf")));
    }

    static bool? ReadFlag(string path, string value)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        return key?.GetValue(value) is int v ? v != 0 : null;
    }
}
