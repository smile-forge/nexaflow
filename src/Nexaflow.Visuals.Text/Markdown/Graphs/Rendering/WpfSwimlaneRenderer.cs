using Nexaflow.Visuals.Text.Markdown.Graphs;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a swimlane <see cref="Graph"/> (parsed by <see cref="Parsers.MermaidSwimlaneParser"/>) as a
/// WPF <see cref="FrameworkElement"/>, themed from a <see cref="MarkdownPalette"/>.  Each top-level
/// subgraph becomes a lane band — horizontal bands stacked vertically for <c>TB</c>/<c>BT</c>, vertical
/// columns for <c>LR</c>/<c>RL</c> — with its nodes flowing along the lane and edges (including
/// cross-lane ones) drawn between node centres.  Output tree: <c>Border → ScrollViewer → Canvas</c>.
/// </summary>
public static class WpfSwimlaneRenderer
{
    private static readonly FontFamily BodyFont = DiagramText.BodyFont;

    private const double NodeW    = 128;
    private const double NodeH    = 50;
    private const double Gap      = 28;
    private const double LanePad  = 14;
    private const double LaneLabel = 30;   // band header strip (left for TB, top for LR)
    private const double Margin   = 16;
    private const double TitleH   = 28;

    public static FrameworkElement Render(Graph graph, MarkdownPalette palette)
    {
        bool horizontalFlow = graph.Direction is GraphDirection.LeftRight or GraphDirection.RightLeft;

        // Lanes = top-level subgraphs; nodes not in any lane fall into a trailing synthetic lane.
        var lanes = graph.Subgraphs.Where(sg => sg.ParentId is null).ToList();
        var laneOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < lanes.Count; i++)
            foreach (var id in lanes[i].NodeIds) laneOf.TryAdd(id, i);

        var orphans = graph.Nodes.Where(n => !laneOf.ContainsKey(n.Id)).Select(n => n.Id).ToList();
        int laneCount = lanes.Count + (orphans.Count > 0 ? 1 : 0);
        if (laneCount == 0) laneCount = 1;

        // Ordered node ids per lane (declaration order within the graph).
        var laneNodeIds = new List<string>[laneCount];
        for (int i = 0; i < laneCount; i++) laneNodeIds[i] = [];
        foreach (var n in graph.Nodes)
            laneNodeIds[laneOf.TryGetValue(n.Id, out int li) ? li : lanes.Count].Add(n.Id);

        int maxNodesInLane = laneNodeIds.Max(l => l.Count);
        maxNodesInLane = Math.Max(maxNodesInLane, 1);

        bool hasTitle = !string.IsNullOrWhiteSpace(graph.Title);
        double titleY = Margin + (hasTitle ? TitleH : 0);

        var pos = new Dictionary<string, Rect>(StringComparer.Ordinal);
        double canvasW, canvasH;

        if (!horizontalFlow)
        {
            // Horizontal bands: label strip on the left, nodes flow left→right; lanes stacked vertically.
            double bandH  = NodeH + LanePad * 2;
            double laneW  = LaneLabel + LanePad + maxNodesInLane * (NodeW + Gap);
            for (int i = 0; i < laneCount; i++)
            {
                double top = titleY + i * bandH;
                var ids = laneNodeIds[i];
                for (int j = 0; j < ids.Count; j++)
                    pos[ids[j]] = new Rect(LaneLabel + LanePad + j * (NodeW + Gap), top + LanePad, NodeW, NodeH);
            }
            canvasW = Margin + laneW + Margin;
            canvasH = titleY + laneCount * bandH + Margin;
        }
        else
        {
            // Vertical columns: label strip on top, nodes flow top→bottom; lanes side by side.
            double colW  = NodeW + LanePad * 2;
            double laneH = LaneLabel + LanePad + maxNodesInLane * (NodeH + Gap);
            for (int i = 0; i < laneCount; i++)
            {
                double left = Margin + i * colW;
                var ids = laneNodeIds[i];
                for (int j = 0; j < ids.Count; j++)
                    pos[ids[j]] = new Rect(left + LanePad, titleY + LaneLabel + LanePad + j * (NodeH + Gap), NodeW, NodeH);
            }
            canvasW = Margin + laneCount * colW + Margin;
            canvasH = titleY + laneH + Margin;
        }

        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = palette.CodeBg };

        if (hasTitle)
        {
            var tb = new TextBlock
            {
                Text = graph.Title, Foreground = palette.Heading, FontFamily = BodyFont,
                FontSize = 15, FontWeight = FontWeights.SemiBold,
            };
            Canvas.SetLeft(tb, Margin);
            Canvas.SetTop(tb, Margin - 2);
            canvas.Children.Add(tb);
        }

        // Lane bands (alternating tint) + labels.
        for (int i = 0; i < laneCount; i++)
        {
            string label = i < lanes.Count ? (string.IsNullOrEmpty(lanes[i].Label) ? lanes[i].Id : lanes[i].Label) : "";
            AddLaneBand(canvas, i, label, horizontalFlow, laneCount, maxNodesInLane, titleY, palette);
        }

        // Edges beneath nodes.
        foreach (var e in graph.Edges)
        {
            if (!pos.TryGetValue(e.SourceId, out var sr) || !pos.TryGetValue(e.TargetId, out var tr)) continue;
            AddEdge(canvas, sr, tr, e, palette);
        }

        // Nodes on top.
        foreach (var n in graph.Nodes)
            if (pos.TryGetValue(n.Id, out var r))
                AddNode(canvas, n, r, palette);

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility   = ScrollBarVisibility.Auto,
            Content = canvas,
        };
        return new Border
        {
            Background = palette.CodeBg, BorderBrush = palette.CodeBorder, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 8, 0, 12), Child = scroller,
        };
    }

    // ── Lane bands ─────────────────────────────────────────────────────────

    private static void AddLaneBand(Canvas canvas, int index, string label, bool horizontalFlow,
        int laneCount, int maxNodes, double titleY, MarkdownPalette palette)
    {
        Brush fill = index % 2 == 0 ? palette.TableHeaderBg : palette.TableAltRowBg;
        double x, y, w, h;

        if (!horizontalFlow)
        {
            double bandH = NodeH + LanePad * 2;
            x = Margin; y = titleY + index * bandH;
            w = LaneLabel + LanePad + maxNodes * (NodeW + Gap); h = bandH;
        }
        else
        {
            double colW = NodeW + LanePad * 2;
            x = Margin + index * colW; y = titleY;
            w = colW; h = LaneLabel + LanePad + maxNodes * (NodeH + Gap);
        }

        var band = new Border
        {
            Width = w, Height = h, Background = fill,
            BorderBrush = palette.CodeBorder, BorderThickness = new Thickness(0.5),
        };
        Canvas.SetLeft(band, x);
        Canvas.SetTop(band, y);
        canvas.Children.Add(band);

        if (string.IsNullOrEmpty(label)) return;
        var lbl = new TextBlock
        {
            Text = label, Foreground = palette.TextMuted, FontFamily = BodyFont,
            FontSize = 11, FontWeight = FontWeights.SemiBold,
        };
        double tw = DiagramText.Measure(label, 11);
        if (!horizontalFlow)
        {
            // Rotated label centred vertically in the band's left strip (LayoutTransform swaps the
            // element's width/height, so the arranged slot is ~fontHeight wide × text-width tall).
            lbl.LayoutTransform = new RotateTransform(-90);
            Canvas.SetLeft(lbl, x + 5);
            Canvas.SetTop(lbl, y + Math.Max(2, (h - tw) / 2));
        }
        else
        {
            Canvas.SetLeft(lbl, x + w / 2 - tw / 2);
            Canvas.SetTop(lbl, y + 8);
        }
        canvas.Children.Add(lbl);
    }

    // ── Nodes ────────────────────────────────────────────────────────────────

    private static void AddNode(Canvas canvas, Node n, Rect r, MarkdownPalette palette)
    {
        Brush stroke = ParseBrush(n.StrokeColor, palette.Accent);
        Brush fill   = ParseBrush(n.FillColor, palette.TableHeaderBg);
        Brush text   = ParseBrush(n.TextColor, palette.Text);
        string label = string.IsNullOrEmpty(n.Label) ? n.Id : n.Label;

        var tb = new TextBlock
        {
            Text = label, Foreground = text, FontFamily = BodyFont, FontSize = 12,
            TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
            MaxWidth = r.Width - 12, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        if (n.Shape is NodeShape.Diamond or NodeShape.Hexagon)
        {
            var grid = new Grid { Width = r.Width, Height = r.Height };
            var poly = new Polygon
            {
                Fill = fill, Stroke = stroke, StrokeThickness = 1.2,
                Points = [new Point(r.Width / 2, 0), new Point(r.Width, r.Height / 2),
                          new Point(r.Width / 2, r.Height), new Point(0, r.Height / 2)],
            };
            grid.Children.Add(poly);
            grid.Children.Add(tb);
            Canvas.SetLeft(grid, r.X);
            Canvas.SetTop(grid, r.Y);
            canvas.Children.Add(grid);
            return;
        }

        double radius = n.Shape switch
        {
            NodeShape.Stadium                          => r.Height / 2,
            NodeShape.RoundedRect                      => 12,
            NodeShape.Circle or NodeShape.DoubleCircle => r.Height / 2,
            _                                          => 3,
        };
        var border = new Border
        {
            Width = r.Width, Height = r.Height, Background = fill, BorderBrush = stroke,
            BorderThickness = new Thickness(n.Shape is NodeShape.DoubleCircle ? 2.5 : 1.2),
            CornerRadius = new CornerRadius(radius), Child = tb,
        };
        Canvas.SetLeft(border, r.X);
        Canvas.SetTop(border, r.Y);
        canvas.Children.Add(border);
    }

    // ── Edges ────────────────────────────────────────────────────────────────

    private static void AddEdge(Canvas canvas, Rect sr, Rect tr, Edge e, MarkdownPalette palette)
    {
        var from = new Point(sr.Left + sr.Width / 2, sr.Top + sr.Height / 2);
        var to   = new Point(tr.Left + tr.Width / 2, tr.Top + tr.Height / 2);
        from = EdgePoint(sr, to);
        to   = EdgePoint(tr, from);

        var line = new Line
        {
            X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y,
            Stroke = palette.TextMuted,
            StrokeThickness = e.Style == EdgeStyle.Thick ? 3 : 1.4,
        };
        if (e.Style == EdgeStyle.Dashed) line.StrokeDashArray = [5, 3];
        if (e.Style == EdgeStyle.Dotted) line.StrokeDashArray = [1.5, 3];
        canvas.Children.Add(line);

        if (e.Arrow != EdgeArrow.None)   AddArrowHead(canvas, from, to, palette.TextMuted);
        if (e.StartArrow != EdgeArrow.None) AddArrowHead(canvas, to, from, palette.TextMuted);

        if (!string.IsNullOrWhiteSpace(e.Label))
        {
            double w = DiagramText.Measure(e.Label, 10);
            var lbl = new Border
            {
                Background = palette.CodeBg, Padding = new Thickness(3, 0, 3, 0),
                Child = new TextBlock { Text = e.Label, Foreground = palette.TextMuted, FontFamily = BodyFont, FontSize = 10 },
            };
            Canvas.SetLeft(lbl, (from.X + to.X) / 2 - w / 2 - 3);
            Canvas.SetTop(lbl, (from.Y + to.Y) / 2 - 8);
            canvas.Children.Add(lbl);
        }
    }

    /// <summary>Where the line from a box centre toward <paramref name="toward"/> crosses the box border.</summary>
    private static Point EdgePoint(Rect box, Point toward)
    {
        var c = new Point(box.Left + box.Width / 2, box.Top + box.Height / 2);
        double dx = toward.X - c.X, dy = toward.Y - c.Y;
        if (dx == 0 && dy == 0) return c;
        double hw = box.Width / 2, hh = box.Height / 2;
        double scale = 1.0 / Math.Max(Math.Abs(dx) / hw, Math.Abs(dy) / hh);
        return new Point(c.X + dx * scale, c.Y + dy * scale);
    }

    private static void AddArrowHead(Canvas canvas, Point from, Point to, Brush brush)
    {
        double angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        const double len = 9, spread = 0.5;
        var p1 = new Point(to.X - len * Math.Cos(angle - spread), to.Y - len * Math.Sin(angle - spread));
        var p2 = new Point(to.X - len * Math.Cos(angle + spread), to.Y - len * Math.Sin(angle + spread));
        canvas.Children.Add(new Polygon { Fill = brush, Points = [to, p1, p2] });
    }

    // ── Helpers ────────────────────────────────────────────────────────────


    private static Brush ParseBrush(string? color, Brush fallback)
    {
        if (string.IsNullOrWhiteSpace(color)) return fallback;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(color)!;
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        catch { return fallback; }
    }
}
