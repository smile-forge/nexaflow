using System.Runtime.InteropServices;

namespace Nexaflow.PrivilegeBridge.Operations;

/// <summary>
/// Broadcasts WM_SETTINGCHANGE("Environment") so already-running processes (Explorer, shells launched
/// afterwards) pick up the changed machine environment without a reboot. Best-effort.
/// </summary>
internal static class EnvironmentBroadcast
{
    private static readonly IntPtr HWND_BROADCAST = new(0xffff);
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
        uint flags, uint timeoutMs, out IntPtr result);

    public static void Notify()
    {
        try { SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 5000, out _); }
        catch { /* best-effort; the value is already written */ }
    }
}
