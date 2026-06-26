using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a Mermaid <see cref="IshikawaDiagram"/> as a fishbone (cause-and-effect) chart, themed from a
/// <see cref="MarkdownPalette"/>.  A horizontal spine points right into the effect (head) box; the
/// categories are diagonal bones alternating above/below the spine, each labelled in a coloured chip,
/// with their nested causes listed as an indented outline.  Each category takes a distinct
/// <see cref="MarkdownPalette.Series"/> colour.
/// </summary>
public static class WpfIshikawaRenderer
{
    private static readonly FontFamily BodyFont = new("Segoe UI");
    private const double TitleFont = 14, HeadFont = 14, CatFont = 12, CauseFont = 11;
    private const double LineH = 16, BoneRise = 24, Indent = 12;

    public static FrameworkElement Render(IshikawaDiagram diagram, MarkdownPalette palette)
    {
        double pad = diagram.Config.DiagramPadding;
        Brush bg = palette.CodeBg;
        Brush border = palette.CodeBorder;
        Brush spineBrush = palette.TextMuted;
        var series = palette.Series;
        Brush CatColor(int i) => series[i % series.Count];

        bool hasTitle = !string.IsNullOrWhiteSpace(diagram.Title);
        double titleH = hasTitle ? TitleFont + 10 : 0;

        // ── Measure each category cluster ───────────────────────────────────
        var clusters = new List<Cluster>();
        for (int k = 0; k < diagram.Categories.Count; k++)
        {
            var rows = new List<(int depth, string text)>();
            foreach (var child in diagram.Categories[k].Children) Flatten(child, 1, rows);

            var labelSz = Measure(diagram.Categories[k].Text, CatFont);
            double labelW = labelSz.Width + 16, labelH = labelSz.Height + 8;

            double outlineW = 0;
            foreach (var (depth, text) in rows)
                outlineW = Math.Max(outlineW, (depth - 1) * Indent + Measure(Bullet(depth) + " " + text, CauseFont).Width);
            double outlineH = rows.Count * LineH;

            double clusterW = Math.Max(labelW, outlineW);
            double reach = BoneRise + labelH + (rows.Count > 0 ? 6 + outlineH : 0);
            clusters.Add(new Cluster(rows, labelW, labelH, clusterW, outlineH, reach, top: k % 2 == 0));
        }

        double topReach    = clusters.Where(c => c.Top).Select(c => c.Reach).DefaultIfEmpty(0).Max();
        double bottomReach = clusters.Where(c => !c.Top).Select(c => c.Reach).DefaultIfEmpty(0).Max();

        // ── Horizontal slot layout ──────────────────────────────────────────
        double cursor = pad + 8;
        foreach (var c in clusters)
        {
            c.SlotW = Math.Max(c.ClusterW, 56) + 26;
            c.Sx = cursor + c.SlotW / 2;
            cursor += c.SlotW;
        }

        var headSz = Measure(string.IsNullOrEmpty(diagram.Head) ? " " : diagram.Head, HeadFont);
        double headW = headSz.Width + 24, headH = headSz.Height + 16;
        double headX = (clusters.Count > 0 ? cursor : pad + 40) + 16;
        double spineStartX = pad;
        double canvasW = headX + headW + pad;

        double topRoom    = Math.Max(topReach, headH / 2 + 8);
        double bottomRoom = Math.Max(bottomReach, headH / 2 + 8);
        double cy = pad + titleH + topRoom;
        double canvasH = cy + bottomRoom + pad;

        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = bg };

        // ── Title ───────────────────────────────────────────────────────────
        if (hasTitle)
        {
            var sz = Measure(diagram.Title, TitleFont);
            AddText(canvas, diagram.Title, palette.Heading, (canvasW - sz.Width) / 2, pad, TitleFont, FontWeights.SemiBold);
        }

        // ── Spine + arrowhead into the head box ─────────────────────────────
        AddLine(canvas, spineStartX, cy, headX, cy, spineBrush, 2);
        var arrow = new Polygon { Fill = spineBrush };
        arrow.Points.Add(new Point(headX, cy));
        arrow.Points.Add(new Point(headX - 9, cy - 5));
        arrow.Points.Add(new Point(headX - 9, cy + 5));
        canvas.Children.Add(arrow);

        // ── Head (effect) box ───────────────────────────────────────────────
        AddBox(canvas, diagram.Head, headX, cy - headH / 2, headW, headH, palette.Accent, palette.Accent, OnAccent, HeadFont, true);

        // ── Category bones ──────────────────────────────────────────────────
        for (int k = 0; k < clusters.Count; k++)
        {
            var c = clusters[k];
            Brush col = CatColor(k);
            double sign = c.Top ? -1 : 1;
            double tipX = c.Sx - 16;
            double tipY = cy + sign * BoneRise;

            AddLine(canvas, c.Sx, cy, tipX, tipY, col, 2);

            double labelY = c.Top ? tipY - c.LabelH : tipY;
            AddBox(canvas, diagram.Categories[k].Text, tipX - c.LabelW / 2, labelY, c.LabelW, c.LabelH,
                Tint(col, 0x33), col, palette.Text, CatFont, true);

            if (c.Rows.Count == 0) continue;

            double outlineTop = c.Top ? labelY - 6 - c.OutlineH : labelY + c.LabelH + 6;
            double connFrom   = c.Top ? labelY : labelY + c.LabelH;
            double connTo     = c.Top ? outlineTop + c.OutlineH : outlineTop;
            AddLine(canvas, tipX, connFrom, tipX, connTo, col, 1);

            double blockLeft = tipX - c.ClusterW / 2;
            for (int i = 0; i < c.Rows.Count; i++)
            {
                var (depth, text) = c.Rows[i];
                Brush rowBrush = depth == 1 ? palette.Text : palette.TextMuted;
                AddText(canvas, Bullet(depth) + " " + text, rowBrush,
                    blockLeft + (depth - 1) * Indent, outlineTop + i * LineH, CauseFont);
            }
        }

        return new Border
        {
            Background = bg, BorderBrush = border, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 8, 0, 12), Child = canvas,
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class Cluster(List<(int depth, string text)> rows, double labelW, double labelH,
        double clusterW, double outlineH, double reach, bool top)
    {
        public List<(int depth, string text)> Rows { get; } = rows;
        public double LabelW { get; } = labelW;
        public double LabelH { get; } = labelH;
        public double ClusterW { get; } = clusterW;
        public double OutlineH { get; } = outlineH;
        public double Reach { get; } = reach;
        public bool Top { get; } = top;
        public double SlotW { get; set; }
        public double Sx { get; set; }
    }

    private static readonly Brush OnAccent = Frozen(Color.FromRgb(0xF5, 0xF8, 0xFF));

    private static void Flatten(IshikawaNode node, int depth, List<(int, string)> rows)
    {
        rows.Add((depth, node.Text));
        foreach (var child in node.Children) Flatten(child, depth + 1, rows);
    }

    private static string Bullet(int depth) => depth == 1 ? "•" : depth == 2 ? "◦" : "·";

    private static void AddBox(Canvas canvas, string text, double x, double y, double w, double h,
        Brush fill, Brush stroke, Brush textBrush, double fontSize, bool bold)
    {
        var box = new Border
        {
            Width = w, Height = h, Background = fill, BorderBrush = stroke, BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock
            {
                Text = text, Foreground = textBrush, FontFamily = BodyFont, FontSize = fontSize,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);
        canvas.Children.Add(box);
    }

    private static void AddText(Canvas canvas, string text, Brush brush, double left, double top, double fontSize, FontWeight? weight = null)
    {
        var tb = new TextBlock
        {
            Text = text, Foreground = brush, FontFamily = BodyFont, FontSize = fontSize,
            FontWeight = weight ?? FontWeights.Normal,
        };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, top);
        canvas.Children.Add(tb);
    }

    private static void AddLine(Canvas canvas, double x1, double y1, double x2, double y2, Brush brush, double thickness)
    {
        canvas.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = thickness });
    }

    private static Brush Tint(Brush b, byte alpha)
    {
        Color c = (b as SolidColorBrush)?.Color ?? Colors.Gray;
        return Frozen(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
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
