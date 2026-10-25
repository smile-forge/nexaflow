using System.Globalization;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Xy;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Mermaid.Xy;

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
internal sealed class PortableXyBuilder : PortableContentBuilder
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

    /// <summary>How big the dot marking a value on a line is.</summary>
    private const double Dotted = 4;

    internal PortableXyBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                               PortableNesting nesting) : base(reading, state, style, isReadOnly, medium, nesting)
    {
        Ink = new PortableDiagramInk(style, medium);
        Words = new PortableDiagramText(reading, state, style, medium, part => Nested(part, double.PositiveInfinity, PortableStanding.Inline));
        Sheet = new PortableDiagramSheet(state, style, medium, isReadOnly, Words, AsSource);
    }

    /// <summary>What the diagram is drawn in: the colours its source and front matter write, and the theme's where they write none.</summary>
    private PortableDiagramInk Ink { get; }

    /// <summary>What every word the diagram draws is made from.</summary>
    private PortableDiagramText Words { get; }

    /// <summary>The sheet the diagram is drawn on: its title, what goes under it, what could not be read, and its card.</summary>
    private PortableDiagramSheet Sheet { get; }

    protected override PortableLaid? Build()
    {
        Sheet.Start(Room);

        var block = MermaidBlock.Of(Reading);
        var drawing = new PortableBuilder(Medium);
        var body = Draw(block, drawing);

        return Sheet.Laid(block, drawing, body, TitleOf(block), Ink.Written(TitleColour), TitleTextSize);
    }

    protected override PortableCharacters Characters => PortableDiagramSheet.Characters(Style);

    // ── What is written ─────────────────────────────────────────────────────

    /// <summary>What a series is drawn as.</summary>
    private enum Drawn { Bar, Line }

    /// <summary>One of an x-axis's categories: what it says as written, and the hole standing where it is still to write.</summary>
    private sealed record Category(ContentPart Name, ContentPart? Hole);

    /// <summary>One axis: the line it was written on — what pressing the axis means — its title, and its categories or its range.</summary>
    /// <param name="Title">What its title says, without its quotes, or null where it has none.</param>
    /// <param name="Categories">Its categories in the order written — none for an axis of numbers.</param>
    /// <param name="Min">Where its range starts, as written, or null.</param>
    /// <param name="Max">Where its range ends, as written, or null.</param>
    private sealed record AxisLine(ContentPart Part, ContentPart? Title, ContentPart? TitleHole, IReadOnlyList<Category> Categories, double? Min, double? Max)
    {
        public bool Categorical => Categories.Count > 0;

        /// <summary>Whether both ends of its range are written.</summary>
        public bool Ranged => Min is not null && Max is not null;
    }

    /// <summary>One value of a series: the value and its label as written — what pressing its bar means — the number, what it comes to, and its label.</summary>
    /// <param name="Value">The number as written — empty where it is still to come.</param>
    /// <param name="Worth">What the number comes to, or null where it is none.</param>
    /// <param name="Label">What its label says, without its quotes, or null where it has none.</param>
    private sealed record Valued(ContentPart Part, ContentPart Value, double? Worth, ContentPart? Label, ContentPart? LabelHole);

    /// <summary>One series: the line it was written on — what pressing its line means, and its legend row — what it is drawn as, its name, its values, and its colour.</summary>
    /// <param name="Name">What its name says, without its quotes, or null where it has none — and then it has no legend row.</param>
    /// <param name="Order">Where it comes among the series written, which is the colour it takes.</param>
    /// <param name="Colour">The colour <c>plotColorPalette</c> writes for its place, or null to leave it to the theme.</param>
    private sealed record Series(ContentPart Part, Drawn Kind, ContentPart? Name, ContentPart? NameHole, IReadOnlyList<Valued> Points, int Order, string? Colour);

    /// <summary>
    /// The chart as written: which way it runs, its two axes, and its series in the order they are written. Each series' values
    /// stand over the categories in order — the first over the first — and an axis written twice is the last one written.
    /// </summary>
    private sealed class Chart(XyConfig config, XyOrientation orientation, AxisLine? x, AxisLine? y, IReadOnlyList<Series> series)
    {
        public XyConfig Config { get; } = config;

        public XyOrientation Orientation { get; } = orientation;

        /// <summary>The x-axis — the categories, or the numbers the values stand over — or null where none is written.</summary>
        public AxisLine? X { get; } = x;

        /// <summary>The y-axis — the numbers the values reach — or null where none is written.</summary>
        public AxisLine? Y { get; } = y;

        /// <summary>The series, in the order written: bars side by side in that order, and lines over the bars.</summary>
        public IReadOnlyList<Series> Series { get; } = series;

        /// <summary>How many places along the x-axis the values stand at: its categories, or else the most values any series has.</summary>
        public int Slots => X is { Categorical: true } axis ? axis.Categories.Count : Series.Select(each => each.Points.Count).DefaultIfEmpty(0).Max();

        /// <summary>
        /// The numbers the values are drawn against: the y-axis's range where both its ends are written, and otherwise the values'
        /// own — from nought where there are bars, and one wide at the least.
        /// </summary>
        public (double Min, double Max) Range
        {
            get
            {
                if (Y is { Ranged: true } axis && axis.Max > axis.Min) return (axis.Min!.Value, axis.Max!.Value);

                var values = Series.SelectMany(each => each.Points).Select(point => point.Worth).OfType<double>().ToList();
                if (values.Count == 0) return (0, 1);

                var min = values.Min();
                var max = values.Max();
                if (Series.Any(each => each.Kind == Drawn.Bar)) (min, max) = (Math.Min(min, 0), Math.Max(max, 0));

                return max > min ? (min, max) : (min, min + 1);
            }
        }
    }

    /// <summary>The chart as written, with what its front matter asks for — which way it runs where it says, over the header's word.</summary>
    private Chart Read()
    {
        var config = PortableDiagramConfig.Of(Reading, XyConfig.Default);
        var orientation = XyOrientation.Vertical;
        AxisLine? x = null, y = null;
        var series = new List<Series>();

        foreach (var part in Reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case XyKinds.Orientation when part.Text.Equals(XyGrammar.Horizontal, StringComparison.OrdinalIgnoreCase):
                    orientation = XyOrientation.Horizontal;
                    break;

                case XyKinds.Axis when Word(part) is { } word:
                    if (word.Equals(XyGrammar.XAxis, StringComparison.OrdinalIgnoreCase)) x = Axised(part);
                    else y = Axised(part);
                    break;

                case XyKinds.Series when Word(part) is { } word:
                    var order = series.Count;
                    series.Add(Plotted(part, word, order, config.Palette.Count > 0 ? config.Palette[order % config.Palette.Count] : null));
                    break;
            }
        }

        return new Chart(config, config.Orientation ?? orientation, x, y, series);
    }

    /// <summary>What an axis or series line starts with — <c>x-axis</c>, <c>bar</c> — as written.</summary>
    private static string? Word(ContentPart part) => part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key)?.Text;

    /// <summary>A title or a name: in quotes, or a word.</summary>
    private static ContentPart? Titled(ContentPart part) =>
        part.Children.FirstOrDefault(child => child.Kind is MermaidKinds.Quoted or MermaidKinds.Name);

    private static AxisLine Axised(ContentPart part)
    {
        var title = Titled(part);
        var categories = part.Children.FirstOrDefault(child => child.Kind == XyKinds.Categories)?.Children
                             .FirstOrDefault(child => child.Kind == MermaidKinds.Names)
                             .Named()
                             .Select(name => new Category(name.Words()!, name.Hole()))
                             .ToList() ?? [];

        var ends = part.Children.FirstOrDefault(child => child.Kind == XyKinds.Range)?.Children
                       .Where(child => child.Kind == MermaidKinds.Amount)
                       .ToList() ?? [];

        return new AxisLine(part, title.Words(), title.Hole(), categories, ends.ElementAtOrDefault(0).Number(), ends.ElementAtOrDefault(1).Number());
    }

    private static Series Plotted(ContentPart part, string word, int order, string? colour)
    {
        var name = Titled(part);
        var points = part.Children.FirstOrDefault(child => child.Kind == XyKinds.Values)?.Children
                         .Where(child => child.Kind == XyKinds.Point)
                         .Select(point =>
                         {
                             var label = point.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Quoted);
                             return new Valued(point, point.Inner(Kinds.Number)!, point.Number(), label.Words(), label.Hole());
                         })
                         .ToList() ?? [];

        var kind = word.Equals(XyGrammar.Line, StringComparison.OrdinalIgnoreCase) ? Drawn.Line : Drawn.Bar;
        return new Series(part, kind, name.Words(), name.Hole(), points, order, colour);
    }

    // ── Laying it out ───────────────────────────────────────────────────────

    /// <summary>The front matter's <c>titleColor</c>, where it writes one.</summary>
    private string? TitleColour => PortableDiagramConfig.Of(Reading, XyConfig.Default).TitleColour;

    /// <summary>The front matter's <c>titleFontSize</c>, where it writes one.</summary>
    private double? TitleTextSize => PortableDiagramConfig.Of(Reading, XyConfig.Default).TitleFontSize;

    /// <summary>No title where the front matter's <c>showTitle</c> is false.</summary>
    private (ContentPart? Part, string? Says, bool AsWritten) TitleOf(MermaidBlock block) =>
        PortableDiagramConfig.Of(Reading, XyConfig.Default).ShowTitle is false ? (null, null, false) : (block.Title, block.TitleSays, block.TitleAsWritten);

    private LayoutSize Draw(MermaidBlock block, PortableBuilder build)
    {
        var chart = Read();

        // A chart of nothing is the source: no axis, no series, nothing to look at.
        if (chart.X is null && chart.Y is null && chart.Series.Count == 0) return Sheet.AsWritten();

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
        if (config.UseMaxWidth && !double.IsInfinity(Sheet.Space)) wide = Math.Min(wide, Sheet.Space);
        var tall = config.Height ?? Tall;

        // Round the plot: the axes' own room, and the legend under it.
        var under = legend.Size.Height > 0 ? legend.Size.Height + (config.LegendPadding ?? Gap * 2) : 0;

        var edges = PortableDiagramPanel.Room(upright, flat, uprightTitle, flatTitle, Gap,
                                              Tick(uprightConfig), Tick(flatConfig))
                    + new DiagramEdges(0, Gap, 0, under);

        var plot = PortableDiagramPanel.Round(wide, tall, edges).Plot;

        // Along the categories, from nought at the first slot's side to one at the last's; and along the numbers, from min to max.
        LayoutPoint On(double along, double value) => horizontal
            ? new LayoutPoint(plot.Left + (value * plot.Width), plot.Top + (along * plot.Height))
            : new LayoutPoint(plot.Left + (along * plot.Width), plot.Bottom - (value * plot.Height));

        double Reach(double value) => Math.Clamp(span.At(value) ?? 0, 0, 1);

        if (Ink.Written(config.BackgroundColour) is { } background)
        {
            build.Open(XyPiece.Plot, part: null, stops: PortableStops.None);
            build.Draw(new PortableWashMark(plot, background));
            build.Close();
        }

        var labels = new List<(PortableDiagramWords Words, LayoutPoint At, string Kind)>();

        Bars(build, chart, slots, config, On, Reach(Math.Clamp(0, min, max)), Reach, labels);
        Lines(build, chart, slots, On, Reach, labels);

        Axis(build, horizontal ? XyPiece.XAxis : XyPiece.YAxis, uprightAxis?.Part, plot.BottomLeft, plot.TopLeft, upright, uprightConfig, after: false, horizontal);
        Axis(build, horizontal ? XyPiece.YAxis : XyPiece.XAxis, flatAxis?.Part, plot.BottomLeft, plot.BottomRight, flat, flatConfig, after: true, horizontal: false);

        new PortableDiagramPanel(plot, wide, tall).Titles(build, XyPiece.AxisTitle, uprightTitle, flatTitle,
                                                          PortableDiagramAxis.Room(flat, upright: false, Tick(flatConfig)) + Gap);

        build.Open(XyPiece.Labels, part: null, stops: PortableStops.None);
        foreach (var (words, at, kind) in labels) words.Set(build, at, kind);
        build.Close();

        var width = Math.Max(plot.Right + edges.Right, edges.Left + legend.Size.Width);
        if (legend.Size.Height > 0) legend.Draw(build, new LayoutPoint(plot.Left + Math.Max(0, (plot.Width - legend.Size.Width) / 2), tall - legend.Size.Height));

        return new LayoutSize(Math.Max(width, wide), tall);
    }

    // ── The axes ────────────────────────────────────────────────────────────

    /// <summary>
    /// Where the categories are drawn along their axis, and what is written at each: a category somebody wrote, a number on
    /// an axis of numbers, or nothing on an axis with neither.
    /// </summary>
    private List<PortableDiagramTick> Categories(Chart chart, int slots)
    {
        var config = chart.Config.XAxis;
        var size = config.LabelFontSize ?? LabelSize;
        var ink = Ink.Written(config.LabelColour) ?? Style.TextMuted;

        if (chart.X is { Categorical: true } x)
            return [.. x.Categories.Select((category, at) =>
                new PortableDiagramTick(Along(chart, at, slots), config.ShowLabel ? Words.Written(category.Name, category.Hole, size, ink) : null))];

        if (chart.X is { Ranged: true } ranged)
        {
            var span = DiagramSpan.Of(ranged.Min!.Value, ranged.Max!.Value, widen: false);
                        return Ticks(config, ranged, span.Ticks().Select(tick => (tick.At, tick.Says)));
        }

        return [.. Enumerable.Range(0, slots).Select(at => new PortableDiagramTick(Along(chart, at, slots), null))];
    }

    /// <summary>Ticks at worked-out numbers, pressed as the axis they are on.</summary>
    private List<PortableDiagramTick> Ticks(XyAxisConfig config, AxisLine? axis, IEnumerable<(double At, string Says)> numbers)
    {
        var size = config.LabelFontSize ?? LabelSize;
        var ink = Ink.Written(config.LabelColour) ?? Style.TextMuted;

        return [.. numbers.Select(number => new PortableDiagramTick(number.At, config.ShowLabel ? Words.Worked(number.Says, axis?.Part, size, ink) : null))];
    }

    /// <summary>
    /// How far along the categories the <paramref name="at"/>th value stands: in the middle of its slot — or, over an axis of
    /// numbers, from the start of the range at the first to its end at the last, as Mermaid spreads them.
    /// </summary>
    private static double Along(Chart chart, int at, int slots) =>
        chart.X is { Categorical: false, Ranged: true } ? (slots > 1 ? (double)at / (slots - 1) : 0.5) : (at + 0.5) / slots;

    private PortableDiagramWords? AxisTitle(XyAxisConfig config, AxisLine? axis) =>
        config.ShowTitle && axis is { Title: not null } or { TitleHole: not null }
            ? Words.Written(axis.Title, axis.TitleHole, config.TitleFontSize ?? TitleSize, Ink.Written(config.TitleColour) ?? Style.Text)
            : null;

    private static double Tick(XyAxisConfig config) => config.ShowTick ? config.TickLength ?? PortableDiagramAxis.TickLength : 0;

    private void Axis(PortableBuilder build, string kind, ContentPart? part, LayoutPoint from, LayoutPoint to, IReadOnlyList<PortableDiagramTick> ticks,
                      XyAxisConfig config, bool after, bool horizontal)
    {
        // Down the side of a horizontal chart the categories run from the top: the first at the top, as they are written.
        var (start, end, marks) = horizontal
            ? (new LayoutPoint(from.X, to.Y), from, ticks)
            : (from, to, ticks);

        PortableDiagramAxis.Draw(build, kind, part, start, end, marks,
                                 new PortableDiagramStroke(Ink.Written(config.LineColour) ?? Style.CodeBorder, config.AxisLineWidth ?? 1),
                                 XyPiece.Tick, after, line: config.ShowAxisLine, tick: Tick(config));
    }

    // ── The series ──────────────────────────────────────────────────────────

    private void Bars(PortableBuilder build, Chart chart, int slots, XyConfig config, Func<double, double, LayoutPoint> on, double foot,
                      Func<double, double> reach, List<(PortableDiagramWords, LayoutPoint, string)> labels)
    {
        var bars = chart.Series.Where(series => series.Kind == Drawn.Bar).ToList();
        var span = Filled / slots / Math.Max(1, bars.Count);

        build.Open(XyPiece.Bars, part: null, stops: PortableStops.None);

        for (var order = 0; order < bars.Count; order++)
        {
            var series = bars[order];
            var fill = Colour(series);

            for (var at = 0; at < series.Points.Count && at < slots; at++)
            {
                if (series.Points[at] is not { Worth: { } worth } point) continue;

                var middle = Along(chart, at, slots) - (Filled / slots / 2) + ((order + 0.5) * span);
                var bar = new LayoutRect(on(middle - (span * 0.45), foot), on(middle + (span * 0.45), reach(worth)));
                var shape = build.Medium.Rectangle(bar);

                build.Open(XyPiece.Bar, point.Part, stops: PortableStops.None);
                build.Draw(PortableDiagramMarks.Outlined(build.Medium, shape, fill, null, 0));
                build.Occupies(shape);
                build.Close();

                if (config.ShowDataLabel) labels.Add(DataLabel(chart, point, worth, bar, fill, reach(worth) >= foot));
            }
        }

        build.Close();
    }

    /// <summary>A bar's value, written just inside its end — or past it, where the front matter asks.</summary>
    private (PortableDiagramWords, LayoutPoint, string) DataLabel(Chart chart, Valued point, double worth, LayoutRect bar, LayoutInk fill, bool rising)
    {
        var config = chart.Config;
        var outside = config.ShowDataLabelOutsideBar;
        var ink = Ink.Written(config.DataLabelColour) ?? (outside ? Style.Text : Ink.Over(fill));
        var words = Words.Worked(worth.ToString("G", CultureInfo.CurrentCulture), point.Part, LabelSize, ink);

        // Towards the bar's foot from its end, or away from it, whichever way the bar runs.
        if (chart.Orientation == XyOrientation.Horizontal)
        {
            var end = rising ? bar.Right : bar.Left;
            var x = rising == outside ? end + Gap : end - Gap - words.Width;
            return (words, new LayoutPoint(x, bar.Top + ((bar.Height - words.Height) / 2)), XyPiece.Value);
        }

        var top = rising ? bar.Top : bar.Bottom;
        var y = rising == outside ? top - Gap - words.Height : top + Gap;
        return (words, new LayoutPoint(bar.Left + ((bar.Width - words.Width) / 2), y), XyPiece.Value);
    }

    private void Lines(PortableBuilder build, Chart chart, int slots, Func<double, double, LayoutPoint> on, Func<double, double> reach,
                       List<(PortableDiagramWords, LayoutPoint, string)> labels)
    {
        build.Open(XyPiece.Lines, part: null, stops: PortableStops.None);

        foreach (var series in chart.Series.Where(series => series.Kind == Drawn.Line))
        {
            var ink = Colour(series);
            var route = new List<LayoutPoint>();
            var dots = new List<(Valued LayoutPoint, LayoutPoint At)>();

            for (var at = 0; at < series.Points.Count && at < slots; at++)
            {
                var point = series.Points[at];
                if (point.Worth is not { } worth) continue;

                var where = on(Along(chart, at, slots), reach(worth));
                route.Add(where);
                dots.Add((point, where));

                if (point.Label is null && point.LabelHole is null) continue;

                // A point's label sits over it — right of it on a horizontal chart — in its line's colour.
                var words = Words.Written(point.Label, point.LabelHole, LabelSize, ink);
                labels.Add((words, chart.Orientation == XyOrientation.Horizontal
                                   ? new LayoutPoint(where.X + Gap, where.Y - (words.Height / 2))
                                   : new LayoutPoint(where.X - (words.Width / 2), where.Y - Gap - words.Height), XyPiece.Label));
            }

            // One point is no line.
            if (route.Count >= 2)
                PortableDiagramConnector.Draw(build, XyPiece.Trace, series.Part, route, new PortableDiagramStroke(ink, LineWidth), DiagramHead.None, DiagramHead.None);

            // A line whose values are labelled marks where each value is, so a label says which point of the line it is about —
            // and every value is marked, not only the labelled ones, so the line still reads as the values it joins.
            if (series.Points.Any(point => point.Label is not null || point.LabelHole is not null) || route.Count == 1)
                foreach (var (point, at) in dots)
                {
                    var dot = build.Medium.Ellipse(at, Dotted, Dotted);

                    build.Open(XyPiece.Dot, point.Part, stops: PortableStops.None);
                    build.Draw(PortableDiagramMarks.Outlined(build.Medium, dot, ink, Ink.Surface, 1.5));
                    build.Occupies(dot);
                    build.Close();
                }
        }

        build.Close();
    }

    /// <summary>What a series is drawn in: <c>plotColorPalette</c>'s colour for its place, or the theme's.</summary>
    private LayoutInk Colour(Series series) => Ink.Series(series.Order, series.Colour);

    // ── The legend ──────────────────────────────────────────────────────────

    /// <summary>A row for each series with a name — along a line under the chart — where the front matter shows a legend.</summary>
    private PortableDiagramLegend Legend(Chart chart)
    {
        var config = chart.Config;
        var size = config.LegendFontSize ?? LegendSize;
        var ink = Ink.Written(config.LegendTextColour) ?? Style.Text;

        IReadOnlyList<PortableDiagramKey> rows = config.ShowLegend
            ? [.. chart.Series.Where(series => series.Name is not null || series.NameHole is not null)
                  .Select(series => new PortableDiagramKey(series.Part, Colour(series), [Words.Written(series.Name, series.NameHole, size, ink)]))]
            : [];

        return new PortableDiagramLegend(rows, [XyPiece.Name], across: true, Style.TextMuted);
    }
}