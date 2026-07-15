using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Nexaflow.Features.VirtualDisk.Services;

/// <summary>
/// Attaches and detaches a disk image through the Windows Virtual Disk API (virtdisk.dll) — the same
/// service the <c>Mount-DiskImage</c>/<c>Dismount-DiskImage</c> cmdlets wrap, but called directly so the app
/// never spawns a process. ISO attaches as a standard user; VHD/VHDX attach needs administrator rights and is
/// therefore invoked from the elevated privilege bridge (see <see cref="MountSupport.RequiresElevation"/>).
/// <para>
/// This file is duplicated verbatim in the PrivilegeBridge (which references only Elevation.Contracts, so it
/// can't share a feature assembly). Keep the two copies identical.
/// </para>
/// </summary>
internal static class NativeVirtualDisk
{
    // {EC984AEC-A0F9-47e9-901F-71415A66345B} — VIRTUAL_STORAGE_TYPE_VENDOR_MICROSOFT.
    private static readonly Guid VendorMicrosoft = new("EC984AEC-A0F9-47e9-901F-71415A66345B");

    private const uint DeviceUnknown = 0, DeviceIso = 1, DeviceVhd = 2, DeviceVhdx = 3;

    private const uint AccessAttachRo = 0x00010000;
    private const uint AccessAttachRw = 0x00020000;
    private const uint AccessDetach   = 0x00040000;

    private const uint OpenFlagNone = 0;

    private const uint AttachFlagReadOnly          = 0x00000001;
    private const uint AttachFlagPermanentLifetime = 0x00000004;

    private const uint DetachFlagNone = 0;

    private const uint AttachVersion1 = 1;

    /// <summary>Attaches <paramref name="imagePath"/> permanently (stays mounted after the handle closes, like
    /// Mount-DiskImage) and returns the drive letter Windows assigned (e.g. <c>"E:"</c>), or null if none was.
    /// Throws <see cref="Win32Exception"/> on failure.</summary>
    public static string? Attach(string imagePath, bool readOnly)
    {
        var storageType = StorageTypeFor(imagePath);
        var access      = readOnly ? AccessAttachRo : AccessAttachRw;

        int rc = OpenVirtualDisk(ref storageType, imagePath, access, OpenFlagNone, IntPtr.Zero, out var handle);
        if (rc != 0) throw new Win32Exception(rc);
        try
        {
            uint before = GetLogicalDrives();
            uint flags  = AttachFlagPermanentLifetime | (readOnly ? AttachFlagReadOnly : 0);
            var  p      = new ATTACH_VIRTUAL_DISK_PARAMETERS { Version = AttachVersion1 };

            rc = AttachVirtualDisk(handle, IntPtr.Zero, flags, 0, ref p, IntPtr.Zero);
            if (rc != 0) throw new Win32Exception(rc);

            return WaitForNewDriveLetter(before);
        }
        finally { CloseHandle(handle); }
    }

    /// <summary>Detaches the disk image at <paramref name="imagePath"/>. Throws <see cref="Win32Exception"/>.</summary>
    public static void Detach(string imagePath)
    {
        var storageType = StorageTypeFor(imagePath);

        int rc = OpenVirtualDisk(ref storageType, imagePath, AccessDetach, OpenFlagNone, IntPtr.Zero, out var handle);
        if (rc != 0) throw new Win32Exception(rc);
        try
        {
            rc = DetachVirtualDisk(handle, DetachFlagNone, 0);
            if (rc != 0) throw new Win32Exception(rc);
        }
        finally { CloseHandle(handle); }
    }

    /// <summary>The mount manager assigns the drive letter a moment after attach, so watch the logical-drive
    /// bitmask for a newly-appeared letter (up to ~5s). Null when the image mounts without a letter.</summary>
    private static string? WaitForNewDriveLetter(uint before)
    {
        for (int i = 0; i < 100; i++)
        {
            uint added = GetLogicalDrives() & ~before;
            if (added != 0)
            {
                for (int bit = 0; bit < 26; bit++)
                    if ((added & (1u << bit)) != 0)
                        return $"{(char)('A' + bit)}:";
            }
            Thread.Sleep(50);
        }
        return null;
    }

    private static VIRTUAL_STORAGE_TYPE StorageTypeFor(string imagePath)
    {
        var deviceId = Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".iso"  => DeviceIso,
            ".vhd"  => DeviceVhd,
            ".vhdx" => DeviceVhdx,
            _       => DeviceUnknown,
        };
        return new VIRTUAL_STORAGE_TYPE
        {
            DeviceId = deviceId,
            VendorId = deviceId == DeviceUnknown ? Guid.Empty : VendorMicrosoft,
        };
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct VIRTUAL_STORAGE_TYPE
    {
        public uint DeviceId;
        public Guid VendorId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ATTACH_VIRTUAL_DISK_PARAMETERS
    {
        public uint Version;    // ATTACH_VIRTUAL_DISK_VERSION_1
        public uint Reserved;   // Version1 union payload
    }

    [DllImport("virtdisk.dll", CharSet = CharSet.Unicode)]
    private static extern int OpenVirtualDisk(
        ref VIRTUAL_STORAGE_TYPE VirtualStorageType, string Path, uint VirtualDiskAccessMask,
        uint Flags, IntPtr Parameters, out IntPtr Handle);

    [DllImport("virtdisk.dll")]
    private static extern int AttachVirtualDisk(
        IntPtr VirtualDiskHandle, IntPtr SecurityDescriptor, uint Flags, uint ProviderSpecificFlags,
        ref ATTACH_VIRTUAL_DISK_PARAMETERS Parameters, IntPtr Overlapped);

    [DllImport("virtdisk.dll")]
    private static extern int DetachVirtualDisk(IntPtr VirtualDiskHandle, uint Flags, uint ProviderSpecificFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetLogicalDrives();
}
