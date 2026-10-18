using System.Diagnostics;
using System.Runtime.InteropServices;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Sandbox.Host;

/// <summary>
/// The sandbox's window on the host. It has to stay open: the sandbox's desktop exists only while its window is connected —
/// closed, there is nothing to capture and nothing to send input to. So it opens maximised on a monitor beside the work,
/// and hands the foreground straight back to whatever had it, so keystrokes meant for the host do not land in the sandbox.
/// </summary>
internal static class SandboxWindow
{
    /// <summary>The Windows Sandbox app's window process: one per connected sandbox.</summary>
    private const string ClientProcess = "WindowsSandboxRemoteSession";

    private const int ShowRestore = 9;
    private const int ShowMaximize = 3;
    private const uint NoZOrder = 0x0004;
    private const uint NoActivate = 0x0010;

    public static bool IsOpen => WindowHandles().Count > 0;

    /// <summary>Connects to the sandbox, which opens its window, and places the window on <paramref name="monitor"/>.</summary>
    public static void Open(string sandboxId, DisplayMonitor monitor, TimeSpan timeout)
    {
        var previous = GetForegroundWindow();
        var before = WindowHandles();
        WsbCli.Connect(sandboxId);

        var deadline = DateTime.UtcNow + timeout;
        IntPtr window;
        while ((window = WindowHandles().FirstOrDefault(h => !before.Contains(h))) == IntPtr.Zero)
        {
            if (DateTime.UtcNow > deadline) throw new SandboxException("The sandbox's window did not open.");
            Thread.Sleep(250);
        }

        Place(window, monitor);
        if (previous != IntPtr.Zero) UiTestGate.BringToForeground(previous, TimeSpan.FromSeconds(2));
    }

    /// <summary>Moves the open window to <paramref name="monitor"/>; false when there is no window to move.</summary>
    public static bool MoveTo(DisplayMonitor monitor)
    {
        var window = WindowHandles().FirstOrDefault();
        if (window == IntPtr.Zero) return false;
        var previous = GetForegroundWindow();
        Place(window, monitor);
        if (previous != IntPtr.Zero && previous != window) UiTestGate.BringToForeground(previous, TimeSpan.FromSeconds(2));
        return true;
    }

    private static void Place(IntPtr window, DisplayMonitor monitor)
    {
        // Restored first: a maximised window moved by its bounds stays maximised on the monitor it was on.
        ShowWindow(window, ShowRestore);
        SetWindowPos(window, IntPtr.Zero, monitor.Left, monitor.Top, monitor.Width, monitor.Height, NoZOrder | NoActivate);
        ShowWindow(window, ShowMaximize);
    }

    private static List<IntPtr> WindowHandles()
    {
        var handles = new List<IntPtr>();
        foreach (var process in Process.GetProcessesByName(ClientProcess))
        {
            using (process)
                if (process.MainWindowHandle != IntPtr.Zero) handles.Add(process.MainWindowHandle);
        }
        return handles;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
