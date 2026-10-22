using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.WindowsApps.Models;

/// <summary>
/// A drive a Store package can live on — one <c>PackageVolume</c> known to the deployment manager,
/// flattened so the pane can bind it without a WinRT type. <see cref="Name"/> is the volume's stable
/// deployment id and is what re-locates the real <c>PackageVolume</c> when a move is actually run.
/// </summary>
/// <param name="Name">The deployment manager's volume id (a media GUID, not a display name).</param>
/// <param name="MountPoint">Where the volume is mounted, e.g. <c>C:\</c>.</param>
/// <param name="PackageStorePath">The <c>WindowsApps</c> folder on that volume.</param>
/// <param name="IsSystem">True for the volume Windows itself is installed on.</param>
/// <param name="FreeBytes">Free space, when the drive could be queried.</param>
public sealed record AppVolume(
    string Name,
    string MountPoint,
    string PackageStorePath,
    bool IsSystem,
    long? FreeBytes)
{
    /// <summary>Drive + free space, as shown in the Move dropdown — e.g. <c>C: (system) — 214 GB free</c>.</summary>
    public string Display
    {
        get
        {
            var drive = string.IsNullOrWhiteSpace(MountPoint) ? Name : MountPoint.TrimEnd('\\');
            return (IsSystem, FreeBytes) switch
            {
                (true, { } free)  => Str.Format("WindowsApps.Advanced.Move.VolumeSystemFreeFormat", drive, SizeFormatter.FormatBytes(free)),
                (true, null)      => Str.Format("WindowsApps.Advanced.Move.VolumeSystemFormat", drive),
                (false, { } free) => Str.Format("WindowsApps.Advanced.Move.VolumeFreeFormat", drive, SizeFormatter.FormatBytes(free)),
                _                 => drive,
            };
        }
    }
}
