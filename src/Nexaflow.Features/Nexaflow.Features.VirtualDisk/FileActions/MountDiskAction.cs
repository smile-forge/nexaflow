using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.VirtualDisk.Services;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.VirtualDisk.FileActions;

/// <summary>Mounts a disk image as a real drive letter using the built-in Windows Virtual Disk Service (no
/// third-party driver). Matched only to natively-mountable images via the <c>/disk/mountable</c> experience
/// (iso/vhd/vhdx); ISO mounts without elevation, VHD/VHDX prompt for UAC. See <see cref="DiskMountService"/>.</summary>
public sealed class MountDiskAction(IShellServices shell) : IFileAction, ICacheable
{
    public static string? StaticExperienceId => "/disk/mountable";
    public string ExperienceId => "/disk/mountable";
    public string ExperienceDescription => Str.Get("VirtualDisk.Action.MountDescription");

    public string DisplayName => Str.Get("VirtualDisk.Action.Mount");
    public string Icon => "📀";

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;

    public Task<bool> PerformActionAsync(string filePath, CancellationToken ct = default)
    {
        // Fire-and-forget: the UI thread's SynchronizationContext carries the shell notifications back.
        _ = DiskMountService.MountAsync(shell, filePath);
        return Task.FromResult(true);
    }

    // SupportsMultipleFiles is false, so a selection mounts the first image and nothing else — this used to
    // mount every one of them, and to return true for an empty selection having mounted nothing at all.
    public Task<bool> PerformActionAsync(IEnumerable<string> filePaths, CancellationToken ct = default)
        => filePaths.FirstOrDefault() is { } first ? PerformActionAsync(first, ct) : Task.FromResult(false);
}
