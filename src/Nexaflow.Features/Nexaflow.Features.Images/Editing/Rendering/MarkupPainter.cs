using Nexaflow.Features.Images.Editing.Recipe;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using System.Collections.Concurrent;

namespace Nexaflow.Features.Images.Editing.Rendering;

/// <summary>
/// Paints annotations with Skia, and answers where each one is — the same measurements the editor hit-tests and
/// draws selection handles with, so a handle sits on exactly what was painted.
/// </summary>
/// <remarks>Text is shaped with HarfBuzz, so any script lays out as it should. Safe to share between threads.</remarks>
public sealed class MarkupPainter : IDisposable
{
    /// <summary>Arrowhead length and half-width, in stroke widths.</summary>
    private const float HeadLength = 4.5f, HeadHalfWidth = 2.5f;

    /// <summary>The plate behind backed text reaches this far past the glyphs, in ems.</summary>
    private const float BackingPad = 0.25f;

    private readonly ConcurrentDictionary<string, SKTypeface> _typefaces = new(StringComparer.OrdinalIgnoreCase);

    public void Paint(SKCanvas canvas, MarkupElement element)
    {
        switch (element)
        {
            case MarkupArrow arrow:   PaintArrow(canvas, arrow);   break;
            case MarkupBox box:       PaintBox(canvas, box);       break;
            case MarkupText text:     PaintText(canvas, text);     break;
            case MarkupStroke stroke: PaintStroke(canvas, stroke); break;
        }
    }

    // ── Painting ────────────────────────────────────────────────────────────

    private static void PaintArrow(SKCanvas canvas, MarkupArrow arrow)
    {
        var from = Point(arrow.From);
        var to   = Point(arrow.To);
        var head = Head(arrow);
        using var paint = Ink(arrow.Color);

        if (head.Shaft is { } shaftEnd)
        {
            paint.Style       = SKPaintStyle.Stroke;
            paint.StrokeWidth = (float)arrow.StrokeWidth;
            paint.StrokeCap   = SKStrokeCap.Round;
            canvas.DrawLine(from, shaftEnd, paint);
        }

        paint.Style = SKPaintStyle.Fill;
        using var path = new SKPath();
        path.MoveTo(to);
        path.LineTo(head.Left);
        path.LineTo(head.Right);
        path.Close();
        canvas.DrawPath(path, paint);
    }

    private static void PaintBox(SKCanvas canvas, MarkupBox box)
    {
        var rect = Rect(box);
        if (box.Fill != 0)
        {
            using var fill = Ink(box.Fill);
            canvas.DrawRect(rect, fill);
        }
        using var stroke = Ink(box.Color);
        stroke.Style       = SKPaintStyle.Stroke;
        stroke.StrokeWidth = (float)box.StrokeWidth;
        stroke.StrokeJoin  = SKStrokeJoin.Miter;
        canvas.DrawRect(rect, stroke);
    }

    private void PaintText(SKCanvas canvas, MarkupText text)
    {
        if (string.IsNullOrEmpty(text.Text)) return;
        var layout = Layout(text);

        canvas.Save();
        canvas.Translate((float)text.Anchor.X, (float)text.Anchor.Y);
        canvas.RotateDegrees((float)text.RotationDegrees);

        if (text.Backing != 0)
        {
            using var plate = Ink(text.Backing);
            canvas.DrawRoundRect(layout.Plate, layout.Font.Size * 0.15f, layout.Font.Size * 0.15f, plate);
        }

        using var ink = Ink(text.Color);
        using var shaper = new SKShaper(layout.Font.Typeface);
        float baseline = -layout.Font.Metrics.Ascent;
        foreach (var line in layout.Lines)
        {
            canvas.DrawShapedText(shaper, line, 0, baseline, SKTextAlign.Left, layout.Font, ink);
            baseline += layout.LineHeight;
        }
        canvas.Restore();
        layout.Font.Dispose();
    }

    private static void PaintStroke(SKCanvas canvas, MarkupStroke stroke)
    {
        if (stroke.Points.IsDefaultOrEmpty) return;
        using var paint = Ink(stroke.Color);
        paint.Style       = SKPaintStyle.Stroke;
        paint.StrokeWidth = (float)stroke.StrokeWidth;
        paint.StrokeCap   = SKStrokeCap.Round;
        paint.StrokeJoin  = SKStrokeJoin.Round;
        if (stroke.Style == StrokeStyle.Highlighter) paint.BlendMode = SKBlendMode.Multiply;

        // One path drawn once: where a highlighter crosses itself the coverage is shared, so it never doubles up.
        using var path = Smoothed(stroke.Points);
        canvas.DrawPath(path, paint);
    }

    // ── Where things are (source pixels) ────────────────────────────────────

    /// <summary>The area <paramref name="element"/> covers, including its line weight.</summary>
    public SKRect Bounds(MarkupElement element)
    {
        switch (element)
        {
            case MarkupArrow arrow:
            {
                var head = Head(arrow);
                var r = Enclose(Point(arrow.From), Point(arrow.To), head.Left, head.Right);
                r.Inflate((float)arrow.StrokeWidth / 2, (float)arrow.StrokeWidth / 2);
                return r;
            }
            case MarkupBox box:
            {
                var r = Rect(box);
                r.Inflate((float)box.StrokeWidth / 2, (float)box.StrokeWidth / 2);
                return r;
            }
            case MarkupText text:
            {
                var layout = Layout(text);
                var r = TextToSource(text).MapRect(layout.Plate);
                layout.Font.Dispose();
                return r;
            }
            case MarkupStroke stroke when !stroke.Points.IsDefaultOrEmpty:
            {
                var r = Enclose([.. stroke.Points.Select(Point)]);
                r.Inflate((float)stroke.StrokeWidth / 2, (float)stroke.StrokeWidth / 2);
                return r;
            }
            default:
                return SKRect.Empty;
        }
    }

    /// <summary>
    /// The outline a selection is drawn around: the text's turned plate, or the bounds as four corners for anything
    /// else.
    /// </summary>
    public SKPoint[] Outline(MarkupElement element)
    {
        if (element is MarkupText text)
        {
            var layout = Layout(text);
            var plate = layout.Plate;
            layout.Font.Dispose();
            return TextToSource(text).MapPoints([new(plate.Left, plate.Top), new(plate.Right, plate.Top),
                                                 new(plate.Right, plate.Bottom), new(plate.Left, plate.Bottom)]);
        }
        var b = Bounds(element);
        return [new(b.Left, b.Top), new(b.Right, b.Top), new(b.Right, b.Bottom), new(b.Left, b.Bottom)];
    }

    /// <summary>
    /// True when <paramref name="point"/> is on <paramref name="element"/> — on its line, within
    /// <paramref name="tolerance"/> source pixels, or inside anything filled.
    /// </summary>
    public bool HitTest(MarkupElement element, SKPoint point, float tolerance)
    {
        switch (element)
        {
            case MarkupArrow arrow:
            {
                var head = Head(arrow);
                return DistanceToSegment(point, Point(arrow.From), Point(arrow.To)) <= arrow.StrokeWidth / 2 + tolerance
                    || InTriangle(point, Point(arrow.To), head.Left, head.Right);
            }
            case MarkupBox box:
            {
                var r = Rect(box);
                if (box.Fill != 0 && r.Contains(point)) return true;
                float reach = (float)box.StrokeWidth / 2 + tolerance;
                var outer = r; outer.Inflate(reach, reach);
                var inner = r; inner.Inflate(-reach, -reach);
                return outer.Contains(point) && !(inner.Width > 0 && inner.Height > 0 && inner.Contains(point));
            }
            case MarkupText text:
            {
                if (!TextToSource(text).TryInvert(out var toText)) return false;
                var layout = Layout(text);
                var plate = layout.Plate;
                layout.Font.Dispose();
                plate.Inflate(tolerance, tolerance);
                return plate.Contains(toText.MapPoint(point));
            }
            case MarkupStroke stroke when !stroke.Points.IsDefaultOrEmpty:
            {
                double reach = stroke.StrokeWidth / 2 + tolerance;
                if (stroke.Points.Length == 1) return SKPoint.Distance(point, Point(stroke.Points[0])) <= reach;
                for (int i = 1; i < stroke.Points.Length; i++)
                    if (DistanceToSegment(point, Point(stroke.Points[i - 1]), Point(stroke.Points[i])) <= reach) return true;
                return false;
            }
            default:
                return false;
        }
    }

    // ── Shared shapes ───────────────────────────────────────────────────────

    private readonly record struct ArrowHead(SKPoint Left, SKPoint Right, SKPoint? Shaft);

    /// <summary>The head's two back corners, and where the shaft stops so its round cap stays under the head.</summary>
    private static ArrowHead Head(MarkupArrow arrow)
    {
        var from = Point(arrow.From);
        var to   = Point(arrow.To);
        float w = (float)arrow.StrokeWidth;
        var d = to - from;
        float length = d.Length;
        if (length < 0.001f) return new ArrowHead(to, to, null);

        var unit = new SKPoint(d.X / length, d.Y / length);
        var normal = new SKPoint(-unit.Y, unit.X);
        float headLength = Math.Min(HeadLength * w, length);
        var back = to - Scale(unit, headLength);
        var shaft = length > headLength ? back + Scale(unit, w / 2) : (SKPoint?)null;
        return new ArrowHead(back + Scale(normal, HeadHalfWidth * w), back - Scale(normal, HeadHalfWidth * w), shaft);
    }

    private sealed record TextLayout(SKFont Font, string[] Lines, float LineHeight, SKRect Plate);

    /// <summary>The lines, their spacing and the plate around them, in the text's own (unturned) space.</summary>
    private TextLayout Layout(MarkupText text)
    {
        var font = new SKFont(Typeface(text.FontFamily), (float)text.FontSize);
        var lines = text.Text.Replace("\r\n", "\n").Split('\n');
        float lineHeight = font.Spacing;
        float width = 0;
        using (var shaper = new SKShaper(font.Typeface))
            foreach (var line in lines) width = Math.Max(width, shaper.Shape(line, 0, 0, font).Width);

        float pad = BackingPad * font.Size;
        var plate = new SKRect(-pad, -pad, width + pad, lines.Length * lineHeight + pad);
        return new TextLayout(font, lines, lineHeight, plate);
    }

    private static SKMatrix TextToSource(MarkupText text) =>
        SKMatrix.CreateRotationDegrees((float)text.RotationDegrees)
            .PostConcat(SKMatrix.CreateTranslation((float)text.Anchor.X, (float)text.Anchor.Y));

    private SKTypeface Typeface(string family) =>
        _typefaces.GetOrAdd(family, f => SKTypeface.FromFamilyName(f) ?? SKTypeface.Default);

    /// <summary>A quadratic curve through the midpoints of a freehand stroke's samples, so it is smooth however it was sampled.</summary>
    private static SKPath Smoothed(IReadOnlyList<MarkupPoint> points)
    {
        var path = new SKPath();
        path.MoveTo(Point(points[0]));
        if (points.Count == 1)
        {
            path.LineTo(Point(points[0]));
            return path;
        }
        for (int i = 1; i < points.Count - 1; i++)
        {
            var p = Point(points[i]);
            var next = Point(points[i + 1]);
            path.QuadTo(p, new SKPoint((p.X + next.X) / 2, (p.Y + next.Y) / 2));
        }
        path.LineTo(Point(points[^1]));
        return path;
    }

    private static SKPaint Ink(uint argb) => new() { Color = new SKColor(argb), IsAntialias = true };

    private static SKPoint Point(MarkupPoint p) => new((float)p.X, (float)p.Y);

    private static SKRect Rect(MarkupBox box) => SKRect.Create(
        (float)Math.Min(box.Corner.X, box.OppositeCorner.X), (float)Math.Min(box.Corner.Y, box.OppositeCorner.Y),
        (float)Math.Abs(box.OppositeCorner.X - box.Corner.X), (float)Math.Abs(box.OppositeCorner.Y - box.Corner.Y));

    private static SKPoint Scale(SKPoint p, float s) => new(p.X * s, p.Y * s);

    private static SKRect Enclose(params SKPoint[] points)
    {
        float l = points.Min(p => p.X), t = points.Min(p => p.Y), r = points.Max(p => p.X), b = points.Max(p => p.Y);
        return new SKRect(l, t, r, b);
    }

    private static double DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
    {
        var ab = b - a;
        float lengthSquared = ab.X * ab.X + ab.Y * ab.Y;
        if (lengthSquared < 1e-6f) return SKPoint.Distance(p, a);
        float t = Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y) / lengthSquared, 0, 1);
        return SKPoint.Distance(p, new SKPoint(a.X + ab.X * t, a.Y + ab.Y * t));
    }

    private static bool InTriangle(SKPoint p, SKPoint a, SKPoint b, SKPoint c)
    {
        float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
        bool negative = d1 < 0 || d2 < 0 || d3 < 0, positive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(negative && positive);

        static float Cross(SKPoint p, SKPoint a, SKPoint b) => (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
    }

    public void Dispose()
    {
        foreach (var typeface in _typefaces.Values)
            if (!ReferenceEquals(typeface, SKTypeface.Default)) typeface.Dispose();
        _typefaces.Clear();
    }
}
