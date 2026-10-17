using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nexaflow.IO.Usb.Windows.Native;

/// <summary>The volume manager's enumeration handle, closed with <c>FindVolumeClose</c>.</summary>
internal sealed class SafeFindVolumeHandle() : SafeHandleZeroOrMinusOneIsInvalid(ownsHandle: true)
{
    protected override bool ReleaseHandle() => Kernel32.FindVolumeClose(handle);
}

internal static unsafe partial class Kernel32
{
    [LibraryImport("kernel32.dll", EntryPoint = "FindFirstVolumeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFindVolumeHandle FindFirstVolume([Out] char[] volumeName, int length);

    [LibraryImport("kernel32.dll", EntryPoint = "FindNextVolumeW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindNextVolume(SafeFindVolumeHandle search, [Out] char[] volumeName, int length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FindVolumeClose(nint search);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumePathNamesForVolumeNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumePathNamesForVolumeName(string volumeName, [Out] char[] pathNames, int length, out int returned);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVolumeInformation(string root, [Out] char[] label, int labelLength, out uint serial,
        out uint maximumComponentLength, out uint flags, [Out] char[] fileSystem, int fileSystemLength);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetDiskFreeSpaceEx(string directory, out ulong freeToCaller, out ulong total, out ulong free);
}
