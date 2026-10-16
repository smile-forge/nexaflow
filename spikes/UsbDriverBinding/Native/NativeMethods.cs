using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nexaflow.Spikes.UsbDriverBinding.Native;

internal static unsafe partial class Kernel32
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint FileShareRead = 0x1;
    public const uint FileShareWrite = 0x2;
    public const uint OpenExisting = 3;
    public const uint FileFlagOverlapped = 0x40000000;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeFileHandle CreateFile(string fileName, uint access, uint share, nint securityAttributes, uint disposition, uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, void* inBuffer, int inSize, void* outBuffer, int outSize, out int bytesReturned, nint overlapped);
}

/// <summary>CTL_CODE(FILE_DEVICE_USB, fn, METHOD_BUFFERED, FILE_ANY_ACCESS) — usbioctl.h.</summary>
internal static class UsbIoctl
{
    static uint Code(uint function) => 0x220000u | (function << 2);

    public static readonly uint GetNodeConnectionInformation = Code(259);
    public static readonly uint GetDescriptorFromNodeConnection = Code(260);
    public static readonly uint GetNodeConnectionDriverKeyName = Code(264);
    public static readonly uint GetNodeConnectionInformationEx = Code(274);
    public static readonly uint GetHubInformationEx = Code(277);
    public static readonly uint GetPortConnectorProperties = Code(278);
    public static readonly uint GetNodeConnectionInformationExV2 = Code(279);

    public static readonly Guid HubInterface = new("f18a0e88-c30c-11d0-8815-00a0c906bed8");
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct DevPropKey(Guid formatId, uint propertyId)
{
    public readonly Guid FormatId = formatId;
    public readonly uint PropertyId = propertyId;
}

internal static class DevPropKeys
{
    static readonly Guid Device = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    static readonly Guid Driver = new("a8b865dd-2e3d-4094-ad97-e593a70c75d6");
    static readonly Guid Relations = new("4340a6c5-93fa-4706-972c-7b648008a5a7");
    static readonly Guid Bus = new("540b947e-8b40-45bc-a8a2-6a0b894cbda2");
    static readonly Guid InstanceIdSet = new("78c34fc8-104a-4aca-9ea4-524d52996e57");

    public static readonly DevPropKey DeviceDesc = new(Device, 2);
    public static readonly DevPropKey HardwareIds = new(Device, 3);
    public static readonly DevPropKey CompatibleIds = new(Device, 4);
    public static readonly DevPropKey Service = new(Device, 6);
    public static readonly DevPropKey Class = new(Device, 9);
    public static readonly DevPropKey ClassGuid = new(Device, 10);
    public static readonly DevPropKey DriverKey = new(Device, 11);
    public static readonly DevPropKey Manufacturer = new(Device, 13);
    public static readonly DevPropKey FriendlyName = new(Device, 14);
    public static readonly DevPropKey LocationInfo = new(Device, 15);
    public static readonly DevPropKey PowerData = new(Device, 32);
    public static readonly DevPropKey LocationPaths = new(Device, 37);
    public static readonly DevPropKey DriverDate = new(Driver, 2);
    public static readonly DevPropKey DriverVersion = new(Driver, 3);
    public static readonly DevPropKey DriverInfPath = new(Driver, 5);
    public static readonly DevPropKey DriverInfSection = new(Driver, 6);
    public static readonly DevPropKey MatchingDeviceId = new(Driver, 8);
    public static readonly DevPropKey DriverProvider = new(Driver, 9);
    public static readonly DevPropKey Parent = new(Relations, 8);
    public static readonly DevPropKey Children = new(Relations, 9);
    public static readonly DevPropKey BusReportedDeviceDesc = new(Bus, 4);
    public static readonly DevPropKey InstanceId = new(InstanceIdSet, 256);
}

internal static unsafe partial class CfgMgr32
{
    public const uint CrSuccess = 0x00;
    public const uint CrBufferSmall = 0x1A;
    public const uint FilterEnumerator = 0x1;
    public const uint FilterPresent = 0x100;
    public const uint InterfaceListPresent = 0x0;

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_List_SizeW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Get_Device_ID_List_Size(out uint length, string? filter, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_ID_ListW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Get_Device_ID_List(string? filter, char* buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Locate_DevNode(out uint devInst, string? deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_DevNode_PropertyW")]
    public static partial uint CM_Get_DevNode_Property(uint devInst, in DevPropKey key, out uint type, byte* buffer, ref uint size, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_List_SizeW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Get_Device_Interface_List_Size(out uint length, in Guid interfaceClass, string? deviceId, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_ListW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Get_Device_Interface_List(in Guid interfaceClass, string? deviceId, char* buffer, uint length, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_PropertyW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint CM_Get_Device_Interface_Property(string deviceInterface, in DevPropKey key, out uint type, byte* buffer, ref uint size, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Reenumerate_DevNode(uint devInst, uint flags);

    /// <summary>flags 0 = CM_REGISTRY_HARDWARE, the devnode's "Device Parameters" key.</summary>
    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Open_DevNode_Key(uint devInst, uint samDesired, uint profile, uint disposition, out nint key, uint flags);
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WinUsbSetupPacket
{
    public byte RequestType;
    public byte Request;
    public ushort Value;
    public ushort Index;
    public ushort Length;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct UsbInterfaceDescriptorRaw
{
    public byte Length, DescriptorType, InterfaceNumber, AlternateSetting, NumEndpoints, InterfaceClass, InterfaceSubClass, InterfaceProtocol, InterfaceString;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WinUsbPipeInformation
{
    public int PipeType;
    public byte PipeId;
    public ushort MaximumPacketSize;
    public byte Interval;
}

internal static unsafe partial class WinUsb
{
    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_Initialize(SafeFileHandle device, out nint interfaceHandle);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_Free(nint interfaceHandle);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_GetDescriptor(nint interfaceHandle, byte type, byte index, ushort languageId, byte* buffer, uint length, out uint transferred);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_QueryInterfaceSettings(nint interfaceHandle, byte alternateIndex, out UsbInterfaceDescriptorRaw descriptor);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_QueryPipe(nint interfaceHandle, byte alternateIndex, byte pipeIndex, out WinUsbPipeInformation pipe);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_QueryDeviceInformation(nint interfaceHandle, uint informationType, ref uint length, byte* buffer);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_ControlTransfer(nint interfaceHandle, WinUsbSetupPacket setup, byte* buffer, uint length, out uint transferred, nint overlapped);
}

internal static partial class Advapi32
{
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(nint token, int informationClass, out int information, int length, out int returned);
}
