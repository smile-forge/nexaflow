using Nexaflow.Features.Hex.ViewModels;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Features.Hex.Controls;

/// <summary>
/// Renders the address + hex-bytes columns. All layout is fixed-width monospace and falls out of the
/// cell metrics <see cref="HexPanelBase"/> measures; only visible rows are drawn.
/// </summary>
public sealed class HexRenderPanel : HexPanelBase
{
    // Column x-positions, all derived from the cell width (see OnMetricsChanged)
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

    // ── Metrics & brushes ─────────────────────────────────────────────────────

    /// <summary>Layout: "00000000  " = 10 chars of address, then three columns per byte ("BB ").</summary>
    protected override void OnMetricsChanged()
    {
        _addrX = 0;
        _hexX  = CharW * 10;             // address + 2 spaces
        _byteW = CharW * 3;              // "BB " per byte
    }

    private void EnsureBrushes()
    {
        _bgBrush     ??= Res("BgBrush");
        _surfBrush   ??= Res("SurfaceBrush");
        _textBrush   ??= Res("TextBrush");
        _mutedBrush  ??= Res("TextMutedBrush");
        _accentBrush ??= Res("AccentBrush");
        _selBrush    ??= SemiAccent(_accentBrush!);
        _editBrush   ??= Res("WarningBrush");
        _nullBrush   ??= Res("TextMutedBrush");
        _borderBrush ??= Res("BorderBrush");
        _cursorStroke ??= _accentBrush;
    }

    // ── Render ────────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        if (Vm == null) return;
        EnsureMetrics();
        EnsureBrushes();

        int rows    = VisibleRowCount;
        if (Vm.VisibleRowCount != rows) Vm.VisibleRowCount = rows;

        dc.DrawRectangle(_bgBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        long topRow  = Vm.TopRow;
        long total   = Vm.TotalRows;
        long vLen    = Vm.Buffer.VirtualLength;
        long selStart = Vm.SelectionStart;
        long selLen   = Vm.SelectionLength;
        long cursor   = Vm.CursorOffset;

        // Right-edge divider
        double divX = _hexX + _byteW * 16 + CharW;
        dc.DrawLine(new Pen(_borderBrush, 1), new Point(divX, 0), new Point(divX, ActualHeight));

        for (int r = 0; r < rows; r++)
        {
            long rowIdx    = topRow + r;
            if (rowIdx >= total) break;
            long rowOffset = rowIdx * 16;
            int  count     = (int)Math.Min(16, vLen - rowOffset);
            if (count <= 0) break;

            var bytes = Vm.Buffer.ReadRange(rowOffset, count);
            double y  = r * RowH;

            // ── address ──────────────────────────────────────────────────────
            dc.DrawText(MakeText(rowOffset.ToString("X8"), _mutedBrush!), new Point(_addrX + CharW, y + 2));

            // ── hex bytes ────────────────────────────────────────────────────
            for (int col = 0; col < count; col++)
            {
                long byteOff = rowOffset + col;
                double x = ByteX(col);

                // selection / cursor background
                bool inSel = selLen > 0 && byteOff >= selStart && byteOff < selStart + selLen;
                bool isCur = byteOff == cursor;

                if (inSel)
                    dc.DrawRectangle(_selBrush, null, new Rect(x - 1, y, _byteW - 1, RowH));
                else if (isCur)
                    dc.DrawRectangle(null, new Pen(_cursorStroke!, 1.5),
                        new Rect(x - 1, y + 1, _byteW - 2, RowH - 2));

                byte b     = bytes[col];
                bool edited = Vm.Buffer.IsEdited(byteOff);
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
        double gap = col >= 8 ? CharW : 0; // extra space in the middle
        return _hexX + col * _byteW + gap;
    }

    // ── Mouse ─────────────────────────────────────────────────────────────────

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (Vm == null) return;
        Focus();
        var pt   = e.GetPosition(this);
        long off = HitTestOffset(pt);
        if (off < 0) return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            Vm.ExtendSelection(off);
        else
            Vm.SetCursor(off);

        e.Handled = true;
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (Vm == null || e.LeftButton != MouseButtonState.Pressed || !IsMouseCaptured) return;
        var pt   = e.GetPosition(this);
        long off = HitTestOffset(pt);
        if (off >= 0) Vm.ExtendSelection(off);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private long HitTestOffset(Point pt)
    {
        if (Vm == null) return -1;
        EnsureMetrics();
        int row = (int)(pt.Y / RowH);
        double relX = pt.X - _hexX;
        if (relX < 0) return (Vm.TopRow + row) * 16; // click in address area → row start

        // Undo the mid-gap shift for cols 8–15
        double halfW = _byteW * 8;
        int col;
        if (relX > halfW + CharW) // after the gap
            col = 8 + (int)((relX - halfW - CharW) / _byteW);
        else
            col = (int)(relX / _byteW);

        col = Math.Clamp(col, 0, 15);
        long off = (Vm.TopRow + row) * 16 + col;
        return Math.Clamp(off, 0, Math.Max(0, Vm.Buffer.VirtualLength - 1));
    }

    // ── Keyboard ──────────────────────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (Vm == null) return;
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        long cur   = Math.Max(0, Vm.CursorOffset);

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
                Navigate(shift, cur + Vm.VisibleRowCount * 16); e.Handled = true; break;
            case Key.PageUp:
                Navigate(shift, cur - Vm.VisibleRowCount * 16); e.Handled = true; break;
            case Key.Home:
                Navigate(shift, (cur / 16) * 16); e.Handled = true; break;
            case Key.End:
                Navigate(shift, (cur / 16) * 16 + 15); e.Handled = true; break;
            case Key.Delete when Vm.EditMode != HexEditMode.ReadOnly:
                _pendingNibble = -1;
                Vm.DeleteByte(cur);
                e.Handled = true; break;
            case Key.Back when Vm.EditMode != HexEditMode.ReadOnly && cur > 0:
                _pendingNibble = -1;
                Vm.DeleteByte(cur - 1);
                Vm.SetCursor(cur - 1);
                e.Handled = true; break;
        }
    }

    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        if (Vm == null || Vm.EditMode == HexEditMode.ReadOnly) return;
        string txt = e.Text.ToUpperInvariant();
        if (txt.Length != 1 || !IsHexChar(txt[0])) return;

        int digit = HexVal(txt[0]);
        long cur  = Math.Max(0, Vm.CursorOffset);

        if (_pendingNibble < 0)
        {
            _pendingNibble = digit;
        }
        else
        {
            byte b = (byte)((_pendingNibble << 4) | digit);
            _pendingNibble = -1;
            Vm.WriteByte(cur, b);
            Vm.SetCursor(Math.Min(cur + 1, Vm.Buffer.VirtualLength - 1));
            Vm.EnsureCursorVisible();
        }
        InvalidateVisual();
        e.Handled = true;
    }

    private void Navigate(bool extend, long target)
    {
        _pendingNibble = -1;
        target = Math.Clamp(target, 0, Math.Max(0, Vm!.Buffer.VirtualLength - 1));
        if (extend) Vm.ExtendSelection(target);
        else        Vm.SetCursor(target);
        Vm.EnsureCursorVisible();
    }

    private static bool IsHexChar(char c) => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F');
    private static int  HexVal(char c)    => c >= 'A' ? c - 'A' + 10 : c - '0';

    // ── Scroll wheel ──────────────────────────────────────────────────────────

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Vm == null) return;
        int delta = e.Delta > 0 ? -3 : 3;
        Vm.ScrollToRow(Vm.TopRow + delta);
        e.Handled = true;
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureMetrics();
        double minW = _hexX + _byteW * 16 + CharW * 2;
        return new Size(double.IsInfinity(availableSize.Width)  ? minW : availableSize.Width,
                        double.IsInfinity(availableSize.Height) ? RowH * 10 : availableSize.Height);
    }
}
