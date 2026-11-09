using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Nexaflow.Features.Pdf.Views;

/// <summary>
/// A frame drawn over part of the pages while a menu about it is open — the image Edit image is for — so the reader sees
/// what the menu acts on before choosing. Drawn in the theme's accent, over whatever the page shows there; never hit.
/// </summary>
internal sealed class PdfAreaMarker : Adorner
{
    private readonly Pen _pen;
    private readonly Brush _fill;
    private Rect? _area;

    public PdfAreaMarker(UIElement pages) : base(pages)
    {
        IsHitTestVisible = false;
        var accent = (pages as FrameworkElement)?.TryFindResource("AccentBrush") as Brush ?? SystemColors.HighlightBrush;
        _pen = new Pen(accent, 2);
        _pen.Freeze();
        _fill = accent.CloneCurrentValue();
        _fill.Opacity = 0.15;
        _fill.Freeze();
    }

    /// <summary>Marks <paramref name="area"/>, in the pages' own coordinates; null marks nothing.</summary>
    public void Show(Rect? area)
    {
        _area = area;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        if (_area is { } area) drawing.DrawRectangle(_fill, _pen, area);
    }
}
