using System.Collections.Generic;

namespace Nexaflow.Features.VirtualDisk.Models;

/// <summary>Top-level facts about one disk image, for the "As Disk" inspector. Cheap to gather — reads the
/// container header, partition table, and each filesystem's superblock only (never a full directory walk).</summary>
/// <param name="Format">Container/filesystem display name (e.g. <c>"VHDX"</c>, <c>"ISO 9660"</c>).</param>
/// <param name="Capacity">Nominal disk capacity in bytes (the filesystem size for a pure-filesystem image).</param>
/// <param name="PartitionScheme"><c>"GPT"</c>, <c>"MBR"</c>, <c>"None"</c>, or <c>"Partitioned"</c>.</param>
/// <param name="Volumes">One entry per browsable volume/filesystem inside the image.</param>
public sealed record DiskInfo(
    string Format,
    long Capacity,
    string PartitionScheme,
    IReadOnlyList<DiskVolumeInfo> Volumes);

/// <summary>One volume (and its filesystem) inside a disk image.</summary>
/// <param name="Index">1-based volume number — matches the <c>Volume N</c> browse folder for multi-volume images.</param>
/// <param name="FileSystem">Filesystem type name (e.g. <c>"NTFS"</c>, <c>"FAT"</c>), or <c>"Unknown"</c>.</param>
/// <param name="Label">Volume label, or null when unset.</param>
/// <param name="Capacity">Filesystem size in bytes.</param>
/// <param name="Used">Used bytes (0 when the filesystem can't report it).</param>
public sealed record DiskVolumeInfo(
    int Index,
    string FileSystem,
    string? Label,
    long Capacity,
    long Used);
