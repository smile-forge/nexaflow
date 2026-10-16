using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Nexaflow.IO.Usb.Inventory;

namespace Nexaflow.IO.Usb.Windows.Native;

/// <summary>Typed, allocation-light reads over CfgMgr32. None of these need privilege.</summary>
internal static unsafe class DevNodes
{
    const uint TypeString = 0x12, TypeStringList = 0x2012, TypeUInt32 = 0x07, TypeGuid = 0x0D, TypeBinary = 0x1003;

    public static IReadOnlyList<string> PresentDeviceIds(string enumerator)
    {
        const uint flags = CfgMgr32.GetIdListFilterEnumerator | CfgMgr32.GetIdListFilterPresent;
        while (true)
        {
            Check(CfgMgr32.CM_Get_Device_ID_List_Size(out uint length, enumerator, flags), "CM_Get_Device_ID_List_Size");
            var buffer = new char[length];
            uint cr;
            fixed (char* p = buffer) cr = CfgMgr32.CM_Get_Device_ID_List(enumerator, p, length, flags);
            if (cr == CfgMgr32.CrBufferSmall) continue; // a device arrived between the two calls
            Check(cr, "CM_Get_Device_ID_List");
            return SplitMultiSz(buffer);
        }
    }

    public static IReadOnlyList<string> Interfaces(Guid interfaceClass, string? instanceId)
    {
        while (true)
        {
            Check(CfgMgr32.CM_Get_Device_Interface_List_Size(out uint length, in interfaceClass, instanceId, CfgMgr32.InterfaceListPresent), "CM_Get_Device_Interface_List_Size");
            var buffer = new char[length];
            uint cr;
            fixed (char* p = buffer) cr = CfgMgr32.CM_Get_Device_Interface_List(in interfaceClass, instanceId, p, length, CfgMgr32.InterfaceListPresent);
            if (cr == CfgMgr32.CrBufferSmall) continue;
            Check(cr, "CM_Get_Device_Interface_List");
            return SplitMultiSz(buffer);
        }
    }

    public static uint? Locate(string instanceId) =>
        CfgMgr32.CM_Locate_DevNode(out uint devInst, instanceId, 0) == CfgMgr32.CrSuccess ? devInst : null;

    public static uint ProblemCode(uint devInst) =>
        CfgMgr32.CM_Get_DevNode_Status(out _, out uint problem, devInst, 0) == CfgMgr32.CrSuccess ? problem : 0;

    public static string? String(uint devInst, in DevPropKey key) => Read(devInst, key, TypeString, static d => DecodeString(d));

    public static IReadOnlyList<string> Strings(uint devInst, in DevPropKey key) =>
        Read(devInst, key, TypeStringList, static d => SplitMultiSz(MemoryMarshal.Cast<byte, char>(d).ToArray())) ?? [];

    public static Guid? Guid(uint devInst, in DevPropKey key) => Read<Guid?>(devInst, key, TypeGuid, static d => new Guid(d[..16]));

    /// <summary>CM_POWER_DATA.PD_MostRecentPowerState.</summary>
    public static UsbPowerState Power(uint devInst) =>
        Read<UsbPowerState?>(devInst, DevPropKeys.PowerData, TypeBinary, static d => d.Length >= 8
            ? BinaryPrimitives.ReadInt32LittleEndian(d[4..]) switch { 1 => UsbPowerState.D0, 2 => UsbPowerState.D1, 3 => UsbPowerState.D2, 4 => UsbPowerState.D3, _ => UsbPowerState.Unknown }
            : UsbPowerState.Unknown) ?? UsbPowerState.Unknown;

    public static string? InterfaceInstanceId(string interfacePath)
    {
        uint size = 0;
        var cr = CfgMgr32.CM_Get_Device_Interface_Property(interfacePath, in DevPropKeys.InstanceId, out _, null, ref size, 0);
        if (cr != CfgMgr32.CrBufferSmall || size == 0) return null;
        var buffer = new byte[size];
        fixed (byte* p = buffer) cr = CfgMgr32.CM_Get_Device_Interface_Property(interfacePath, in DevPropKeys.InstanceId, out _, p, ref size, 0);
        return cr == CfgMgr32.CrSuccess ? DecodeString(buffer) : null;
    }

    /// <summary>The devnode's "Device Parameters" key, or null when it has none.</summary>
    public static RegistryKey? HardwareKey(uint devInst, bool writable)
    {
        uint cr = CfgMgr32.CM_Open_DevNode_Key(devInst, writable ? CfgMgr32.KeyReadWrite : CfgMgr32.KeyRead, 0, CfgMgr32.RegDispositionOpenExisting, out nint handle, CfgMgr32.RegistryHardware);
        return cr == CfgMgr32.CrSuccess ? RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true)) : null;
    }

    public static UsbDevNodeDriver Driver(uint devInst) => new(
        String(devInst, DevPropKeys.Service), String(devInst, DevPropKeys.Class), Guid(devInst, DevPropKeys.ClassGuid),
        String(devInst, DevPropKeys.DriverInfPath), String(devInst, DevPropKeys.DriverInfSection),
        String(devInst, DevPropKeys.DriverVersion), String(devInst, DevPropKeys.DriverProvider),
        String(devInst, DevPropKeys.MatchingDeviceId), ProblemCode(devInst),
        Strings(devInst, DevPropKeys.HardwareIds), Strings(devInst, DevPropKeys.CompatibleIds));

    delegate T Decoder<T>(ReadOnlySpan<byte> data);

    static T? Read<T>(uint devInst, in DevPropKey key, uint expectedType, Decoder<T> decode)
    {
        uint size = 0;
        var cr = CfgMgr32.CM_Get_DevNode_Property(devInst, in key, out uint type, null, ref size, 0);
        if (cr != CfgMgr32.CrBufferSmall || size == 0) return default;
        Span<byte> buffer = size <= 512 ? stackalloc byte[(int)size] : new byte[size];
        fixed (byte* p = buffer) cr = CfgMgr32.CM_Get_DevNode_Property(devInst, in key, out type, p, ref size, 0);
        return cr == CfgMgr32.CrSuccess && type == expectedType ? decode(buffer[..(int)size]) : default;
    }

    static string DecodeString(ReadOnlySpan<byte> data) => MemoryMarshal.Cast<byte, char>(data).ToString().TrimEnd('\0');

    static IReadOnlyList<string> SplitMultiSz(char[] buffer) => new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);

    static void Check(uint cr, string call)
    {
        if (cr != CfgMgr32.CrSuccess) throw new InvalidOperationException($"{call} failed: CR 0x{cr:X2}");
    }
}
