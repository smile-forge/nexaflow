using Nexaflow.IO.Usb.Inventory;

namespace Nexaflow.IO.Usb.Storage;

/// <summary>
/// A mounted file system on a USB disk, as Windows sees it. <see cref="Error"/> says why a volume could not be read —
/// a card reader with no card answers "not ready".
/// </summary>
/// <param name="VolumeName">The volume's own name, <c>\\?\Volume{…}\</c>, which outlives drive letters.</param>
/// <param name="MountPoints">Where it is mounted: drive roots such as <c>E:\</c> first, then folders.</param>
public sealed record UsbVolume(
    string VolumeName, IReadOnlyList<string> MountPoints, string? Label, string? FileSystem,
    long? Capacity, long? Free, bool IsReadOnly, string? Error)
{
    /// <summary>The path to browse it by: its first mount point, or the volume name when it has none.</summary>
    public string Root => MountPoints.Count > 0 ? MountPoints[0] : VolumeName;

    /// <summary>"E: Backups" — the drive and the label, as a person names a volume.</summary>
    public string DisplayName
    {
        get
        {
            string where = MountPoints.Count > 0 ? MountPoints[0].TrimEnd('\\') : "Not mounted";
            return string.IsNullOrEmpty(Label) ? where : $"{where} {Label}";
        }
    }
}

/// <summary>One disk a storage function presents — a card reader presents one per slot — and its volumes.</summary>
public sealed record UsbDisk(string InstanceId, string? Name, int? DiskNumber, long? Size, IReadOnlyList<UsbVolume> Volumes, string? Error);

/// <summary>
/// Finds the disks a USB storage function presents and the volumes Windows mounted on them. It asks Windows only — the
/// storage stack, the volume manager — and reads nothing from the media itself, so it needs no privilege.
/// </summary>
public interface IUsbStorageLocator
{
    IReadOnlyList<UsbDisk> DisksOf(UsbFunctionNode function);
}
