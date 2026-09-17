using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Quadrant;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Quadrant;

/// <summary>The pieces a quadrant chart's layout is made of — its layers, and what is in them.</summary>
public static class QuadrantPiece
{
    /// <summary>The four quadrants, and one — standing for the line writing its caption, where one is written.</summary>
    public const string Quadrants = "Quadrants";
    public const string Quadrant = "Quadrant";

    /// <summary>The line round the chart and the lines between its quadrants.</summary>
    public const string Borders = "Borders";

    /// <summary>The points, and one — standing for the point as written.</summary>
    public const string Points = "Points";
    public const string Point = "Point";

    /// <summary>What is written on the chart: captions, the axes' ends, and the points' names.</summary>
    public const string Words = "Words";
    public const string Caption = "Caption";
    public const string AxisLabel = "AxisLabel";
    public const string Name = "Name";
}

/// <summary>
/// Draws a <c>quadrantChart</c> block: four quadrants each tinted and captioned, the ends of its axes along their halves, and
/// a dot for each point where it stands, its name under it.
///
/// <para>
/// <strong>Everything drawn stands for what was written.</strong> A quadrant stands for its caption's line, a dot for its
/// point, and the words are the characters written — a caption, an axis's end, a point's name typed into where it is drawn.
/// The first quadrant is top right and the rest go anticlockwise, as Mermaid numbers them; the x-axis's words go over the
/// chart where there are no points, and under it where there are.
/// </para>
/// </summary>
internal sealed class QuadrantBuilder : MermaidBuilder<QuadrantChart>
{
    /// <summary>How big the chart is drawn before anything asks for another size.</summary>
    private const double Side = 380;

    private const double Smallest = 120;
    private const double Gap = 6;
    private const double AxisSize = 12;
    private const double CaptionSize = 13;
    private const double NameSize = 11;
    private const double Radius = 5;

    /// <summary>How solid a quadrant is tinted where no front matter colours it.</summary>
    private const double Tint = 0.16;

    private QuadrantBuilder(EditState state, MarkdownPalette palette, double pixelsPerDip, double room, bool writing)
        : base(state, palette, pixelsPerDip, room, writing) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    public static Laid Build(EditState state, MarkdownPalette palette, double pixelsPerDip, double room = double.PositiveInfinity,
                             bool writing = false) =>
        new QuadrantBuilder(state, palette, pixelsPerDip, room, writing).Lay();

    /// <inheritdoc/>
    protected override QuadrantChart Of(MermaidBlock block) => QuadrantChart.Of(block);

    /// <summary>The front matter's <c>quadrantTitleFill</c>, where it writes one.</summary>
    protected override string? TitleColour => Diagram?.Config.TitleFill;

    /// <summary>The front matter's <c>titleFontSize</c>, where it writes one.</summary>
    protected override double? TitleTextSize => Diagram?.Config.TitleFontSize;

    protected override Size Draw(QuadrantChart chart, LayoutBuilder build)
    {
        // A chart with nothing written in it is the source.
        if (chart.Points.Count == 0 && chart.Regions.All(region => region is null) && new[] { chart.Left, chart.Right, chart.Bottom, chart.Top }.All(end => end is null))
            return AsWritten(build);

        var config = chart.Config;
        var along = new[] { chart.Left, chart.Right }.Select(end => Words(end, config.XAxisLabelFontSize ?? AxisSize, config.XAxisTextFill, Palette.TextMuted)).ToList();
        var up = new[] { chart.Bottom, chart.Top }.Select(end => Words(end, config.YAxisLabelFontSize ?? AxisSize, config.YAxisTextFill, Palette.TextMuted)).ToList();

        // As big as it is asked to be, unless the room less the y-axis's words says smaller — kept its shape either way.
        var beside = up.OfType<DiagramWords>().Select(words => words.Width + Gap).DefaultIfEmpty(0).Max();
        var (wide, tall) = (config.ChartWidth ?? Side, config.ChartHeight ?? Side);
        if (!double.IsInfinity(Space) && wide + beside > Space)
        {
            var scale = Math.Max(Smallest, Space - beside) / wide;
            (wide, tall) = (wide * scale, tall * scale);
        }

        var plot = new Rect(0, 0, wide, tall);
        var words = new List<(DiagramWords Words, Point At, string Kind)>();

        // The x-axis's ends along its halves, over the chart or under it; the y-axis's up its halves, left of it or right.
        for (var end = 0; end < 2; end++)
        {
            if (along[end] is { } x)
                words.Add((x, new Point((wide * (end == 0 ? 0.25 : 0.75)) - (x.Width / 2), chart.XAxisOnTop ? -Gap - x.Height : tall + Gap), QuadrantPiece.AxisLabel));

            if (up[end] is { } y)
                words.Add((y, new Point(config.YAxisOnRight ? wide + Gap : -Gap - y.Width, (tall * (end == 0 ? 0.75 : 0.25)) - (y.Height / 2)), QuadrantPiece.AxisLabel));
        }

        // Each caption in the middle of its quadrant — or at its top, out of the points' way, where there are points.
        var cells = Enumerable.Range(0, 4).Select(index => Cell(plot, index)).ToList();
        for (var index = 0; index < 4; index++)
        {
            if (Words(chart.Regions[index], config.QuadrantLabelFontSize ?? CaptionSize, config.QuadrantTextFills[index], Palette.Text) is not { } caption) continue;

            var cell = cells[index];
            var top = chart.Points.Count > 0 ? cell.Top + Gap : cell.Top + ((cell.Height - caption.Height) / 2);
            words.Add((caption, new Point(cell.Left + ((cell.Width - caption.Width) / 2), top), QuadrantPiece.Caption));
        }

        var dots = new List<(QuadrantPoint Point, Point Centre, double Radius)>();
        foreach (var point in chart.Points.Where(point => point.Placed))
        {
            var centre = new Point(Math.Clamp(point.X!.Value, 0, 1) * wide, (1 - Math.Clamp(point.Y!.Value, 0, 1)) * tall);
            var radius = point.Style.Radius ?? config.PointRadius ?? Radius;
            dots.Add((point, centre, radius));

            if (Words(point.Name, config.PointLabelFontSize ?? NameSize, config.PointTextFill, Palette.Text) is { } name)
                words.Add((name, new Point(centre.X - (name.Width / 2), centre.Y + radius + 2), QuadrantPiece.Name));
        }

        // Words reach past the chart's edges: everything moves over so they are not cut off.
        var room = new DiagramRoom();
        room.Reach(plot);
        foreach (var (said, at, _) in words) room.Reach(said, at);
        foreach (var (_, centre, radius) in dots) room.Reach(new Rect(centre.X - radius, centre.Y - radius, radius * 2, radius * 2));
        var shift = room.Shift;

        // What is drawn over the quadrants, which a press there means rather than the quadrant under it.
        var over = new GeometryGroup();
        foreach (var (said, at, _) in words) over.Children.Add(new RectangleGeometry(new Rect(at + shift, new Size(said.Width, said.Height))));
        foreach (var (_, centre, radius) in dots) over.Children.Add(new EllipseGeometry(centre + shift, radius, radius));
        over.Freeze();

        Quadrants(build, chart, cells, shift, over);
        Borders(build, config, plot, shift);
        Points(build, config, dots, shift);

        build.Open(QuadrantPiece.Words, part: null, stops: Stops.None);
        foreach (var (said, at, kind) in words) said.Set(build, at + shift, kind);
        build.Close();

        return room.Size;
    }

    /// <summary>A quadrant's cell: the first top right, then anticlockwise.</summary>
    private static Rect Cell(Rect plot, int index)
    {
        var right = index is 0 or 3;
        var top = index is 0 or 1;
        return new Rect(plot.Left + (right ? plot.Width / 2 : 0), plot.Top + (top ? 0 : plot.Height / 2), plot.Width / 2, plot.Height / 2);
    }

    private DiagramWords? Words(QuadrantText? text, double size, string? fill, Brush ink) =>
        text is null ? null : Written(text.Says, text.Hole, size, Ink.Written(fill) ?? ink);

    private void Quadrants(LayoutBuilder build, QuadrantChart chart, IReadOnlyList<Rect> cells, Vector shift, Geometry over)
    {
        build.Open(QuadrantPiece.Quadrants, part: null, stops: Stops.None);

        for (var index = 0; index < 4; index++)
        {
            var shape = new RectangleGeometry(Rect.Offset(cells[index], shift));
            shape.Freeze();

            var fill = Ink.Written(chart.Config.QuadrantFills[index]) ?? DiagramInk.Faded(Ink.Series(index), Tint);

            build.Open(QuadrantPiece.Quadrant, chart.Regions[index]?.Part, stops: Stops.None);
            build.Draw(new GeometryMark(shape, fill, null, 0));

            // A quadrant stands where nothing drawn over it does: a press on its caption or a point means that.
            if (chart.Regions[index] is not null)
            {
                var stands = new CombinedGeometry(GeometryCombineMode.Exclude, shape, over);
                stands.Freeze();
                build.Occupies(stands);
            }

            build.Close();
        }

        build.Close();
    }

    private void Borders(LayoutBuilder build, QuadrantConfig config, Rect plot, Vector shift)
    {
        plot.Offset(shift);

        var outside = new RectangleGeometry(plot);
        var inside = new GeometryGroup
        {
            Children =
            {
                new LineGeometry(new Point(plot.Left + (plot.Width / 2), plot.Top), new Point(plot.Left + (plot.Width / 2), plot.Bottom)),
                new LineGeometry(new Point(plot.Left, plot.Top + (plot.Height / 2)), new Point(plot.Right, plot.Top + (plot.Height / 2))),
            },
        };
        outside.Freeze();
        inside.Freeze();

        build.Open(QuadrantPiece.Borders, part: null, stops: Stops.None);
        build.Draw(new GeometryMark(inside, null, Ink.Written(config.InternalBorderFill) ?? Palette.CodeBorder, config.InternalBorderWidth ?? 1));
        build.Draw(new GeometryMark(outside, null, Ink.Written(config.ExternalBorderFill) ?? Palette.CodeBorder, config.ExternalBorderWidth ?? 1));
        build.Close();
    }

    private void Points(LayoutBuilder build, QuadrantConfig config, IReadOnlyList<(QuadrantPoint Point, Point Centre, double Radius)> dots, Vector shift)
    {
        build.Open(QuadrantPiece.Points, part: null, stops: Stops.None);

        foreach (var (point, centre, radius) in dots)
        {
            var shape = new EllipseGeometry(centre + shift, radius, radius);
            shape.Freeze();

            var fill = Ink.Written(point.Style.Colour) ?? Ink.Written(config.PointFill) ?? Ink.Series(point.Order);
            var stroke = Ink.Written(point.Style.StrokeColour);

            build.Open(QuadrantPiece.Point, point.Part, stops: Stops.None);
            build.Draw(new GeometryMark(shape, fill, stroke, stroke is null ? 0 : point.Style.StrokeWidth ?? 1));
            build.Occupies(shape);
            build.Close();
        }

        build.Close();
    }
}
