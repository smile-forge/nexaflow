using System.Globalization;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Pie;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Mermaid.Pie;

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
internal sealed class PortablePieBuilder : PortableContentBuilder
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

    internal PortablePieBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
                              PortableHost host) : base(reading, state, style, isReadOnly, medium, host)
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

        return Sheet.Laid(block, drawing, body, (block.Title, block.TitleSays, block.TitleAsWritten), Ink.Written(TitleColour), TitleTextSize);
    }

    protected override PortableCharacters Characters => PortableDiagramSheet.Characters(Style);

    /// <summary>What the front matter asks of the chart, as its stages left it on the block.</summary>
    private PieConfig Config => PortableDiagramConfig.Of(Reading, PieConfig.Default);

    /// <summary>A slice as its stages left it, and the part of the reading that is.</summary>
    private readonly record struct Slice(ContentPart Part, PieSliceNode Said);

    /// <summary>The front matter's <c>pieTitleTextColor</c>, where it writes one.</summary>
    private string? TitleColour => Config.TitleTextColour;

    /// <summary>The front matter's <c>pieTitleTextSize</c>, where it writes one.</summary>
    private double? TitleTextSize => Config.TitleTextSize;

    private LayoutSize Draw(MermaidBlock block, PortableBuilder build)
    {
        var config = Config;
        var all = new List<Slice>();

        foreach (var part in Reading.Root.SelfAndDescendants())
            if (part.Node is PieSliceNode said && part.Words() is not null) all.Add(new Slice(part, said));

        var slices = all.Where(slice => slice.Said.Drawn).ToList();
        var listed = all.Where(slice => slice.Said.Listed).ToList();

        // A pie of nothing is the source: there is no chart to look at, and what the reader wants is their own lines
        // back with whatever is wrong with them said underneath.
        if (listed.Count == 0) return Sheet.AsWritten();

        var rows = listed.Select(slice => Key(config, slice)).ToList();

        // The room the block is given is what the chart is fitted into: it shrinks, and where it cannot shrink enough
        // beside its legend, the legend goes under it. What is done with the block after that — where it sits on the
        // line, whether it is centred — is the document's, not the chart's.
        var where = Fitted(Legend(rows, config.Legend), config.Legend);
        var legend = Legend(rows, where);

        var radius = Fitting(legend.Size, where);
        var chartSize = new LayoutSize((radius * 2) + (Margin * 2), (radius * 2) + (Margin * 2));
        var (at, legendAt, size) = Places(chartSize, legend.Size, where, radius);

        var centre = new LayoutPoint(at.X + Margin + radius, at.Y + Margin + radius);
        Wedges(build, config, slices, centre, radius);
        Shares(build, config, slices, centre, radius);
        legend.Draw(build, legendAt);

        return size;
    }

    // ── The wedges ──────────────────────────────────────────────────────────

    private void Wedges(PortableBuilder build, PieConfig config, IReadOnlyList<Slice> slices, LayoutPoint centre, double radius)
    {
        var inner = radius * config.DonutHole;
        var gap = config.StrokeWidth;

        // A line round each wedge rather than one round the chart: a slice the config pulls out has to carry its own,
        // or it stands outside the very ring that was meant to hold it.
        var edge = config.OuterStrokeWidth ?? 0;
        var rim = edge > 0 ? Ink.Written(config.OuterStroke) ?? Style.CodeBorder : null;

        build.Open(PiePiece.Slices, part: null, stops: PortableStops.None);

        // Nothing worth a wedge — a chart still being written, or one whose every slice is worth nought: its outline, so
        // there is a chart to fill in.
        if (slices.Count == 0) build.Draw(PortableDiagramMarks.Outlined(build.Medium, Ring(build.Medium, centre, inner, radius), null, Style.CodeBorder, 1));

        var from = -Math.PI / 2;
        for (var at = 0; at < slices.Count; at++)
        {
            var slice = slices[at];
            var sweep = slice.Said.Share * 2 * Math.PI;
            var whole = slices.Count == 1;

            var shape = whole
                ? Ring(build.Medium, centre, inner, radius)
                : Sector(build.Medium, centre, inner, radius, from, from + sweep, gap);

            if (slice.Said.Highlighted) shape = Moved(build.Medium, shape, Out(from + (sweep / 2), PulledOut));

            build.Open(PiePiece.Wedge, slice.Part, stops: PortableStops.None);
            build.Draw(PortableDiagramMarks.Outlined(build.Medium, shape, Fill(config, slice), rim, edge));
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
    private static LayoutShape Sector(ILayoutMedium medium, LayoutPoint centre, double inner, double outer, double from, double to, double gap)
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

        var pen = medium.Outline();
        var start = On(centre, apex, from + narrow);
        pen.Begin(apex > 0 ? start : On(centre, apex, from), filled: true, closed: true);

        pen.LineTo(On(centre, outer, from + wide), stroked: true, smooth: false);
        pen.ArcTo(On(centre, outer, to - wide), outer, outer, 0,
                  large: sweep - (wide * 2) > Math.PI, true, stroked: true, smooth: false);
        pen.LineTo(On(centre, apex, to - narrow), stroked: true, smooth: false);

        if (apex > 0)
            pen.ArcTo(start, apex, apex, 0,
                      large: sweep - (narrow * 2) > Math.PI, false, stroked: true, smooth: false);

        return pen.Close();
    }

    /// <summary>The whole chart as one shape — what a single slice is, and what the rim is drawn as.</summary>
    private static LayoutShape Ring(ILayoutMedium medium, LayoutPoint centre, double inner, double outer)
    {
        var outside = medium.Ellipse(centre, outer, outer);
        if (inner <= 0) return outside;

        return medium.Combine(outside, medium.Ellipse(centre, inner, inner), ShapeCombine.Exclude);
    }

    /// <summary>How much of an angle half a gap takes up at a radius.</summary>
    private static double Inset(double half, double radius) =>
        half <= 0 || radius <= 0 ? 0 : Math.Asin(Math.Min(1, half / radius));

    private static LayoutPoint On(LayoutPoint centre, double radius, double angle) =>
        new(centre.X + (radius * Math.Cos(angle)), centre.Y + (radius * Math.Sin(angle)));

    private static LayoutVector Out(double angle, double by) => new(by * Math.Cos(angle), by * Math.Sin(angle));

    /// <summary>The wedge pulled out along <paramref name="by"/>, held in a group of its own that carries the move.</summary>
    private static LayoutShape Moved(ILayoutMedium medium, LayoutShape shape, LayoutVector by) =>
        medium.Transformed(medium.Group([shape]), LayoutMatrix.Translation(by.X, by.Y));

    // ── What is written on them ─────────────────────────────────────────────

    private void Shares(PortableBuilder build, PieConfig config, IReadOnlyList<Slice> slices, LayoutPoint centre, double radius)
    {
        var inner = radius * config.DonutHole;
        var reach = inner + ((radius - inner) * config.TextPosition);

        build.Open(PiePiece.Shares, part: null, stops: PortableStops.None);

        var from = -Math.PI / 2;
        for (var order = 0; order < slices.Count; order++)
        {
            var slice = slices[order];
            var share = slice.Said.Share;
            var sweep = share * 2 * Math.PI;
            var middle = from + (sweep / 2);
            from += sweep;

            if (share < Labelled) continue;

            // What a share says is worked out rather than written, so there is nowhere in it to put a caret — but it
            // still stands for the slice, so pressing it means that slice like everything else drawn for it.
            var words = Words.Worked(Percent(share), slice.Part, config.SectionTextSize ?? ShareSize, Over(config, slice), 600);
            var where = On(centre, reach, middle) + (slice.Said.Highlighted ? Out(middle, PulledOut) : default);

            words.Set(build, new LayoutPoint(where.X - (words.Width / 2), where.Y - (words.Height / 2)), PiePiece.Share);
        }

        build.Close();
    }

    private static string Percent(double share) =>
        (share * 100).ToString(share >= 0.1 ? "0.#" : "0.##", CultureInfo.CurrentCulture) + "%";

    // ── The legend ──────────────────────────────────────────────────────────

    /// <summary>A slice's row: its colour, its label, its value where the row shows one, and its share where its value counts — nought for one worth nought.</summary>
    private PortableDiagramKey Key(PieConfig config, Slice slice)
    {
        var size = config.LegendTextSize ?? LegendSize;
        var ink = Ink.Written(config.LegendTextColour) ?? Style.Text;
        var label = slice.Part.Words()!;
        var value = slice.Part.Inner(Kinds.Number);

        return new PortableDiagramKey(slice.Part, slice.Said.Counted ? Fill(config, slice) : null,
        [
            Words.Written(label, label.Parent.Hole(), size, ink),
            slice.Said.ValueShown && value is not null ? Words.Written(value, value.Parent.Hole(), size, ink) : null,
            slice.Said.Counted ? Words.Worked(Percent(slice.Said.Share), slice.Part, size, Style.TextMuted) : null,
        ]);
    }

    /// <summary>The legend, down a column beside the chart or along a line over or under it.</summary>
    private PortableDiagramLegend Legend(IReadOnlyList<PortableDiagramKey> rows, PieLegend where) =>
        new(rows, Columns, across: where is PieLegend.Top or PieLegend.Bottom, Style.TextMuted) { Room = LegendRoom };

    // ── Where it all goes ───────────────────────────────────────────────────

    /// <summary>How big the chart is drawn: as big as it likes, unless the room it has says smaller.</summary>
    private double Fitting(LayoutSize legend, PieLegend where)
    {
        if (double.IsInfinity(Sheet.Space)) return Radius;

        var beside = where is PieLegend.Left or PieLegend.Right ? legend.Width + Apart : 0;
        var room = (Sheet.Space - beside - (Margin * 2)) / 2;

        return Math.Max(Smallest, Math.Min(Radius, room));
    }

    /// <summary>
    /// Where the legend can go in the room there is. Beside the chart it takes its width away from the chart's, so in a
    /// column too narrow for both the legend goes under instead — which is room the chart never had to give up.
    /// </summary>
    private PieLegend Fitted(PortableDiagramLegend beside, PieLegend asked)
    {
        if (double.IsInfinity(Sheet.Space) || asked is not (PieLegend.Left or PieLegend.Right)) return asked;

        return beside.Size.Width + Apart + (Smallest * 2) + (Margin * 2) <= Sheet.Space ? asked : PieLegend.Bottom;
    }

    /// <summary>Where the chart goes, where the legend goes, and how much room the two of them take together.</summary>
    private static (LayoutPoint Chart, LayoutPoint Legend, LayoutSize LayoutSize) Places(LayoutSize chart, LayoutSize legend, PieLegend where, double radius)
    {
        var beside = legend.Width + Apart;
        var under = legend.Height + Apart;

        return where switch
        {
            PieLegend.Left => (new LayoutPoint(beside, 0), new LayoutPoint(0, Middle(chart.Height, legend.Height)),
                               new LayoutSize(chart.Width + beside, Math.Max(chart.Height, legend.Height))),

            PieLegend.Top => (new LayoutPoint(0, under), new LayoutPoint(Middle(chart.Width, legend.Width), 0),
                              new LayoutSize(Math.Max(chart.Width, legend.Width), chart.Height + under)),

            PieLegend.Bottom => (default, new LayoutPoint(Middle(chart.Width, legend.Width), chart.Height + Apart),
                                 new LayoutSize(Math.Max(chart.Width, legend.Width), chart.Height + under)),

            // In the middle of the chart, which is what a donut's hole is for.
            PieLegend.Centre => (default,
                                 new LayoutPoint(Middle(chart.Width, legend.Width), Middle(chart.Height, legend.Height)),
                                 chart),

            _ => (default, new LayoutPoint(chart.Width + Apart, Middle(chart.Height, legend.Height)),
                  new LayoutSize(chart.Width + beside, Math.Max(chart.Height, legend.Height))),
        };
    }

    private static double Middle(double room, double taken) => Math.Max(0, (room - taken) / 2);

    // ── Colour ──────────────────────────────────────────────────────────────

    /// <summary>
    /// What a slice is drawn in: the colour its front matter asks for, and otherwise the theme's next series colour.
    /// A slice the config picks out is drawn at full strength, whatever the rest are drawn at.
    /// </summary>
    private LayoutInk Fill(PieConfig config, Slice slice)
    {
        var colour = Ink.Series(Math.Max(0, slice.Said.Order), slice.Said.Colour);
        return config.Opacity is { } opacity && !slice.Said.Highlighted ? Ink.Faded(colour, opacity) : colour;
    }

    /// <summary>What is written on a slice: the front matter's ink, or whichever of the theme's reads against the slice.</summary>
    private LayoutInk Over(PieConfig config, Slice slice) =>
        Ink.Written(config.SectionTextColour) ?? Ink.Over(Fill(config, slice));
}
