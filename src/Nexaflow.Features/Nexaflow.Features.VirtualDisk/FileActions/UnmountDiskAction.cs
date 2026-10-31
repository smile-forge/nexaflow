using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common;
using Nexaflow.Features.VirtualDisk.Services;
using Nexaflow.Icons;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Features.VirtualDisk.FileActions;

/// <summary>Ejects a mounted disk image. Offered as a drive action, but only on drives backed by an image
/// this app mounted (via <see cref="AppliesToFolder"/> — a dictionary lookup, so it never blocks a
/// selection), so it never shows on ordinary drives. Resolves the drive's backing image, then detaches it
/// (ISO in-process, VHD/VHDX elevated).</summary>
public sealed class UnmountDiskAction(IShellServices shell) : IFolderAction, ICacheable
{
    private readonly DiskMounter _mounter = new();

    public bool IsDestructive => false;
    public bool SupportsMultipleFiles => false;
    public IconRef Icon => IconRef.Emoji("⏏");
    public string DisplayName => Str.Get("VirtualDisk.Action.Unmount");

    public bool RequiresRefresh => false;
    public bool CanPerformAction => true;
    public bool AppliesToRoot => false;
    public bool AppliesToDrives => true;

    public bool AppliesToFolder(string folderPath) =>
        DriveRootLetter(folderPath) is { } letter && _mounter.IsImageBacked(letter);

    public Task<bool> PerformActionAsync(string folderPath, CancellationToken ct = default)
    {
        if (DriveRootLetter(folderPath) is not { } letter) return Task.FromResult(false);
        var imagePath = _mounter.ImagePathForDrive(letter);
        if (string.IsNullOrEmpty(imagePath))
        {
            shell.ShowError(Str.Format("VirtualDisk.Mount.NotMountedFormat", letter));
            return Task.FromResult(false);
        }
        _ = DiskMountService.UnmountAsync(shell, imagePath);
        return Task.FromResult(true);
    }

    public async Task<bool> PerformActionAsync(IEnumerable<string> folderPaths, CancellationToken ct = default)
    {
        foreach (var path in folderPaths) await PerformActionAsync(path, ct);
        return true;
    }

    /// <summary>The drive letter of a <b>drive root</b> (<c>"E:"</c> or <c>"E:\"</c>) only — null for a
    /// subfolder like <c>"E:\sub"</c>, so Unmount surfaces on the drive itself, not inside it.</summary>
    private static char? DriveRootLetter(string? path)
    {
        var t = path?.Trim();
        if (t is null || t.Length is < 2 or > 3) return null;
        if (!char.IsLetter(t[0]) || t[1] != ':') return null;
        if (t.Length == 3 && t[2] is not ('\\' or '/')) return null;
        return char.ToUpperInvariant(t[0]);
    }
}
