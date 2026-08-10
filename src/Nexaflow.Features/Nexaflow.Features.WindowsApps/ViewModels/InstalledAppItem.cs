using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Features.WindowsApps.Models;
using Nexaflow.Visuals.Common.Formatting;

namespace Nexaflow.Features.WindowsApps.ViewModels;

/// <summary>
/// Display wrapper over an <see cref="InstalledApp"/>. Exposes the raw sort keys
/// (<see cref="Name"/>/<see cref="Publisher"/>/<see cref="InstallDate"/>/<see cref="SizeBytes"/>) for the
/// <c>ICollectionView</c> sort, plus formatted text for the grid cells. <see cref="SizeBytes"/> is
/// observable so the background size pass can fill it in after the list is already shown.
/// </summary>
public sealed partial class InstalledAppItem(InstalledApp app) : ObservableObject
{
    public InstalledApp App { get; } = app;

    public string Name => App.Name;
    public string Publisher => App.Publisher ?? string.Empty;
    public string Version => App.Version ?? string.Empty;
    public DateTime? InstallDate => App.InstallDate;

    /// <summary>On-disk size in bytes. Null until known; the second load pass measures missing ones.</summary>
    [ObservableProperty] private long? _sizeBytes = app.SizeBytes;

    public string InstallDateText => App.InstallDate?.ToString("yyyy-MM-dd") ?? "—";
    public string SourceLabel => App.Source == AppSource.Store ? "Store" : "Win32";

    public string SizeText => FormatSize(SizeBytes);
    partial void OnSizeBytesChanged(long? value) => OnPropertyChanged(nameof(SizeText));

    /// <summary>True when an install location is recorded and exists — gates the "Open Location" action.</summary>
    public bool HasLocation =>
        !string.IsNullOrWhiteSpace(App.InstallLocation) && Directory.Exists(App.InstallLocation);

    /// <summary>
    /// A Store/MSIX package — gates "Move" and "Advanced options", both of which act on a package
    /// identity a Win32 program doesn't have.
    /// </summary>
    public bool IsStore => App.Source == AppSource.Store;

    /// <summary>
    /// The program registered a maintenance-mode command and didn't set <c>NoModify</c> — gates
    /// "Modify", exactly as Add/remove programs decides whether to offer it.
    /// </summary>
    public bool CanModify =>
        App.Source == AppSource.Win32 && !App.ModifyBlocked && !string.IsNullOrWhiteSpace(App.ModifyPath);

    /// <summary>
    /// Likely a stale leftover: a Win32 entry with no install folder on disk <i>and</i> no working
    /// uninstaller — so it can't be removed normally. Gates the "Remove from list" action (deletes the
    /// orphaned registry record). Store apps are never orphaned.
    /// </summary>
    public bool IsOrphaned =>
        App.Source == AppSource.Win32 && !HasLocation && !UninstallerExists();

    /// <summary>True when the registered uninstaller can actually be run (an existing exe, or msiexec).</summary>
    private bool UninstallerExists()
    {
        var cmd = App.UninstallString?.Trim();
        if (string.IsNullOrEmpty(cmd)) return false;
        // An MSI uninstall (msiexec /x{guid}) is always runnable even when the app's files are gone.
        if (cmd.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) return true;

        string exe;
        if (cmd.StartsWith('"'))
        {
            var end = cmd.IndexOf('"', 1);
            exe = end > 0 ? cmd[1..end] : cmd.Trim('"');
        }
        else
        {
            var space = cmd.IndexOf(' ');
            exe = space > 0 ? cmd[..space] : cmd;
        }
        return !string.IsNullOrWhiteSpace(exe) && File.Exists(exe);
    }

    private static string FormatSize(long? bytes)
        => bytes is { } b && b > 0 ? SizeFormatter.FormatBytes(b) : "—";
}
