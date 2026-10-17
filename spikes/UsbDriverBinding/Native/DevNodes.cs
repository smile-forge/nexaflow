using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Nexaflow.Spikes.UsbDriverBinding.Native;

/// <summary>Typed reads over CfgMgr32 — the unprivileged half of the driver view.</summary>
internal static unsafe class DevNodes
{
    const uint TypeString = 0x12, TypeStringList = 0x2012, TypeUInt32 = 0x07, TypeGuid = 0x0D, TypeBinary = 0x1003;

    public static IReadOnlyList<string> PresentDeviceIds(string enumerator)
    {
        const uint flags = CfgMgr32.FilterEnumerator | CfgMgr32.FilterPresent;
        while (true)
        {
            Check(CfgMgr32.CM_Get_Device_ID_List_Size(out uint length, enumerator, flags), "CM_Get_Device_ID_List_Size");
            var buffer = new char[length];
            uint cr;
            fixed (char* p = buffer) cr = CfgMgr32.CM_Get_Device_ID_List(enumerator, p, length, flags);
            if (cr == CfgMgr32.CrBufferSmall) continue;
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

    public static (uint Status, uint Problem)? Status(uint devInst) =>
        CfgMgr32.CM_Get_DevNode_Status(out uint status, out uint problem, devInst, 0) == CfgMgr32.CrSuccess ? (status, problem) : null;

    public static string? String(uint devInst, DevPropKey key) => Read(devInst, key, TypeString, static d => DecodeString(d));
    public static string[] Strings(uint devInst, DevPropKey key) => Read(devInst, key, TypeStringList, static d => SplitMultiSz(MemoryMarshal.Cast<byte, char>(d).ToArray()).ToArray()) ?? [];
    public static uint? UInt32(uint devInst, DevPropKey key) => Read<uint?>(devInst, key, TypeUInt32, static d => BinaryPrimitives.ReadUInt32LittleEndian(d));
    public static Guid? Guid(uint devInst, DevPropKey key) => Read<Guid?>(devInst, key, TypeGuid, static d => new Guid(d[..16]));
    public static byte[]? Binary(uint devInst, DevPropKey key) => Read(devInst, key, TypeBinary, static d => d.ToArray());

    /// <summary>CM_POWER_DATA.PD_MostRecentPowerState — D0 is awake; D2/D3 is a selectively suspended USB device.</summary>
    public static string? PowerState(uint devInst) =>
        Binary(devInst, DevPropKeys.PowerData) is { Length: >= 8 } data
            ? BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)) switch { 1 => "D0", 2 => "D1", 3 => "D2", 4 => "D3", var s => $"state {s}" }
            : null;

    public static string? InterfaceInstanceId(string interfacePath)
    {
        uint size = 0;
        var cr = CfgMgr32.CM_Get_Device_Interface_Property(interfacePath, in DevPropKeys.InstanceId, out _, null, ref size, 0);
        if (cr != CfgMgr32.CrBufferSmall || size == 0) return null;
        var buffer = new byte[size];
        fixed (byte* p = buffer) cr = CfgMgr32.CM_Get_Device_Interface_Property(interfacePath, in DevPropKeys.InstanceId, out _, p, ref size, 0);
        return cr == CfgMgr32.CrSuccess ? DecodeString(buffer) : null;
    }

    delegate T Decoder<T>(ReadOnlySpan<byte> data);

    static T? Read<T>(uint devInst, DevPropKey key, uint expectedType, Decoder<T> decode)
    {
        uint size = 0;
        var cr = CfgMgr32.CM_Get_DevNode_Property(devInst, in key, out uint type, null, ref size, 0);
        if (cr != CfgMgr32.CrBufferSmall || size == 0) return default;
        var buffer = new byte[size];
        fixed (byte* p = buffer) cr = CfgMgr32.CM_Get_DevNode_Property(devInst, in key, out type, p, ref size, 0);
        return cr == CfgMgr32.CrSuccess && type == expectedType ? decode(buffer.AsSpan(0, (int)size)) : default;
    }

    static string DecodeString(ReadOnlySpan<byte> data) => MemoryMarshal.Cast<byte, char>(data).ToString().TrimEnd('\0');

    static IReadOnlyList<string> SplitMultiSz(char[] buffer) =>
        new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries);

    static void Check(uint cr, string call)
    {
        if (cr != CfgMgr32.CrSuccess) throw new InvalidOperationException($"{call} failed: CR 0x{cr:X2}");
    }
}
