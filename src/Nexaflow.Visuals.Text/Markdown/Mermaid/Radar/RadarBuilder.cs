using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Radar;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Radar;

/// <summary>The pieces a radar chart's layout is made of — its layers, and what is in them. Its legend is <see cref="DiagramLegend"/>'s.</summary>
public static class RadarPiece
{
    /// <summary>The graticule: the rings behind the curves that mark the scale.</summary>
    public const string Graticule = "Graticule";

    /// <summary>One ring. It stands for the option that shapes the graticule, where one is written.</summary>
    public const string Ring = "Ring";

    /// <summary>The spokes, one per axis.</summary>
    public const string Spokes = "Spokes";

    /// <summary>One spoke. It stands for its axis as it was written, so pressing it means that axis.</summary>
    public const string Spoke = "Spoke";

    /// <summary>The curves, drawn one over another in the order they are written.</summary>
    public const string Curves = "Curves";

    /// <summary>One curve's shape, filled. It stands for the curve as it was written.</summary>
    public const string Curve = "Curve";

    /// <summary>What is written at the end of each spoke.</summary>
    public const string Labels = "Labels";

    /// <summary>An axis's label — or its name, where it has none — typed into where it is drawn.</summary>
    public const string Label = "Label";

    /// <summary>A curve's label — or its name, where it has none — in its legend row.</summary>
    public const string Name = "Name";
}

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
internal sealed class RadarBuilder : MermaidBuilder
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

    internal RadarBuilder(ContentReading reading, EditState state, StyleFormat style, bool isReadOnly, Nesting nesting) : base(reading, state, style, isReadOnly, nesting) { }

    /// <summary>The chart as its stages left it on the block.</summary>
    private RadarBlockNode? Chart => Reading.Root.Node as RadarBlockNode;

    /// <summary>A curve as its stages left it, and the part of the reading that is.</summary>
    private readonly record struct Curve(ContentPart Part, RadarCurveNode Said);

    /// <summary>The front matter's <c>titleColor</c>, where it writes one.</summary>
    protected override string? TitleColour => Chart?.Config.TitleTextColour;

    /// <summary>The front matter's <c>fontSize</c>, where it writes one.</summary>
    protected override double? TitleTextSize => Chart?.Config.TitleTextSize;

    protected override Size Draw(MermaidBlock block, LayoutBuilder build)
    {
        if (Chart is not { } chart) return AsWritten(build);
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
        if (axes.Count == 0) return AsWritten(build);

        var labels = axes.Select(axis => Written(Says(axis), HoleOf(axis), config.AxisLabelTextSize ?? LabelSize, Palette.Text)).ToList();
        var margins = new Thickness(config.MarginLeft ?? Margin, config.MarginTop ?? Margin, config.MarginRight ?? Margin, config.MarginBottom ?? Margin);

        var legend = new DiagramLegend(chart.ShowsLegend ? [.. listed.Select(curve => Key(config, curve))] : [], Columns, across: false, Palette.TextMuted)
        {
            Square = config.LegendBoxSize ?? DiagramLegend.SwatchSize,
        };

        var beside = legend.Size.Width > 0 ? legend.Size.Width + Apart : 0;
        var room = config.UseMaxWidth ? Space : double.PositiveInfinity;
        var under = beside > 0 && Wide(Smallest) + beside > room;
        var radius = Fitting(Math.Min(config.Width ?? Radius * 2, config.Height ?? Radius * 2) / 2, room - (under ? 0 : beside));

        var extent = Extent(radius);
        var centre = new Point(margins.Left - extent.X, margins.Top - extent.Y);
        var chartSize = new Size(extent.Width + margins.Left + margins.Right, extent.Height + margins.Top + margins.Bottom);

        Graticule(build, chart, shaped, axes.Count, centre, radius);
        Spokes(build, config, axes, centre, radius);
        Curves(build, chart, axes.Count, [.. listed.Where(curve => curve.Said.Drawn)], centre, radius);
        Labels(build, labels, axes.Select((_, at) => Label(at, radius, centre)).ToList());

        if (legend.Size.Width <= 0) return chartSize;

        if (under)
        {
            legend.Draw(build, new Point(Math.Max(0, (chartSize.Width - legend.Size.Width) / 2), chartSize.Height + Apart));
            return new Size(Math.Max(chartSize.Width, legend.Size.Width), chartSize.Height + Apart + legend.Size.Height);
        }

        legend.Draw(build, new Point(chartSize.Width + Apart, margins.Top));
        return new Size(chartSize.Width + beside, Math.Max(chartSize.Height, margins.Top + legend.Size.Height));

        // Where each label sits round a chart of a radius, its middle at the origin: hung off the point past the end of its
        // spoke by the side facing the chart, so a label under the chart hangs from its top and one right of it from its left.
        Rect Label(int at, double reach, Point middle)
        {
            var angle = Angle(at, axes.Count);
            var (across, down) = (Math.Cos(angle), Math.Sin(angle));
            var words = labels[at];
            var point = On(middle, (reach * config.AxisLabelFactor) + LabelGap, angle);

            return new Rect(point.X - (words.Width * (1 - across) / 2), point.Y - (words.Height * (1 - down) / 2), words.Width, words.Height);
        }

        // Everything the chart draws at a radius, round its middle at the origin: its rings, its spokes and its labels.
        Rect Extent(double reach)
        {
            var reached = new Rect(-reach, -reach, reach * 2, reach * 2);
            var spoke = reach * config.AxisScaleFactor;
            reached.Union(new Rect(-spoke, -spoke, spoke * 2, spoke * 2));

            for (var at = 0; at < axes.Count; at++) reached.Union(Label(at, reach, default));
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

    private void Graticule(LayoutBuilder build, RadarBlockNode chart, ContentPart? shaped, int count, Point centre, double radius)
    {
        var config = chart.Config;
        var ink = Ink.Written(config.GraticuleColour) ?? Palette.CodeBorder;
        var fill = DiagramInk.Faded(ink, config.GraticuleOpacity ?? GraticuleOpacity);

        build.Open(RadarPiece.Graticule, part: null, stops: Stops.None);

        for (var ring = 1; ring <= chart.Ticks; ring++)
        {
            var reach = radius * ring / chart.Ticks;

            // A polygon of fewer than three corners encloses nothing, so it is a circle like the rest.
            var shape = chart.Graticule == RadarGraticule.Polygon && count >= 3
                ? DiagramCurve.Closed([.. Enumerable.Range(0, count).Select(at => On(centre, reach, Angle(at, count)))])
                : Circle(centre, reach);

            build.Open(RadarPiece.Ring, shaped, stops: Stops.None);
            build.Draw(new GeometryMark(shape, fill, ink, config.GraticuleStrokeWidth ?? 1));
            build.Occupies(shape);
            build.Close();
        }

        build.Close();
    }

    private static Geometry Circle(Point centre, double radius)
    {
        var circle = new EllipseGeometry(centre, radius, radius);
        circle.Freeze();
        return circle;
    }

    // ── The spokes ──────────────────────────────────────────────────────────

    private void Spokes(LayoutBuilder build, RadarConfig config, IReadOnlyList<ContentPart> axes, Point centre, double radius)
    {
        var stroke = new DiagramStroke(Ink.Written(config.AxisColour) ?? Palette.TextMuted, config.AxisStrokeWidth ?? 1);
        var reach = radius * config.AxisScaleFactor;

        build.Open(RadarPiece.Spokes, part: null, stops: Stops.None);

        // A spoke of no length is nothing to draw, or to press.
        if (reach > 0)
            for (var at = 0; at < axes.Count; at++)
                DiagramConnector.Draw(build, RadarPiece.Spoke, Standing(axes[at]), [centre, On(centre, reach, Angle(at, axes.Count))], stroke,
                                      DiagramHead.None, DiagramHead.None);

        build.Close();
    }

    // ── The curves ──────────────────────────────────────────────────────────

    private void Curves(LayoutBuilder build, RadarBlockNode chart, int spokes, IReadOnlyList<Curve> curves, Point centre, double radius)
    {
        var config = chart.Config;
        var tension = chart.Graticule == RadarGraticule.Circle ? config.CurveTension : 0;

        // Where a curve gives an axis nothing it stays at the middle, as a value of min would.
        var shapes = curves
            .Select(curve => DiagramCurve.Closed(
                [.. curve.Said.Points.Select((point, at) => On(centre, radius * Reach(chart, point ?? chart.Min), Angle(at, spokes)))], tension))
            .ToList();

        build.Open(RadarPiece.Curves, part: null, stops: Stops.None);

        for (var order = 0; order < curves.Count; order++)
        {
            var curve = curves[order];
            var ink = Colour(curve.Said);

            // What a press on it means: all of it but where a curve drawn over it covers it, so a press lands on the curve seen there.
            Geometry stands = shapes[order];
            foreach (var over in shapes.Skip(order + 1))
                stands = new CombinedGeometry(GeometryCombineMode.Exclude, stands, over);
            stands.Freeze();

            build.Open(RadarPiece.Curve, curve.Part, stops: Stops.None);
            build.Draw(new GeometryMark(shapes[order], DiagramInk.Faded(ink, config.CurveOpacity ?? CurveOpacity), ink, config.CurveStrokeWidth ?? CurveStrokeWidth));
            build.Occupies(stands);
            build.Close();
        }

        build.Close();
    }

    /// <summary>What a curve is drawn in: the front matter's colour for its place, or the theme's.</summary>
    private Brush Colour(RadarCurveNode curve) => Ink.Series(curve.Order, curve.Colour);

    /// <summary>How far out along its axis a value reaches, from nought at the middle to one at the rim — no further either way.</summary>
    private static double Reach(RadarBlockNode chart, double value) => Math.Clamp((value - chart.Min) / (chart.Max - chart.Min), 0, 1);

    // ── The labels ──────────────────────────────────────────────────────────

    private static void Labels(LayoutBuilder build, IReadOnlyList<DiagramWords> labels, IReadOnlyList<Rect> places)
    {
        build.Open(RadarPiece.Labels, part: null, stops: Stops.None);

        for (var at = 0; at < labels.Count; at++)
            labels[at].Set(build, places[at].TopLeft, RadarPiece.Label);

        build.Close();
    }

    // ── The legend ──────────────────────────────────────────────────────────

    /// <summary>A curve's row: its colour, where it has anything to draw, and what it is called.</summary>
    private DiagramKey Key(RadarConfig config, Curve curve) =>
        new(Standing(curve.Part), curve.Said.Drawn ? Colour(curve.Said) : null,
            [Written(Says(curve.Part), HoleOf(curve.Part), config.LegendTextSize ?? LegendSize, Palette.Text)]);

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

    private static Point On(Point centre, double radius, double angle) =>
        new(centre.X + (radius * Math.Cos(angle)), centre.Y + (radius * Math.Sin(angle)));
}
