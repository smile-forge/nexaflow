using System.Collections.Generic;
using Nexaflow.Features.Common;
using Nexaflow.Features.VirtualDisk.Services;

namespace Nexaflow.Features.VirtualDisk.FileActions;

/// <summary>Mounts a disk image as a real drive letter using the built-in Windows Virtual Disk Service (no
/// third-party driver). Matched only to natively-mountable images via the <c>/disk/mountable</c> experience
/// (iso/vhd/vhdx); ISO mounts without elevation, VHD/VHDX prompt for UAC. See <see cref="DiskMountService"/>.</summary>
public sealed class MountDiskAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/disk/mountable";
    public string ExperienceId => "/disk/mountable";
    public string ExperienceDescription => "Mountable disk image";

    public string DisplayName => "Mount";
    public string Icon => "📀";

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;

    public bool PerformAction(string filePath)
    {
        // Fire-and-forget: the UI thread's SynchronizationContext carries the shell notifications back.
        _ = DiskMountService.MountAsync(shell, filePath);
        return true;
    }

    public bool PerformAction(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths) PerformAction(path);
        return true;
    }
}
