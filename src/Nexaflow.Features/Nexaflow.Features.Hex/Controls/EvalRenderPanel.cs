using Nexaflow.Features.Hex.ViewModels;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Features.Hex.Controls;

/// <summary>
/// Custom FrameworkElement that renders the text-interpretation (evaluate) pane.
/// Each row shows 16 bytes decoded with the active encoding; invalid sequences → '·'.
/// </summary>
public sealed class EvalRenderPanel : FrameworkElement
{
    private HexViewModel? _vm;

    private bool     _metricsReady;
    private double   _charW;
    private double   _rowH;
    private double   _dpi;
    private Typeface _face = new("Cascadia Code, Consolas, Courier New");
    private const double FontSize = 12.0;

    private Brush? _bgBrush, _textBrush, _mutedBrush, _accentBrush,
                   _selBrush, _borderBrush, _cursorStroke;

    public EvalRenderPanel()
    {
        Focusable    = true;
        Cursor       = Cursors.IBeam;
        ClipToBounds = true;
    }

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
        _metricsReady = true;
    }

    private void EnsureBrushes()
    {
        _bgBrush     ??= Res("BgBrush");
        _textBrush   ??= Res("TextBrush");
        _mutedBrush  ??= Res("TextMutedBrush");
        _accentBrush ??= Res("AccentBrush");
        _selBrush    ??= MakeSemiAccent();
        _borderBrush ??= Res("BorderBrush");
        _cursorStroke ??= _accentBrush;
    }

    // FindResource throws (naming the key) if a theme resource is missing — no silent literal fallback.
    private Brush Res(string key) => (Brush)FindResource(key);
    private Brush MakeSemiAccent()
    {
        var c = ((SolidColorBrush)_accentBrush!).Color;
        var b = new SolidColorBrush(Color.FromArgb(60, c.R, c.G, c.B));
        b.Freeze(); return b;
    }

    // ── Row count (keep in sync with HexRenderPanel) ──────────────────────────

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

        int rows = VisibleRowCount;
        dc.DrawRectangle(_bgBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        long topRow   = _vm.TopRow;
        long total    = _vm.TotalRows;
        long vLen     = _vm.Buffer.VirtualLength;
        long selStart = _vm.SelectionStart;
        long selLen   = _vm.SelectionLength;
        long cursor   = _vm.CursorOffset;
        var  enc      = _vm.ResolvedEncoding;

        for (int r = 0; r < rows; r++)
        {
            long rowIdx    = topRow + r;
            if (rowIdx >= total) break;
            long rowOffset = rowIdx * 16;
            int  count     = (int)Math.Min(16, vLen - rowOffset);
            if (count <= 0) break;

            var bytes = _vm.Buffer.ReadRange(rowOffset, count);
            double y  = r * _rowH;

            // Decode row into printable characters
            char[] chars = Decode(bytes, count, enc);

            // Draw per-character with selection highlights
            for (int col = 0; col < chars.Length; col++)
            {
                long byteOff = rowOffset + col;
                double x = col * _charW + 4;

                bool inSel = selLen > 0 && byteOff >= selStart && byteOff < selStart + selLen;
                bool isCur = byteOff == cursor;

                if (inSel)
                    dc.DrawRectangle(_selBrush, null, new Rect(x - 1, y, _charW + 1, _rowH));
                else if (isCur)
                    dc.DrawRectangle(null, new Pen(_cursorStroke!, 1.5),
                        new Rect(x - 1, y + 1, _charW, _rowH - 2));

                bool isPrint = chars[col] != '·';
                dc.DrawText(MakeText(chars[col].ToString(), isPrint ? _textBrush! : _mutedBrush!),
                    new Point(x, y + 2));
            }
        }
    }

    // ── Decode bytes for the eval pane ────────────────────────────────────────

    private static char[] Decode(byte[] bytes, int count, HexEncoding enc)
    {
        var result = new char[count]; // at most count printable chars (may be fewer for multi-byte)

        switch (enc)
        {
            case HexEncoding.Utf8:
                return DecodeUtf8(bytes, count);

            case HexEncoding.Utf16LE:
            case HexEncoding.Utf16BE:
                return DecodeUtf16(bytes, count, enc == HexEncoding.Utf16BE);

            default: // ASCII
                for (int i = 0; i < count; i++)
                    result[i] = bytes[i] is >= 0x20 and < 0x7F ? (char)bytes[i] : '·';
                return result;
        }
    }

    private static char[] DecodeUtf8(byte[] bytes, int count)
    {
        var result = new char[count];
        int i = 0, j = 0;
        while (i < count)
        {
            byte b = bytes[i];
            if (b < 0x80) { result[j++] = b >= 0x20 ? (char)b : '·'; i++; continue; }

            int seqLen = b >= 0xF0 ? 4 : b >= 0xE0 ? 3 : b >= 0xC0 ? 2 : 0;
            if (seqLen == 0 || i + seqLen > count) { result[j++] = '·'; i++; continue; }

            // Validate continuation bytes
            bool valid = true;
            for (int k = 1; k < seqLen; k++)
                if ((bytes[i + k] & 0xC0) != 0x80) { valid = false; break; }

            if (!valid) { result[j++] = '·'; i++; continue; }

            try
            {
                string s = Encoding.UTF8.GetString(bytes, i, seqLen);
                result[j++] = s.Length > 0 ? s[0] : '·';
                // Fill extra positions with space for alignment
                for (int k = 1; k < seqLen && j < count; k++) result[j++] = ' ';
            }
            catch { result[j++] = '·'; }
            i += seqLen;
        }
        // Pad remaining
        while (j < count) result[j++] = ' ';
        return result;
    }

    private static char[] DecodeUtf16(byte[] bytes, int count, bool bigEndian)
    {
        var result = new char[count];
        for (int i = 0, j = 0; i + 1 < count; i += 2, j += 2)
        {
            ushort codeUnit = bigEndian
                ? (ushort)((bytes[i] << 8) | bytes[i + 1])
                : (ushort)((bytes[i + 1] << 8) | bytes[i]);
            char c = codeUnit >= 0x20 && codeUnit < 0xFFFE ? (char)codeUnit : '·';
            result[j]     = c;
            if (j + 1 < count) result[j + 1] = ' '; // second byte of pair
        }
        if (count % 2 == 1) result[count - 1] = '·';
        return result;
    }

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
        long off = HitTestOffset(e.GetPosition(this));
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
        int col = Math.Clamp((int)((pt.X - 4) / _charW), 0, 15);
        long off = (_vm.TopRow + row) * 16 + col;
        return Math.Clamp(off, 0, Math.Max(0, _vm.Buffer.VirtualLength - 1));
    }

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
        double minW = _charW * 17; // 16 chars + a bit of padding
        return new Size(double.IsInfinity(availableSize.Width)  ? minW : availableSize.Width,
                        double.IsInfinity(availableSize.Height) ? _rowH * 10 : availableSize.Height);
    }

    private FormattedText MakeText(string s, Brush brush)
        => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
               _face, FontSize, brush, _dpi);
}
