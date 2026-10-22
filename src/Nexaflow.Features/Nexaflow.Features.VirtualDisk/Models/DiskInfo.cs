using System.Collections.Generic;

namespace Nexaflow.Features.VirtualDisk.Models;

/// <summary>Top-level facts about one disk image, for the "As Disk" inspector. Cheap to gather — reads the
/// container header, partition table, and each filesystem's superblock only (never a full directory walk).</summary>
/// <param name="Format">Container/filesystem name (e.g. <c>"VHDX"</c>, <c>"ISO 9660"</c>, <see cref="RawDiskFormat"/>) —
/// an invariant value the AI tools report as it is; the inspector labels it for display.</param>
/// <param name="Capacity">Nominal disk capacity in bytes (the filesystem size for a pure-filesystem image).</param>
/// <param name="PartitionScheme"><c>"GPT"</c>, <c>"MBR"</c>, <see cref="NoPartitionScheme"/>, or
/// <see cref="OtherPartitionScheme"/> — invariant, labelled for display like <paramref name="Format"/>.</param>
/// <param name="Volumes">One entry per browsable volume/filesystem inside the image.</param>
public sealed record DiskInfo(
    string Format,
    long Capacity,
    string PartitionScheme,
    IReadOnlyList<DiskVolumeInfo> Volumes)
{
    /// <summary>The <see cref="Format"/> of a raw sector image (<c>.img</c>/<c>.raw</c>).</summary>
    public const string RawDiskFormat = "Raw disk";

    /// <summary>The <see cref="PartitionScheme"/> of an image with no partition table.</summary>
    public const string NoPartitionScheme = "None";

    /// <summary>The <see cref="PartitionScheme"/> of a partition table that is neither GPT nor MBR.</summary>
    public const string OtherPartitionScheme = "Partitioned";
}

/// <summary>One volume (and its filesystem) inside a disk image.</summary>
/// <param name="Index">1-based volume number — matches the <c>Volume N</c> browse folder for multi-volume images.</param>
/// <param name="FileSystem">Filesystem type name (e.g. <c>"NTFS"</c>, <c>"FAT"</c>), or <see cref="UnknownFileSystem"/>.</param>
/// <param name="Label">Volume label, or null when unset.</param>
/// <param name="Capacity">Filesystem size in bytes.</param>
/// <param name="Used">Used bytes (0 when the filesystem can't report it).</param>
public sealed record DiskVolumeInfo(
    int Index,
    string FileSystem,
    string? Label,
    long Capacity,
    long Used)
{
    /// <summary>The <see cref="FileSystem"/> of a volume whose filesystem could not be detected.</summary>
    public const string UnknownFileSystem = "Unknown";
}
