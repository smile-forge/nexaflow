using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nexaflow.IO.Usb.Windows.Native;

/// <summary>WINUSB_SETUP_PACKET.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WinUsbSetupPacket
{
    public byte RequestType;
    public byte Request;
    public ushort Value;
    public ushort Index;
    public ushort Length;
}

/// <summary>USB_INTERFACE_DESCRIPTOR, as WinUsb_QueryInterfaceSettings returns it.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct WinUsbInterfaceDescriptor
{
    public byte Length, DescriptorType, InterfaceNumber, AlternateSetting, NumEndpoints, InterfaceClass, InterfaceSubClass, InterfaceProtocol, InterfaceString;
}

/// <summary>WINUSB_PIPE_INFORMATION: PipeType is USBD_PIPE_TYPE — control, isochronous, bulk, interrupt.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WinUsbPipeInformation
{
    public int PipeType;
    public byte PipeId;
    public ushort MaximumPacketSize;
    public byte Interval;
}

/// <summary>winusb.dll. Its file handle must be opened overlapped; every transfer here is.</summary>
internal static unsafe partial class WinUsb
{
    public const int ErrorAccessDenied = 5, ErrorSharingViolation = 32, ErrorGenFailure = 31, ErrorSemTimeout = 121,
        ErrorOperationAborted = 995, ErrorIoPending = 997, ErrorDeviceNotConnected = 1167;

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_Initialize(SafeFileHandle device, out nint interfaceHandle);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_Free(nint interfaceHandle);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_GetAssociatedInterface(nint interfaceHandle, byte associatedInterfaceIndex, out nint associatedInterfaceHandle);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_QueryInterfaceSettings(nint interfaceHandle, byte alternateInterfaceNumber, out WinUsbInterfaceDescriptor descriptor);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_QueryPipe(nint interfaceHandle, byte alternateInterfaceNumber, byte pipeIndex, out WinUsbPipeInformation pipe);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_ControlTransfer(nint interfaceHandle, WinUsbSetupPacket setup, byte* buffer, uint length, uint* transferred, NativeOverlapped* overlapped);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_ReadPipe(nint interfaceHandle, byte pipeId, byte* buffer, uint length, uint* transferred, NativeOverlapped* overlapped);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_WritePipe(nint interfaceHandle, byte pipeId, byte* buffer, uint length, uint* transferred, NativeOverlapped* overlapped);

    [LibraryImport("winusb.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WinUsb_AbortPipe(nint interfaceHandle, byte pipeId);
}
