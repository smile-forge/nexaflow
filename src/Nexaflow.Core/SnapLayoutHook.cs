using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Nexaflow.Core;

// Routes WM_NCHITTEST over the maximize button to HTMAXBUTTON so Windows
// shows the Snap Layouts flyout on hover, then wires hover visuals and click.
internal sealed class SnapLayoutHook
{
    private const int WM_NCHITTEST     = 0x0084;
    private const int WM_NCMOUSELEAVE  = 0x02A2;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_NCLBUTTONUP   = 0x00A2;
    private const int HTMAXBUTTON      = 9;

    private static readonly Brush MaxHoverBrush =
        new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF));

    private readonly Button _maxButton;
    private readonly Action _toggleMaxRestore;
    private bool _maxHovered;

    internal SnapLayoutHook(Button maxButton, Action toggleMaxRestore)
    {
        _maxButton        = maxButton;
        _toggleMaxRestore = toggleMaxRestore;
    }

    internal void Install(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                if (IsOverMaxButton(lParam))
                {
                    SetMaxHover(true);
                    handled = true;
                    return (IntPtr)HTMAXBUTTON;
                }
                SetMaxHover(false);
                break;

            case WM_NCMOUSELEAVE:
                SetMaxHover(false);
                break;

            case WM_NCLBUTTONDOWN:
                if (wParam.ToInt32() == HTMAXBUTTON) handled = true;
                break;

            case WM_NCLBUTTONUP:
                if (wParam.ToInt32() == HTMAXBUTTON)
                {
                    handled = true;
                    _toggleMaxRestore();
                }
                break;
        }
        return IntPtr.Zero;
    }

    private bool IsOverMaxButton(IntPtr lParam)
    {
        long lp     = lParam.ToInt64();
        var  screen = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
        var  topLeft    = _maxButton.PointToScreen(new Point(0, 0));
        var  bottomRight = _maxButton.PointToScreen(
            new Point(_maxButton.ActualWidth, _maxButton.ActualHeight));
        return new Rect(topLeft, bottomRight).Contains(screen);
    }

    private void SetMaxHover(bool on)
    {
        if (_maxHovered == on) return;
        _maxHovered = on;
        _maxButton.Background = on ? MaxHoverBrush : Brushes.Transparent;
        _maxButton.Foreground = (Brush)_maxButton.FindResource(on ? "TextBrush" : "TextMutedBrush");
    }
}
