using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Draws one C4 element card — bold title, bracketed stereotype, wrapped description — inside a
/// footprint <see cref="C4ElementMetrics"/> measured.
///
/// One painter serves both C4 pipelines: a structural diagram places it as a graph node, and a C4
/// sequence diagram places the same card as a participant head. That is the whole reason it is a
/// standalone painter rather than a method on either renderer.
///
/// The returned element is a <see cref="Canvas"/> whose **first child is the outline
/// <see cref="Shape"/>** — <c>WpfGraphRenderer.Highlight</c> selects a composite node by restroking
/// <c>Children[0]</c>, so the order is load-bearing, not incidental.
/// </summary>
internal static class C4ElementPainter
{
    /// <summary>
    /// Builds the card at exactly <paramref name="w"/>×<paramref name="h"/>.  Pass
    /// <paramref name="showDescription"/> false to keep the title and stereotype but drop the prose
    /// (C4-PlantUML's sequence diagrams hide descriptions unless asked for them).
    /// </summary>
    internal static FrameworkElement Build(
        string label, C4ElementInfo info, double w, double h, C4Palette palette, bool showDescription = true)
    {
        var (fill, stroke, ink) = palette.BrushesFor(info);
        var cell = new Canvas { Width = w, Height = h };

        // 1. the outline — FIRST, so Highlight can find it
        var outline = new Path
        {
            Data            = Outline(info.Shape, w, h),
            Fill            = fill,
            Stroke          = stroke,
            StrokeThickness = info.BorderThickness,
            StrokeLineJoin  = PenLineJoin.Round,
        };
        if (info.BorderStyle is EdgeStyle.Dashed) outline.StrokeDashArray = new DoubleCollection([5, 3]);
        if (info.BorderStyle is EdgeStyle.Dotted) outline.StrokeDashArray = new DoubleCollection([2, 3]);
        cell.Children.Add(outline);

        // 2. the text stack, inset past whatever the shape reserved
        double top    = info.Shape is C4ElementShape.Person or C4ElementShape.PersonOutline
                            ? C4ElementMetrics.PersonHeadH
                            : info.Shape == C4ElementShape.PersonPortrait ? C4ElementMetrics.PortraitH
                            : info.Shape == C4ElementShape.Database ? C4ElementMetrics.DbCapH
                            : 0;
        double padX   = C4ElementMetrics.PadX + (info.Shape == C4ElementShape.Queue ? C4ElementMetrics.QueueEndW / 2 : 0);
        double textW  = Math.Max(10, w - 2 * padX);

        var stack = new StackPanel { Width = textW };
        stack.Children.Add(Row(label, textW, 12, FontWeights.SemiBold, ink));

        string stereotype = info.Stereotype();
        if (stereotype.Length > 0)
            stack.Children.Add(Row(stereotype, textW, 10, FontWeights.Normal, C4Palette.MetaInk(ink)));

        if (showDescription && !string.IsNullOrWhiteSpace(info.Description))
        {
            var descr = Row(info.Description!, textW, 10.5, FontWeights.Normal, C4Palette.MetaInk(ink));
            descr.Margin = new Thickness(0, C4ElementMetrics.DescGap, 0, 0);
            stack.Children.Add(descr);
        }

        // Centre the stack in the space left under the shape's own reservation.
        stack.Measure(new Size(textW, double.PositiveInfinity));
        double avail = Math.Max(0, h - top - (info.Shape == C4ElementShape.Database ? C4ElementMetrics.DbCapH : 0));
        double y = top + Math.Max(0, (avail - stack.DesiredSize.Height) / 2);
        cell.Children.Add(stack.Place(padX, y));

        return cell;
    }

    // ── Outlines ─────────────────────────────────────────────────────────────

    /// <summary>The card's outline as one geometry, so the whole shape is a single strokeable Shape.</summary>
    private static Geometry Outline(C4ElementShape shape, double w, double h)
    {
        double half = 0.75;                       // keep the stroke inside the cell
        var body = new Rect(half, half, Math.Max(1, w - 2 * half), Math.Max(1, h - 2 * half));

        switch (shape)
        {
            case C4ElementShape.Queue:
            {
                double r = body.Height / 2;
                var g = new RectangleGeometry(body, r, r);
                g.Freeze();
                return g;
            }

            case C4ElementShape.Database:
            {
                double cap = C4ElementMetrics.DbCapH;
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    // A drum: elliptical top, straight sides, elliptical bottom.
                    c.BeginFigure(new Point(body.Left, body.Top + cap), true, true);
                    c.ArcTo(new Point(body.Right, body.Top + cap), new Size(body.Width / 2, cap), 0, false, SweepDirection.Clockwise, true, true);
                    c.LineTo(new Point(body.Right, body.Bottom - cap), true, true);
                    c.ArcTo(new Point(body.Left, body.Bottom - cap), new Size(body.Width / 2, cap), 0, false, SweepDirection.Clockwise, true, true);
                }
                g.Freeze();
                return g;
            }

            case C4ElementShape.Person:
            case C4ElementShape.PersonOutline:
            {
                // Head above a rounded card — C4's person, and the shape SHOW_PERSON_OUTLINE keeps.
                double headR = C4ElementMetrics.PersonHeadH / 2 - 1;
                double cardTop = C4ElementMetrics.PersonHeadH;
                var card = new RectangleGeometry(
                    new Rect(body.Left, cardTop, body.Width, Math.Max(1, body.Bottom - cardTop)), 8, 8);
                var head = new EllipseGeometry(new Point(body.Left + body.Width / 2, headR + 1), headR, headR);
                var g = new GeometryGroup { FillRule = FillRule.Nonzero };
                g.Children.Add(card);
                g.Children.Add(head);
                g.Freeze();
                return g;
            }

            case C4ElementShape.PersonPortrait:
            {
                // A framed head inside a band across the top of the card. Nonzero, not EvenOdd: a
                // cut-out would punch through to whatever is behind the card (the page on its own,
                // a boundary's tint inside one), which reads as a hole rather than a portrait.
                double band = C4ElementMetrics.PortraitH;
                double headR = band / 2 - 4;
                var card = new RectangleGeometry(body, 8, 8);
                var head = new EllipseGeometry(new Point(body.Left + body.Width / 2, body.Top + band / 2), headR, headR);
                var g = new GeometryGroup { FillRule = FillRule.Nonzero };
                g.Children.Add(card);
                g.Children.Add(head);
                g.Freeze();
                return g;
            }

            default:
            {
                var g = new RectangleGeometry(body, 6, 6);
                g.Freeze();
                return g;
            }
        }
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    private static TextBlock Row(string text, double width, double fontSize, FontWeight weight, Brush ink) => new()
    {
        Text          = text,
        Width         = width,
        TextWrapping  = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        TextTrimming  = TextTrimming.CharacterEllipsis,
        Foreground    = ink,
        FontFamily    = DiagramText.BodyFont,
        FontSize      = fontSize,
        FontWeight    = weight,
    };
}
