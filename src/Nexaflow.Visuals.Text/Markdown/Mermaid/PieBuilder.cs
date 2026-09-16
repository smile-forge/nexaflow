using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Pie;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>The pieces a pie chart's layout is made of — its layers, and what is in them.</summary>
public static class PiePiece
{
    /// <summary>The wedges, which is the chart itself.</summary>
    public const string Slices = "Slices";

    /// <summary>One wedge. It stands for the whole slice as it was written, so pressing it means that line.</summary>
    public const string Wedge = "Wedge";

    /// <summary>What is written on the wedges — each slice's share.</summary>
    public const string Shares = "Shares";

    /// <inheritdoc cref="Shares"/>
    public const string Share = "Share";

    /// <summary>The legend, which says what the wedges are.</summary>
    public const string Legend = "Legend";

    /// <summary>One row of it: a swatch, a label, and the value where the chart shows its values.</summary>
    public const string Row = "Row";

    public const string Swatch = "Swatch";

    public const string Label = "Label";

    /// <summary>A slice's value, as it was written — which is what typing in the legend changes.</summary>
    public const string Value = "Value";


}

/// <summary>
/// Draws a <c>pie</c> block: a wedge per slice, its share written on it, and a legend saying what each one is.
///
/// <para>
/// <strong>Three layers, and they are the layout's parents.</strong> The wedges, what is written on them, and the
/// legend are drawn as three subtrees, so the shares or the legend can be turned off without touching the rest and the
/// labels are always above every wedge. What belongs with what is not in the layout at all: a wedge, the share written
/// on it and its legend row all point at the same slice in the source, so choosing any of them chooses all three, and
/// the value in the legend is the number itself and is typed into.
/// </para>
/// <para>
/// <strong>The gaps between wedges are gaps.</strong> Each radial edge is moved in by half a gap, parallel to where it
/// was, so the space between two slices is the same width all the way out — where insetting by an angle leaves a gap
/// that opens out towards the rim — and where the slices meet in the middle the apex moves out far enough for the two
/// edges to meet. Drawing a seam in the background colour instead, as a stroke, is what a chart does when it cannot say
/// what it occupies: it breaks the moment anything is drawn behind it, and hands a press on one slice to another.
/// </para>
/// </summary>
internal sealed class PieBuilder : MermaidBuilder
{
    /// <summary>How big the chart is drawn before anything asks it to be smaller.</summary>
    private const double Radius = 130;

    private const double Smallest = 60;

    /// <summary>Clear air round the whole thing, and between the chart and its legend.</summary>
    private const double Margin = 12;

    private const double Apart = 24;

    private const double SwatchSize = 14;
    private const double SwatchGap = 8;
    private const double RowGap = 6;

    /// <summary>How wide a legend column may be before its text is set narrower.</summary>
    private const double LegendRoom = 240;

    private const double ShareSize = 10.5;
    private const double LegendSize = 11.5;

    /// <summary>How small a slice may be and still have its share written on it.</summary>
    private const double Labelled = 0.05;

    /// <summary>How far a slice the config picks out is pulled out of the chart.</summary>
    private const double PulledOut = 8;

    private PieChart? _chart;

    private PieBuilder(EditState state, MarkdownPalette palette, double pixelsPerDip, double room, bool writing)
        : base(state, palette, pixelsPerDip, room, writing) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    /// <param name="writing">Whether somebody is writing in it, which draws what is still to be written.</param>
    public static Laid Build(EditState state, MarkdownPalette palette, double pixelsPerDip, double room = double.PositiveInfinity,
                             bool writing = false) =>
        new PieBuilder(state, palette, pixelsPerDip, room, writing).Lay();

    /// <summary>The same, for a block that has no caret in it.</summary>
    public static Laid Build(string source, MarkdownPalette palette, double pixelsPerDip, double room = double.PositiveInfinity,
                             bool writing = false) =>
        Build(EditState.For(source), palette, pixelsPerDip, room, writing);

    public static Editing.ContentElement Element(string source, DiagramRenderOptions options) =>
        Host(source, options, Build, readOnly: false);

    /// <inheritdoc/>
    protected override ContentNode Reading(string source) => PiePipeline.Read(source, holes: Writing);

    /// <summary>The chart's own title where it has one, and the front matter's otherwise.</summary>
    protected override (ContentPart? Part, string? Text) TitleOf(MermaidBlock block) =>
        _chart is null ? base.TitleOf(block) : (_chart.Title, _chart.TitleText);

    /// <summary>The front matter's <c>pieTitleTextColor</c>, where it writes one.</summary>
    protected override Brush TitleInk => Colour(_chart?.Config.TitleTextColour) ?? base.TitleInk;

    protected override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        var chart = _chart = PieChart.Of(block);
        var slices = chart.Slices.Where(slice => slice.Drawn).ToList();

        // While the chart is being written every slice written has a row, drawn or not: one still waiting for its value, or
        // worth nothing yet, is where the reader is typing, and a row that went away would take the caret with it. A chart
        // only being read lists what it draws.
        IReadOnlyList<PieSlice> listed = Writing ? chart.Slices : slices;

        // A pie of nothing is the source: there is no chart to look at, and what the reader wants is their own lines
        // back with whatever is wrong with them said underneath.
        if (listed.Count == 0) return AsWritten(build);

        var rows = listed.Select(slice => Row(chart, slice, slices.IndexOf(slice))).ToList();

        // The room the block is given is what the chart is fitted into: it shrinks, and where it cannot shrink enough
        // beside its legend, the legend goes under it. What is done with the block after that — where it sits on the
        // line, whether it is centred — is the document's, not the chart's.
        var where = Fitted(rows, chart.Config.Legend);
        var legend = Shape(rows, where);

        var radius = Fitting(legend, where);
        var chartSize = new Size((radius * 2) + (Margin * 2), (radius * 2) + (Margin * 2));
        var (at, legendAt, size) = Places(chartSize, legend, where, radius);

        var centre = new Point(at.X + Margin + radius, at.Y + Margin + radius);
        Wedges(build, chart, slices, centre, radius);
        Shares(build, chart, slices, centre, radius);
        Legend(build, chart, rows, legendAt, where);

        return size;
    }

    // ── The wedges ──────────────────────────────────────────────────────────

    private void Wedges(LayoutBuilder build, PieChart chart, IReadOnlyList<PieSlice> slices, Point centre, double radius)
    {
        var inner = radius * chart.Config.DonutHole;
        var gap = chart.Config.StrokeWidth;

        // A line round each wedge rather than one round the chart: a slice the config pulls out has to carry its own,
        // or it stands outside the very ring that was meant to hold it.
        var edge = chart.Config.OuterStrokeWidth ?? 0;
        var rim = edge > 0 ? Colour(chart.Config.OuterStroke) ?? Palette.CodeBorder : null;

        build.Open(PiePiece.Slices, part: null, stops: Stops.None);

        // Nothing worth a wedge yet, which only a chart being written shows: its outline, so there is a chart to fill in.
        if (slices.Count == 0) build.Draw(new GeometryMark(Ring(centre, inner, radius), null, Palette.CodeBorder, 1));

        var from = -Math.PI / 2;
        for (var at = 0; at < slices.Count; at++)
        {
            var slice = slices[at];
            var sweep = chart.Share(slice) * 2 * Math.PI;
            var whole = slices.Count == 1;

            var shape = whole
                ? Ring(centre, inner, radius)
                : Sector(centre, inner, radius, from, from + sweep, gap);

            if (slice.Highlighted) shape = Moved(shape, Out(from + (sweep / 2), PulledOut));

            build.Open(PiePiece.Wedge, slice.Part, stops: Stops.None);
            build.Draw(new GeometryMark(shape, Ink(chart, slice, at), rim, edge));
            build.Occupies(shape);
            build.Close();

            from += sweep;
        }

        build.Close();
    }

    /// <summary>
    /// A wedge with a gap of <paramref name="gap"/> between it and whatever is beside it: each radial edge moved in by
    /// half of that, parallel to itself, which is what makes the gap the same width all the way out.
    /// </summary>
    private static Geometry Sector(Point centre, double inner, double outer, double from, double to, double gap)
    {
        var half = Math.Max(gap, 0) / 2;
        var sweep = to - from;

        // The angle half a gap comes to at a radius. A slice too narrow to take one keeps its edges where they are:
        // a sliver drawn as nothing at all would be a slice the reader cannot see or press.
        var wide = Inset(half, outer);
        if (sweep <= (wide * 2) + 0.001) { half = 0; wide = 0; }

        // Where the two edges meet: on a donut that is the inner arc, and on a pie it is however far out the edges
        // have to start for a gap of this width to close.
        var apex = inner > 0 ? inner : half > 0 ? half / Math.Sin(sweep / 2) : 0;
        var narrow = apex > 0 ? Math.Min(Inset(half, apex), (sweep / 2) - 0.001) : 0;

        var shape = new StreamGeometry();
        using (var pen = shape.Open())
        {
            var start = On(centre, apex, from + narrow);
            pen.BeginFigure(apex > 0 ? start : On(centre, apex, from), isFilled: true, isClosed: true);

            pen.LineTo(On(centre, outer, from + wide), isStroked: true, isSmoothJoin: false);
            pen.ArcTo(On(centre, outer, to - wide), new Size(outer, outer), 0,
                      isLargeArc: sweep - (wide * 2) > Math.PI, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: false);
            pen.LineTo(On(centre, apex, to - narrow), isStroked: true, isSmoothJoin: false);

            if (apex > 0)
                pen.ArcTo(start, new Size(apex, apex), 0,
                          isLargeArc: sweep - (narrow * 2) > Math.PI, SweepDirection.Counterclockwise, isStroked: true, isSmoothJoin: false);
        }

        shape.Freeze();
        return shape;
    }

    /// <summary>The whole chart as one shape — what a single slice is, and what the rim is drawn as.</summary>
    private static Geometry Ring(Point centre, double inner, double outer)
    {
        var outside = new EllipseGeometry(centre, outer, outer);
        if (inner <= 0)
        {
            outside.Freeze();
            return outside;
        }

        var shape = new CombinedGeometry(GeometryCombineMode.Exclude, outside, new EllipseGeometry(centre, inner, inner));
        shape.Freeze();
        return shape;
    }

    /// <summary>How much of an angle half a gap takes up at a radius.</summary>
    private static double Inset(double half, double radius) =>
        half <= 0 || radius <= 0 ? 0 : Math.Asin(Math.Min(1, half / radius));

    private static Point On(Point centre, double radius, double angle) =>
        new(centre.X + (radius * Math.Cos(angle)), centre.Y + (radius * Math.Sin(angle)));

    private static Vector Out(double angle, double by) => new(by * Math.Cos(angle), by * Math.Sin(angle));

    private static Geometry Moved(Geometry shape, Vector by)
    {
        var moved = new GeometryGroup { Transform = new TranslateTransform(by.X, by.Y) };
        moved.Children.Add(shape);
        moved.Freeze();
        return moved;
    }

    // ── What is written on them ─────────────────────────────────────────────

    private void Shares(LayoutBuilder build, PieChart chart, IReadOnlyList<PieSlice> slices, Point centre, double radius)
    {
        var inner = radius * chart.Config.DonutHole;
        var reach = inner + ((radius - inner) * chart.Config.TextPosition);

        build.Open(PiePiece.Shares, part: null, stops: Stops.None);

        var from = -Math.PI / 2;
        for (var order = 0; order < slices.Count; order++)
        {
            var slice = slices[order];
            var share = chart.Share(slice);
            var sweep = share * 2 * Math.PI;
            var middle = from + (sweep / 2);
            from += sweep;

            if (share < Labelled) continue;

            var ink = Over(chart, slice, order);
            var text = Text(Percent(share), chart.Config.SectionTextSize ?? ShareSize, ink, FontWeights.SemiBold);
            var pulled = slice.Highlighted ? Out(middle, PulledOut) : default;
            var where = On(centre, reach, middle) + pulled;

            // What a share says is worked out rather than written, so there is nowhere in it to put a caret — but it
            // still stands for the slice, so pressing it means that slice like everything else drawn for it.
            LayoutText.Words(build, text, new Point(where.X - (text.Width / 2), where.Y - (text.Height / 2)),
                             text.Width, TextAlignment.Left, slice.Part, PiePiece.Share, maps: false, ink: ink);
        }

        build.Close();
    }

    private static string Percent(double share) =>
        (share * 100).ToString(share >= 0.1 ? "0.#" : "0.##", CultureInfo.CurrentCulture) + "%";

    // ── The legend ──────────────────────────────────────────────────────────

    /// <summary>One row of the legend, measured but not yet placed.</summary>
    /// <param name="Ink">What the slice is drawn in, or null for one with no wedge to match.</param>
    /// <param name="Value">The value, where the row shows one.</param>
    /// <param name="Share">Its share of the chart, where it has a wedge to have one.</param>
    /// <param name="Letter">A small letter in the legend's type, which is what a hole in the row is sized by.</param>
    private sealed record Entry(PieSlice Slice, Brush? Ink, FormattedText Label, FormattedText? Value, FormattedText? Share,
                                FormattedText Letter)
    {
        public double Height => Math.Max(SwatchSize, Math.Max(Letter.Height, Share?.Height ?? 0));

        /// <summary>How wide the label is set: its words, or the hole standing where they go.</summary>
        public double LabelWidth => Slice.LabelHole is null ? Label.Width : LayoutText.HoleWidth(Letter);

        /// <summary>How wide the value is set: nothing where the row shows none, its number, or the hole standing where it goes.</summary>
        public double ValueWidth => Value is null ? 0 : Slice.ValueHole is null ? Value.Width : LayoutText.HoleWidth(Letter);

        public double Width => SwatchSize + SwatchGap + LabelWidth + SwatchGap + (Value is null ? 0 : ValueWidth + SwatchGap)
                               + (Share?.Width ?? 0);

        /// <summary>Where each part of the row goes when the rows are set as a table — see <see cref="PieBuilder.Legend"/>.</summary>
        public double Left(IReadOnlyList<Entry> rows, int column) => column switch
        {
            0 => SwatchSize + SwatchGap,
            1 => SwatchSize + SwatchGap + rows.Max(row => row.LabelWidth) + SwatchGap,
            _ => SwatchSize + SwatchGap + rows.Max(row => row.LabelWidth) + SwatchGap
                 + (rows.Any(row => row.Value is not null) ? rows.Max(row => row.ValueWidth) + SwatchGap : 0),
        };
    }

    /// <param name="order">Where the slice comes among those drawn, which is the colour it takes — or -1 for one not drawn.</param>
    private Entry Row(PieChart chart, PieSlice slice, int order)
    {
        var size = chart.Config.LegendTextSize ?? LegendSize;
        var ink = Colour(chart.Config.LegendTextColour) ?? Palette.Text;

        // The value where the chart shows its values — and, however it is set, wherever one is still to be written or is
        // wrong, since the row is then the only place to write it.
        var shown = chart.ShowsData || slice.ValueHole is not null || slice.Trouble is not null;

        return new Entry(
            slice,
            slice.Drawn ? Ink(chart, slice, order) : null,
            Text(Shown(slice.Label), size, ink),
            shown && slice.Value is not null ? Text(slice.Value.Text, size, ink) : null,
            slice.Drawn ? Text(Percent(chart.Share(slice)), size, Palette.TextMuted) : null,
            Text("x", size, ink));
    }

    private static Size Shape(IReadOnlyList<Entry> rows, PieLegend where) =>
        where is PieLegend.Top or PieLegend.Bottom
            ? new Size(rows.Sum(row => row.Width + Apart) - Apart, rows.Max(row => row.Height))
            : new Size(Math.Min(LegendRoom, rows.Max(row => row.Left(rows, 2) + (row.Share?.Width ?? 0))),
                       rows.Sum(row => row.Height + RowGap) - RowGap);

    private void Legend(LayoutBuilder build, PieChart chart, IReadOnlyList<Entry> rows, Point at, PieLegend where)
    {
        build.Open(PiePiece.Legend, part: null, stops: Stops.None);

        var across = where is PieLegend.Top or PieLegend.Bottom;
        var ink = Colour(chart.Config.LegendTextColour) ?? Palette.Text;
        var x = at.X;
        var y = at.Y;

        foreach (var row in rows)
        {
            build.Open(PiePiece.Row, row.Slice.Part, new Point(x, y), stops: Stops.None);

            var middle = (row.Height - SwatchSize) / 2;
            build.Open(PiePiece.Swatch, part: null, new Point(0, middle), stops: Stops.None);
            build.Draw(Swatch(row.Ink));
            build.Close();

            // Down a column the rows are a table, so the values line up under each other; along a row they are set one
            // after another, because a table of one row is a row.
            Cell(build, row.Label, row.Slice.Label, row.Slice.LabelHole, new Point(row.Left(rows, 0), 0), PiePiece.Label,
                 row.Letter, ink);

            var value = across ? row.Left(rows, 0) + row.LabelWidth + SwatchGap : row.Left(rows, 1);

            // The value is the number itself, so this is where it is typed into.
            if (row.Value is { } worth)
                Cell(build, worth, row.Slice.Value, row.Slice.ValueHole, new Point(value, 0), PiePiece.Value, row.Letter, ink);

            if (row.Share is { } share)
            {
                var left = across ? value + (row.Value is null ? 0 : row.ValueWidth + SwatchGap) : row.Left(rows, 2);
                LayoutText.Words(build, share, new Point(left, 0), share.Width, TextAlignment.Left,
                                 row.Slice.Part, PiePiece.Share, maps: false, ink: Palette.TextMuted);
            }

            build.Close();

            if (across) x += row.Width + Apart;
            else y += row.Height + RowGap;
        }

        build.Close();
    }

    /// <summary>A row's swatch: the colour its wedge is drawn in, or only the square one goes in for a slice with no wedge yet.</summary>
    private LayoutMark Swatch(Brush? ink)
    {
        if (ink is not null) return new RuleMark(new Rect(0, 0, SwatchSize, SwatchSize), ink);

        var square = new RectangleGeometry(new Rect(0.5, 0.5, SwatchSize - 1, SwatchSize - 1));
        square.Freeze();
        return new GeometryMark(square, null, Palette.TextMuted, 1);
    }

    /// <summary>What a row says in one of its columns: the words written there, or the hole standing where they are still to go.</summary>
    private static void Cell(LayoutBuilder build, FormattedText text, ContentPart? part, ContentPart? hole, Point at, string kind,
                             FormattedText letter, Brush ink)
    {
        if (hole is not null) LayoutText.Hole(build, hole, at, letter, ink);
        else LayoutText.Words(build, text, at, text.Width, TextAlignment.Left, part, kind,
                              maps: text.Text == part?.Text, writes: part is not null, ink: ink);
    }

    // ── Where it all goes ───────────────────────────────────────────────────

    /// <summary>How big the chart is drawn: as big as it likes, unless the room it has says smaller.</summary>
    private double Fitting(Size legend, PieLegend where)
    {
        if (double.IsInfinity(Space)) return Radius;

        var beside = where is PieLegend.Left or PieLegend.Right ? legend.Width + Apart : 0;
        var room = (Space - beside - (Margin * 2)) / 2;

        return Math.Max(Smallest, Math.Min(Radius, room));
    }

    /// <summary>
    /// Where the legend can go in the room there is. Beside the chart it takes its width away from the chart's, so in a
    /// column too narrow for both the legend goes under instead — which is room the chart never had to give up.
    /// </summary>
    private PieLegend Fitted(IReadOnlyList<Entry> rows, PieLegend asked)
    {
        if (double.IsInfinity(Space) || asked is not (PieLegend.Left or PieLegend.Right)) return asked;

        var beside = Shape(rows, asked).Width + Apart + (Smallest * 2) + (Margin * 2);
        return beside <= Space ? asked : PieLegend.Bottom;
    }

    /// <summary>Where the chart goes, where the legend goes, and how much room the two of them take together.</summary>
    private static (Point Chart, Point Legend, Size Size) Places(Size chart, Size legend, PieLegend where, double radius)
    {
        var beside = legend.Width + Apart;
        var under = legend.Height + Apart;

        return where switch
        {
            PieLegend.Left => (new Point(beside, 0), new Point(0, Middle(chart.Height, legend.Height)),
                               new Size(chart.Width + beside, Math.Max(chart.Height, legend.Height))),

            PieLegend.Top => (new Point(0, under), new Point(Middle(chart.Width, legend.Width), 0),
                              new Size(Math.Max(chart.Width, legend.Width), chart.Height + under)),

            PieLegend.Bottom => (default, new Point(Middle(chart.Width, legend.Width), chart.Height + Apart),
                                 new Size(Math.Max(chart.Width, legend.Width), chart.Height + under)),

            // In the middle of the chart, which is what a donut's hole is for.
            PieLegend.Centre => (default,
                                 new Point(Middle(chart.Width, legend.Width), Middle(chart.Height, legend.Height)),
                                 chart),

            _ => (default, new Point(chart.Width + Apart, Middle(chart.Height, legend.Height)),
                  new Size(chart.Width + beside, Math.Max(chart.Height, legend.Height))),
        };
    }

    private static double Middle(double room, double taken) => Math.Max(0, (room - taken) / 2);

    // ── Colour ──────────────────────────────────────────────────────────────

    /// <summary>
    /// What a slice is drawn in: the colour its front matter asks for, and otherwise the theme's next series colour.
    /// A slice the config picks out is drawn at full strength, whatever the rest are drawn at.
    /// </summary>
    private Brush Ink(PieChart chart, PieSlice slice, int order)
    {
        var colour = Colour(slice.Colour) ?? Palette.Series[Math.Max(0, order) % Palette.Series.Count];

        if (chart.Config.Opacity is not { } opacity || slice.Highlighted || opacity >= 1) return colour;

        var faded = colour.Clone();
        faded.Opacity = Math.Max(0, opacity);
        faded.Freeze();
        return faded;
    }

    /// <summary>What is written on a slice, in ink that reads against it.</summary>
    private Brush Over(PieChart chart, PieSlice slice, int order) =>
        Colour(chart.Config.SectionTextColour)
        // The theme's ink and its ground: which of the two reads on a slice depends on how bright the slice is.
        ?? DiagramBrushes.OnColor(DiagramBrushes.ColorOf(Ink(chart, slice, order), Colors.Gray), Palette.QrDark, Palette.QrLight);
}
