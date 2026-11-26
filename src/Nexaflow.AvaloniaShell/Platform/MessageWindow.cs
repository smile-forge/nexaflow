using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Nexaflow.AvaloniaShell.Platform;

/// <summary>
/// A message-only Win32 window made on the UI thread, which Avalonia's message loop pumps as it pumps its own windows: how the shell
/// hears what Windows tells a window — the clipboard changing, a watched folder changing. Nexaflow.Core makes the same with WPF's
/// <c>HwndSource</c>; nothing in it is either toolkit's.
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private const string ClassName = "Nexaflow.AvaloniaShell.MessageWindow";
    private static readonly IntPtr HwndMessage = new(-3);

    /// <summary>Held for the process's life: Windows calls it through the pointer the class was registered with.</summary>
    private static readonly WindowProcedure Procedure = Dispatch;
    private static readonly Dictionary<IntPtr, MessageWindow> Live = [];
    private static bool _registered;

    private readonly Func<int, IntPtr, IntPtr, bool> _handle;

    /// <summary>The window's handle, which a listener or a registration is made against.</summary>
    public IntPtr Handle { get; private set; }

    /// <param name="name">The window's name, for anything that lists windows.</param>
    /// <param name="handle">Handles a message sent to the window, saying whether it did.</param>
    public MessageWindow(string name, Func<int, IntPtr, IntPtr, bool> handle)
    {
        _handle = handle;
        Register();
        Handle = CreateWindowExW(0, ClassName, name, 0, 0, 0, 0, 0, HwndMessage, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        if (Handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        Live[Handle] = this;
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        Live.Remove(Handle);
        DestroyWindow(Handle);
        Handle = IntPtr.Zero;
    }

    private static void Register()
    {
        if (_registered) return;
        var windowClass = new WindowClass
        {
            Size       = (uint)Marshal.SizeOf<WindowClass>(),
            Procedure  = Marshal.GetFunctionPointerForDelegate(Procedure),
            Instance   = GetModuleHandleW(null),
            ClassName  = ClassName,
        };
        if (RegisterClassExW(ref windowClass) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        _registered = true;
    }

    private static IntPtr Dispatch(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
        => Live.TryGetValue(window, out var live) && live._handle((int)message, wParam, lParam)
            ? IntPtr.Zero
            : DefWindowProcW(window, message, wParam, lParam);

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size;
        public uint Style;
        public IntPtr Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WindowClass windowClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style, int x, int y, int width, int height,
                                                 IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? module);
}
