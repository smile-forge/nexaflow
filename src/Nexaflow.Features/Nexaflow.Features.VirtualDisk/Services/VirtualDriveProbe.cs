using System;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;

namespace Nexaflow.Features.VirtualDisk.Services;

/// <summary>
/// Native, in-process check for "is this drive letter a mounted disk image?" — asks the storage stack
/// directly with a single <c>IOCTL_STORAGE_QUERY_PROPERTY</c>, reading the volume's bus type. A mounted
/// VHD/VHDX (and, on Windows 8+, a mounted ISO) reports <c>BusTypeFileBackedVirtual</c>; real hardware
/// (SATA/USB/NVMe/…) never does. Replaces a per-selection <c>powershell.exe</c> spawn — microseconds, no
/// process, no cache needed on the hot path.
/// </summary>
internal static class VirtualDriveProbe
{
    private const uint IoctlStorageQueryProperty = 0x002D1400;
    private const uint OpenExisting = 3;
    private const uint FileShareReadWrite = 0x00000003;

    // STORAGE_BUS_TYPE values that mean "file/virtual backed".
    private const uint BusTypeVirtual = 0x0E;
    private const uint BusTypeFileBackedVirtual = 0x0F;

    // Offset of the BusType field within STORAGE_DEVICE_DESCRIPTOR (after the fixed header up to it).
    private const int BusTypeOffset = 28;

    /// <summary>True when <paramref name="driveLetter"/> is a volume of a mounted disk image (VHD/VHDX/ISO).
    /// Best-effort: any failure (drive gone, access denied, odd device) reads as "not image-backed".</summary>
    public static bool IsFileBackedVirtual(char driveLetter)
    {
        if (!char.IsLetter(driveLetter)) return false;

        try
        {
            using var handle = CreateFileW(
                $@"\\.\{char.ToUpperInvariant(driveLetter)}:",
                dwDesiredAccess: 0,               // query-only needs no read/write access
                dwShareMode: FileShareReadWrite,
                lpSecurityAttributes: IntPtr.Zero,
                dwCreationDisposition: OpenExisting,
                dwFlagsAndAttributes: 0,
                hTemplateFile: IntPtr.Zero);

            if (handle.IsInvalid) return false;

            // STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceProperty (0), QueryType = Standard (0) }.
            var input  = new byte[12];
            var output = new byte[512];

            if (!DeviceIoControl(handle, IoctlStorageQueryProperty,
                                 input, (uint)input.Length, output, (uint)output.Length,
                                 out var returned, IntPtr.Zero))
                return false;

            if (returned < BusTypeOffset + sizeof(uint)) return false;

            var busType = BitConverter.ToUInt32(output, BusTypeOffset);
            return busType is BusTypeVirtual or BusTypeFileBackedVirtual;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice, uint dwIoControlCode,
        byte[] lpInBuffer, uint nInBufferSize, byte[] lpOutBuffer, uint nOutBufferSize,
        out uint lpBytesReturned, IntPtr lpOverlapped);
}
