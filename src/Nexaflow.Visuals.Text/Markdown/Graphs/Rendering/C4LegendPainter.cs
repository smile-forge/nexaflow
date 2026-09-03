using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// The legend block a <c>SHOW_LEGEND()</c> asks for: a bordered strip of swatch-and-label rows,
/// wrapping to the width it is given.
///
/// Panel-based rather than canvas-measured, because a legend has no geometry the diagram depends on
/// — it is appended under the finished layout and only has to size itself.
/// </summary>
internal static class C4LegendPainter
{
    private const double SwatchW = 22;
    private const double SwatchH = 13;

    /// <summary>
    /// <paramref name="details"/> is <c>SHOW_LEGEND</c>'s <c>$details</c>, which in C4-PlantUML sets
    /// the row font size (0 / 10 / 14). <c>None</c> keeps the rows but at the smallest readable size
    /// rather than hiding them, because a legend nobody can read is not the same as no legend.
    /// </summary>
    internal static FrameworkElement Build(
        IReadOnlyList<GraphLegendEntry> entries, C4Palette c4, MarkdownPalette palette,
        C4LegendDetails details = C4LegendDetails.Small)
    {
        double fontSize = details switch
        {
            C4LegendDetails.None   => 9,
            C4LegendDetails.Normal => 13,
            _                      => 10.5,
        };
        var rows = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 720 };

        foreach (var entry in entries)
        {
            // Resolve the swatch exactly as a card resolves its fill: a literal colour first, then
            // the kind's own place in the grading. A legend in colours the diagram does not use
            // would be actively misleading.
            Brush kindFill = entry.External ? c4.External
                           : entry.Kind is C4ElementKind k ? c4.ForKind(k)
                           : c4.System;
            Color fill = DiagramBrushes.ParseCss(entry.FillColor)
                      ?? DiagramBrushes.ColorOf(kindFill, Colors.SteelBlue);
            Color stroke = DiagramBrushes.ParseCss(entry.StrokeColor)
                        ?? DiagramBrushes.ColorOf(palette.CodeBorder, Colors.Gray);

            var swatch = Swatch(entry.Shape, fill, stroke);
            var text = new TextBlock
            {
                Text              = entry.Label,
                Foreground        = palette.Text,
                FontFamily        = DiagramText.BodyFont,
                FontSize          = fontSize,
                Margin            = new Thickness(6, 0, 16, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            row.Children.Add(swatch);
            row.Children.Add(text);
            rows.Children.Add(row);
        }

        return new Border
        {
            Background      = palette.CodeBg,
            BorderBrush     = palette.CodeBorder,
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(10, 6, 10, 6),
            Child           = rows,
        };
    }

    /// <summary>The little shape in front of a legend row — the element's own outline in miniature.</summary>
    private static FrameworkElement Swatch(C4ElementShape? shape, Color fill, Color stroke)
    {
        Brush f = DiagramBrushes.Frozen(fill), s = DiagramBrushes.Frozen(stroke);

        return shape switch
        {
            C4ElementShape.Queue => new Rectangle
            {
                Width = SwatchW, Height = SwatchH, RadiusX = SwatchH / 2, RadiusY = SwatchH / 2,
                Fill = f, Stroke = s, StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center,
            },
            C4ElementShape.Database => new Ellipse
            {
                Width = SwatchW, Height = SwatchH,
                Fill = f, Stroke = s, StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center,
            },
            _ => new Rectangle
            {
                Width = SwatchW, Height = SwatchH, RadiusX = 3, RadiusY = 3,
                Fill = f, Stroke = s, StrokeThickness = 1, VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }
}
