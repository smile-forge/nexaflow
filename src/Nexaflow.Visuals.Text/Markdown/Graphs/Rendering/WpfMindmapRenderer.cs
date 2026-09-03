using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a <see cref="Mindmap"/> as a WPF <see cref="FrameworkElement"/>, themed from a
/// <see cref="MarkdownPalette"/>.  The tree is laid out left/right-balanced around the root
/// (a "tidy tree" on each side); each top-level branch and its descendants share a colour,
/// connected to their parents by curved links.
/// </summary>
public static class WpfMindmapRenderer
{
    private static readonly FontFamily BodyFont = DiagramText.BodyFont;

    private const double LevelGap = 44;
    private const double VGap     = 16;
    private const double LineH    = 16;
    private const double PadX     = 12;
    private const double PadY     = 7;
    private const double Outer    = 16;
    private const double TitleH   = 26;

    // Theme (set per render — markdown renders synchronously on the UI thread).
    private static Brush Text = Brushes.White, Muted = Brushes.Gray, RootBorder = Brushes.SteelBlue, Bg = Brushes.Black;
    private static IReadOnlyList<Brush> Series = MarkdownPalette.DefaultSeries;
    private static Color RootColor = Colors.SteelBlue;

    public static FrameworkElement Render(Mindmap map, MarkdownPalette palette)
    {
        Text = palette.Text; Muted = palette.TextMuted; RootBorder = palette.Accent; Bg = palette.CodeBg;
        Series = palette.Series;
        RootColor = (palette.Accent as SolidColorBrush)?.Color ?? Colors.SteelBlue;

        if (map.Root is null)
            return new TextBlock { Text = "(empty mindmap)", Foreground = Muted, FontSize = 12 };

        bool hasTitle = !string.IsNullOrWhiteSpace(map.Title);
        var nodes = map.All().ToList();
        foreach (var n in nodes) SizeNode(n);

        Layout(map.Root, hasTitle);

        double minX = nodes.Min(n => n.X - n.W / 2), minY = nodes.Min(n => n.Y - n.H / 2);
        double dx = Outer - minX, dy = Outer + (hasTitle ? TitleH : 0) - minY;
        foreach (var n in nodes) { n.X += dx; n.Y += dy; }

        double canvasW = nodes.Max(n => n.X + n.W / 2) + Outer;
        double canvasH = nodes.Max(n => n.Y + n.H / 2) + Outer;
        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = Bg };

        // 1. connectors (behind nodes)
        foreach (var n in nodes)
        {
            if (n.Parent is null) continue;
            DrawConnector(canvas, n.Parent, n, ColorOf(n));
        }

        // 2. nodes
        foreach (var n in nodes)
            DrawNode(canvas, n);

        // 3. title
        if (hasTitle)
        {
            double tw = DiagramText.Measure(map.Title, 15);
            canvas.Children.Add(new TextBlock { Text = map.Title, Foreground = RootBorder, FontFamily = BodyFont, FontSize = 15, FontWeight = FontWeights.SemiBold }.At((canvasW - tw) / 2, Outer - 2));
        }

        return new Border
        {
            Background = Bg, BorderBrush = palette.CodeBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 8, 0, 12),
            Child = new ScrollViewer
            {
                Content = canvas,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
                MaxHeight = 600,
            },
        };
    }

    private static Brush ColorOf(MindmapNode n) =>
        n.BranchIndex < 0 ? RootBorder : Series[n.BranchIndex % Series.Count];

    // ── Layout ───────────────────────────────────────────────────────────────

    private static void Layout(MindmapNode root, bool hasTitle)
    {
        root.X = 0; root.Y = 0;
        if (root.Children.Count == 0) return;

        var (right, left) = Split(root.Children);

        double cursor = 0;
        foreach (var c in right) AssignY(c, ref cursor);
        var rightNodes = Collect(right);
        double rightTop = rightNodes.Min(n => n.Y - n.H / 2), rightBot = rightNodes.Max(n => n.Y + n.H / 2);

        cursor = 0;
        foreach (var c in left) AssignY(c, ref cursor);
        var leftNodes = Collect(left);
        double leftTop = leftNodes.Count > 0 ? leftNodes.Min(n => n.Y - n.H / 2) : 0;
        double leftBot = leftNodes.Count > 0 ? leftNodes.Max(n => n.Y + n.H / 2) : 0;

        double rootY = Math.Max(Math.Max(rightBot - rightTop, leftBot - leftTop), root.H) / 2;
        Shift(rightNodes, rootY - (rightTop + rightBot) / 2);
        if (leftNodes.Count > 0) Shift(leftNodes, rootY - (leftTop + leftBot) / 2);
        root.Y = rootY;

        foreach (var c in right) AssignX(c, root.X, root.W, +1);
        foreach (var c in left)  AssignX(c, root.X, root.W, -1);
    }

    private static (List<MindmapNode> right, List<MindmapNode> left) Split(List<MindmapNode> children)
    {
        var right = new List<MindmapNode>(); var left = new List<MindmapNode>();
        int rw = 0, lw = 0;
        foreach (var c in children)
        {
            int w = LeafCount(c);
            if (rw <= lw) { right.Add(c); rw += w; } else { left.Add(c); lw += w; }
        }
        return (right, left);
    }

    private static int LeafCount(MindmapNode n) =>
        n.Children.Count == 0 ? 1 : n.Children.Sum(LeafCount);

    private static void AssignY(MindmapNode node, ref double cursor)
    {
        if (node.Children.Count == 0)
        {
            node.Y = cursor + node.H / 2;
            cursor += node.H + VGap;
        }
        else
        {
            foreach (var c in node.Children) AssignY(c, ref cursor);
            node.Y = (node.Children[0].Y + node.Children[^1].Y) / 2;
        }
    }

    private static void AssignX(MindmapNode node, double parentX, double parentW, int sign)
    {
        node.X = parentX + sign * (parentW / 2 + LevelGap + node.W / 2);
        foreach (var c in node.Children) AssignX(c, node.X, node.W, sign);
    }

    private static List<MindmapNode> Collect(List<MindmapNode> roots)
    {
        var list = new List<MindmapNode>();
        void Dfs(MindmapNode n) { list.Add(n); foreach (var c in n.Children) Dfs(c); }
        foreach (var r in roots) Dfs(r);
        return list;
    }

    private static void Shift(List<MindmapNode> nodes, double dy)
    {
        foreach (var n in nodes) n.Y += dy;
    }

    // ── Sizing ───────────────────────────────────────────────────────────────

    private static void SizeNode(MindmapNode n)
    {
        var (tw, lines) = MeasureBlock(n.Text);
        double th = lines * LineH;
        n.W = tw + PadX * 2;
        n.H = th + PadY * 2;

        switch (n.Shape)
        {
            case MindmapShape.Circle:  { double d = Math.Max(Math.Max(tw, th) + 20, 42); n.W = d; n.H = d; break; }
            case MindmapShape.Hexagon: n.W += 20; break;
            case MindmapShape.Cloud:   n.W += 18; n.H += 6; break;
            case MindmapShape.Bang:    n.W += 16; n.H += 12; break;
        }
    }

    // ── Connectors ───────────────────────────────────────────────────────────

    private static void DrawConnector(Canvas canvas, MindmapNode parent, MindmapNode child, Brush color)
    {
        bool right = child.X >= parent.X;
        var from = new Point(parent.X + (right ? parent.W / 2 : -parent.W / 2), parent.Y);
        var to   = new Point(child.X  + (right ? -child.W / 2 : child.W / 2),   child.Y);
        double mx = (from.X + to.X) / 2;

        var fig = new PathFigure { StartPoint = from, IsFilled = false };
        fig.Segments.Add(new BezierSegment(new Point(mx, from.Y), new Point(mx, to.Y), to, isStroked: true));
        canvas.Children.Add(new Path
        {
            Data = new PathGeometry([fig]),
            Stroke = color,
            StrokeThickness = Math.Max(1.8, 3.6 - child.Depth * 0.5),
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
        });
    }

    // ── Nodes ────────────────────────────────────────────────────────────────

    private static void DrawNode(Canvas canvas, MindmapNode n)
    {
        Brush color = ColorOf(n);
        Color c = (color as SolidColorBrush)?.Color ?? RootColor;
        Brush fill = DiagramBrushes.Tint(c, n.Parent is null ? (byte)0x3A : (byte)0x22);
        double left = n.X - n.W / 2, top = n.Y - n.H / 2;

        switch (n.Shape)
        {
            case MindmapShape.Circle:
                canvas.Children.Add(new Ellipse { Width = n.W, Height = n.H, Fill = fill, Stroke = color, StrokeThickness = 2 }.At(left, top));
                break;
            case MindmapShape.Hexagon:
                canvas.Children.Add(Hexagon(n, fill, color));
                break;
            case MindmapShape.Cloud:
                canvas.Children.Add(new Ellipse { Width = n.W, Height = n.H, Fill = fill, Stroke = color, StrokeThickness = 2 }.At(left, top));
                break;
            case MindmapShape.Bang:
                canvas.Children.Add(Bang(n, fill, color));
                break;
            case MindmapShape.Square:
                canvas.Children.Add(new Rectangle { Width = n.W, Height = n.H, Fill = fill, Stroke = color, StrokeThickness = 1.8 }.At(left, top));
                break;
            default: // Default + Rounded
                double r = n.Shape == MindmapShape.Rounded ? n.H / 2 : 8;
                canvas.Children.Add(new Rectangle { Width = n.W, Height = n.H, RadiusX = r, RadiusY = r, Fill = fill, Stroke = color, StrokeThickness = 1.8 }.At(left, top));
                break;
        }

        var (_, lines) = MeasureBlock(n.Text);
        var tb = new TextBlock
        {
            Text = n.Text, Foreground = Text, FontFamily = BodyFont, FontSize = 12.5,
            FontWeight = n.Parent is null ? FontWeights.SemiBold : FontWeights.Normal,
            Width = n.W - 8, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.NoWrap,
        };
        Canvas.SetLeft(tb, n.X - (n.W - 8) / 2);
        Canvas.SetTop(tb, n.Y - lines * LineH / 2);
        canvas.Children.Add(tb);
    }

    private static Polygon Hexagon(MindmapNode n, Brush fill, Brush stroke)
    {
        double hw = n.W / 2, hh = n.H / 2, qw = hw * 0.3;
        return new Polygon
        {
            Points = new PointCollection
            {
                new(n.X - hw,      n.Y), new(n.X - hw + qw, n.Y - hh), new(n.X + hw - qw, n.Y - hh),
                new(n.X + hw,      n.Y), new(n.X + hw - qw, n.Y + hh), new(n.X - hw + qw, n.Y + hh),
            },
            Fill = fill, Stroke = stroke, StrokeThickness = 1.8,
        };
    }

    private static Polygon Bang(MindmapNode n, Brush fill, Brush stroke)
    {
        const int spikes = 10;
        double outerW = n.W / 2, outerH = n.H / 2, innerW = outerW * 0.78, innerH = outerH * 0.78;
        var pts = new PointCollection();
        for (int i = 0; i < spikes * 2; i++)
        {
            double ang = Math.PI * i / spikes - Math.PI / 2;
            bool outer = i % 2 == 0;
            pts.Add(new Point(n.X + (outer ? outerW : innerW) * Math.Cos(ang), n.Y + (outer ? outerH : innerH) * Math.Sin(ang)));
        }
        return new Polygon { Points = pts, Fill = fill, Stroke = stroke, StrokeThickness = 1.6 };
    }

    // ── Colour & text helpers ─────────────────────────────────────────────────


    private static (double w, int lines) MeasureBlock(string text)
    {
        var parts = text.Split('\n');
        double w = 0;
        foreach (var p in parts) w = Math.Max(w, DiagramText.Measure(p, 12.5));
        return (w, parts.Length);
    }

}
