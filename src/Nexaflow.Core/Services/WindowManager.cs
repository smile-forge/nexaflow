using System.Runtime.InteropServices;
using System.Windows;
using Nexaflow.Core.Models;
using Nexaflow.Core.ViewModels;

namespace Nexaflow.Core.Services;

/// <summary>
/// Tracks every open shell window and handles tab tearoff / cross-window transfer.
/// The application shuts down when the last window is unregistered.
/// </summary>
public static class WindowManager
{
    // ── Layout constants that mirror MainWindow.xaml ──────────────────────
    // Row 0 (top bar) height in logical pixels
    private const double TopBarHeight = 72;
    // Row 1 sub-row 0 (tab bar) height in logical pixels
    private const double TabBarHeight = 38;
    // Tab strip occupies the right 55 % of the tab bar (left column is 45 %)
    private const double TabStripColumnFraction = 0.45;
    // Left margin inside the StackPanel before the first tab
    private const double TabStripInternalMargin = 8;
    // Default window size as declared in MainWindow.xaml (logical pixels)
    private const double DefaultWindowWidth  = 1280;
    private const double DefaultWindowHeight = 780;

    private static readonly List<MainWindow> _windows = [];

    public static IReadOnlyList<MainWindow> OpenWindows => _windows;

    public static void Register(MainWindow window) => _windows.Add(window);

    public static void Unregister(MainWindow window)
    {
        _windows.Remove(window);
        if (_windows.Count == 0)
            Application.Current.Shutdown();
    }

    /// <summary>
    /// Returns true if <paramref name="logicalPoint"/> (logical px) lies within any registered window.
    /// </summary>
    public static bool IsPointOverAnyWindow(Point logicalPoint)
    {
        foreach (var win in _windows)
        {
            var bounds = new Rect(win.Left, win.Top, win.ActualWidth, win.ActualHeight);
            if (bounds.Contains(logicalPoint))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Removes <paramref name="tab"/> from its current owner and spawns a new window
    /// so that the first tab in its strip lands as close as possible to the drop point.
    /// </summary>
    public static void TearOffTab(TabEntry tab)
    {
        RemoveTabFromOwner(tab);

        // Cursor pos in physical pixels
        GetCursorPos(out POINT physPt);

        // Get the monitor the cursor is on and its DPI
        var hMon = MonitorFromPoint(physPt, MONITOR_DEFAULTTONEAREST);
        GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
        double scaleX = dpiX / 96.0;
        double scaleY = dpiY / 96.0;

        // Convert cursor to logical pixels (WPF coordinate space for this monitor)
        double dropX = physPt.X / scaleX;
        double dropY = physPt.Y / scaleY;

        // Get work area in physical pixels, convert to logical
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMon, ref mi);
        var work = new Rect(
            mi.rcWork.Left   / scaleX,
            mi.rcWork.Top    / scaleY,
            (mi.rcWork.Right  - mi.rcWork.Left) / scaleX,
            (mi.rcWork.Bottom - mi.rcWork.Top)  / scaleY);

        Application.Current.Dispatcher.Invoke(() =>
        {
            var win = new MainWindow(tab);

            // Offset from window top-left to where the first tab pill sits:
            //   X: left edge of the tab strip column + internal strip margin
            //   Y: vertical centre of the tab bar row
            double tabOffsetX = DefaultWindowWidth  * TabStripColumnFraction + TabStripInternalMargin;
            double tabOffsetY = TopBarHeight + TabBarHeight / 2.0;

            double left = dropX - tabOffsetX;
            double top  = dropY - tabOffsetY;

            // Clamp so the window stays within the monitor's work area
            left = Math.Max(work.Left, Math.Min(left, work.Right  - DefaultWindowWidth));
            top  = Math.Max(work.Top,  Math.Min(top,  work.Bottom - DefaultWindowHeight));

            win.Left = left;
            win.Top  = top;
            win.Show();
        });
    }

    /// <summary>
    /// Moves <paramref name="tab"/> from its current owner into <paramref name="targetVm"/>.
    /// </summary>
    public static void TransferTab(TabEntry tab, ShellViewModel targetVm)
    {
        RemoveTabFromOwner(tab, except: targetVm);
        targetVm.ReceiveTab(tab);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static void RemoveTabFromOwner(TabEntry tab, ShellViewModel? except = null)
    {
        foreach (var win in _windows)
        {
            var vm = win.ViewModel;
            if (vm == except) continue;
            if (vm.Tabs.Contains(tab))
            {
                vm.RemoveTab(tab);
                return;
            }
        }
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int  cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int  dwFlags;
    }

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int  MDT_EFFECTIVE_DPI        = 0;

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lp);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>
    /// Returns the current cursor position in WPF logical pixels,
    /// accounting for the DPI of whichever monitor the cursor is on.
    /// </summary>
    public static Point GetCursorScreenPos()
    {
        GetCursorPos(out POINT physPt);
        var hMon = MonitorFromPoint(physPt, MONITOR_DEFAULTTONEAREST);
        GetDpiForMonitor(hMon, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
        return new Point(physPt.X / (dpiX / 96.0), physPt.Y / (dpiY / 96.0));
    }
}
