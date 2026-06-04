namespace Nexaflow.Features.WindowsApps.Models;

/// <summary>Where an installed app was discovered — drives how it is uninstalled.</summary>
public enum AppSource
{
    /// <summary>Traditional Win32 program from a registry Uninstall key.</summary>
    Win32,
    /// <summary>Microsoft Store / UWP (MSIX) package.</summary>
    Store
}

/// <summary>
/// One installed application, as shown in the Add/remove-programs style list. Immutable snapshot
/// produced by an <see cref="Services.IInstalledAppSource"/>; the uninstall payload
/// (<see cref="UninstallString"/> for Win32, <see cref="PackageFullName"/> for Store) is carried so
/// the originating source can later remove it.
/// </summary>
public sealed record InstalledApp
{
    public required string Name { get; init; }
    public string? Publisher { get; init; }
    public string? Version { get; init; }
    public DateTime? InstallDate { get; init; }

    /// <summary>Estimated on-disk size in bytes, when known.</summary>
    public long? SizeBytes { get; init; }

    public AppSource Source { get; init; }
    public string? InstallLocation { get; init; }

    /// <summary>Win32 only: the command the vendor registered to uninstall the program.</summary>
    public string? UninstallString { get; init; }

    /// <summary>Store only: the package full name passed to <c>RemovePackageAsync</c>.</summary>
    public string? PackageFullName { get; init; }

    /// <summary>
    /// Win32 only: where this entry lives in the registry, so an orphaned leftover record can be
    /// removed. Null for Store apps.
    /// </summary>
    public RegistryEntryLocation? RegistryLocation { get; init; }
}

/// <summary>The Uninstall-hive location of a Win32 entry (which root + bitness view + subkey name).</summary>
public sealed record RegistryEntryLocation(bool IsLocalMachine, bool Is64BitView, string SubKeyName);
