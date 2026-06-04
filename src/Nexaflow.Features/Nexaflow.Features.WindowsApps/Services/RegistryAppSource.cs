using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;
using Nexaflow.Features.WindowsApps.Models;

namespace Nexaflow.Features.WindowsApps.Services;

/// <summary>
/// Enumerates traditional Win32 programs from the three Uninstall hives (HKLM 64-bit, HKLM 32-bit
/// WOW node, HKCU) — the same set Windows' "Add or remove programs" shows. Uninstall runs the
/// vendor-registered <c>UninstallString</c> (which may be interactive and prompt for elevation).
/// </summary>
public sealed class RegistryAppSource : IInstalledAppSource
{
    private const string UninstallSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public AppSource Source => AppSource.Win32;

    public Task<IReadOnlyList<InstalledApp>> EnumerateAsync(CancellationToken ct) =>
        Task.Run<IReadOnlyList<InstalledApp>>(() =>
        {
            var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);

            ReadHive(RegistryHive.LocalMachine, RegistryView.Registry64, apps, ct);
            ReadHive(RegistryHive.LocalMachine, RegistryView.Registry32, apps, ct);
            ReadHive(RegistryHive.CurrentUser,  RegistryView.Registry64, apps, ct);

            return apps.Values.ToList();
        }, ct);

    private static void ReadHive(RegistryHive hive, RegistryView view,
                                 Dictionary<string, InstalledApp> apps, CancellationToken ct)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(UninstallSubKey);
            if (uninstall is null) return;

            foreach (var subName in uninstall.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                using var key = uninstall.OpenSubKey(subName);
                if (key is null) continue;

                var app = TryReadEntry(key);
                if (app is null) continue;

                app = app with
                {
                    RegistryLocation = new RegistryEntryLocation(
                        IsLocalMachine: hive == RegistryHive.LocalMachine,
                        Is64BitView:    view == RegistryView.Registry64,
                        SubKeyName:     subName)
                };

                // First write wins (HKLM 64-bit before 32-bit before HKCU); collapses 32/64 dupes.
                var dedupeKey = $"{app.Name} {app.Version}";
                apps.TryAdd(dedupeKey, app);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* a hive we can't read contributes nothing */ }
    }

    private static InstalledApp? TryReadEntry(RegistryKey key)
    {
        var name = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(name)) return null;

        // Hide the things Add/remove programs hides: OS components, updates, and child entries.
        if (GetDword(key, "SystemComponent") == 1) return null;
        if (key.GetValue("ParentKeyName") is not null || key.GetValue("ParentDisplayName") is not null)
            return null;
        if (key.GetValue("ReleaseType") is string rt &&
            (rt.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
             rt.Contains("Hotfix", StringComparison.OrdinalIgnoreCase) ||
             rt.Contains("ServicePack", StringComparison.OrdinalIgnoreCase)))
            return null;

        var uninstallString = (key.GetValue("UninstallString") as string)
                              ?? key.GetValue("QuietUninstallString") as string;

        var location = (key.GetValue("InstallLocation") as string)?.Trim();
        bool hasFolder = !string.IsNullOrEmpty(location) && Directory.Exists(location);

        // Size: EstimatedSize (KB) is optional and many installers omit it. We take it here if present;
        // measuring the folder for the rest is deferred to the second load pass (it's the slow part).
        long? size = GetDword(key, "EstimatedSize") is { } kb && kb > 0 ? (long)kb * 1024 : null;

        // InstallDate is likewise optional; approximate from the install folder's creation time (cheap).
        var installDate = ParseInstallDate(key.GetValue("InstallDate") as string);
        if (installDate is null && hasFolder)
        {
            try { installDate = Directory.GetCreationTime(location!); } catch { }
        }

        return new InstalledApp
        {
            Name            = name.Trim(),
            Publisher       = (key.GetValue("Publisher") as string)?.Trim(),
            Version         = (key.GetValue("DisplayVersion") as string)?.Trim(),
            InstallDate     = installDate,
            SizeBytes       = size,
            Source          = AppSource.Win32,
            InstallLocation = location,
            UninstallString = uninstallString,
        };
    }

    private static int? GetDword(RegistryKey key, string name) =>
        key.GetValue(name) is int i ? i : null;

    private static DateTime? ParseInstallDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateTime.TryParseExact(raw, "yyyyMMdd", CultureInfo.InvariantCulture,
                                   DateTimeStyles.None, out var d))
            return d;
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)
            ? d : null;
    }

    public Task<UninstallResult> UninstallAsync(InstalledApp app, CancellationToken ct) =>
        Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(app.UninstallString))
                return UninstallResult.Fail("No uninstall command is registered for this program.");
            var (file, args) = SplitCommand(app.UninstallString);
            if (string.IsNullOrEmpty(file))
                return UninstallResult.Fail("The registered uninstall command is empty.");

            // MSI install verb → uninstall verb, so the command actually removes the product.
            if (file.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase) ||
                file.Equals("msiexec", StringComparison.OrdinalIgnoreCase))
                args = args.Replace("/I", "/X", StringComparison.OrdinalIgnoreCase);

            try
            {
                var proc = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });
                if (proc is null)
                    return UninstallResult.Fail("The uninstaller could not be started.");

                // Wait for the (often interactive) uninstaller so we can report its outcome.
                proc.WaitForExit();
                return InterpretExitCode(proc.ExitCode);
            }
            catch (Exception ex) { return UninstallResult.Fail(ex.Message); }
        }, ct);

    /// <summary>Maps an uninstaller exit code to a result. 0 / reboot-required count as success.</summary>
    private static UninstallResult InterpretExitCode(int code) => code switch
    {
        0 or 1641 or 3010 => UninstallResult.Ok,                       // success (1641/3010 = reboot needed)
        1602              => UninstallResult.Fail("Uninstall was cancelled."),
        _                 => UninstallResult.Fail($"The uninstaller reported an error (exit code {code}).")
    };

    /// <summary>
    /// Removes the orphaned Uninstall registry entry (used when the program's files are already gone).
    /// Deleting an HKLM key needs elevation — returns false if access is denied.
    /// </summary>
    public Task<bool> DeleteRecordAsync(InstalledApp app, CancellationToken ct) =>
        Task.Run(() =>
        {
            if (app.RegistryLocation is not { } loc) return false;
            var hive = loc.IsLocalMachine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            var view = loc.Is64BitView ? RegistryView.Registry64 : RegistryView.Registry32;
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(UninstallSubKey, writable: true);
                if (uninstall is null) return false;
                uninstall.DeleteSubKeyTree(loc.SubKeyName, throwOnMissingSubKey: false);
                return true;
            }
            catch { return false; }   // UnauthorizedAccessException when HKLM and not elevated
        }, ct);

    /// <summary>Splits a registry command line into executable + arguments, honouring a quoted path.</summary>
    private static (string File, string Args) SplitCommand(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            if (end > 0)
                return (command[1..end], command[(end + 1)..].Trim());
        }
        var space = command.IndexOf(' ');
        return space < 0 ? (command, string.Empty) : (command[..space], command[(space + 1)..].Trim());
    }
}
