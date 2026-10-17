using System.Runtime.InteropServices;

namespace Nexaflow.Spikes.UsbDriverBinding.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct SpDevInfoData
{
    public uint Size;
    public Guid ClassGuid;
    public uint DevInst;
    public nuint Reserved;

    public static SpDevInfoData Create() => new() { Size = (uint)Marshal.SizeOf<SpDevInfoData>() };
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SpDevInstallParams
{
    public uint Size;
    public uint Flags;
    public uint FlagsEx;
    public nint HwndParent;
    public nint InstallMsgHandler;
    public nint InstallMsgHandlerContext;
    public nint FileQueue;
    public nuint ClassInstallReserved;
    public uint Reserved;
    public fixed char DriverPath[260];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SpDrvInfoData
{
    public uint Size;
    public uint DriverType;
    public nuint Reserved;
    public fixed char Description[256];
    public fixed char MfgName[256];
    public fixed char ProviderName[256];
    public uint DriverDateLow;
    public uint DriverDateHigh;
    public ulong DriverVersion;
}

internal static unsafe partial class SetupApi
{
    public const uint DiEnumSingleInf = 0x00010000;
    public const uint DiFlagsExAllowExcludedDrivers = 0x00000800;
    public const uint ClassDriver = 1;
    public const uint CompatibleDriver = 2;
    public const uint RegistryClassGuid = 0x08;
    public const int DetailDataSize = 1584; // sizeof(SP_DRVINFO_DETAIL_DATA_W) on x64
    public const int DetailSectionOffset = 32;
    public const int DetailInfFileOffset = 544;

    [LibraryImport("setupapi.dll", SetLastError = true)]
    public static partial nint SetupDiCreateDeviceInfoList(nint classGuid, nint hwnd);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiDestroyDeviceInfoList(nint set);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiOpenDeviceInfoW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiOpenDeviceInfo(nint set, string instanceId, nint hwnd, uint flags, ref SpDevInfoData data);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInstallParamsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDeviceInstallParams(nint set, ref SpDevInfoData data, ref SpDevInstallParams parameters);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiSetDeviceInstallParamsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiSetDeviceInstallParams(nint set, ref SpDevInfoData data, ref SpDevInstallParams parameters);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiBuildDriverInfoList(nint set, ref SpDevInfoData data, uint driverType);

    [LibraryImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiDestroyDriverInfoList(nint set, ref SpDevInfoData data, uint driverType);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiEnumDriverInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiEnumDriverInfo(nint set, ref SpDevInfoData data, uint driverType, uint index, ref SpDrvInfoData driver);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDriverInfoDetailW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiGetDriverInfoDetail(nint set, ref SpDevInfoData data, ref SpDrvInfoData driver, byte* detail, uint size, out uint required);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiSetSelectedDriverW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiSetSelectedDriver(nint set, ref SpDevInfoData data, ref SpDrvInfoData driver);

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiSetDeviceRegistryPropertyW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetupDiSetDeviceRegistryProperty(nint set, ref SpDevInfoData data, uint property, byte* buffer, uint size);
}

internal static partial class NewDev
{
    public const uint InstallNoFinishUi = 0x2;
    public const uint RollbackNoUi = 0x1;

    [LibraryImport("newdev.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DiInstallDevice(nint hwnd, nint set, ref SpDevInfoData data, ref SpDrvInfoData driver, uint flags, out int needReboot);

    [LibraryImport("newdev.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DiRollbackDriver(nint set, ref SpDevInfoData data, nint hwnd, uint flags, out int needReboot);

    [LibraryImport("newdev.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DiUninstallDevice(nint hwnd, nint set, ref SpDevInfoData data, uint flags, out int needReboot);
}
