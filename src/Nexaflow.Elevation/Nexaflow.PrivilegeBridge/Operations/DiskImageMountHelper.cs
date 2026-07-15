using System;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>
/// Attaches/detaches a disk image through the native Windows Virtual Disk API (<see cref="NativeVirtualDisk"/>)
/// while running elevated. VHD/VHDX attach requires administrator rights, which is why it happens here in the
/// bridge rather than in-process. No process is spawned.
/// </summary>
internal static class DiskImageMountHelper
{
    public static (bool Success, string? DriveLetter, string Message) Mount(string imagePath)
    {
        try
        {
            var readOnly = imagePath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase);
            var drive    = NativeVirtualDisk.Attach(imagePath, readOnly);
            return (true, drive, "Mounted.");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public static (bool Success, string Message) Dismount(string imagePath)
    {
        try
        {
            NativeVirtualDisk.Detach(imagePath);
            return (true, "Dismounted.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
