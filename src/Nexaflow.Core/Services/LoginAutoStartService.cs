using Microsoft.Win32;

namespace Nexaflow.Core.Services;

/// <summary>
/// Manages the per-user "start with Windows" entry under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. When enabled, Nexaflow launches at
/// login with <c>--prestart</c> as a windowless daemon (see <see cref="App.IsResident"/>) so the
/// first window the user opens appears instantly — all app init is already done.
/// </summary>
public static class LoginAutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName  = "Nexaflow";

    /// <summary>The launch command written to the Run key: the current exe plus <c>--prestart</c>.</summary>
    private static string Command => $"\"{Environment.ProcessPath}\" --prestart";

    /// <summary>True when the Run entry is currently present.</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) is string;
            }
            catch { return false; }
        }
    }

    /// <summary>
    /// Adds or removes the login entry. Best-effort — a missing or locked Run key must never crash
    /// the config-save flow.
    /// </summary>
    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key is null) return;

            if (enabled)
                key.SetValue(ValueName, Command);
            else if (key.GetValue(ValueName) is not null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* best effort */ }
    }
}
