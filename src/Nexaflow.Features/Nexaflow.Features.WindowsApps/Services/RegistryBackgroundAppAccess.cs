using Microsoft.Win32;
using Nexaflow.Features.WindowsApps.Models;

namespace Nexaflow.Features.WindowsApps.Services;

/// <summary>
/// The real background-execution policy store: <c>HKCU\Software\Microsoft\Windows\CurrentVersion\
/// BackgroundAccessApplications\{PackageFamilyName}</c> — the same per-user key Windows' own
/// "Background apps permissions" dropdown writes.
///
/// Two values carry the tri-state. <c>DisabledByUser</c> records the user's refusal (mirrored into the
/// effective <c>Disabled</c>), and <c>IgnoreBatterySaver</c> lifts the app above battery-saver
/// throttling. So: <i>Never</i> = disabled, <i>Always</i> = enabled + ignores battery saver,
/// <i>Power optimized</i> = enabled and subject to it (the default, and what an absent key means).
/// A sibling <c>DisabledBySystem</c> is written by policy, not by us — it is read as a denial but
/// never overwritten.
/// </summary>
public sealed class RegistryBackgroundAppAccess : IBackgroundAppAccess
{
    private const string Root =
        @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications";

    public BackgroundAppMode Get(string packageFamilyName)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyName)) return BackgroundAppMode.PowerOptimized;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{Root}\{packageFamilyName}");
            if (key is null) return BackgroundAppMode.PowerOptimized;   // never touched ⇒ the default

            if (Dword(key, "DisabledByUser") == 1 ||
                Dword(key, "DisabledBySystem") == 1 ||
                Dword(key, "Disabled") == 1)
                return BackgroundAppMode.Never;

            return Dword(key, "IgnoreBatterySaver") == 1
                ? BackgroundAppMode.Always
                : BackgroundAppMode.PowerOptimized;
        }
        catch { return BackgroundAppMode.PowerOptimized; }
    }

    public bool Set(string packageFamilyName, BackgroundAppMode mode)
    {
        if (string.IsNullOrWhiteSpace(packageFamilyName)) return false;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey($@"{Root}\{packageFamilyName}",
                                                              writable: true);
            if (key is null) return false;

            var denied = mode == BackgroundAppMode.Never ? 1 : 0;
            key.SetValue("DisabledByUser",     denied,                                    RegistryValueKind.DWord);
            key.SetValue("Disabled",           denied,                                    RegistryValueKind.DWord);
            key.SetValue("IgnoreBatterySaver", mode == BackgroundAppMode.Always ? 1 : 0,  RegistryValueKind.DWord);
            return true;
        }
        catch { return false; }
    }

    private static int? Dword(RegistryKey key, string name) => key.GetValue(name) is int i ? i : null;
}
