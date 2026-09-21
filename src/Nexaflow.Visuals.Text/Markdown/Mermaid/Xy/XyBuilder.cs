using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Xy;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Xy;

/// <summary>The pieces an xychart's layout is made of — its layers, and what is in them. Its legend is <see cref="DiagramLegend"/>'s.</summary>
public static class XyPiece
{
    /// <summary>The plot's background, where the front matter colours it.</summary>
    public const string Plot = "Plot";

    /// <summary>The bars, and one bar — standing for its value as written, so pressing it means that value.</summary>
    public const string Bars = "Bars";
    public const string Bar = "Bar";

    /// <summary>The lines, and one line — standing for its series.</summary>
    public const string Lines = "Lines";
    public const string Trace = "Trace";

    /// <summary>The x-axis — the categories, or the numbers the values stand over — and the y-axis, the numbers they reach.</summary>
    public const string XAxis = "XAxis";
    public const string YAxis = "YAxis";

    /// <summary>What is written at a tick: a category, typed into, or a number worked out.</summary>
    public const string Tick = "Tick";

    /// <summary>An axis's title.</summary>
    public const string AxisTitle = "AxisTitle";

    /// <summary>What is written on the plot: each line's point labels, and each bar's value where the front matter asks.</summary>
    public const string Labels = "Labels";
    public const string Label = "Label";
    public const string Value = "Value";

    /// <summary>A series' name, in its legend row.</summary>
    public const string Name = "Name";
}

/// <summary>
/// Draws an <c>xychart</c> block: its two axes, a bar per value of each bar series — side by side where there are several —
/// a line through each line series, and a legend naming the series that have names.
///
/// <para>
/// <strong>Layers by how it looks.</strong> Bars, lines over them, the axes, and the words on the plot are subtrees of their
/// own. What belongs together is said by the source: a bar stands for its value, a line and its legend row for its series,
/// and a category under its tick is the category written, typed into where it is drawn.
/// </para>
/// <para>
/// <strong>Orientation only turns it.</strong> The x-axis is always the categories and the y-axis the numbers; a horizontal
/// chart draws the categories down the side and the numbers along the foot, and its bars reach right. Axis titles are set
/// level — an upright axis's over it — since words on the layout tree are not turned; <c>labelRotation</c> is not applied
/// for the same reason.
/// </para>
/// </summary>
internal sealed class XyBuilder : MermaidBuilder<XyChart>
{
    /// <summary>How big the chart is drawn before anything asks for another size.</summary>
    private const double Wide = 560;
    private const double Tall = 320;
    private const double Gap = 6;
    private const double LabelSize = 11.5;
    private const double TitleSize = 12.5;
    private const double LegendSize = 12;

    /// <summary>How much of a slot its bars take together, the rest the air between slots.</summary>
    private const double Filled = 0.8;

    private const double LineWidth = 2;

    internal XyBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly) : base(reading, state, style, isReadOnly) { }

    /// <inheritdoc/>
    protected override XyChart Of(MermaidBlock block) => XyChart.Of(block);

    /// <summary>The front matter's <c>titleColor</c>, where it writes one.</summary>
    protected override string? TitleColour => Diagram?.Config.TitleColour;

    /// <summary>The front matter's <c>titleFontSize</c>, where it writes one.</summary>
    protected override double? TitleTextSize => Diagram?.Config.TitleFontSize;

    /// <summary>No title where the front matter's <c>showTitle</c> is false.</summary>
    protected override (ContentPart? Part, string? Text) TitleOf(MermaidBlock block) =>
        Diagram?.Config.ShowTitle == false ? (null, null) : base.TitleOf(block);

    protected override Size Draw(XyChart chart, LayoutBuilder build)
    {
        // A chart of nothing is the source: no axis, no series, nothing to look at.
        if (chart.X is null && chart.Y is null && chart.Series.Count == 0) return AsWritten(build);

        var config = chart.Config;
        var horizontal = chart.Orientation == XyOrientation.Horizontal;
        var slots = Math.Max(1, chart.Slots);

        // The numbers: as written, or the values' own widened out to round numbers.
        var (min, max) = chart.Range;
        var span = DiagramSpan.Of(min, max, widen: chart.Y is not { Ranged: true });

        var values = Ticks(config.YAxis, chart.Y, span.Ticks().Select(tick => (tick.At, tick.Says)));
        var categories = Categories(chart, slots);

        var (upright, flat) = horizontal ? (categories, values) : (values, categories);
        var (uprightConfig, flatConfig) = horizontal ? (config.XAxis, config.YAxis) : (config.YAxis, config.XAxis);
        var (uprightAxis, flatAxis) = horizontal ? (chart.X, chart.Y) : (chart.Y, chart.X);

        var uprightTitle = AxisTitle(uprightConfig, uprightAxis);
        var flatTitle = AxisTitle(flatConfig, flatAxis);
        var legend = Legend(chart);

        var wide = config.Width ?? Wide;
        if (config.UseMaxWidth && !double.IsInfinity(Space)) wide = Math.Min(wide, Space);
        var tall = config.Height ?? Tall;

        // Round the plot: the axes' own room, and the legend under it.
        var under = legend.Size.Height > 0 ? legend.Size.Height + (config.LegendPadding ?? Gap * 2) : 0;

        var edges = DiagramPanel.Room(upright, flat, uprightTitle, flatTitle, Gap,
                                      Tick(uprightConfig), Tick(flatConfig))
                    + new DiagramEdges(0, Gap, 0, under);

        var plot = DiagramPanel.Round(wide, tall, edges).Plot;

        // Along the categories, from nought at the first slot's side to one at the last's; and along the numbers, from min to max.
        Point On(double along, double value) => horizontal
            ? new Point(plot.Left + (value * plot.Width), plot.Top + (along * plot.Height))
            : new Point(plot.Left + (along * plot.Width), plot.Bottom - (value * plot.Height));

        double Reach(double value) => Math.Clamp(span.At(value) ?? 0, 0, 1);

        if (Ink.Written(config.BackgroundColour) is { } background)
        {
            build.Open(XyPiece.Plot, part: null, stops: Stops.None);
            build.Draw(new WashMark(plot, background));
            build.Close();
        }

        var labels = new List<(DiagramWords Words, Point At, string Kind)>();

        Bars(build, chart, slots, config, On, Reach(Math.Clamp(0, min, max)), Reach, labels);
        Lines(build, chart, slots, On, Reach, labels);

        Axis(build, horizontal ? XyPiece.XAxis : XyPiece.YAxis, uprightAxis?.Part, plot.BottomLeft, plot.TopLeft, upright, uprightConfig, after: false, horizontal);
        Axis(build, horizontal ? XyPiece.YAxis : XyPiece.XAxis, flatAxis?.Part, plot.BottomLeft, plot.BottomRight, flat, flatConfig, after: true, horizontal: false);

        new DiagramPanel(plot, wide, tall).Titles(build, XyPiece.AxisTitle, uprightTitle, flatTitle,
                                                  DiagramAxis.Room(flat, upright: false, Tick(flatConfig)) + Gap);

        build.Open(XyPiece.Labels, part: null, stops: Stops.None);
        foreach (var (words, at, kind) in labels) words.Set(build, at, kind);
        build.Close();

        var width = Math.Max(plot.Right + edges.Right, edges.Left + legend.Size.Width);
        if (legend.Size.Height > 0) legend.Draw(build, new Point(plot.Left + Math.Max(0, (plot.Width - legend.Size.Width) / 2), tall - legend.Size.Height));

        return new Size(Math.Max(width, wide), tall);
    }

    // ── The axes ────────────────────────────────────────────────────────────

    /// <summary>
    /// Where the categories are drawn along their axis, and what is written at each: a category somebody wrote, a number on
    /// an axis of numbers, or nothing on an axis with neither.
    /// </summary>
    private List<DiagramTick> Categories(XyChart chart, int slots)
    {
        var config = chart.Config.XAxis;
        var size = config.LabelFontSize ?? LabelSize;
        var ink = Ink.Written(config.LabelColour) ?? Palette.TextMuted;

        if (chart.X is { Categorical: true } x)
            return [.. x.Categories.Select((category, at) =>
                new DiagramTick(Along(chart, at, slots), config.ShowLabel ? Written(category.Name, category.Hole, size, ink) : null))];

        if (chart.X is { Ranged: true } ranged)
        {
            var span = DiagramSpan.Of(ranged.Min!.Value, ranged.Max!.Value, widen: false);
                        return Ticks(config, ranged, span.Ticks().Select(tick => (tick.At, tick.Says)));
        }

        return [.. Enumerable.Range(0, slots).Select(at => new DiagramTick(Along(chart, at, slots), null))];
    }

    /// <summary>Ticks at worked-out numbers, pressed as the axis they are on.</summary>
    private List<DiagramTick> Ticks(XyAxisConfig config, XyAxis? axis, IEnumerable<(double At, string Says)> numbers)
    {
        var size = config.LabelFontSize ?? LabelSize;
        var ink = Ink.Written(config.LabelColour) ?? Palette.TextMuted;

        return [.. numbers.Select(number => new DiagramTick(number.At, config.ShowLabel ? Worked(number.Says, axis?.Part, size, ink) : null))];
    }

    /// <summary>
    /// How far along the categories the <paramref name="at"/>th value stands: in the middle of its slot — or, over an axis of
    /// numbers, from the start of the range at the first to its end at the last, as Mermaid spreads them.
    /// </summary>
    private static double Along(XyChart chart, int at, int slots) =>
        chart.X is { Categorical: false, Ranged: true } ? (slots > 1 ? (double)at / (slots - 1) : 0.5) : (at + 0.5) / slots;

    private DiagramWords? AxisTitle(XyAxisConfig config, XyAxis? axis) =>
        config.ShowTitle && axis is { Title: not null } or { TitleHole: not null }
            ? Written(axis.Title, axis.TitleHole, config.TitleFontSize ?? TitleSize, Ink.Written(config.TitleColour) ?? Palette.Text)
            : null;

    private static double Tick(XyAxisConfig config) => config.ShowTick ? config.TickLength ?? DiagramAxis.TickLength : 0;

    private void Axis(LayoutBuilder build, string kind, ContentPart? part, Point from, Point to, IReadOnlyList<DiagramTick> ticks,
                      XyAxisConfig config, bool after, bool horizontal)
    {
        // Down the side of a horizontal chart the categories run from the top: the first at the top, as they are written.
        var (start, end, marks) = horizontal
            ? (new Point(from.X, to.Y), from, ticks)
            : (from, to, ticks);

        DiagramAxis.Draw(build, kind, part, start, end, marks,
                         new DiagramStroke(Ink.Written(config.LineColour) ?? Palette.CodeBorder, config.AxisLineWidth ?? 1),
                         XyPiece.Tick, after, line: config.ShowAxisLine, tick: Tick(config));
    }

    // ── The series ──────────────────────────────────────────────────────────

    private void Bars(LayoutBuilder build, XyChart chart, int slots, XyConfig config, Func<double, double, Point> on, double foot,
                      Func<double, double> reach, List<(DiagramWords, Point, string)> labels)
    {
        var bars = chart.Series.Where(series => series.Kind == XySeriesKind.Bar).ToList();
        var span = Filled / slots / Math.Max(1, bars.Count);

        build.Open(XyPiece.Bars, part: null, stops: Stops.None);

        for (var order = 0; order < bars.Count; order++)
        {
            var series = bars[order];
            var fill = Colour(series);

            for (var at = 0; at < series.Points.Count && at < slots; at++)
            {
                if (series.Points[at] is not { Worth: { } worth } point) continue;

                var middle = Along(chart, at, slots) - (Filled / slots / 2) + ((order + 0.5) * span);
                var bar = new Rect(on(middle - (span * 0.45), foot), on(middle + (span * 0.45), reach(worth)));
                var shape = new RectangleGeometry(bar);
                shape.Freeze();

                build.Open(XyPiece.Bar, point.Part, stops: Stops.None);
                build.Draw(new GeometryMark(shape, fill, null, 0));
                build.Occupies(shape);
                build.Close();

                if (config.ShowDataLabel) labels.Add(DataLabel(chart, point, worth, bar, fill, reach(worth) >= foot));
            }
        }

        build.Close();
    }

    /// <summary>A bar's value, written just inside its end — or past it, where the front matter asks.</summary>
    private (DiagramWords, Point, string) DataLabel(XyChart chart, XyPoint point, double worth, Rect bar, Brush fill, bool rising)
    {
        var config = chart.Config;
        var outside = config.ShowDataLabelOutsideBar;
        var ink = Ink.Written(config.DataLabelColour) ?? (outside ? Palette.Text : Ink.Over(fill));
        var words = Worked(worth.ToString("G", CultureInfo.CurrentCulture), point.Part, LabelSize, ink);

        // Towards the bar's foot from its end, or away from it, whichever way the bar runs.
        if (chart.Orientation == XyOrientation.Horizontal)
        {
            var end = rising ? bar.Right : bar.Left;
            var x = rising == outside ? end + Gap : end - Gap - words.Width;
            return (words, new Point(x, bar.Top + ((bar.Height - words.Height) / 2)), XyPiece.Value);
        }

        var top = rising ? bar.Top : bar.Bottom;
        var y = rising == outside ? top - Gap - words.Height : top + Gap;
        return (words, new Point(bar.Left + ((bar.Width - words.Width) / 2), y), XyPiece.Value);
    }

    private void Lines(LayoutBuilder build, XyChart chart, int slots, Func<double, double, Point> on, Func<double, double> reach,
                       List<(DiagramWords, Point, string)> labels)
    {
        build.Open(XyPiece.Lines, part: null, stops: Stops.None);

        foreach (var series in chart.Series.Where(series => series.Kind == XySeriesKind.Line))
        {
            var ink = Colour(series);
            var route = new List<Point>();

            for (var at = 0; at < series.Points.Count && at < slots; at++)
            {
                var point = series.Points[at];
                if (point.Worth is not { } worth) continue;

                var where = on(Along(chart, at, slots), reach(worth));
                route.Add(where);

                if (point.Label is null && point.LabelHole is null) continue;

                // A point's label sits over it — right of it on a horizontal chart — in its line's colour.
                var words = Written(point.Label, point.LabelHole, LabelSize, ink);
                labels.Add((words, chart.Orientation == XyOrientation.Horizontal
                                   ? new Point(where.X + Gap, where.Y - (words.Height / 2))
                                   : new Point(where.X - (words.Width / 2), where.Y - Gap - words.Height), XyPiece.Label));
            }

            // One point is no line.
            if (route.Count >= 2)
                DiagramConnector.Draw(build, XyPiece.Trace, series.Part, route, new DiagramStroke(ink, LineWidth), DiagramHead.None, DiagramHead.None);
        }

        build.Close();
    }

    /// <summary>What a series is drawn in: <c>plotColorPalette</c>'s colour for its place, or the theme's.</summary>
    private Brush Colour(XySeries series) => Ink.Series(series.Order, series.Colour);

    // ── The legend ──────────────────────────────────────────────────────────

    /// <summary>A row for each series with a name — along a line under the chart — where the front matter shows a legend.</summary>
    private DiagramLegend Legend(XyChart chart)
    {
        var config = chart.Config;
        var size = config.LegendFontSize ?? LegendSize;
        var ink = Ink.Written(config.LegendTextColour) ?? Palette.Text;

        IReadOnlyList<DiagramKey> rows = config.ShowLegend
            ? [.. chart.Series.Where(series => series.Name is not null || series.NameHole is not null)
                  .Select(series => new DiagramKey(series.Part, Colour(series), [Written(series.Name, series.NameHole, size, ink)]))]
            : [];

        return new DiagramLegend(rows, [XyPiece.Name], across: true, Palette.TextMuted);
    }
}
