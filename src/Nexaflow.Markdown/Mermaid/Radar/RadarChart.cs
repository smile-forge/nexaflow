using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid.Radar.Stages;

namespace Nexaflow.Markdown.Mermaid.Radar;

/// <summary>What a radar's graticule — the rings behind its curves that mark the scale — is drawn as.</summary>
public enum RadarGraticule
{
    Circle,
    Polygon,
}

/// <summary>One axis, read: where it was written, what it is called, and what is drawn at the end of its spoke.</summary>
/// <param name="Part">The axis as it was written — its name and its label — which is what a press on its spoke means.</param>
/// <param name="Name">Its name as written, without its quotes: what a curve's value names it by.</param>
/// <param name="Label">What its label says, without its brackets or quotes, or null where it has none.</param>
public sealed record RadarAxis(ContentPart Part, ContentPart Name, ContentPart? Label)
{
    /// <summary>What it is called — empty for an axis still to name.</summary>
    public string Id => Name.Text;

    /// <summary>What is drawn for it: its label, or its name where it has none.</summary>
    public ContentPart Says => Label ?? Name;

    /// <summary>The hole standing where what is drawn for it is still to be written, where holes were asked for and it is.</summary>
    public ContentPart? Hole { get; init; }
}

/// <summary>One curve, read: where it was written, what it is called, and how far it reaches along each axis.</summary>
/// <param name="Part">The curve as it was written — name, label and values — which is what a press on it means.</param>
/// <param name="Name">Its name as written, without its quotes.</param>
/// <param name="Label">What its label says, without its brackets or quotes, or null where it has none.</param>
/// <param name="Points">The value it gives each of the chart's axes, in their order — null for an axis it gives none.</param>
/// <param name="Order">Where it comes among the curves written, which is the colour it takes.</param>
/// <param name="Colour">The colour the front matter writes for its place, or null to leave it to the theme.</param>
public sealed record RadarCurve(ContentPart Part, ContentPart Name, ContentPart? Label, IReadOnlyList<double?> Points, int Order, string? Colour)
{
    /// <summary>What is written for it in the legend: its label, or its name where it has none.</summary>
    public ContentPart Says => Label ?? Name;

    /// <summary>The hole standing where what is written for it is still to be written, where holes were asked for and it is.</summary>
    public ContentPart? Hole { get; init; }

    /// <summary>Whether it gives any axis a value, which is what there is to draw.</summary>
    public bool Drawn => Points.Any(point => point is not null);
}

/// <summary>
/// A <c>radar-beta</c> block, read: its axes and its curves in the order they are written, the scale its options set, and what
/// its front matter asks for. Its title is the block's (<see cref="MermaidBlock.Title"/>).
///
/// <para>
/// What <see cref="Chemistry.Molecule"/> is to a SMILES string — the tree read back into the chart it describes, every part
/// kept, so what the builder draws can point at what the reader wrote. Which axis a value is for was worked out by the stage
/// (<see cref="ResolveCurves"/>); this only reads it back.
/// </para>
/// </summary>
public sealed class RadarChart
{
    /// <summary>How many rings a graticule has where <c>ticks</c> says nothing.</summary>
    public const int Rings = 5;

    private RadarChart(MermaidBlock block, RadarConfig config, IReadOnlyList<RadarAxis> axes, IReadOnlyList<RadarCurve> curves)
    {
        Block = block;
        Config = config;
        Axes = axes;
        Curves = curves;
    }

    /// <summary>Reads a block: parsed, then worked over by its stages (<see cref="MermaidParser.Read"/>).</summary>
    public static RadarChart Read(string? block) => Of(MermaidParser.Read(block));

    /// <summary>Reads a tree the stages have already been over.</summary>
    public static RadarChart Of(ContentNode tree) => Of(MermaidBlock.Of(tree));

    /// <summary>Reads a block that has already been read — the shared parse, worked over by the radar's own stage.</summary>
    public static RadarChart Of(MermaidBlock block)
    {
        var config = RadarConfig.Read(block.Config);
        var axes = new List<RadarAxis>();
        var written = new List<ContentPart>();
        var options = new Dictionary<string, ContentPart>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in block.Reading.Root.SelfAndDescendants())
        {
            switch (part.Kind)
            {
                case RadarKinds.Axis when Axis(part) is { } axis:
                    axes.Add(axis);
                    break;

                case RadarKinds.Curve:
                    written.Add(part);
                    break;

                // The last one written wins.
                case RadarKinds.Option when part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key) is { } word:
                    options[word.Text] = part;
                    break;
            }
        }

        var curves = written.Select((part, order) => Curve(part, order, axes, config)).OfType<RadarCurve>().ToList();
        var chart = new RadarChart(block, config, axes, curves);

        chart.Min = options.GetValueOrDefault(RadarGrammar.Min).Number() ?? 0;
        chart.Top = options.GetValueOrDefault(RadarGrammar.Max).Number();
        chart.Ticks = options.GetValueOrDefault(RadarGrammar.Ticks).Number() is { } ticks ? (int)ticks : Rings;
        chart.Graticule = Setting(options.GetValueOrDefault(RadarGrammar.Graticule)) is { } graticule
                          && graticule.Equals(RadarGrammar.Polygon, StringComparison.OrdinalIgnoreCase)
            ? RadarGraticule.Polygon
            : RadarGraticule.Circle;
        chart.ShowsLegend = !string.Equals(Setting(options.GetValueOrDefault(RadarGrammar.ShowLegend)), "false", StringComparison.OrdinalIgnoreCase);
        chart.Shaped = options.GetValueOrDefault(RadarGrammar.Graticule) ?? options.GetValueOrDefault(RadarGrammar.Ticks);

        return chart;
    }

    /// <summary>The block this was read from — its front matter, its header, its title, everything written in it.</summary>
    public MermaidBlock Block { get; }

    /// <summary>What the front matter asks for.</summary>
    public RadarConfig Config { get; }

    /// <summary>The axes, in the order they are written, which is clockwise from straight up.</summary>
    public IReadOnlyList<RadarAxis> Axes { get; }

    /// <summary>The curves, in the order they are written, which is the order they are drawn one over another.</summary>
    public IReadOnlyList<RadarCurve> Curves { get; }

    /// <summary>The value at the middle of the chart: <c>min</c>, or nought.</summary>
    public double Min { get; private set; }

    /// <summary>The value at the rim as <c>max</c> writes it, or null where it writes none.</summary>
    public double? Top { get; private set; }

    /// <summary>
    /// The value at the rim: <c>max</c>, or the greatest value any curve gives — and, where that is no more than
    /// <see cref="Min"/>, one more than it, so there is a scale to draw on.
    /// </summary>
    public double Max
    {
        get
        {
            var max = Top ?? Curves.SelectMany(curve => curve.Points).OfType<double>().DefaultIfEmpty(Min).Max();
            return max > Min ? max : Min + 1;
        }
    }

    /// <summary>How many rings the graticule has.</summary>
    public int Ticks { get; private set; } = Rings;

    public RadarGraticule Graticule { get; private set; }

    /// <summary>Whether the legend is drawn.</summary>
    public bool ShowsLegend { get; private set; } = true;

    /// <summary>The option that shapes the graticule — <c>graticule</c>, or else <c>ticks</c> — which is what a press on a ring means; null where neither is written.</summary>
    public ContentPart? Shaped { get; private set; }

    /// <summary>How far out along its axis a value reaches, from nought at the middle to one at the rim — no further either way.</summary>
    public double Reach(double value) => Math.Clamp((value - Min) / (Max - Min), 0, 1);

    /// <summary>The value a curve gives an axis of this chart, where it gives it one.</summary>
    public double? Value(RadarCurve curve, RadarAxis axis)
    {
        for (var at = 0; at < Axes.Count && at < curve.Points.Count; at++)
            if (ReferenceEquals(Axes[at], axis)) return curve.Points[at];

        return null;
    }

    private static RadarAxis? Axis(ContentPart part)
    {
        if (part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name) is not { } name || name.Words() is not { } words) return null;

        var label = part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label);
        return new RadarAxis(part, words, label.Words()) { Hole = label is null ? name.Hole() : label.Hole() };
    }

    private static RadarCurve? Curve(ContentPart part, int order, IReadOnlyList<RadarAxis> axes, RadarConfig config)
    {
        if (part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Name) is not { } name || name.Words() is not { } words) return null;

        var label = part.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Label);
        var entries = part.Children.FirstOrDefault(child => child.Kind == RadarKinds.Values)?.Children
                          .Where(child => child.Kind == RadarKinds.Entry)
                          .ToList() ?? [];

        var points = axes.Select(axis => axis.Id.Length == 0
                                     ? null
                                     : entries.FirstOrDefault(entry => entry.Fact(RadarRoles.For) == axis.Id).Number())
                         .ToList();

        return new RadarCurve(part, words, label.Words(), points, order, config.Swatches.GetValueOrDefault(order % RadarConfig.PaletteSize))
        {
            Hole = label is null ? name.Hole() : label.Hole(),
        };
    }

    /// <summary>What an option is set to, where it is set to anything that is not wrong.</summary>
    private static string? Setting(ContentPart? option) =>
        option.Inner(MermaidKinds.Setting) is { Trouble: null, Length: > 0 } setting ? setting.Text : null;
}
