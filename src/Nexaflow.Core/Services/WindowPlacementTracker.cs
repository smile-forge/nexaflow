using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Nexaflow.Core.Models;

namespace Nexaflow.Core.Services;

/// <summary>
/// Keeps one window's <see cref="PlacementIntent"/> current from the window's messages, and does what it decides:
/// opens the window at a saved placement, and puts it back on its monitor once the displays return it. Installed
/// from the window's <c>OnSourceInitialized</c> — the handle exists, the window is not yet shown.
/// </summary>
internal sealed class WindowPlacementTracker
{
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const int WM_DISPLAYCHANGE    = 0x007E;
    private const int WM_SYSCOMMAND       = 0x0112;
    private const int WM_POWERBROADCAST   = 0x0218;
    private const int WM_ENTERSIZEMOVE    = 0x0231;
    private const int WM_EXITSIZEMOVE     = 0x0232;

    private const int SC_MINIMIZE = 0xF020;
    private const int SC_MAXIMIZE = 0xF030;
    private const int SC_RESTORE  = 0xF120;

    private const int PBT_APMSUSPEND          = 0x0004;
    private const int PBT_APMRESUMESUSPEND    = 0x0007;
    private const int PBT_APMRESUMEAUTOMATIC  = 0x0012;
    private const int PBT_POWERSETTINGCHANGE  = 0x8013;
    private const uint DisplayOff             = 0;
    private const uint DisplayOn              = 1;
    private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0;

    private const int SW_SHOWNOACTIVATE  = 4;
    private const int SW_SHOWMINNOACTIVE = 7;

    private static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new("6FE69556-704A-47A0-8F24-C28D936FDA47");

    private readonly PlacementIntent _intent = new();
    private WindowPlacement? _openAt;
    private Dispatcher? _dispatcher;
    private IntPtr _hwnd;
    private IntPtr _displayStateNotification;

    private bool _inSizeMove;                  // a drag or resize: read once, when it ends
    private WindowPlacement? _sizeMoveStart;
    private bool _commandRunning;             // a min / max / restore command, until the dispatcher is idle again
    private bool _applying;                    // our own SetWindowPlacement
    private bool _settleQueued;

    /// <summary>Where the user means the window to be; null until it has been shown or opened at a placement.</summary>
    internal WindowPlacement? Intended => _intent.Intended;

    /// <summary>Opens the window at <paramref name="placement"/> when it is first shown. Call before <c>Show</c>.</summary>
    internal void OpenAt(WindowPlacement placement) => _openAt = placement;

    internal void Install(Window window)
    {
        _dispatcher = window.Dispatcher;
        _hwnd       = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        // Display off/on reaches only windows that ask for it. Registering delivers the current state at once,
        // which settles nothing that is not in transition.
        var displayState = GUID_CONSOLE_DISPLAY_STATE;
        _displayStateNotification = RegisterPowerSettingNotification(_hwnd, ref displayState, DEVICE_NOTIFY_WINDOW_HANDLE);
        window.Closed += (_, _) =>
        {
            if (_displayStateNotification != IntPtr.Zero) UnregisterPowerSettingNotification(_displayStateNotification);
            _displayStateNotification = IntPtr.Zero;
            _hwnd = IntPtr.Zero;
        };

        if (_openAt is { } placement)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            Apply(_intent.Open(placement, AttachedMonitors.Read()), opening: true);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_ENTERSIZEMOVE:
                _inSizeMove    = true;
                _sizeMoveStart = ReadLive(AttachedMonitors.Read());
                break;

            case WM_EXITSIZEMOVE:
                ObserveSizeMoveEnd();
                break;

            // The caption buttons, a caption double-click and the system menu all arrive as a command; what it
            // moves happens before the dispatcher is next idle.
            case WM_SYSCOMMAND when (wParam.ToInt64() & 0xFFF0) is SC_MINIMIZE or SC_MAXIMIZE or SC_RESTORE:
                if (!_commandRunning)
                {
                    _commandRunning = true;
                    _dispatcher!.InvokeAsync(() => _commandRunning = false, DispatcherPriority.Background);
                }
                break;

            case WM_WINDOWPOSCHANGED when !_inSizeMove && !_applying:
                Observe(byUser: _commandRunning);
                break;

            case WM_DISPLAYCHANGE:
                AttachedMonitors.Forget();
                _intent.TopologyChanging();
                QueueSettle();
                break;

            case WM_POWERBROADCAST:
                OnPowerBroadcast(wParam.ToInt32(), lParam);
                break;
        }
        return IntPtr.Zero;
    }

    private void OnPowerBroadcast(int kind, IntPtr lParam)
    {
        switch (kind)
        {
            case PBT_APMSUSPEND:
                _intent.TopologyChanging();
                break;

            case PBT_APMRESUMEAUTOMATIC:
            case PBT_APMRESUMESUSPEND:
                AttachedMonitors.Forget();
                QueueSettle();
                break;

            case PBT_POWERSETTINGCHANGE:
                var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(lParam);
                if (setting.PowerSetting != GUID_CONSOLE_DISPLAY_STATE) break;
                if (setting.Data == DisplayOff) _intent.TopologyChanging();
                else if (setting.Data == DisplayOn) QueueSettle();
                break;
        }
    }

    // A press on the caption enters the move loop without moving anything; only a drag or resize that changed
    // the placement is the user placing the window.
    private void ObserveSizeMoveEnd()
    {
        var start = _sizeMoveStart;
        _inSizeMove    = false;
        _sizeMoveStart = null;

        var attached = AttachedMonitors.Read();
        if (ReadLive(attached) is { } live)
            _intent.Observed(live, attached, byUser: start is null || !live.SamePlaceAs(start));
    }

    private void Observe(bool byUser)
    {
        var attached = AttachedMonitors.Read();
        if (ReadLive(attached) is { } live) _intent.Observed(live, attached, byUser);
    }

    // The monitors are read once the burst of display messages has been handled, not on each one.
    private void QueueSettle()
    {
        if (_settleQueued || _dispatcher is null) return;
        _settleQueued = true;
        _dispatcher.InvokeAsync(Settle, DispatcherPriority.Background);
    }

    private void Settle()
    {
        _settleQueued = false;
        if (_hwnd == IntPtr.Zero) return;

        var attached = AttachedMonitors.Read();
        if (_intent.TopologySettled(attached, ReadLive(attached)) is { } back)
            Apply(back, opening: false);
    }

    private WindowPlacement? ReadLive(IReadOnlyList<MonitorArea> attached)
    {
        if (_hwnd == IntPtr.Zero) return null;

        var native = new WINDOWPLACEMENT { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (!GetWindowPlacement(_hwnd, ref native)) return null;
        if (AttachedMonitors.Of(_hwnd, attached) is not { } monitor) return null;

        PixelRect? shown = GetWindowRect(_hwnd, out var rect) ? AttachedMonitors.ToPixelRect(rect) : null;
        return WindowPlacement.FromNative(native.showCmd, native.flags,
                                          AttachedMonitors.ToPixelRect(native.rcNormalPosition), shown, monitor);
    }

    // Opening shows the window as it will be shown anyway. Putting it back after a display change never activates
    // it, and leaves a minimised window minimised with its restore placement corrected.
    private void Apply(WindowPlacement placement, bool opening)
    {
        var native = new WINDOWPLACEMENT
        {
            length           = Marshal.SizeOf<WINDOWPLACEMENT>(),
            ptMinPosition    = new WindowManager.POINT { X = -1, Y = -1 },
            ptMaxPosition    = new WindowManager.POINT { X = -1, Y = -1 },
            rcNormalPosition = new WindowManager.RECT
            {
                Left = placement.Normal.Left, Top = placement.Normal.Top,
                Right = placement.Normal.Right, Bottom = placement.Normal.Bottom,
            },
        };

        if (!opening && IsIconic(_hwnd))
        {
            native.showCmd = SW_SHOWMINNOACTIVE;
            native.flags   = placement.Maximized ? WindowPlacement.WPF_RESTORETOMAXIMIZED : 0;
        }
        else if (placement.Maximized)
            native.showCmd = WindowPlacement.SW_SHOWMAXIMIZED;
        else
            native.showCmd = opening ? WindowPlacement.SW_SHOWNORMAL : SW_SHOWNOACTIVATE;

        _applying = true;
        try { SetWindowPlacement(_hwnd, ref native); }
        finally { _applying = false; }
    }

    // ── P/Invoke ──────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int                 length;
        public int                 flags;
        public int                 showCmd;
        public WindowManager.POINT ptMinPosition;
        public WindowManager.POINT ptMaxPosition;
        public WindowManager.RECT  rcNormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public uint DataLength;
        public uint Data;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPlacement(IntPtr hwnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowManager.RECT rect);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr RegisterPowerSettingNotification(IntPtr recipient, ref Guid powerSetting, int flags);

    [DllImport("user32.dll")]
    private static extern bool UnregisterPowerSettingNotification(IntPtr handle);
}
