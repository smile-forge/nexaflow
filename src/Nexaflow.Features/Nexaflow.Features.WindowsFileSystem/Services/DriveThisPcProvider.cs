using Nexaflow.Features.Common.ThisPc;
using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// The volumes of this computer — fixed, removable, optical and network drives — as This PC's first rows, each browsed at
/// its own root. The file browser's registry always offers it, whatever else the catalog holds; the shell's pickers find it
/// as they find every provider.
/// <para>
/// <see cref="GetItems"/> reads only what Windows answers at once — the letters and the drive types — and labels each row
/// by its letter. The volume label, the capacity and the file system wait for <see cref="GetDetailAsync"/>, because a
/// drive that is not ready (an empty optical bay, a share that is down) blocks whoever asks it.
/// </para>
/// </summary>
public sealed class DriveThisPcProvider(IVirtualFileSystem vfs) : IThisPcItemProvider
{
    public string ProviderId => "drive";

    public int SortOrder => 0;

    /// <summary>Never raised: a drive plugged in or taken out shows the next time This PC is drawn.</summary>
    public event Action? Changed { add { } remove { } }

    public IReadOnlyList<ThisPcItem> GetItems()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { return []; }

        var items = new List<ThisPcItem>(drives.Length);
        foreach (var drive in drives)
        {
            string root;
            try { root = drive.RootDirectory.FullName; } catch { continue; }

            items.Add(new ThisPcItem
            {
                Id         = $"{ProviderId}.{root.TrimEnd('\\').TrimEnd(':')}",
                Label      = DriveLabel(drive, ready: false),
                TargetPath = root,
                TypeLabel  = DriveTypeLabel(drive),
                Icon       = IconFor(drive),
                Backing    = ThisPcItemBacking.OwnPath,
                SortOrder  = items.Count,
            });
        }
        return items;
    }

    public async Task<ThisPcItemDetail> GetDetailAsync(ThisPcItem item, CancellationToken ct = default)
    {
        var volume = await Task.Run(() => Probe(item.TargetPath), ct);
        return volume.Available ? volume with { HasChildren = await vfs.HasChildFoldersAsync(item.TargetPath, ct) } : volume;
    }

    /// <summary>What the volume at <paramref name="root"/> answers once it is asked — its label, capacity, file system,
    /// and whether it is solid-state — or that it is not there.</summary>
    private static ThisPcItemDetail Probe(string root)
    {
        try
        {
            var drive = new DriveInfo(root);
            if (!drive.IsReady) return new ThisPcItemDetail { Available = false, Label = DriveLabel(drive, ready: false) };

            long total = drive.TotalSize;
            var icon = IconFor(drive);
            // Telling a solid-state volume from a spinning one takes a device query, so only an answering volume is asked.
            if (icon == ThisPcItemIcon.Disk && drive.DriveType == DriveType.Fixed && NativeMethods.IsNoSeekPenalty(root))
                icon = ThisPcItemIcon.SolidState;

            return new ThisPcItemDetail
            {
                Available  = true,
                Label      = DriveLabel(drive, ready: true),
                UsedBytes  = total - drive.TotalFreeSpace,
                TotalBytes = total,
                FileSystem = drive.DriveFormat,
                Icon       = icon,
            };
        }
        catch { return new ThisPcItemDetail { Available = false }; }
    }

    /// <summary>
    /// A drive's display name: <c>"Volume (C:)"</c>, or bare <c>"C:\"</c> when the volume label can't be had. Reading
    /// <see cref="DriveInfo.VolumeLabel"/> on a drive that isn't ready can block on the hardware, so until the drive has
    /// answered it is named by its letter.
    /// </summary>
    private static string DriveLabel(DriveInfo drive, bool ready)
    {
        if (!ready) return drive.Name;
        try
        {
            return string.IsNullOrWhiteSpace(drive.VolumeLabel)
                ? drive.Name
                : $"{drive.VolumeLabel} ({drive.Name.TrimEnd('\\')})";
        }
        catch { return drive.Name; }
    }

    /// <summary>The "Type" column text for a drive.</summary>
    private static string DriveTypeLabel(DriveInfo drive)
    {
        try
        {
            return drive.DriveType switch
            {
                DriveType.CDRom     => "CD/DVD Drive",
                DriveType.Removable => "Removable Disk",
                DriveType.Network   => "Network Drive",
                DriveType.Ram       => "RAM Disk",
                _                   => "Local Disk",
            };
        }
        catch { return "Local Disk"; }
    }

    /// <summary>A drive's icon kind, from its type alone.</summary>
    private static ThisPcItemIcon IconFor(DriveInfo drive)
    {
        try
        {
            return drive.DriveType switch
            {
                DriveType.CDRom     => ThisPcItemIcon.Optical,
                DriveType.Removable => ThisPcItemIcon.Removable,
                DriveType.Network   => ThisPcItemIcon.Network,
                _                   => ThisPcItemIcon.Disk,
            };
        }
        catch { return ThisPcItemIcon.Disk; }
    }
}
