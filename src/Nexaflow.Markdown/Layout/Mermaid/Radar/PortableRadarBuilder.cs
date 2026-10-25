using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Radar;
using Nexaflow.Markdown.Layout.Prose;

namespace Nexaflow.Markdown.Layout.Mermaid.Radar;

/// <summary>
/// Draws a <c>radar-beta</c> block: a spoke per axis with what it is called at its end, the graticule's rings behind them, a
/// filled curve through how far each curve reaches along every axis, and a legend saying which curve is which.
///
/// <para>
/// <strong>Layers by how it looks.</strong> The rings, the spokes, the curves and the labels are four subtrees, so the curves
/// are always over the web and the labels over everything. What belongs together is said by the source: a spoke and its
/// label point at the axis they were written as, a curve and its legend row at the curve — so choosing one chooses the other —
/// and a label or a name is the characters written and is typed into.
/// </para>
/// <para>
/// <strong>Placed as Mermaid places it.</strong> The first axis points straight up and the rest follow it clockwise. A curve
/// reaches out along each axis as far as its value is from <c>min</c> to <c>max</c>, rounded by <c>curveTension</c> over a
/// circular graticule and straight over a polygon. The legend sits to the right of the chart, as Mermaid's does, and goes
/// under it where the room is too narrow for both.
/// </para>
/// </summary>
internal sealed class PortableRadarBuilder : PortableContentBuilder
{
    /// <summary>How big the chart is drawn before anything asks for another size.</summary>
    private const double Radius = 130;

    private const double Smallest = 50;

    /// <summary>Clear air round the chart where the front matter asks for none, and between the chart and its legend.</summary>
    private const double Margin = 12;

    private const double Apart = 24;

    /// <summary>Clear air between the end of where a label sits and the label.</summary>
    private const double LabelGap = 4;

    private const double LabelSize = 12;
    private const double LegendSize = 12;

    /// <summary>How solid a curve is filled where no front matter says: enough to see where curves cross.</summary>
    private const double CurveOpacity = 0.3;

    private const double CurveStrokeWidth = 2;

    /// <summary>How solid each ring is filled where no front matter says: faint, since the rings stack towards the middle.</summary>
    private const double GraticuleOpacity = 0.08;

    /// <summary>What the legend's one column is.</summary>
    private static readonly string[] Columns = [RadarPiece.Name];

    internal PortableRadarBuilder(ContentReading reading, EditState state, PortableStyle style, bool isReadOnly, ILayoutMedium medium,
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

    /// <summary>The chart as its stages left it on the block.</summary>
    private RadarBlockNode? Chart => Reading.Root.Node as RadarBlockNode;

    /// <summary>A curve as its stages left it, and the part of the reading that is.</summary>
    private readonly record struct Curve(ContentPart Part, RadarCurveNode Said);

    /// <summary>The front matter's <c>titleColor</c>, where it writes one.</summary>
    private string? TitleColour => Chart?.Config.TitleTextColour;

    /// <summary>The front matter's <c>fontSize</c>, where it writes one.</summary>
    private double? TitleTextSize => Chart?.Config.TitleTextSize;

    private LayoutSize Draw(MermaidBlock block, PortableBuilder build)
    {
        if (Chart is not { } chart) return Sheet.AsWritten();
        var config = chart.Config;

        // Which axes have spokes and which curves the legend lists, its stages said — while the chart is being written, those
        // still to name as well.
        var axes = new List<ContentPart>();
        var listed = new List<Curve>();
        ContentPart? shaped = null;

        foreach (var part in Reading.Root.SelfAndDescendants())
        {
            switch (part.Node)
            {
                case RadarAxisNode:
                    axes.Add(part);
                    break;

                case RadarCurveNode { Listed: true } curve:
                    listed.Add(new Curve(part, curve));
                    break;

                case RadarShapingNode:
                    shaped = part;
                    break;
            }
        }

        // A radar of no axes is the source: there is nothing to draw a curve on, and what the reader wants is their own lines
        // back with whatever is wrong with them said underneath.
        if (axes.Count == 0) return Sheet.AsWritten();

        var labels = axes.Select(axis => Words.Written(Says(axis), HoleOf(axis), config.AxisLabelTextSize ?? LabelSize, Style.Text)).ToList();
        var margins = new DiagramEdges(config.MarginLeft ?? Margin, config.MarginTop ?? Margin, config.MarginRight ?? Margin, config.MarginBottom ?? Margin);

        var legend = new PortableDiagramLegend(chart.ShowsLegend ? [.. listed.Select(curve => Key(config, curve))] : [], Columns, across: false, Style.TextMuted)
        {
            Square = config.LegendBoxSize ?? PortableDiagramLegend.SwatchSize,
        };

        var beside = legend.Size.Width > 0 ? legend.Size.Width + Apart : 0;
        var room = config.UseMaxWidth ? Sheet.Space : double.PositiveInfinity;
        var under = beside > 0 && Wide(Smallest) + beside > room;
        var radius = Fitting(Math.Min(config.Width ?? Radius * 2, config.Height ?? Radius * 2) / 2, room - (under ? 0 : beside));

        var extent = Extent(radius);
        var centre = new LayoutPoint(margins.Left - extent.X, margins.Top - extent.Y);
        var chartSize = new LayoutSize(extent.Width + margins.Left + margins.Right, extent.Height + margins.Top + margins.Bottom);

        Graticule(build, chart, shaped, axes.Count, centre, radius);
        Spokes(build, config, axes, centre, radius);
        Curves(build, chart, axes.Count, [.. listed.Where(curve => curve.Said.Drawn)], centre, radius);
        Labels(build, labels, axes.Select((_, at) => Label(at, radius, centre)).ToList());

        if (legend.Size.Width <= 0) return chartSize;

        if (under)
        {
            legend.Draw(build, new LayoutPoint(Math.Max(0, (chartSize.Width - legend.Size.Width) / 2), chartSize.Height + Apart));
            return new LayoutSize(Math.Max(chartSize.Width, legend.Size.Width), chartSize.Height + Apart + legend.Size.Height);
        }

        legend.Draw(build, new LayoutPoint(chartSize.Width + Apart, margins.Top));
        return new LayoutSize(chartSize.Width + beside, Math.Max(chartSize.Height, margins.Top + legend.Size.Height));

        // Where each label sits round a chart of a radius, its middle at the origin: hung off the point past the end of its
        // spoke by the side facing the chart, so a label under the chart hangs from its top and one right of it from its left.
        LayoutRect Label(int at, double reach, LayoutPoint middle)
        {
            var angle = Angle(at, axes.Count);
            var (across, down) = (Math.Cos(angle), Math.Sin(angle));
            var words = labels[at];
            var point = On(middle, (reach * config.AxisLabelFactor) + LabelGap, angle);

            return new LayoutRect(point.X - (words.Width * (1 - across) / 2), point.Y - (words.Height * (1 - down) / 2), words.Width, words.Height);
        }

        // Everything the chart draws at a radius, round its middle at the origin: its rings, its spokes and its labels.
        LayoutRect Extent(double reach)
        {
            var reached = new LayoutRect(-reach, -reach, reach * 2, reach * 2);
            var spoke = reach * config.AxisScaleFactor;
            reached = LayoutRect.Union(reached, new LayoutRect(-spoke, -spoke, spoke * 2, spoke * 2));

            for (var at = 0; at < axes.Count; at++) reached = LayoutRect.Union(reached, Label(at, reach, default));
            return reached;
        }

        double Wide(double reach) => Extent(reach).Width + margins.Left + margins.Right;

        // As big as it is asked to be, unless the room says smaller — and never smaller than can be read.
        double Fitting(double wanted, double width)
        {
            wanted = Math.Max(Smallest, wanted);
            if (double.IsInfinity(width) || Wide(wanted) <= width) return wanted;
            if (Wide(Smallest) >= width) return Smallest;

            var (low, high) = (Smallest, wanted);
            for (var step = 0; step < 24; step++)
            {
                var middle = (low + high) / 2;
                if (Wide(middle) <= width) low = middle;
                else high = middle;
            }

            return low;
        }
    }

    // ── The graticule ───────────────────────────────────────────────────────

    private void Graticule(PortableBuilder build, RadarBlockNode chart, ContentPart? shaped, int count, LayoutPoint centre, double radius)
    {
        var config = chart.Config;
        var ink = Ink.Written(config.GraticuleColour) ?? Style.CodeBorder;
        var fill = PortableDiagramInk.Faded(build.Medium, ink, config.GraticuleOpacity ?? GraticuleOpacity);

        build.Open(RadarPiece.Graticule, part: null, stops: PortableStops.None);

        for (var ring = 1; ring <= chart.Ticks; ring++)
        {
            var reach = radius * ring / chart.Ticks;

            // A polygon of fewer than three corners encloses nothing, so it is a circle like the rest.
            var shape = chart.Graticule == RadarGraticule.Polygon && count >= 3
                ? PortableDiagramCurve.Closed(build.Medium, [.. Enumerable.Range(0, count).Select(at => On(centre, reach, Angle(at, count)))])
                : Circle(build.Medium, centre, reach);

            build.Open(RadarPiece.Ring, shaped, stops: PortableStops.None);
            build.Draw(PortableDiagramMarks.Outlined(build.Medium, shape, fill, ink, config.GraticuleStrokeWidth ?? 1));
            build.Occupies(shape);
            build.Close();
        }

        build.Close();
    }

    private static LayoutShape Circle(ILayoutMedium medium, LayoutPoint centre, double radius)
    {
        var circle = medium.Ellipse(centre, radius, radius);
        return circle;
    }

    // ── The spokes ──────────────────────────────────────────────────────────

    private void Spokes(PortableBuilder build, RadarConfig config, IReadOnlyList<ContentPart> axes, LayoutPoint centre, double radius)
    {
        var stroke = new PortableDiagramStroke(Ink.Written(config.AxisColour) ?? Style.TextMuted, config.AxisStrokeWidth ?? 1);
        var reach = radius * config.AxisScaleFactor;

        build.Open(RadarPiece.Spokes, part: null, stops: PortableStops.None);

        // A spoke of no length is nothing to draw, or to press.
        if (reach > 0)
            for (var at = 0; at < axes.Count; at++)
                PortableDiagramConnector.Draw(build, RadarPiece.Spoke, Standing(axes[at]), [centre, On(centre, reach, Angle(at, axes.Count))], stroke,
                                              DiagramHead.None, DiagramHead.None);

        build.Close();
    }

    // ── The curves ──────────────────────────────────────────────────────────

    private void Curves(PortableBuilder build, RadarBlockNode chart, int spokes, IReadOnlyList<Curve> curves, LayoutPoint centre, double radius)
    {
        var config = chart.Config;
        var tension = chart.Graticule == RadarGraticule.Circle ? config.CurveTension : 0;

        // Where a curve gives an axis nothing it stays at the middle, as a value of min would.
        var shapes = curves
            .Select(curve => PortableDiagramCurve.Closed(build.Medium, 
                [.. curve.Said.Points.Select((point, at) => On(centre, radius * Reach(chart, point ?? chart.Min), Angle(at, spokes)))], tension))
            .ToList();

        build.Open(RadarPiece.Curves, part: null, stops: PortableStops.None);

        for (var order = 0; order < curves.Count; order++)
        {
            var curve = curves[order];
            var ink = Colour(curve.Said);

            // What a press on it means: all of it but where a curve drawn over it covers it, so a press lands on the curve seen there.
            LayoutShape stands = shapes[order];
            foreach (var over in shapes.Skip(order + 1))
                stands = build.Medium.Combine(stands, over, ShapeCombine.Exclude);

            build.Open(RadarPiece.Curve, curve.Part, stops: PortableStops.None);
            build.Draw(PortableDiagramMarks.Outlined(build.Medium, shapes[order], PortableDiagramInk.Faded(build.Medium, ink, config.CurveOpacity ?? CurveOpacity), ink, config.CurveStrokeWidth ?? CurveStrokeWidth));
            build.Occupies(stands);
            build.Close();
        }

        build.Close();
    }

    /// <summary>What a curve is drawn in: the front matter's colour for its place, or the theme's.</summary>
    private LayoutInk Colour(RadarCurveNode curve) => Ink.Series(curve.Order, curve.Colour);

    /// <summary>How far out along its axis a value reaches, from nought at the middle to one at the rim — no further either way.</summary>
    private static double Reach(RadarBlockNode chart, double value) => Math.Clamp((value - chart.Min) / (chart.Max - chart.Min), 0, 1);

    // ── The labels ──────────────────────────────────────────────────────────

    private static void Labels(PortableBuilder build, IReadOnlyList<PortableDiagramWords> labels, IReadOnlyList<LayoutRect> places)
    {
        build.Open(RadarPiece.Labels, part: null, stops: PortableStops.None);

        for (var at = 0; at < labels.Count; at++)
            labels[at].Set(build, places[at].TopLeft, RadarPiece.Label);

        build.Close();
    }

    // ── The legend ──────────────────────────────────────────────────────────

    /// <summary>A curve's row: its colour, where it has anything to draw, and what it is called.</summary>
    private PortableDiagramKey Key(RadarConfig config, Curve curve) =>
        new(Standing(curve.Part), curve.Said.Drawn ? Colour(curve.Said) : null,
            [Words.Written(Says(curve.Part), HoleOf(curve.Part), config.LegendTextSize ?? LegendSize, Style.Text)]);

    // ── Where it goes ───────────────────────────────────────────────────────

    /// <summary>
    /// What a spoke or a legend row stands for: its axis or curve as written — or nothing, where nothing of it is written yet, since
    /// only the hole standing in its name is somewhere to write.
    /// </summary>
    private static ContentPart? Standing(ContentPart part) => part.Length > 0 ? part : null;

    /// <summary>What is written for an axis or a curve: its label, or its name where it has none.</summary>
    private static ContentPart Says(ContentPart item) => LabelOf(item).Words() ?? NameOf(item).Words()!;

    /// <summary>The hole standing where what is written for an axis or a curve is still to be written, where holes were asked for and it is.</summary>
    private static ContentPart? HoleOf(ContentPart item) => LabelOf(item) is { } label ? label.Hole() : NameOf(item).Hole();

    private static ContentPart? LabelOf(ContentPart item) => item.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label);

    private static ContentPart? NameOf(ContentPart item) => item.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name);

    /// <summary>Which way the <paramref name="at"/>th of <paramref name="count"/> axes points: the first straight up, and the rest clockwise.</summary>
    private static double Angle(int at, int count) => (-Math.PI / 2) + (2 * Math.PI * at / count);

    private static LayoutPoint On(LayoutPoint centre, double radius, double angle) =>
        new(centre.X + (radius * Math.Cos(angle)), centre.Y + (radius * Math.Sin(angle)));
}
