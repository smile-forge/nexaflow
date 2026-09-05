using Nexaflow.Features.Hex.ViewModels;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Features.Hex.Controls;

/// <summary>
/// Renders the text-interpretation (evaluate) pane. Each row shows 16 bytes decoded with the active
/// encoding; invalid sequences → '·'. Shares its cell metrics with <see cref="HexRenderPanel"/> through
/// <see cref="HexPanelBase"/>, so the two panes always line up row for row.
/// </summary>
public sealed class EvalRenderPanel : HexPanelBase
{
    private Brush? _bgBrush, _textBrush, _mutedBrush, _accentBrush,
                   _selBrush, _borderBrush, _cursorStroke;

    public EvalRenderPanel()
    {
        Focusable    = true;
        Cursor       = Cursors.IBeam;
        ClipToBounds = true;
    }

    // ── Brushes ───────────────────────────────────────────────────────────────

    private void EnsureBrushes()
    {
        _bgBrush     ??= Res("BgBrush");
        _textBrush   ??= Res("TextBrush");
        _mutedBrush  ??= Res("TextMutedBrush");
        _accentBrush ??= Res("AccentBrush");
        _selBrush    ??= SemiAccent(_accentBrush!);
        _borderBrush ??= Res("BorderBrush");
        _cursorStroke ??= _accentBrush;
    }

    // ── Render ────────────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        if (Vm == null) return;
        EnsureMetrics();
        EnsureBrushes();

        int rows = VisibleRowCount;
        dc.DrawRectangle(_bgBrush, null, new Rect(0, 0, ActualWidth, ActualHeight));

        long topRow   = Vm.TopRow;
        long total    = Vm.TotalRows;
        long vLen     = Vm.Buffer.VirtualLength;
        long selStart = Vm.SelectionStart;
        long selLen   = Vm.SelectionLength;
        long cursor   = Vm.CursorOffset;
        var  enc      = Vm.ResolvedEncoding;

        for (int r = 0; r < rows; r++)
        {
            long rowIdx    = topRow + r;
            if (rowIdx >= total) break;
            long rowOffset = rowIdx * 16;
            int  count     = (int)Math.Min(16, vLen - rowOffset);
            if (count <= 0) break;

            var bytes = Vm.Buffer.ReadRange(rowOffset, count);
            double y  = r * RowH;

            // Decode row into printable characters
            char[] chars = Decode(bytes, count, enc);

            // Draw per-character with selection highlights
            for (int col = 0; col < chars.Length; col++)
            {
                long byteOff = rowOffset + col;
                double x = col * CharW + 4;

                bool inSel = selLen > 0 && byteOff >= selStart && byteOff < selStart + selLen;
                bool isCur = byteOff == cursor;

                if (inSel)
                    dc.DrawRectangle(_selBrush, null, new Rect(x - 1, y, CharW + 1, RowH));
                else if (isCur)
                    dc.DrawRectangle(null, new Pen(_cursorStroke!, 1.5),
                        new Rect(x - 1, y + 1, CharW, RowH - 2));

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
        long off = HitTestOffset(e.GetPosition(this));
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
        int col = Math.Clamp((int)((pt.X - 4) / CharW), 0, 15);
        long off = (Vm.TopRow + row) * 16 + col;
        return Math.Clamp(off, 0, Math.Max(0, Vm.Buffer.VirtualLength - 1));
    }

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
        double minW = CharW * 17; // 16 chars + a bit of padding
        return new Size(double.IsInfinity(availableSize.Width)  ? minW : availableSize.Width,
                        double.IsInfinity(availableSize.Height) ? RowH * 10 : availableSize.Height);
    }
}
