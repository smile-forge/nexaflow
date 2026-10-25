using System.Runtime.InteropServices;
using Nexaflow.Core.Models;

namespace Nexaflow.Core.Services;

/// <summary>
/// The monitors attached now, each with the stable <see cref="MonitorArea.Id"/> a window placement is saved
/// against. Enumerating monitors is cheap; resolving their device paths (<c>QueryDisplayConfig</c>) is not, so the
/// result is kept against the set of monitors enumerated and resolved again only when that set changes or
/// <see cref="Forget"/> says the displays have.
/// </summary>
internal static class AttachedMonitors
{
    private static readonly object Gate = new();
    private static string? _key;
    private static IReadOnlyList<MonitorArea> _monitors = [];

    /// <summary>Every attached monitor.</summary>
    internal static IReadOnlyList<MonitorArea> Read()
    {
        var found = Enumerate();
        var key   = string.Join('|', found.Select(f => $"{f.Device},{f.Bounds},{f.WorkArea},{f.IsPrimary}"));

        lock (Gate)
        {
            if (key == _key) return _monitors;

            var paths = DevicePaths();
            _monitors = found.Select(f => new MonitorArea(paths.GetValueOrDefault(f.Device, f.Device), f.Bounds, f.WorkArea, f.IsPrimary))
                             .ToList();
            _key = key;
            return _monitors;
        }
    }

    /// <summary>The displays changed: a monitor now at a device name and position another held must not keep its id.</summary>
    internal static void Forget()
    {
        lock (Gate) _key = null;
    }

    /// <summary>The monitor in <paramref name="attached"/> that <paramref name="hwnd"/> is on — for a minimised
    /// window, the one it restores to; null when the window's monitor is not among them.</summary>
    internal static MonitorArea? Of(IntPtr hwnd, IReadOnlyList<MonitorArea> attached)
    {
        var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info)) return null;

        var bounds = ToPixelRect(info.rcMonitor);
        return attached.FirstOrDefault(m => m.Bounds == bounds);
    }

    internal static PixelRect ToPixelRect(WindowManager.RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);

    private readonly record struct Found(string Device, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary);

    private static List<Found> Enumerate()
    {
        var found = new List<Found>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (GetMonitorInfo(monitor, ref info))
                found.Add(new Found(info.szDevice, ToPixelRect(info.rcMonitor), ToPixelRect(info.rcWork),
                                    (info.dwFlags & MONITORINFOF_PRIMARY) != 0));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    /// <summary>Each active display device name (<c>\\.\DISPLAY1</c>) mapped to its monitor's device path. Empty
    /// when the display configuration cannot be read, which leaves every monitor identified by its device name.</summary>
    private static Dictionary<string, string> DevicePaths()
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // The topology can change between sizing the buffers and filling them; that is the only retry.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount) != 0) return paths;

            var pathInfo = new DISPLAYCONFIG_PATH_INFO[pathCount];
            var modeInfo = new DISPLAYCONFIG_MODE_INFO[modeCount];
            int result = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, pathInfo, ref modeCount, modeInfo, IntPtr.Zero);
            if (result == ERROR_INSUFFICIENT_BUFFER) continue;
            if (result != 0) return paths;

            for (int i = 0; i < pathCount; i++)
            {
                var source = new DISPLAYCONFIG_SOURCE_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type      = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        size      = Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                        adapterId = pathInfo[i].sourceInfo.adapterId,
                        id        = pathInfo[i].sourceInfo.id,
                    },
                };
                var target = new DISPLAYCONFIG_TARGET_DEVICE_NAME
                {
                    header = new DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type      = DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME,
                        size      = Marshal.SizeOf<DISPLAYCONFIG_TARGET_DEVICE_NAME>(),
                        adapterId = pathInfo[i].targetInfo.adapterId,
                        id        = pathInfo[i].targetInfo.id,
                    },
                };
                if (DisplayConfigGetDeviceInfo(ref source) != 0 || DisplayConfigGetDeviceInfo(ref target) != 0) continue;

                // A cloned display is one source with several targets: the first names it.
                if (target.monitorDevicePath is { Length: > 0 } path)
                    paths.TryAdd(source.viewGdiDeviceName, path);
            }
            return paths;
        }
        return paths;
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int  MONITORINFOF_PRIMARY     = 1;
    private const uint QDC_ONLY_ACTIVE_PATHS    = 0x00000002;
    private const int  ERROR_INSUFFICIENT_BUFFER = 122;
    private const int  DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;
    private const int  DISPLAYCONFIG_DEVICE_INFO_GET_TARGET_NAME = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int                cbSize;
        public WindowManager.RECT rcMonitor;
        public WindowManager.RECT rcWork;
        public int                dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public uint refreshRateNumerator;
        public uint refreshRateDenominator;
        public uint scanLineOrdering;
        public int  targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_PATH_INFO
    {
        public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
        public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
        public uint                           flags;
    }

    // Only sized here: the modes are never read, but QueryDisplayConfig fills them alongside the paths.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct DISPLAYCONFIG_MODE_INFO { [FieldOffset(0)] public uint infoType; }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
    {
        public int  type;
        public int  size;
        public LUID adapterId;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string viewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
    {
        public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
        public uint   flags;
        public uint   outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint   connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]  public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string monitorDevicePath;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] DISPLAYCONFIG_PATH_INFO[] paths,
                                                 ref uint modeCount, [Out] DISPLAYCONFIG_MODE_INFO[] modes, IntPtr topologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_TARGET_DEVICE_NAME request);
}
