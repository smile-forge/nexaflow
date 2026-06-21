using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Nexaflow.Core.Services;

/// <summary>
/// DPI-aware screen utilities: cursor position, monitor info, and point-in-window testing.
/// Tab/window lifecycle management has moved to <see cref="ShellServices"/>.
/// </summary>
public static class WindowManager
{
    // ── Point-in-window testing ───────────────────────────────────────────

    /// <summary>
    /// Returns true if <paramref name="logicalPoint"/> (logical px) lies within any
    /// of the supplied windows.
    /// </summary>
    public static bool IsPointOverAnyWindow(Point logicalPoint, IEnumerable<Window> windows)
    {
        foreach (var win in windows)
        {
            var bounds = new Rect(win.Left, win.Top, win.ActualWidth, win.ActualHeight);
            if (bounds.Contains(logicalPoint))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if <paramref name="logicalPoint"/> lies within any currently open window.
    /// </summary>
    public static bool IsPointOverAnyWindow(Point logicalPoint)
        => IsPointOverAnyWindow(logicalPoint,
               Application.Current.Windows.Cast<Window>());

    // ── Cursor / DPI helpers ──────────────────────────────────────────────

    /// <summary>
    /// Returns the current cursor position in WPF logical pixels,
    /// accounting for the DPI of whichever monitor the cursor is on.
    /// </summary>
    public static Point GetCursorScreenPos()
    {
        GetCursorPos(out POINT physPt);
        var hMon = MonitorFromPoint(physPt, MONITOR_DEFAULTTONEAREST);
        GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out uint _);
        return new Point(physPt.X / (dpiX / 96.0), physPt.Y / (dpiX / 96.0));
    }

    /// <summary>
    /// Returns cursor position (physical px), monitor handle, logical scale, and monitor
    /// work area (logical px). Used by <see cref="ShellServices"/> for tearoff positioning.
    /// </summary>
    internal static (double dropX, double dropY, IntPtr hMon, double scale, Rect workArea)
        GetCursorInfo()
    {
        GetCursorPos(out POINT physPt);
        var hMon = MonitorFromPoint(physPt, MONITOR_DEFAULTTONEAREST);
        GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out uint _);
        double scale = dpiX / 96.0;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMon, ref mi);

        var work = new Rect(
            mi.rcWork.Left   / scale,
            mi.rcWork.Top    / scale,
            (mi.rcWork.Right  - mi.rcWork.Left) / scale,
            (mi.rcWork.Bottom - mi.rcWork.Top)  / scale);

        return (physPt.X / scale, physPt.Y / scale, hMon, scale, work);
    }

    /// <summary>
    /// Returns the work area (WPF logical px) of the monitor that <paramref name="win"/> currently
    /// occupies, or null if it has no native handle yet. Lets a follow-up window open on the same
    /// monitor as the one that is closing (e.g. the setup wizard → main window). Call while the
    /// window's HWND is still alive (e.g. from its <c>Closing</c> handler).
    /// </summary>
    internal static Rect? GetWorkAreaForWindow(Window win)
    {
        var hwnd = new WindowInteropHelper(win).Handle;
        if (hwnd == IntPtr.Zero) return null;

        var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out uint _);
        double scale = dpiX / 96.0;

        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMon, ref mi);

        return new Rect(
            mi.rcWork.Left   / scale,
            mi.rcWork.Top    / scale,
            (mi.rcWork.Right  - mi.rcWork.Left) / scale,
            (mi.rcWork.Bottom - mi.rcWork.Top)  / scale);
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int  cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int  dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int  MDT_EFFECTIVE_DPI        = 0;

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT lp);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType,
                                                out uint dpiX, out uint dpiY);
}
