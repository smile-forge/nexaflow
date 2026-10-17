using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nexaflow.IO.Usb.Windows.Native;

/// <summary>hid.dll: the HID class driver's user-mode side. Report types are HidP_Input 0, HidP_Output 1, HidP_Feature 2.</summary>
internal static unsafe partial class HidDll
{
    public const int StatusSuccess = 0x00110000;
    public const int CapsSize = 64;
    public const int ButtonCapsSize = 72;
    public const int ValueCapsSize = 72;

    public static readonly Guid InterfaceClass = new("4d1e55b2-f16f-11cf-88cb-001111000030");

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetPreparsedData(SafeFileHandle device, out nint preparsed);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_FreePreparsedData(nint preparsed);

    [LibraryImport("hid.dll")]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetProductString(SafeFileHandle device, char* buffer, uint bufferBytes);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_GetFeature(SafeFileHandle device, byte* report, uint length);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_SetFeature(SafeFileHandle device, byte* report, uint length);

    [LibraryImport("hid.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static partial bool HidD_SetOutputReport(SafeFileHandle device, byte* report, uint length);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetCaps(nint preparsed, byte* caps);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetButtonCaps(int reportType, byte* caps, ref ushort length, nint preparsed);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetValueCaps(int reportType, byte* caps, ref ushort length, nint preparsed);

    /// <summary>The buttons down in a report, as USAGE_AND_PAGE pairs (usage, then page).</summary>
    [LibraryImport("hid.dll")]
    public static partial int HidP_GetUsagesEx(int reportType, ushort linkCollection, ushort* usages, ref uint length,
        nint preparsed, byte* report, uint reportLength);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetUsageValue(int reportType, ushort usagePage, ushort linkCollection, ushort usage,
        out uint value, nint preparsed, byte* report, uint reportLength);

    [LibraryImport("hid.dll")]
    public static partial int HidP_GetUsageValueArray(int reportType, ushort usagePage, ushort linkCollection, ushort usage,
        byte* buffer, ushort bufferLength, nint preparsed, byte* report, uint reportLength);
}
