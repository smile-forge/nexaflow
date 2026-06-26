using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a Mermaid <see cref="VennDiagram"/> as overlapping circles, themed from a
/// <see cref="MarkdownPalette"/> and configured by <see cref="VennConfig"/>.  Circles use a canonical
/// layout (one / two side-by-side / three in a triangle, a ring for four+), with radii scaled by each
/// set's area-weight size; each circle takes a <c>venn</c>-palette colour at low opacity.  Set labels sit
/// toward the outer edge, intersection (<c>union</c>) labels at the overlap centroid, and <c>text</c> items
/// stack under their region's label.
/// </summary>
public static class WpfVennRenderer
{
    private static readonly FontFamily BodyFont = new("Segoe UI");
    private const double TitleFont = 15, SetFont = 13, UnionFont = 12, ItemFont = 11, ItemH = 15;

    public static FrameworkElement Render(VennDiagram diagram, MarkdownPalette palette)
    {
        var cfg = diagram.Config;
        Brush bg = palette.CodeBg, border = palette.CodeBorder;
        int n = diagram.Sets.Count;

        Brush SetBrush(int i) =>
            i < cfg.SetPalette.Count ? cfg.SetPalette[i] : palette.Series[i % palette.Series.Count];

        double canvasW = Math.Max(cfg.Width, 160), canvasH = Math.Max(cfg.Height, 120);
        bool hasTitle = !string.IsNullOrWhiteSpace(diagram.Title);
        double titleH = hasTitle ? TitleFont + 10 : 0;
        double pad = cfg.Padding + 8;

        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = bg };

        if (hasTitle)
        {
            var sz = Measure(diagram.Title, TitleFont);
            AddText(canvas, diagram.Title, palette.Heading, (canvasW - sz.Width) / 2, cfg.Padding + 2, TitleFont, FontWeights.SemiBold);
        }

        if (n == 0) return Frame(canvas, bg, border);

        // ── Canonical unit layout + size-scaled radii ───────────────────────
        var centers = UnitCenters(n);
        double maxSize = diagram.Sets.Max(s => s.Size ?? 10);
        if (maxSize <= 0) maxSize = 10;
        var rUnit = diagram.Sets.Select(s => Math.Sqrt((s.Size ?? 10) / maxSize)).ToArray();   // ∈ (0,1]

        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        for (int i = 0; i < n; i++)
        {
            minX = Math.Min(minX, centers[i].X - rUnit[i]); maxX = Math.Max(maxX, centers[i].X + rUnit[i]);
            minY = Math.Min(minY, centers[i].Y - rUnit[i]); maxY = Math.Max(maxY, centers[i].Y + rUnit[i]);
        }
        double bboxW = Math.Max(maxX - minX, 0.1), bboxH = Math.Max(maxY - minY, 0.1);

        double plotLeft = pad, plotTop = pad + titleH;
        double plotW = Math.Max(canvasW - 2 * pad, 40), plotH = Math.Max(canvasH - titleH - 2 * pad, 40);
        double scale = Math.Min(plotW / bboxW, plotH / bboxH);
        double layoutCx = (minX + maxX) / 2, layoutCy = (minY + maxY) / 2;
        double plotCx = plotLeft + plotW / 2, plotCy = plotTop + plotH / 2;

        Point Screen(int i) => new(plotCx + (centers[i].X - layoutCx) * scale, plotCy + (centers[i].Y - layoutCy) * scale);
        var px = new Point[n];
        var rpx = new double[n];
        for (int i = 0; i < n; i++) { px[i] = Screen(i); rpx[i] = rUnit[i] * scale; }

        // ── Circles ─────────────────────────────────────────────────────────
        for (int i = 0; i < n; i++)
        {
            var s = diagram.Sets[i];
            Brush baseColor = s.Fill is string f ? ParseBrush(f, SetBrush(i)) : SetBrush(i);
            var ring = new Ellipse
            {
                Width = rpx[i] * 2, Height = rpx[i] * 2,
                Fill = WithOpacity(baseColor, s.FillOpacity ?? cfg.FillOpacity),
                Stroke = s.Stroke is string st ? ParseBrush(st, baseColor) : baseColor,
                StrokeThickness = 2,
            };
            Canvas.SetLeft(ring, px[i].X - rpx[i]);
            Canvas.SetTop(ring, px[i].Y - rpx[i]);
            canvas.Children.Add(ring);
        }

        // ── Set labels + items (toward the outer edge) ──────────────────────
        double cenX = px.Average(p => p.X), cenY = px.Average(p => p.Y);
        for (int i = 0; i < n; i++)
        {
            var s = diagram.Sets[i];
            double dx = px[i].X - cenX, dy = px[i].Y - cenY;
            double len = Math.Sqrt(dx * dx + dy * dy);
            double ux = len > 1e-6 ? dx / len : 0, uy = len > 1e-6 ? dy / len : -1;
            var anchor = new Point(px[i].X + ux * rpx[i] * 0.5, px[i].Y + uy * rpx[i] * 0.5);
            Brush textBrush = s.TextColor is string tc ? ParseBrush(tc, palette.Text) : palette.Text;
            DrawRegion(canvas, s.Display, s.Items, anchor, textBrush, palette.TextMuted, SetFont);
        }

        // ── Union (intersection) labels + items ─────────────────────────────
        var idx = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < n; i++) idx[diagram.Sets[i].Id] = i;
        foreach (var u in diagram.Unions)
        {
            var members = u.SetIds.Where(idx.ContainsKey).Select(id => idx[id]).ToList();
            if (members.Count == 0) continue;
            var anchor = new Point(members.Average(i => px[i].X), members.Average(i => px[i].Y));
            Brush textBrush = u.TextColor is string tc ? ParseBrush(tc, palette.Text) : palette.Text;
            DrawRegion(canvas, u.Display, u.Items, anchor, textBrush, palette.TextMuted, UnionFont);
        }

        return Frame(canvas, bg, border);
    }

    // ── Layout ────────────────────────────────────────────────────────────────

    private static Point[] UnitCenters(int n) => n switch
    {
        1 => [new Point(0, 0)],
        2 => [new Point(-0.5, 0), new Point(0.5, 0)],
        3 => [new Point(0, -0.5), new Point(-0.43, 0.28), new Point(0.43, 0.28)],
        _ => Enumerable.Range(0, n).Select(i =>
        {
            double a = -Math.PI / 2 + i * 2 * Math.PI / n;
            return new Point(0.62 * Math.Cos(a), 0.62 * Math.Sin(a));
        }).ToArray(),
    };

    private static void DrawRegion(Canvas canvas, string title, List<VennItem> items, Point anchor,
        Brush titleBrush, Brush itemBrush, double titleFont)
    {
        double totalH = (string.IsNullOrEmpty(title) ? 0 : titleFont + 4) + items.Count * ItemH;
        double y = anchor.Y - totalH / 2;

        if (!string.IsNullOrEmpty(title))
        {
            var sz = Measure(title, titleFont);
            AddText(canvas, title, titleBrush, anchor.X - sz.Width / 2, y, titleFont, FontWeights.SemiBold);
            y += titleFont + 4;
        }
        foreach (var item in items)
        {
            var sz = Measure(item.Display, ItemFont);
            AddText(canvas, item.Display, itemBrush, anchor.X - sz.Width / 2, y, ItemFont);
            y += ItemH;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Border Frame(Canvas canvas, Brush bg, Brush border) => new()
    {
        Background = bg, BorderBrush = border, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 8, 0, 12), Child = canvas,
    };

    private static Brush WithOpacity(Brush b, double opacity)
    {
        Color c = (b as SolidColorBrush)?.Color ?? Colors.Gray;
        var brush = new SolidColorBrush(c) { Opacity = Math.Clamp(opacity, 0, 1) };
        brush.Freeze();
        return brush;
    }

    private static Brush ParseBrush(string color, Brush fallback)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(color)!;
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        catch { return fallback; }
    }

    private static void AddText(Canvas canvas, string text, Brush brush, double left, double top, double fontSize, FontWeight? weight = null)
    {
        var tb = new TextBlock { Text = text, Foreground = brush, FontFamily = BodyFont, FontSize = fontSize, FontWeight = weight ?? FontWeights.Normal };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, top);
        canvas.Children.Add(tb);
    }

    private static Size Measure(string text, double fontSize)
    {
        var ft = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(BodyFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            fontSize, Brushes.Black, 1.0);
        return new Size(ft.Width, ft.Height);
    }
}
