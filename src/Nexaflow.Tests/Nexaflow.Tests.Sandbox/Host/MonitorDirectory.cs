using System.Runtime.InteropServices;

namespace Nexaflow.Tests.Sandbox.Host;

/// <summary>One display: its device (<c>\\.\DISPLAY1</c>), the name its EDID gives it, and its work area in physical pixels.</summary>
internal sealed record DisplayMonitor(string Device, string Name, bool Primary, int Left, int Top, int Width, int Height)
{
    public override string ToString() => $"{Name} ({Device})";
}

/// <summary>
/// The monitors attached to the host, named as their makers name them. The names come from the display configuration,
/// the work areas from the monitor list; the two meet on the device name. Coordinates are physical pixels, so the
/// process asking must be per-monitor DPI aware.
/// </summary>
internal static class MonitorDirectory
{
    public static IReadOnlyList<DisplayMonitor> All()
    {
        var names = FriendlyNames();
        var monitors = new List<DisplayMonitor>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (handle, _, ref _, _) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(handle, ref info))
                monitors.Add(new DisplayMonitor(
                    info.Device,
                    names.GetValueOrDefault(info.Device, info.Device),
                    (info.Flags & MonitorInfoPrimary) != 0,
                    info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top));
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    /// <summary>
    /// The monitor <paramref name="wanted"/> names — part of its name ("DELL") or its device ("DISPLAY2") — or, with none
    /// named, the first that is not the primary: the sandbox's window belongs beside the work, not on top of it.
    /// </summary>
    public static DisplayMonitor Choose(string? wanted)
    {
        var monitors = All();
        if (monitors.Count == 0) throw new SandboxException("No monitors were found to put the sandbox's window on.");
        if (string.IsNullOrWhiteSpace(wanted))
            return monitors.FirstOrDefault(m => !m.Primary) ?? monitors[0];

        return monitors.FirstOrDefault(m => m.Device.EndsWith(wanted.TrimStart('\\', '.'), StringComparison.OrdinalIgnoreCase))
            ?? monitors.FirstOrDefault(m => m.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new SandboxException(
                $"No monitor is called '{wanted}'. There are: {string.Join(", ", monitors)}.");
    }

    /// <summary>Device name to EDID name, for every active display.</summary>
    private static Dictionary<string, string> FriendlyNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != 0) return names;

        var paths = new DisplayConfigPathInfo[pathCount];
        var modes = new DisplayConfigModeInfo[modeCount];
        if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return names;

        foreach (var path in paths.Take((int)pathCount))
        {
            var source = new DisplayConfigSourceDeviceName
            {
                Header = Header(GetSourceName, Marshal.SizeOf<DisplayConfigSourceDeviceName>(), path.SourceInfo.AdapterId, path.SourceInfo.Id),
            };
            var target = new DisplayConfigTargetDeviceName
            {
                Header = Header(GetTargetName, Marshal.SizeOf<DisplayConfigTargetDeviceName>(), path.TargetInfo.AdapterId, path.TargetInfo.Id),
            };
            if (DisplayConfigGetDeviceInfo(ref source) == 0 && DisplayConfigGetDeviceInfo(ref target) == 0
                && !string.IsNullOrWhiteSpace(target.MonitorFriendlyDeviceName))
                names[source.ViewGdiDeviceName] = target.MonitorFriendlyDeviceName;
        }
        return names;
    }

    private static DisplayConfigDeviceInfoHeader Header(uint type, int size, Luid adapter, uint id) =>
        new() { Type = type, Size = (uint)size, AdapterId = adapter, Id = id };

    private const uint QdcOnlyActivePaths = 0x2;
    private const uint GetSourceName = 1;
    private const uint GetTargetName = 2;
    private const uint MonitorInfoPrimary = 0x1;

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr deviceContext, ref NativeRect bounds, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr deviceContext, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags, ref uint pathCount, [Out] DisplayConfigPathInfo[] paths, ref uint modeCount, [Out] DisplayConfigModeInfo[] modes, IntPtr topology);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName request);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName request);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public NativeRect Bounds;
        public NativeRect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIndex;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public uint RefreshRateNumerator;
        public uint RefreshRateDenominator;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    /// <summary>Asked for only to size the query; its union is never read.</summary>
    [StructLayout(LayoutKind.Sequential, Size = 64)]
    private struct DisplayConfigModeInfo
    {
        public uint InfoType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string MonitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string MonitorDevicePath;
    }
}
