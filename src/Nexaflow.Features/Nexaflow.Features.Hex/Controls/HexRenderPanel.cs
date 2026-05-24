using Nexaflow.Features.Hex.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Features.Hex.Controls;

/// <summary>
/// Custom FrameworkElement that renders the address + hex-bytes columns.
/// All layout is fixed-width monospace; only visible rows are drawn.
/// </summary>
public sealed class HexRenderPanel : FrameworkElement
{
    private HexViewModel? _vm;

    // ── Metrics (computed once after entering visual tree) ────────────────────
    private bool     _metricsReady;
    private double   _charW;
    private double   _rowH;
    private double   _dpi;
    private Typeface _face = new("Cascadia Code, Consolas, Courier New");
    private const double FontSize = 12.0;

    // Column x-positions (set in ComputeMetrics)
    private double _addrX;   // start of address column
    private double _hexX;    // start of first hex byte
    private double _byteW;   // per-byte width including trailing space

    // Cached brushes (looked up once from resources)
    private Brush? _bgBrush, _surfBrush, _textBrush, _mutedBrush,
                   _accentBrush, _selBrush, _editBrush, _nullBrush,
                   _borderBrush, _cursorStroke;

    // Pending nibble for hex input (first of two hex digits typed)
    private int _pendingNibble = -1; // -1 = no pending nibble

    public HexRenderPanel()
    {
        Focusable   = true;
        Cursor      = Cursors.IBeam;
        ClipToBounds = true;
    }

    // ── ViewModel wiring ──────────────────────────────────────────────────────

    public void Attach(HexViewModel vm)
    {
        if (_vm != null) _vm.InvalidateView -= OnInvalidate;
        _vm = vm;
        _vm.InvalidateView += OnInvalidate;
        InvalidateVisual();
    }

    private void OnInvalidate() => InvalidateVisual();

    // ── Metrics & brushes ─────────────────────────────────────────────────────

    private void EnsureMetrics()
    {
        if (_metricsReady) return;
        _dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var ft = MakeText("M", Brushes.White);
        _charW = ft.Width;
        _rowH  = Math.Max(ft.Height + 4, 18);

        // Layout: "00000000  " = 10 chars address, then bytes
        _addrX = 0;
        _hexX  = _charW * 10;            // address + 2 spaces
        _byteW = _charW * 3;             // "BB " per byte
        _metricsReady = true;
    }

    private void EnsureBrushes()
    {
        _bgBrush     ??= Res("BgBrush")       ?? Brushes.Black;
        _surfBrush   ??= Res("SurfaceBrush")  ?? Brushes.DimGray;
        _textBrush   ??= Res("TextBrush")     ?? Brushes.White;
        _mutedBrush  ??= Res("TextMutedBrush")?? Brushes.Gray;
        _accentBrush ??= Res("AccentBrush")   ?? Brushes.DodgerBlue;
        _selBrush    ??= MakeSemiAccent();
        _editBrush   ??= Brushes.Orange;
        _nullBrush   ??= Res("TextMutedBrush")?? Brushes.DarkGray;
        _borderBrush ??= Res("BorderBrush")   ?? Brushes.DimGray;
        _cursorStroke ??= _accentBrush;
    }

    private Brush? Res(string key) => TryFindResource(key) as Brush;
    private Brush MakeSemiAccent()
    {
        var c = (_accentBrush as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        var b = new SolidColorBrush(Color.FromArgb(60, c.R, c.G, c.B));
        b.Freeze(); return b;
    }

    // ── Row count exposed to ViewModel ────────────────────────────────────────

    public int VisibleRowCount
    {
        get
        {
            EnsureMetrics();
            return ActualHeight > 0 ? Math.Max(1, (int)(ActualHeight / _rowH)) : 1;
        }
    }

    // ── Render ────────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        if (_vm == null) return;
        EnsureMetrics();
        EnsureBrushes();

        int rows    = VisibleRowCount;
        if (_vm.VisibleRowCount != rows) _vm.VisibleRowCount = rows;

        dc.DrawRectangle(_bgBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        long topRow  = _vm.TopRow;
        long total   = _vm.TotalRows;
        long vLen    = _vm.Buffer.VirtualLength;
        long selStart = _vm.SelectionStart;
        long selLen   = _vm.SelectionLength;
        long cursor   = _vm.CursorOffset;

        // Right-edge divider
        double divX = _hexX + _byteW * 16 + _charW;
        dc.DrawLine(new Pen(_borderBrush, 1), new Point(divX, 0), new Point(divX, ActualHeight));

        for (int r = 0; r < rows; r++)
        {
            long rowIdx    = topRow + r;
            if (rowIdx >= total) break;
            long rowOffset = rowIdx * 16;
            int  count     = (int)Math.Min(16, vLen - rowOffset);
            if (count <= 0) break;

            var bytes = _vm.Buffer.ReadRange(rowOffset, count);
            double y  = r * _rowH;

            // ── address ──────────────────────────────────────────────────────
            dc.DrawText(MakeText(rowOffset.ToString("X8"), _mutedBrush!), new Point(_addrX + _charW, y + 2));

            // ── hex bytes ────────────────────────────────────────────────────
            for (int col = 0; col < count; col++)
            {
                long byteOff = rowOffset + col;
                double x = ByteX(col);

                // selection / cursor background
                bool inSel = selLen > 0 && byteOff >= selStart && byteOff < selStart + selLen;
                bool isCur = byteOff == cursor;

                if (inSel)
                    dc.DrawRectangle(_selBrush, null, new Rect(x - 1, y, _byteW - 1, _rowH));
                else if (isCur)
                    dc.DrawRectangle(null, new Pen(_cursorStroke!, 1.5),
                        new Rect(x - 1, y + 1, _byteW - 2, _rowH - 2));

                byte b     = bytes[col];
                bool edited = _vm.Buffer.IsEdited(byteOff);
                var  brush  = edited ? _editBrush! : (b == 0 ? _nullBrush! : _textBrush!);

                string hex = (_pendingNibble >= 0 && isCur)
                    ? _pendingNibble.ToString("X") + "_"
                    : b.ToString("X2");

                dc.DrawText(MakeText(hex, brush), new Point(x, y + 2));
            }
        }
    }

    private double ByteX(int col)
    {
        double gap = col >= 8 ? _charW : 0; // extra space in the middle
        return _hexX + col * _byteW + gap;
    }

    private FormattedText MakeText(string s, Brush brush)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
               _face, FontSize, brush, _dpi);

    // ── Mouse ─────────────────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (_vm == null) return;
        Focus();
        var pt   = e.GetPosition(this);
        long off = HitTestOffset(pt);
        if (off < 0) return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            _vm.ExtendSelection(off);
        else
            _vm.SetCursor(off);

        e.Handled = true;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_vm == null || e.LeftButton != MouseButtonState.Pressed || !IsMouseCaptured) return;
        var pt   = e.GetPosition(this);
        long off = HitTestOffset(pt);
        if (off >= 0) _vm.ExtendSelection(off);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private long HitTestOffset(Point pt)
    {
        if (_vm == null) return -1;
        EnsureMetrics();
        int row = (int)(pt.Y / _rowH);
        double relX = pt.X - _hexX;
        if (relX < 0) return (_vm.TopRow + row) * 16; // click in address area → row start

        // Undo the mid-gap shift for cols 8–15
        double halfW = _byteW * 8;
        int col;
        if (relX > halfW + _charW) // after the gap
            col = 8 + (int)((relX - halfW - _charW) / _byteW);
        else
            col = (int)(relX / _byteW);

        col = Math.Clamp(col, 0, 15);
        long off = (_vm.TopRow + row) * 16 + col;
        return Math.Clamp(off, 0, Math.Max(0, _vm.Buffer.VirtualLength - 1));
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_vm == null) return;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        long cur   = Math.Max(0, _vm.CursorOffset);

        switch (e.Key)
        {
            case Key.Right:
                Navigate(shift, cur + 1); e.Handled = true; break;
            case Key.Left:
                Navigate(shift, cur - 1); e.Handled = true; break;
            case Key.Down:
                Navigate(shift, cur + 16); e.Handled = true; break;
            case Key.Up:
                Navigate(shift, cur - 16); e.Handled = true; break;
            case Key.PageDown:
                Navigate(shift, cur + _vm.VisibleRowCount * 16); e.Handled = true; break;
            case Key.PageUp:
                Navigate(shift, cur - _vm.VisibleRowCount * 16); e.Handled = true; break;
            case Key.Home:
                Navigate(shift, (cur / 16) * 16); e.Handled = true; break;
            case Key.End:
                Navigate(shift, (cur / 16) * 16 + 15); e.Handled = true; break;
            case Key.Delete when _vm.EditMode != HexEditMode.ReadOnly:
                _pendingNibble = -1;
                _vm.DeleteByte(cur);
                e.Handled = true; break;
            case Key.Back when _vm.EditMode != HexEditMode.ReadOnly && cur > 0:
                _pendingNibble = -1;
                _vm.DeleteByte(cur - 1);
                _vm.SetCursor(cur - 1);
                e.Handled = true; break;
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (_vm == null || _vm.EditMode == HexEditMode.ReadOnly) return;
        string txt = e.Text.ToUpperInvariant();
        if (txt.Length != 1 || !IsHexChar(txt[0])) return;

        int digit = HexVal(txt[0]);
        long cur  = Math.Max(0, _vm.CursorOffset);

        if (_pendingNibble < 0)
        {
            _pendingNibble = digit;
        }
        else
        {
            byte b = (byte)((_pendingNibble << 4) | digit);
            _pendingNibble = -1;
            _vm.WriteByte(cur, b);
            _vm.SetCursor(Math.Min(cur + 1, _vm.Buffer.VirtualLength - 1));
            _vm.EnsureCursorVisible();
        }
        InvalidateVisual();
        e.Handled = true;
    }

    private void Navigate(bool extend, long target)
    {
        _pendingNibble = -1;
        target = Math.Clamp(target, 0, Math.Max(0, _vm!.Buffer.VirtualLength - 1));
        if (extend) _vm.ExtendSelection(target);
        else        _vm.SetCursor(target);
        _vm.EnsureCursorVisible();
    }

    private static bool IsHexChar(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
    private static int  HexVal(char c)    => c >= 'A' ? c - 'A' + 10 : c - '0';

    // ── Scroll wheel ──────────────────────────────────────────────────────────

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (_vm == null) return;
        int delta = e.Delta > 0 ? -3 : 3;
        _vm.ScrollToRow(_vm.TopRow + delta);
        e.Handled = true;
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureMetrics();
        double minW = _hexX + _byteW * 16 + _charW * 2;
        return new Size(double.IsInfinity(availableSize.Width)  ? minW : availableSize.Width,
                        double.IsInfinity(availableSize.Height) ? _rowH * 10 : availableSize.Height);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (_vm != null)
        {
            int newCount = VisibleRowCount;
            if (_vm.VisibleRowCount != newCount)
                _vm.VisibleRowCount = newCount;
        }
    }
}
