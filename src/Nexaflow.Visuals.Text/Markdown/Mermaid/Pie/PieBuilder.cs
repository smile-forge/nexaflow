using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Pie;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Pie;

/// <summary>The pieces a pie chart's layout is made of — its layers, and what is in them. Its legend is <see cref="DiagramLegend"/>'s.</summary>
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

    /// <summary>A slice's label, in its legend row.</summary>
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
internal sealed class PieBuilder : MermaidBuilder<PieChart>
{
    /// <summary>How big the chart is drawn before anything asks it to be smaller.</summary>
    private const double Radius = 130;

    private const double Smallest = 60;

    /// <summary>Clear air round the chart, and between the chart and its legend.</summary>
    private const double Margin = 12;

    private const double Apart = 24;

    /// <summary>How wide a legend column may be before it is said to be narrower.</summary>
    private const double LegendRoom = 240;

    private const double ShareSize = 10.5;
    private const double LegendSize = 11.5;

    /// <summary>How small a slice may be and still have its share written on it.</summary>
    private const double Labelled = 0.05;

    /// <summary>How far a slice the config picks out is pulled out of the chart.</summary>
    private const double PulledOut = 8;

    /// <summary>What each of the legend's columns is: a slice's label, its value, and its share.</summary>
    private static readonly string[] Columns = [PiePiece.Label, PiePiece.Value, PiePiece.Share];

    private PieBuilder(EditState state, MarkdownPalette palette, double pixelsPerDip, double room, bool writing)
        : base(state, palette, pixelsPerDip, room, writing) { }

    /// <summary>Lays a block's source out. Never null, and never throws.</summary>
    /// <param name="writing">Whether somebody is writing in it, which draws what is still to be written.</param>
    public static Laid Build(EditState state, MarkdownPalette palette, double pixelsPerDip, double room = double.PositiveInfinity,
                             bool writing = false) =>
        new PieBuilder(state, palette, pixelsPerDip, room, writing).Lay();

    /// <inheritdoc/>
    protected override PieChart Of(MermaidBlock block) => PieChart.Of(block);

    /// <summary>The front matter's <c>pieTitleTextColor</c>, where it writes one.</summary>
    protected override string? TitleColour => Diagram?.Config.TitleTextColour;

    /// <summary>The front matter's <c>pieTitleTextSize</c>, where it writes one.</summary>
    protected override double? TitleTextSize => Diagram?.Config.TitleTextSize;

    protected override Size Draw(PieChart chart, LayoutBuilder build)
    {
        var slices = chart.Slices.Where(slice => slice.Drawn).ToList();

        // While the chart is being written every slice written has a row, drawn or not: one still waiting for its value, or
        // worth nothing yet, is where the reader is typing, and a row that went away would take the caret with it. A chart
        // only being read lists what it draws.
        IReadOnlyList<PieSlice> listed = Writing ? chart.Slices : slices;

        // A pie of nothing is the source: there is no chart to look at, and what the reader wants is their own lines
        // back with whatever is wrong with them said underneath.
        if (listed.Count == 0) return AsWritten(build);

        var rows = listed.Select(slice => Key(chart, slice, slices.IndexOf(slice))).ToList();

        // The room the block is given is what the chart is fitted into: it shrinks, and where it cannot shrink enough
        // beside its legend, the legend goes under it. What is done with the block after that — where it sits on the
        // line, whether it is centred — is the document's, not the chart's.
        var where = Fitted(Legend(rows, chart.Config.Legend), chart.Config.Legend);
        var legend = Legend(rows, where);

        var radius = Fitting(legend.Size, where);
        var chartSize = new Size((radius * 2) + (Margin * 2), (radius * 2) + (Margin * 2));
        var (at, legendAt, size) = Places(chartSize, legend.Size, where, radius);

        var centre = new Point(at.X + Margin + radius, at.Y + Margin + radius);
        Wedges(build, chart, slices, centre, radius);
        Shares(build, chart, slices, centre, radius);
        legend.Draw(build, legendAt);

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
        var rim = edge > 0 ? Ink.Written(chart.Config.OuterStroke) ?? Palette.CodeBorder : null;

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
            build.Draw(new GeometryMark(shape, Fill(chart, slice, at), rim, edge));
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

            // What a share says is worked out rather than written, so there is nowhere in it to put a caret — but it
            // still stands for the slice, so pressing it means that slice like everything else drawn for it.
            var words = Worked(Percent(share), slice.Part, chart.Config.SectionTextSize ?? ShareSize, Over(chart, slice, order), FontWeights.SemiBold);
            var where = On(centre, reach, middle) + (slice.Highlighted ? Out(middle, PulledOut) : default);

            words.Set(build, new Point(where.X - (words.Width / 2), where.Y - (words.Height / 2)), PiePiece.Share);
        }

        build.Close();
    }

    private static string Percent(double share) =>
        (share * 100).ToString(share >= 0.1 ? "0.#" : "0.##", CultureInfo.CurrentCulture) + "%";

    // ── The legend ──────────────────────────────────────────────────────────

    /// <summary>A slice's row: its colour, its label, its value where the row shows one, and its share where it has a wedge to have one.</summary>
    /// <param name="order">Where the slice comes among those drawn, which is the colour it takes — or -1 for one not drawn.</param>
    private DiagramKey Key(PieChart chart, PieSlice slice, int order)
    {
        var size = chart.Config.LegendTextSize ?? LegendSize;
        var ink = Ink.Written(chart.Config.LegendTextColour) ?? Palette.Text;

        // The value where the chart shows its values — and, however it is set, wherever one is still to be written or is
        // wrong, since the row is then the only place to write it.
        var shown = chart.ShowsData || slice.ValueHole is not null || slice.Trouble is not null;

        return new DiagramKey(slice.Part, slice.Drawn ? Fill(chart, slice, order) : null,
        [
            Written(slice.Label, slice.LabelHole, size, ink),
            shown && slice.Value is not null ? Written(slice.Value, slice.ValueHole, size, ink) : null,
            slice.Drawn ? Worked(Percent(chart.Share(slice)), slice.Part, size, Palette.TextMuted) : null,
        ]);
    }

    /// <summary>The legend, down a column beside the chart or along a line over or under it.</summary>
    private DiagramLegend Legend(IReadOnlyList<DiagramKey> rows, PieLegend where) =>
        new(rows, Columns, across: where is PieLegend.Top or PieLegend.Bottom, Palette.TextMuted) { Room = LegendRoom };

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
    private PieLegend Fitted(DiagramLegend beside, PieLegend asked)
    {
        if (double.IsInfinity(Space) || asked is not (PieLegend.Left or PieLegend.Right)) return asked;

        return beside.Size.Width + Apart + (Smallest * 2) + (Margin * 2) <= Space ? asked : PieLegend.Bottom;
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
    private Brush Fill(PieChart chart, PieSlice slice, int order)
    {
        var colour = Ink.Series(Math.Max(0, order), slice.Colour);
        return chart.Config.Opacity is { } opacity && !slice.Highlighted ? DiagramInk.Faded(colour, opacity) : colour;
    }

    /// <summary>What is written on a slice: the front matter's ink, or whichever of the theme's reads against the slice.</summary>
    private Brush Over(PieChart chart, PieSlice slice, int order) =>
        Ink.Written(chart.Config.SectionTextColour) ?? Ink.Over(Fill(chart, slice, order));
}
