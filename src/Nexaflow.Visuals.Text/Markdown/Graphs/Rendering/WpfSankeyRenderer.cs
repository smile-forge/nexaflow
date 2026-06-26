using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a Mermaid <see cref="SankeyDiagram"/> as a flow diagram, themed from a
/// <see cref="MarkdownPalette"/> and configured by <see cref="SankeyConfig"/>.  Nodes are layered left→right
/// (longest-path depth, adjusted by <see cref="SankeyConfig.NodeAlignment"/>), sized by their throughput,
/// and connected by bezier ribbons whose thickness is the link value and whose colour follows
/// <see cref="SankeyConfig.LinkColor"/> (source / target / gradient / fixed).  Per-node colours come from
/// <see cref="SankeyConfig.NodeColors"/> or the palette's series bank.
/// </summary>
public static class WpfSankeyRenderer
{
    private static readonly FontFamily BodyFont = new("Segoe UI");
    private const double Pad = 12, TitleFont = 15, LabelFont = 12, ValueFont = 10, LabelGap = 6;

    public static FrameworkElement Render(SankeyDiagram diagram, MarkdownPalette palette)
    {
        var cfg = diagram.Config;
        int n = diagram.Nodes.Count;
        Brush bg = palette.CodeBg, border = palette.CodeBorder;

        bool hasTitle = !string.IsNullOrWhiteSpace(diagram.Title);
        double titleH = hasTitle ? TitleFont + 8 : 0;

        if (n == 0)
            return Frame(new Canvas { Width = 120, Height = 60, Background = bg }, bg, border);

        var links = diagram.Links;
        var outLinks = new List<int>[n];
        var inLinks = new List<int>[n];
        for (int i = 0; i < n; i++) { outLinks[i] = []; inLinks[i] = []; }
        for (int li = 0; li < links.Count; li++) { outLinks[links[li].Source].Add(li); inLinks[links[li].Target].Add(li); }

        // Node throughput.
        var val = new double[n];
        for (int i = 0; i < n; i++)
        {
            double outSum = outLinks[i].Sum(li => links[li].Value);
            double inSum  = inLinks[i].Sum(li => links[li].Value);
            val[i] = Math.Max(Math.Max(outSum, inSum), 1e-6);
        }

        // Layers: longest-path depth (from sources) and height (to sinks).
        var depth = new int[n]; var height = new int[n];
        Array.Fill(depth, -1); Array.Fill(height, -1);
        for (int i = 0; i < n; i++) { Depth(i); Height(i); }
        int maxLayer = depth.Length > 0 ? depth.Max() : 0;

        var layer = new int[n];
        for (int i = 0; i < n; i++)
            layer[i] = cfg.NodeAlignment switch
            {
                SankeyNodeAlignment.Left   => depth[i],
                SankeyNodeAlignment.Right  => maxLayer - height[i],
                SankeyNodeAlignment.Center => (int)Math.Round((depth[i] + (maxLayer - height[i])) / 2.0),
                _                          => outLinks[i].Count == 0 ? maxLayer : depth[i],   // Justify
            };
        int numLayers = layer.Max() + 1;
        var byLayer = new List<int>[numLayers];
        for (int L = 0; L < numLayers; L++) byLayer[L] = [];
        for (int i = 0; i < n; i++) byLayer[layer[i]].Add(i);

        // Vertical scale so the densest layer fits.
        double plotH = Math.Max(cfg.Height - 2 * Pad - titleH, 120);
        double scale = double.PositiveInfinity;
        foreach (var nodes in byLayer)
        {
            double sum = nodes.Sum(i => val[i]);
            double padL = Math.Max(nodes.Count - 1, 0) * cfg.NodePadding;
            if (sum > 0) scale = Math.Min(scale, (plotH - padL) / sum);
        }
        if (double.IsInfinity(scale) || scale <= 0) scale = 1;

        var h = new double[n];
        for (int i = 0; i < n; i++) h[i] = Math.Max(val[i] * scale, 1);

        double layerGap = numLayers > 1 ? Math.Max(110, cfg.Width / numLayers) : 0;
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = layer[i] * layerGap;

        // Stack nodes within each layer, centred vertically.
        var y = new double[n];
        foreach (var nodes in byLayer)
        {
            double layerH = nodes.Sum(i => h[i]) + Math.Max(nodes.Count - 1, 0) * cfg.NodePadding;
            double cursor = (plotH - layerH) / 2;
            foreach (int i in nodes) { y[i] = cursor; cursor += h[i] + cfg.NodePadding; }
        }

        double Center(int i) => y[i] + h[i] / 2;
        for (int i = 0; i < n; i++)
        {
            outLinks[i].Sort((a, b) => Center(links[a].Target).CompareTo(Center(links[b].Target)));
            inLinks[i].Sort((a, b) => Center(links[a].Source).CompareTo(Center(links[b].Source)));
        }

        // Link band endpoints (stacked on each node edge).
        var sy = new double[links.Count]; var ty = new double[links.Count]; var th = new double[links.Count];
        var srcOff = new double[n]; var tgtOff = new double[n];
        for (int i = 0; i < n; i++)
            foreach (int li in outLinks[i]) { th[li] = links[li].Value * scale; sy[li] = y[i] + srcOff[i]; srcOff[i] += th[li]; }
        for (int i = 0; i < n; i++)
            foreach (int li in inLinks[i]) { ty[li] = y[i] + tgtOff[i]; tgtOff[i] += links[li].Value * scale; }

        // Colours.
        var nodeBrush = new Brush[n];
        for (int i = 0; i < n; i++)
            nodeBrush[i] = cfg.NodeColors.TryGetValue(diagram.Nodes[i].Name, out var nb) ? nb : palette.Series[i % palette.Series.Count];

        // Labels + horizontal bounds.
        double plotW = numLayers > 1 ? (numLayers - 1) * layerGap + cfg.NodeWidth : cfg.NodeWidth;
        var labelTextW = new double[n]; var labelRight = new bool[n];
        for (int i = 0; i < n; i++)
        {
            string name = diagram.Nodes[i].Name;
            double w = Measure(name, LabelFont).Width;
            if (cfg.ShowValues) w = Math.Max(w, Measure(ValueText(cfg, val[i]), ValueFont).Width);
            labelTextW[i] = w;
            labelRight[i] = x[i] + cfg.NodeWidth / 2 < plotW / 2;
        }

        double minX = 0, maxX = plotW;
        for (int i = 0; i < n; i++)
        {
            if (labelRight[i]) maxX = Math.Max(maxX, x[i] + cfg.NodeWidth + LabelGap + labelTextW[i]);
            else minX = Math.Min(minX, x[i] - LabelGap - labelTextW[i]);
        }

        double offsetX = Pad - minX;
        double canvasW = (maxX - minX) + 2 * Pad;
        double plotTop = Pad + titleH;
        double canvasH = plotTop + plotH + Pad;

        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = bg };

        if (hasTitle)
        {
            var sz = Measure(diagram.Title, TitleFont);
            AddText(canvas, diagram.Title, palette.Heading, (canvasW - sz.Width) / 2, Pad, TitleFont, FontWeights.SemiBold);
        }

        // Links (behind nodes).
        for (int li = 0; li < links.Count; li++)
        {
            double x0 = offsetX + x[links[li].Source] + cfg.NodeWidth;
            double x1 = offsetX + x[links[li].Target];
            double a = plotTop + sy[li], b = plotTop + ty[li];
            canvas.Children.Add(new Path
            {
                Data = Ribbon(x0, a, x1, b, th[li]),
                Fill = LinkBrush(cfg, nodeBrush[links[li].Source], nodeBrush[links[li].Target]),
            });
        }

        // Nodes.
        for (int i = 0; i < n; i++)
        {
            var rect = new Rectangle { Width = cfg.NodeWidth, Height = h[i], Fill = nodeBrush[i] };
            Canvas.SetLeft(rect, offsetX + x[i]);
            Canvas.SetTop(rect, plotTop + y[i]);
            canvas.Children.Add(rect);
        }

        // Labels.
        for (int i = 0; i < n; i++)
            AddLabel(canvas, diagram, cfg, palette, i, offsetX + x[i], plotTop + Center(i), labelRight[i], labelTextW[i], val[i]);

        return Frame(canvas, bg, border);

        // ── local recursive layer helpers ──
        int Depth(int node)
        {
            if (depth[node] >= 0) return depth[node];
            depth[node] = 0;   // cycle guard: provisional
            int d = 0;
            foreach (int li in inLinks[node]) d = Math.Max(d, Depth(links[li].Source) + 1);
            return depth[node] = d;
        }
        int Height(int node)
        {
            if (height[node] >= 0) return height[node];
            height[node] = 0;
            int hh = 0;
            foreach (int li in outLinks[node]) hh = Math.Max(hh, Height(links[li].Target) + 1);
            return height[node] = hh;
        }
    }

    // ── Drawing ───────────────────────────────────────────────────────────────

    private static void AddLabel(Canvas canvas, SankeyDiagram diagram, SankeyConfig cfg, MarkdownPalette palette,
        int i, double nodeLeft, double centerY, bool right, double textW, double value)
    {
        string name = diagram.Nodes[i].Name;
        var tb = new TextBlock { FontFamily = BodyFont, FontSize = LabelFont, Foreground = palette.Text };
        tb.Inlines.Add(new System.Windows.Documents.Run(name));
        double blockH = Measure(name, LabelFont).Height;
        if (cfg.ShowValues)
        {
            tb.Inlines.Add(new System.Windows.Documents.LineBreak());
            tb.Inlines.Add(new System.Windows.Documents.Run(ValueText(cfg, value)) { Foreground = palette.TextMuted, FontSize = ValueFont });
            blockH += Measure(ValueText(cfg, value), ValueFont).Height;
        }
        tb.TextAlignment = right ? TextAlignment.Left : TextAlignment.Right;

        FrameworkElement element = tb;
        if (cfg.LabelStyle == SankeyLabelStyle.Outlined)
            element = new Border
            {
                Background = palette.CodeBg, BorderBrush = palette.CodeBorder, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 1, 3, 1), Child = tb,
            };

        double pad = cfg.LabelStyle == SankeyLabelStyle.Outlined ? 4 : 0;
        double left = right ? nodeLeft + cfg.NodeWidth + LabelGap - pad : nodeLeft - LabelGap - textW - pad;
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, centerY - blockH / 2 - pad);
        canvas.Children.Add(element);
    }

    private static Geometry Ribbon(double x0, double sy, double x1, double ty, double th)
    {
        double xm = (x0 + x1) / 2;
        var fig = new PathFigure { StartPoint = new Point(x0, sy), IsClosed = true, IsFilled = true };
        fig.Segments.Add(new BezierSegment(new Point(xm, sy), new Point(xm, ty), new Point(x1, ty), true));
        fig.Segments.Add(new LineSegment(new Point(x1, ty + th), true));
        fig.Segments.Add(new BezierSegment(new Point(xm, ty + th), new Point(xm, sy + th), new Point(x0, sy + th), true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        geo.Freeze();
        return geo;
    }

    private static Brush LinkBrush(SankeyConfig cfg, Brush srcColor, Brush tgtColor)
    {
        switch (cfg.LinkColor)
        {
            case SankeyLinkColor.Source: return Translucent(srcColor, 0.45);
            case SankeyLinkColor.Target: return Translucent(tgtColor, 0.45);
            case SankeyLinkColor.Custom: return Translucent(cfg.LinkColorCustom ?? srcColor, 0.45);
            default:
                var g = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5), Opacity = 0.5,
                };
                g.GradientStops.Add(new GradientStop(ColorOf(srcColor), 0));
                g.GradientStops.Add(new GradientStop(ColorOf(tgtColor), 1));
                g.Freeze();
                return g;
        }
    }

    private static string ValueText(SankeyConfig cfg, double value) => cfg.Prefix + Fmt(value) + cfg.Suffix;

    private static Border Frame(Canvas canvas, Brush bg, Brush border) => new()
    {
        Background = bg, BorderBrush = border, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 8, 0, 12), Child = canvas,
    };

    private static Color ColorOf(Brush b) => (b as SolidColorBrush)?.Color ?? Colors.Gray;

    private static Brush Translucent(Brush b, double opacity)
    {
        var brush = new SolidColorBrush(ColorOf(b)) { Opacity = Math.Clamp(opacity, 0, 1) };
        brush.Freeze();
        return brush;
    }

    private static void AddText(Canvas canvas, string text, Brush brush, double left, double top, double fontSize, FontWeight? weight = null)
    {
        var tb = new TextBlock { Text = text, Foreground = brush, FontFamily = BodyFont, FontSize = fontSize, FontWeight = weight ?? FontWeights.Normal };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, top);
        canvas.Children.Add(tb);
    }

    private static string Fmt(double v) =>
        Math.Abs(v - Math.Round(v)) < 1e-9
            ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture)
            : v.ToString("0.###", CultureInfo.InvariantCulture);

    private static Size Measure(string text, double fontSize)
    {
        var ft = new FormattedText(
            text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(BodyFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            fontSize, Brushes.Black, 1.0);
        return new Size(ft.Width, ft.Height);
    }
}
