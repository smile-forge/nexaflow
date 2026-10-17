using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Nexaflow.IO.Common.Hardware;

/// <summary>
/// The configuration manager's device interfaces: the present interfaces of a class, and the devnode that owns one.
/// None of it needs privilege.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class DeviceInterfaces
{
    static readonly Guid InstanceIdSet = new("78c34fc8-104a-4aca-9ea4-524d52996e57");
    const uint Success = 0, BufferSmall = 0x1A, Present = 0;

    /// <summary>Every present interface of <paramref name="interfaceClass"/>, as symbolic links.</summary>
    public static IReadOnlyList<string> Of(Guid interfaceClass)
    {
        while (true)
        {
            if (CM_Get_Device_Interface_List_SizeW(out uint length, ref interfaceClass, null, Present) != Success) return [];
            var buffer = new char[length];
            uint cr = CM_Get_Device_Interface_ListW(ref interfaceClass, null, buffer, length, Present);
            if (cr == BufferSmall) continue; // one arrived between the two calls
            return cr == Success ? new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries) : [];
        }
    }

    /// <summary>The device instance id of the devnode that owns <paramref name="interfacePath"/>, or null.</summary>
    public static string? InstanceIdOf(string interfacePath)
    {
        var key = new DevPropKey { FormatId = InstanceIdSet, PropertyId = 256 };
        uint size = 0;
        if (CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out _, null, ref size, 0) != BufferSmall || size == 0) return null;
        var buffer = new byte[size];
        if (CM_Get_Device_Interface_PropertyW(interfacePath, ref key, out _, buffer, ref size, 0) != Success) return null;
        return System.Text.Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DevPropKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern uint CM_Get_Device_Interface_PropertyW(string deviceInterface, ref DevPropKey key, out uint type,
        [Out] byte[]? buffer, ref uint size, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern uint CM_Get_Device_Interface_List_SizeW(out uint length, ref Guid interfaceClass, string? deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern uint CM_Get_Device_Interface_ListW(ref Guid interfaceClass, string? deviceId, [Out] char[] buffer, uint length, uint flags);
}
