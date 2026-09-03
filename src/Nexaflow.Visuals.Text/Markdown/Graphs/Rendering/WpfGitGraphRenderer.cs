using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

/// <summary>
/// Renders a <see cref="GitGraph"/> as a WPF <see cref="FrameworkElement"/>, themed from a
/// <see cref="MarkdownPalette"/>.  Each branch is a coloured lane; commits are nodes connected
/// to their parents (branch-offs and merges drawn as curves), with branch labels, commit tags
/// and explicit commit ids.  Supports LR (default), TB and BT orientations.
/// </summary>
public static class WpfGitGraphRenderer
{
    private static readonly FontFamily BodyFont = DiagramText.BodyFont;

    private const double ColGap   = 50;   // spacing along the time axis (per position)
    private const double LaneGap  = 46;   // spacing across lanes (per branch)
    private const double R        = 9;    // commit node radius
    private const double Outer    = 16;
    private const double TitleH   = 26;

    public static FrameworkElement Render(GitGraph graph, MarkdownPalette palette)
    {
        Brush bgBrush     = palette.CodeBg;
        Brush borderBrush = palette.CodeBorder;
        Brush titleBrush  = palette.Heading;
        Brush textBrush   = palette.Text;
        Brush mutedBrush  = palette.TextMuted;
        var   series      = palette.Series;
        Brush Branch(int lane) => series[lane % series.Count];

        if (graph.Commits.Count == 0)
            return new TextBlock { Text = "(empty git graph)", Foreground = mutedBrush, FontSize = 12 };

        bool hasTitle = !string.IsNullOrWhiteSpace(graph.Title);
        bool vertical = graph.Orientation != GitOrientation.LeftRight;
        int  maxPos   = graph.MaxPosition;
        int  maxLane  = graph.MaxLane;

        // Branch label sizing.
        double branchLabelW = graph.Branches.Max(b => DiagramText.Measure(b.Name, 11)) + 16;

        double laneAxis0 = Outer + (vertical ? 0 : branchLabelW + 8);            // origin of the lane axis
        double timeAxis0 = Outer + (hasTitle ? TitleH : 0) + (vertical ? branchLabelW + 8 : 14);

        Point P(int pos, int lane)
        {
            double along = (graph.Orientation == GitOrientation.BottomTop ? maxPos - pos : pos) * ColGap;
            double cross = lane * LaneGap;
            return vertical
                ? new Point(laneAxis0 + cross + R, timeAxis0 + along + R)
                : new Point(laneAxis0 + along + R, timeAxis0 + cross + R);
        }

        double canvasW = (vertical ? laneAxis0 + maxLane * LaneGap + 2 * R + 120
                                   : laneAxis0 + maxPos  * ColGap  + 2 * R + 90);
        double canvasH = (vertical ? timeAxis0 + maxPos  * ColGap  + 2 * R + 40
                                   : timeAxis0 + maxLane * LaneGap + 2 * R + 36);
        var canvas = new Canvas { Width = canvasW, Height = canvasH, Background = bgBrush };

        var byId = new Dictionary<string, GitCommit>(StringComparer.Ordinal);
        foreach (var c in graph.Commits) byId[c.Id] = c;   // tolerate duplicate custom ids

        // 1. connectors (behind nodes)
        foreach (var c in graph.Commits)
        {
            var to = P(c.Position, c.Lane);
            for (int pi = 0; pi < c.Parents.Count; pi++)
            {
                if (!byId.TryGetValue(c.Parents[pi], out var p)) continue;
                var from = P(p.Position, p.Lane);
                bool secondary = pi > 0;
                Brush col = secondary ? Branch(p.Lane) : Branch(c.Lane);
                DrawConnector(canvas, from, to, col, dashed: c.IsCherryPick && secondary, vertical);
            }
        }

        // 2. commit nodes + tags + ids
        foreach (var c in graph.Commits)
        {
            var pt = P(c.Position, c.Lane);
            DrawNode(canvas, c, pt, Branch(c.Lane), bgBrush);
            if (!string.IsNullOrEmpty(c.Tag)) DrawTag(canvas, c.Tag!, pt, Branch(c.Lane), textBrush, bgBrush, vertical);
            if (c.ShowLabel) DrawCommitId(canvas, c.Id, pt, mutedBrush, vertical);
        }

        // 3. branch labels (at the start of each lane)
        foreach (var b in graph.Branches)
        {
            int firstPos = graph.Commits.Where(c => c.Branch == b.Name).Select(c => c.Position).DefaultIfEmpty(0).Min();
            var anchor = P(firstPos, b.Lane);
            DrawBranchLabel(canvas, b.Name, anchor, Branch(b.Lane), bgBrush, vertical, laneAxis0, timeAxis0);
        }

        // 4. title
        if (hasTitle)
        {
            double tw = DiagramText.Measure(graph.Title, 15);
            canvas.Children.Add(new TextBlock { Text = graph.Title, Foreground = titleBrush, FontFamily = BodyFont, FontSize = 15, FontWeight = FontWeights.SemiBold }.At((canvasW - tw) / 2, Outer - 2));
        }

        return new Border
        {
            Background = bgBrush, BorderBrush = borderBrush, BorderThickness = new Thickness(1),
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

    // ── Connectors ───────────────────────────────────────────────────────────

    private static void DrawConnector(Canvas canvas, Point from, Point to, Brush color, bool dashed, bool vertical)
    {
        Shape shape;
        bool sameLane = vertical ? Math.Abs(from.X - to.X) < 0.5 : Math.Abs(from.Y - to.Y) < 0.5;
        if (sameLane)
        {
            shape = new Line { X1 = from.X, Y1 = from.Y, X2 = to.X, Y2 = to.Y };
        }
        else
        {
            var fig = new PathFigure { StartPoint = from, IsFilled = false };
            Point cp1, cp2;
            if (vertical) { double my = (from.Y + to.Y) / 2; cp1 = new Point(from.X, my); cp2 = new Point(to.X, my); }
            else          { double mx = (from.X + to.X) / 2; cp1 = new Point(mx, from.Y); cp2 = new Point(mx, to.Y); }
            fig.Segments.Add(new BezierSegment(cp1, cp2, to, isStroked: true));
            shape = new Path { Data = new PathGeometry([fig]) };
        }
        shape.Stroke = color;
        shape.StrokeThickness = 2;
        if (dashed) shape.StrokeDashArray = new DoubleCollection([4, 3]);
        canvas.Children.Add(shape);
    }

    // ── Commit nodes ─────────────────────────────────────────────────────────

    private static void DrawNode(Canvas canvas, GitCommit c, Point pt, Brush color, Brush bg)
    {
        switch (c.Type)
        {
            case GitCommitType.Highlight:
                canvas.Children.Add(new Rectangle { Width = R * 2.4, Height = R * 2.4, RadiusX = 3, RadiusY = 3, Fill = color, Stroke = bg, StrokeThickness = 2 }.At(pt.X - R * 1.2, pt.Y - R * 1.2));
                return;

            case GitCommitType.Reverse:
                AddCircle(canvas, pt, R, color, bg);
                canvas.Children.Add(new Line { X1 = pt.X - 4, Y1 = pt.Y - 4, X2 = pt.X + 4, Y2 = pt.Y + 4, Stroke = bg, StrokeThickness = 1.6 });
                canvas.Children.Add(new Line { X1 = pt.X - 4, Y1 = pt.Y + 4, X2 = pt.X + 4, Y2 = pt.Y - 4, Stroke = bg, StrokeThickness = 1.6 });
                return;

            default:
                if (c.IsMerge)
                {
                    AddCircle(canvas, pt, R, color, bg);
                    AddCircle(canvas, pt, R * 0.45, bg, bg);   // ring look for merge commits
                }
                else if (c.IsCherryPick)
                {
                    AddCircle(canvas, pt, R, color, bg);
                    canvas.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = bg }.At(pt.X - 2.5, pt.Y - 2.5));
                }
                else
                {
                    AddCircle(canvas, pt, R, color, bg);
                }
                return;
        }
    }

    private static void AddCircle(Canvas canvas, Point pt, double r, Brush fill, Brush stroke) =>
        canvas.Children.Add(new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = stroke, StrokeThickness = 1.5 }.At(pt.X - r, pt.Y - r));

    private static void DrawTag(Canvas canvas, string tag, Point pt, Brush color, Brush text, Brush bg, bool vertical)
    {
        double w = DiagramText.Measure(tag, 10) + 10;
        var border = new Border
        {
            Background = bg, BorderBrush = color, BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0),
            Child = new TextBlock { Text = tag, Foreground = text, FontFamily = BodyFont, FontSize = 10 },
        };
        if (vertical) { Canvas.SetLeft(border, pt.X + R + 6); Canvas.SetTop(border, pt.Y - 9); }
        else          { Canvas.SetLeft(border, pt.X - w / 2); Canvas.SetTop(border, pt.Y - R - 20); }
        canvas.Children.Add(border);
    }

    private static void DrawCommitId(Canvas canvas, string id, Point pt, Brush muted, bool vertical)
    {
        var tb = new TextBlock { Text = id, Foreground = muted, FontFamily = BodyFont, FontSize = 9.5 };
        if (vertical) { Canvas.SetLeft(tb, pt.X + R + 6); Canvas.SetTop(tb, pt.Y + 4); }
        else          { double w = DiagramText.Measure(id, 9.5); Canvas.SetLeft(tb, pt.X - w / 2); Canvas.SetTop(tb, pt.Y + R + 4); }
        canvas.Children.Add(tb);
    }

    private static void DrawBranchLabel(Canvas canvas, string name, Point anchor, Brush color, Brush bg,
        bool vertical, double laneAxis0, double timeAxis0)
    {
        double w = DiagramText.Measure(name, 11) + 14;
        var border = new Border
        {
            Background = color, CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 1),
            Child = new TextBlock { Text = name, Foreground = OnColor(color), FontFamily = BodyFont, FontSize = 11, FontWeight = FontWeights.SemiBold },
        };
        if (vertical) { Canvas.SetLeft(border, anchor.X - w / 2); Canvas.SetTop(border, timeAxis0 - 24); }
        else          { Canvas.SetLeft(border, laneAxis0 - w - 8); Canvas.SetTop(border, anchor.Y - 11); }
        canvas.Children.Add(border);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Brush OnColor(Brush b)
    {
        var c = (b as SolidColorBrush)?.Color ?? Colors.Gray;
        double lum = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
        var w = new SolidColorBrush(lum > 150 ? Colors.Black : Colors.White);
        w.Freeze();
        return w;
    }

}
