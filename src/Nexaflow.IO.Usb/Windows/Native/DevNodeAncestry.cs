using System.Runtime.InteropServices;

namespace Nexaflow.IO.Usb.Windows.Native;

internal static unsafe partial class CfgMgr32
{
    /// <summary>CM_LOCATE_DEVNODE_PHANTOM: find a devnode even when it is no longer present — as one just removed is.</summary>
    public const uint LocatePhantom = 0x1;

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll")]
    public static partial uint CM_Get_Device_ID_Size(out uint length, uint devInst, uint flags);

    [LibraryImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_IDW")]
    public static partial uint CM_Get_Device_ID(uint devInst, char* buffer, uint length, uint flags);
}
