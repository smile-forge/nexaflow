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

    // The max button's bounds in screen (device) pixels.
    //
    // WM_NCHITTEST is NOT a rare message: every render pass re-invalidates hit-testing, which makes
    // WPF synchronise the mouse, which sends another hit-test - so an idle window with a tab open
    // takes tens of these per second forever. Resolving the button's rect from the visual tree on
    // each one (two PointToScreen calls, each walking the ancestry into the compositor) made this
    // hook one of the larger measurable idle costs. Cache the rect instead and recompute it only
    // after something that can actually move the button.
    private Rect _maxRect;
    private bool _maxRectValid;

    internal SnapLayoutHook(Button maxButton, Action toggleMaxRestore)
    {
        _maxButton        = maxButton;
        _toggleMaxRestore = toggleMaxRestore;
    }

    internal void Install(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);

        // Everything that can move the button relative to the screen. LayoutUpdated covers the
        // button being re-positioned by chrome reflow without the window itself changing; it is
        // free while idle (no layout passes run) and only ever costs one recompute on the next
        // hit-test, so it is never worse than resolving the rect every time.
        window.LocationChanged   += (_, _) => _maxRectValid = false;
        window.SizeChanged       += (_, _) => _maxRectValid = false;
        window.StateChanged      += (_, _) => _maxRectValid = false;
        window.DpiChanged        += (_, _) => _maxRectValid = false;
        _maxButton.LayoutUpdated += (_, _) => _maxRectValid = false;
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
        if (!_maxRectValid)
        {
            // Not rooted in a presentation source yet (or collapsed): PointToScreen would throw,
            // and there is nothing to be over. Leave the cache invalid so the next message retries.
            if (!_maxButton.IsVisible || _maxButton.ActualWidth <= 0 || _maxButton.ActualHeight <= 0)
                return false;

            _maxRect = new Rect(
                _maxButton.PointToScreen(new Point(0, 0)),
                _maxButton.PointToScreen(new Point(_maxButton.ActualWidth, _maxButton.ActualHeight)));
            _maxRectValid = true;
        }

        long lp = lParam.ToInt64();
        return _maxRect.Contains(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
    }

    private void SetMaxHover(bool on)
    {
        if (_maxHovered == on) return;
        _maxHovered = on;
        _maxButton.Background = on ? MaxHoverBrush : Brushes.Transparent;
        _maxButton.Foreground = (Brush)_maxButton.FindResource(on ? "TextBrush" : "TextMutedBrush");
    }
}
