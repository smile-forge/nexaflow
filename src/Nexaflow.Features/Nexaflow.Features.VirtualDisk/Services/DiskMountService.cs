using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Elevation.Contracts;
using Nexaflow.Features.Common;

namespace Nexaflow.Features.VirtualDisk.Services;

/// <summary>
/// The single place mount policy lives, so the "Mount" file action and the inspector's action bar behave
/// identically. Chooses the in-process path (ISO — no admin) or the elevated privilege-bridge path
/// (VHD/VHDX), runs the work off the UI thread, and reports the outcome through the shell. Awaited from a UI
/// command, so the shell notifications resume on the UI thread.
/// </summary>
public static class DiskMountService
{
    /// <summary>Mounts <paramref name="imagePath"/>, notifying the assigned drive letter or the failure.
    /// Stays quiet when the user declines the UAC prompt.</summary>
    public static async Task MountAsync(IShellServices shell, string imagePath, CancellationToken ct = default)
    {
        var name = Path.GetFileName(imagePath);
        if (MountSupport.RequiresElevation(imagePath))
        {
            var res = await shell.RunElevatedAsync(
                ElevatedRequest.Single(ElevatedOps.DiskMount, (ElevatedArgs.ImagePath, imagePath)), ct);
            if (res.WasDeclined) return;
            if (!res.Success) { shell.ShowError($"Couldn't mount {name}: {res.Message}"); return; }
            var drive = res.Operations.Count > 0
                ? res.Operations[0].Data.GetValueOrDefault(ElevatedArgs.DriveLetter)
                : null;
            Notify(shell, name, drive);
        }
        else
        {
            var outcome = await Task.Run(() => new DiskMounter().Mount(imagePath), ct);
            if (!outcome.Success) { shell.ShowError($"Couldn't mount {name}: {outcome.Error}"); return; }
            Notify(shell, name, outcome.DriveLetter);
        }
        DiskMounter.InvalidateCache();
    }

    /// <summary>Unmounts <paramref name="imagePath"/> (ISO in-process, VHD/VHDX elevated).</summary>
    public static async Task UnmountAsync(IShellServices shell, string imagePath, CancellationToken ct = default)
    {
        var name = Path.GetFileName(imagePath);
        if (MountSupport.RequiresElevation(imagePath))
        {
            var res = await shell.RunElevatedAsync(
                ElevatedRequest.Single(ElevatedOps.DiskUnmount, (ElevatedArgs.ImagePath, imagePath)), ct);
            if (res.WasDeclined) return;
            if (!res.Success) { shell.ShowError($"Couldn't unmount {name}: {res.Message}"); return; }
        }
        else
        {
            var ok = await Task.Run(() => new DiskMounter().Dismount(imagePath), ct);
            if (!ok) { shell.ShowError($"Couldn't unmount {name}."); return; }
        }
        DiskMounter.InvalidateCache();
        shell.ShowNotification($"Unmounted {name}.");
    }

    private static void Notify(IShellServices shell, string name, string? drive) =>
        shell.ShowNotification(drive is null ? $"Mounted {name}." : $"Mounted {name} as {drive}");
}
