using System.Buffers.Binary;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Storage;
using Nexaflow.IO.Usb.Windows.Native;

namespace Nexaflow.IO.Usb.Windows;

/// <summary>
/// USB disks and their volumes, joined the way Windows joins them: a function's <c>DiskDrive</c> children carry a disk
/// interface whose device number every volume's extents name. One walk of the machine's volumes answers every disk.
/// Handles are opened with no access rights, which every one of these queries accepts, so nothing needs elevation and
/// no media is read.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUsbStorage : IUsbStorageLocator
{
    static readonly Guid DiskInterface = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

    const uint StorageGetDeviceNumber = 0x002D1080;
    const uint DiskGetDriveGeometryEx = 0x000700A0;
    const uint VolumeGetVolumeDiskExtents = 0x00560000;
    const uint FileReadOnlyVolume = 0x00080000;

    public IReadOnlyList<UsbDisk> DisksOf(UsbFunctionNode function)
    {
        var disks = function.Children.Where(c => string.Equals(c.ClassName, "DiskDrive", StringComparison.OrdinalIgnoreCase)).ToList();
        if (disks.Count == 0) return [];

        var volumes = Volumes();
        return [.. disks.Select(d => Disk(d, volumes))];
    }

    static UsbDisk Disk(UsbChildNode child, IReadOnlyList<(UsbVolume Volume, IReadOnlyList<int> Disks)> volumes)
    {
        var path = DevNodes.Interfaces(DiskInterface, child.InstanceId).FirstOrDefault();
        if (path is null) return new(child.InstanceId, child.Name, null, null, [], "Windows has not started this disk.");

        using var handle = Open(path);
        if (handle.IsInvalid) return new(child.InstanceId, child.Name, null, null, [], Kernel32.LastError("Opening the disk"));

        int? number = DeviceNumber(handle);
        long? size = DiskSize(handle);
        var onDisk = number is { } n ? volumes.Where(v => v.Disks.Contains(n)).Select(v => v.Volume).ToList() : [];
        return new(child.InstanceId, child.Name, number, size, onDisk, number is null ? "Windows did not say which disk number this is." : null);
    }

    /// <summary>Every volume on the machine and the disks its extents lie on.</summary>
    static IReadOnlyList<(UsbVolume Volume, IReadOnlyList<int> Disks)> Volumes()
    {
        var found = new List<(UsbVolume, IReadOnlyList<int>)>();
        var name = new char[260];
        using var search = Kernel32.FindFirstVolume(name, name.Length);
        if (search.IsInvalid) return found;
        do
        {
            string volume = new string(name).TrimEnd('\0');
            var disks = ExtentsOf(volume);
            if (disks.Count > 0) found.Add((Describe(volume), disks));
        }
        while (Kernel32.FindNextVolume(search, name, name.Length));
        return found;
    }

    static IReadOnlyList<int> ExtentsOf(string volumeName)
    {
        using var handle = Open(volumeName.TrimEnd('\\'));
        if (handle.IsInvalid) return [];

        Span<byte> buffer = stackalloc byte[8 + 24 * 8];
        if (!Ioctl(handle, VolumeGetVolumeDiskExtents, buffer, out _)) return [];
        int count = Math.Min(BinaryPrimitives.ReadInt32LittleEndian(buffer), 8);
        var disks = new int[count];
        for (int i = 0; i < count; i++) disks[i] = BinaryPrimitives.ReadInt32LittleEndian(buffer[(8 + i * 24)..]);
        return disks;
    }

    static UsbVolume Describe(string volumeName)
    {
        var mounts = MountPoints(volumeName);
        var label = new char[261];
        var fileSystem = new char[261];
        if (!Kernel32.GetVolumeInformation(volumeName, label, label.Length, out _, out _, out uint flags, fileSystem, fileSystem.Length))
            return new(volumeName, mounts, null, null, null, null, false, Kernel32.LastError("Reading the volume"));

        long? capacity = null, free = null;
        if (Kernel32.GetDiskFreeSpaceEx(volumeName, out ulong available, out ulong total, out _))
            (capacity, free) = ((long)total, (long)available);

        return new(volumeName, mounts, Text(label), Text(fileSystem), capacity, free, (flags & FileReadOnlyVolume) != 0, null);
    }

    /// <summary>Drive roots first, then folder mount points, as Windows lists them.</summary>
    static IReadOnlyList<string> MountPoints(string volumeName)
    {
        var buffer = new char[1024];
        if (!Kernel32.GetVolumePathNamesForVolumeName(volumeName, buffer, buffer.Length, out _)) return [];
        var paths = new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return [.. paths.OrderBy(p => p.Length == 3 ? 0 : 1).ThenBy(p => p, StringComparer.OrdinalIgnoreCase)];
    }

    static int? DeviceNumber(SafeFileHandle disk)
    {
        Span<byte> number = stackalloc byte[12];
        return Ioctl(disk, StorageGetDeviceNumber, number, out _) ? BinaryPrimitives.ReadInt32LittleEndian(number[4..]) : null;
    }

    static long? DiskSize(SafeFileHandle disk)
    {
        Span<byte> geometry = stackalloc byte[256];
        return Ioctl(disk, DiskGetDriveGeometryEx, geometry, out int returned) && returned >= 32
            ? BinaryPrimitives.ReadInt64LittleEndian(geometry[24..]) : null;
    }

    static SafeFileHandle Open(string path) =>
        Kernel32.CreateFile(path, 0, Kernel32.FileShareRead | Kernel32.FileShareWrite, 0, Kernel32.OpenExisting, 0, 0);

    static unsafe bool Ioctl(SafeFileHandle handle, uint code, Span<byte> output, out int returned)
    {
        fixed (byte* p = output) return Kernel32.DeviceIoControl(handle, code, null, 0, p, output.Length, out returned, 0);
    }

    static string? Text(char[] buffer)
    {
        var s = new string(buffer).TrimEnd('\0');
        return s.Length == 0 ? null : s;
    }
}
