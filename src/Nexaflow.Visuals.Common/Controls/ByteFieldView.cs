using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexaflow.Visuals.Common.Controls;

/// <summary>A run of bytes in a buffer: where it starts and how many.</summary>
public readonly record struct ByteSpan(int Offset, int Length)
{
    public int End => Offset + Length;
    public bool Contains(int offset) => offset >= Offset && offset < End;
}

/// <summary>
/// A hex dump of a small buffer — offset, hex and text columns — that shows structure: a
/// <see cref="Selection"/> (the record being looked at) under a <see cref="Highlight"/> (the field being looked at),
/// and reports the byte under the pointer as <see cref="HoveredOffset"/> so its owner can highlight the field that
/// byte belongs to. The two directions meet in the owner: hover a byte and the field lights up, hover a field in a
/// table and its bytes do.
/// </summary>
/// <remarks>
/// <para>
/// Only the rows in view are drawn: the control scrolls itself (<see cref="IScrollInfo"/>) when hosted in a
/// <see cref="ScrollViewer"/> with <c>CanContentScroll="True"</c>, so a buffer of any size costs the same per frame,
/// and moving the highlight repaints without re-measuring. A highlight or selection that changes scrolls itself
/// into view.
/// </para>
/// <para>
/// It hard-codes no colour: the brushes are properties for the host to bind to theme tokens, as
/// <see cref="SparklineChart"/>'s are. Text uses the inherited <see cref="TextElement.FontSize"/> with
/// <see cref="FontFamily"/>, so a page's zoom reaches it.
/// </para>
/// </remarks>
public sealed class ByteFieldView : FrameworkElement, IScrollInfo
{
    public static readonly DependencyProperty BytesProperty = DependencyProperty.Register(
        nameof(Bytes), typeof(byte[]), typeof(ByteFieldView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((ByteFieldView)d).OnContentChanged()));

    public static readonly DependencyProperty BytesPerRowProperty = DependencyProperty.Register(
        nameof(BytesPerRow), typeof(int), typeof(ByteFieldView),
        new FrameworkPropertyMetadata(16, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((ByteFieldView)d).OnContentChanged()),
        v => v is int n && n is >= 1 and <= 64);

    public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
        nameof(Selection), typeof(ByteSpan?), typeof(ByteFieldView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((ByteFieldView)d).Reveal(e.NewValue as ByteSpan?)));

    public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register(
        nameof(Highlight), typeof(ByteSpan?), typeof(ByteFieldView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((ByteFieldView)d).OnHighlightChanged(e.NewValue as ByteSpan?)));

    public static readonly DependencyProperty HoveredOffsetProperty = DependencyProperty.Register(
        nameof(HoveredOffset), typeof(int), typeof(ByteFieldView),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextElement.FontFamilyProperty.AddOwner(
        typeof(ByteFieldView), new FrameworkPropertyMetadata(new FontFamily("Consolas"), FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((ByteFieldView)d).ResetMetrics()));

    public static readonly DependencyProperty FontSizeProperty = TextElement.FontSizeProperty.AddOwner(
        typeof(ByteFieldView), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.Inherits, (d, _) => ((ByteFieldView)d).ResetMetrics()));

    public static readonly DependencyProperty ForegroundProperty = RegisterBrush(nameof(Foreground));
    public static readonly DependencyProperty OffsetForegroundProperty = RegisterBrush(nameof(OffsetForeground));
    public static readonly DependencyProperty SelectionBrushProperty = RegisterBrush(nameof(SelectionBrush));
    public static readonly DependencyProperty HighlightBrushProperty = RegisterBrush(nameof(HighlightBrush));
    public static readonly DependencyProperty HoverBrushProperty = RegisterBrush(nameof(HoverBrush));

    static DependencyProperty RegisterBrush(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(ByteFieldView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The buffer shown. Replaced, never mutated in place.</summary>
    public byte[]? Bytes { get => (byte[]?)GetValue(BytesProperty); set => SetValue(BytesProperty, value); }

    public int BytesPerRow { get => (int)GetValue(BytesPerRowProperty); set => SetValue(BytesPerRowProperty, value); }

    /// <summary>The record in view — a descriptor in its blob, say. Washed under everything else.</summary>
    public ByteSpan? Selection { get => (ByteSpan?)GetValue(SelectionProperty); set => SetValue(SelectionProperty, value); }

    /// <summary>The field in view, drawn over the selection.</summary>
    public ByteSpan? Highlight { get => (ByteSpan?)GetValue(HighlightProperty); set => SetValue(HighlightProperty, value); }

    /// <summary>The byte under the pointer, or -1. Written by the control; bind it to learn which field to highlight.</summary>
    public int HoveredOffset { get => (int)GetValue(HoveredOffsetProperty); set => SetValue(HoveredOffsetProperty, value); }

    public FontFamily FontFamily { get => (FontFamily)GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => (double)GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public Brush? Foreground { get => (Brush?)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public Brush? OffsetForeground { get => (Brush?)GetValue(OffsetForegroundProperty); set => SetValue(OffsetForegroundProperty, value); }
    public Brush? SelectionBrush { get => (Brush?)GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
    public Brush? HighlightBrush { get => (Brush?)GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }
    public Brush? HoverBrush { get => (Brush?)GetValue(HoverBrushProperty); set => SetValue(HoverBrushProperty, value); }

    double _charWidth, _lineHeight;
    Vector _offset;
    Size _viewport, _extent;

    public ByteFieldView()
    {
        ClipToBounds = true;
        Focusable = false;
    }

    int Rows => Bytes is { Length: > 0 } b ? (b.Length + BytesPerRow - 1) / BytesPerRow : 0;

    int OffsetDigits => Bytes is { Length: > 0xFFFF } ? 8 : 4;

    // Columns, in characters: the offset, two spaces, three per byte with one more between the halves of a row,
    // two spaces, then one per byte of text.
    double HexLeft => (OffsetDigits + 2) * _charWidth;
    double ByteLeft(int column) => HexLeft + (column * 3 + (column >= BytesPerRow / 2 && BytesPerRow >= 8 ? 1 : 0)) * _charWidth;
    double TextLeft => ByteLeft(BytesPerRow) + 2 * _charWidth;
    double ContentWidth => TextLeft + BytesPerRow * _charWidth;

    void ResetMetrics()
    {
        _charWidth = 0;
        InvalidateMeasure();
    }

    void EnsureMetrics()
    {
        if (_charWidth > 0) return;
        var probe = Text("0", null);
        _charWidth = probe.WidthIncludingTrailingWhitespace;
        _lineHeight = Math.Ceiling(probe.Height + 2);
    }

    FormattedText Text(string text, Brush? brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            FontSize, brush ?? System.Windows.Media.Brushes.Transparent, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    void OnContentChanged()
    {
        _offset = default;
        ScrollOwner?.InvalidateScrollInfo();
    }

    protected override Size MeasureOverride(Size available)
    {
        EnsureMetrics();
        var extent = new Size(ContentWidth, Rows * _lineHeight);
        var viewport = new Size(
            double.IsInfinity(available.Width) ? extent.Width : available.Width,
            double.IsInfinity(available.Height) ? extent.Height : available.Height);
        UpdateScrollInfo(extent, viewport);
        return new Size(Math.Min(extent.Width, viewport.Width), Math.Min(extent.Height, viewport.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        UpdateScrollInfo(_extent, final);
        return final;
    }

    void UpdateScrollInfo(Size extent, Size viewport)
    {
        if (extent == _extent && viewport == _viewport) return;
        _extent = extent;
        _viewport = viewport;
        _offset = new Vector(Clamp(_offset.X, extent.Width - viewport.Width), Clamp(_offset.Y, extent.Height - viewport.Height));
        ScrollOwner?.InvalidateScrollInfo();
    }

    static double Clamp(double value, double max) => Math.Max(0, Math.Min(value, Math.Max(0, max)));

    protected override void OnRender(DrawingContext dc)
    {
        var bytes = Bytes;
        dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(RenderSize)); // hit-testable everywhere
        if (bytes is not { Length: > 0 }) return;
        EnsureMetrics();

        int perRow = BytesPerRow;
        int first = Math.Max(0, (int)(_offset.Y / _lineHeight));
        int last = Math.Min(Rows - 1, (int)((_offset.Y + _viewport.Height) / _lineHeight));
        dc.PushTransform(new TranslateTransform(-_offset.X, -_offset.Y));

        for (int row = first; row <= last; row++)
        {
            double y = row * _lineHeight;
            int start = row * perRow, count = Math.Min(perRow, bytes.Length - start);

            Wash(dc, Selection, SelectionBrush, start, count, y);
            Wash(dc, Highlight, HighlightBrush, start, count, y);
            if (HoveredOffset >= start && HoveredOffset < start + count && HoverBrush is { } hover)
            {
                int column = HoveredOffset - start;
                var pen = new Pen(hover, 1);
                dc.DrawRectangle(null, pen, new Rect(ByteLeft(column) - _charWidth * 0.25, y + 0.5, _charWidth * 2.5, _lineHeight - 1));
                dc.DrawRectangle(null, pen, new Rect(TextLeft + column * _charWidth, y + 0.5, _charWidth, _lineHeight - 1));
            }

            dc.DrawText(Text(start.ToString(OffsetDigits == 8 ? "X8" : "X4", CultureInfo.InvariantCulture), OffsetForeground ?? Foreground), new Point(0, y + 1));
            var hex = new char[count * 3 + 1];
            var text = new char[count];
            int at = 0;
            for (int i = 0; i < count; i++)
            {
                if (i == perRow / 2 && perRow >= 8) hex[at++] = ' ';
                byte b = bytes[start + i];
                hex[at++] = HexDigit(b >> 4);
                hex[at++] = HexDigit(b & 0xF);
                if (i < count - 1) hex[at++] = ' ';
                text[i] = b is >= 0x20 and < 0x7F ? (char)b : '·';
            }
            dc.DrawText(Text(new string(hex, 0, at), Foreground), new Point(HexLeft, y + 1));
            dc.DrawText(Text(new string(text), OffsetForeground ?? Foreground), new Point(TextLeft, y + 1));
        }
        dc.Pop();
    }

    static char HexDigit(int nibble) => (char)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);

    /// <summary>Washes the part of <paramref name="span"/> on one row, in both the hex and the text columns.</summary>
    void Wash(DrawingContext dc, ByteSpan? span, Brush? brush, int rowStart, int count, double y)
    {
        if (span is not { Length: > 0 } s || brush is null) return;
        int from = Math.Max(s.Offset, rowStart), to = Math.Min(s.End, rowStart + count);
        if (from >= to) return;
        int c0 = from - rowStart, c1 = to - rowStart - 1;
        double left = ByteLeft(c0) - _charWidth * 0.5, right = ByteLeft(c1) + _charWidth * 2.5;
        dc.DrawRectangle(brush, null, new Rect(left, y, right - left, _lineHeight));
        dc.DrawRectangle(brush, null, new Rect(TextLeft + c0 * _charWidth, y, (c1 - c0 + 1) * _charWidth, _lineHeight));
    }

    /// <summary>The byte at a point, in either column, or -1.</summary>
    public int OffsetAt(Point point)
    {
        if (Bytes is not { Length: > 0 } bytes || _charWidth <= 0) return -1;
        double x = point.X + _offset.X, y = point.Y + _offset.Y;
        int row = (int)(y / _lineHeight);
        if (row < 0 || row >= Rows) return -1;

        int column = -1;
        if (x >= TextLeft && x < TextLeft + BytesPerRow * _charWidth) column = (int)((x - TextLeft) / _charWidth);
        else
            for (int c = 0; c < BytesPerRow; c++)
                if (x >= ByteLeft(c) - _charWidth * 0.5 && x < ByteLeft(c) + _charWidth * 2.5) { column = c; break; }

        int offset = row * BytesPerRow + column;
        return column < 0 || offset >= bytes.Length ? -1 : offset;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int offset = OffsetAt(e.GetPosition(this));
        if (offset != HoveredOffset) SetCurrentValue(HoveredOffsetProperty, offset);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SetCurrentValue(HoveredOffsetProperty, -1);
    }

    void OnHighlightChanged(ByteSpan? span)
    {
        // A highlight the pointer made is already under the pointer; one made elsewhere (a table row) may not be.
        if (span is { } s && !s.Contains(HoveredOffset)) Reveal(span);
    }

    /// <summary>Scrolls just enough to show the start of <paramref name="span"/>.</summary>
    void Reveal(ByteSpan? span)
    {
        if (span is not { } s || _lineHeight <= 0 || BytesPerRow <= 0) return;
        double top = s.Offset / BytesPerRow * _lineHeight;
        double bottom = ((s.End - 1) / BytesPerRow + 1) * _lineHeight;
        if (top >= _offset.Y && bottom <= _offset.Y + _viewport.Height) return;
        SetVerticalOffset(bottom - top > _viewport.Height || top < _offset.Y ? top : bottom - _viewport.Height);
    }

    // ── IScrollInfo ─────────────────────────────────────────────────────────────────────────────────────────

    public bool CanVerticallyScroll { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    double Line => _lineHeight > 0 ? _lineHeight : 16;

    public void LineUp() => SetVerticalOffset(_offset.Y - Line);
    public void LineDown() => SetVerticalOffset(_offset.Y + Line);
    public void LineLeft() => SetHorizontalOffset(_offset.X - _charWidth * 3);
    public void LineRight() => SetHorizontalOffset(_offset.X + _charWidth * 3);
    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);
    public void PageLeft() => SetHorizontalOffset(_offset.X - _viewport.Width);
    public void PageRight() => SetHorizontalOffset(_offset.X + _viewport.Width);
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - Line * SystemParameters.WheelScrollLines);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + Line * SystemParameters.WheelScrollLines);
    public void MouseWheelLeft() => LineLeft();
    public void MouseWheelRight() => LineRight();

    public void SetVerticalOffset(double offset)
    {
        // Whole rows, so a row is never cut across the top edge.
        double snapped = Math.Round(Clamp(offset, _extent.Height - _viewport.Height) / Line) * Line;
        if (snapped == _offset.Y) return;
        _offset.Y = Clamp(snapped, _extent.Height - _viewport.Height);
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateVisual();
    }

    public void SetHorizontalOffset(double offset)
    {
        double clamped = Clamp(offset, _extent.Width - _viewport.Width);
        if (clamped == _offset.X) return;
        _offset.X = clamped;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateVisual();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;
}
