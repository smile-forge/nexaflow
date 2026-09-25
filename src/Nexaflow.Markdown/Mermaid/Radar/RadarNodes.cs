using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Radar;

/// <summary>What a radar's graticule — the rings behind its curves that mark the scale — is drawn as.</summary>
public enum RadarGraticule
{
    Circle,
    Polygon,
}

/// <summary>
/// A <c>radar-beta</c> block as its stages leave it: what its front matter asks for, and the scale, the rings and the legend its
/// options set — the last one written winning, and Mermaid's own where none is.
/// </summary>
public sealed class RadarBlockNode : ContentNode
{
    internal RadarBlockNode(ContentNode written, RadarConfig config) : base(written) => this.Config = config;

    private RadarBlockNode(ContentNode shape, RadarBlockNode said) : base(shape)
    {
        this.Config = said.Config;
        this.Min = said.Min;
        this.Max = said.Max;
        this.Ticks = said.Ticks;
        this.Graticule = said.Graticule;
        this.ShowsLegend = said.ShowsLegend;
    }

    public RadarConfig Config { get; }

    /// <summary>The value at the middle of the chart: <c>min</c>, or nought.</summary>
    public double Min { get; init; }

    /// <summary>
    /// The value at the rim: <c>max</c>, or the greatest value any curve gives — and, where that is no more than
    /// <see cref="Min"/>, one more than it, so there is a scale to draw on.
    /// </summary>
    public double Max { get; init; }

    /// <summary>How many rings the graticule has.</summary>
    public int Ticks { get; init; }

    public RadarGraticule Graticule { get; init; }

    /// <summary>Whether the legend is drawn.</summary>
    public bool ShowsLegend { get; init; }

    protected override ContentNode Reshaped(ContentNode shape) => new RadarBlockNode(shape, this);
}

/// <summary>
/// An axis that has a spoke: one named, or — while somebody is writing — one still to name, so there is a spoke to name it
/// on. The spokes are these in the order they are written, which is the order every curve's
/// <see cref="RadarCurveNode.Points"/> are in.
/// </summary>
public sealed class RadarAxisNode : ContentNode
{
    internal RadarAxisNode(ContentNode written) : base(written) { }

    protected override ContentNode Reshaped(ContentNode shape) => new RadarAxisNode(shape);
}

/// <summary>A curve as its stages leave it: how far it reaches along each spoke, the colour it takes, and whether the legend lists it.</summary>
public sealed class RadarCurveNode : ContentNode
{
    internal RadarCurveNode(ContentNode written) : base(written) { }

    private RadarCurveNode(ContentNode shape, RadarCurveNode said) : base(shape)
    {
        this.Points = said.Points;
        this.Order = said.Order;
        this.Colour = said.Colour;
        this.Drawn = said.Drawn;
        this.Listed = said.Listed;
    }

    /// <summary>The value it gives each spoke, in the order the spokes are written — null for one it gives nothing.</summary>
    public IReadOnlyList<double?> Points { get; init; } = [];

    /// <summary>Where it comes among the curves written, which is the colour the theme gives it.</summary>
    public int Order { get; init; }

    /// <summary>The colour the front matter writes for its place — null where it writes none, and the theme decides.</summary>
    public string? Colour { get; init; }

    /// <summary>Whether it gives any spoke a value, which is what there is to draw.</summary>
    public bool Drawn { get; init; }

    /// <summary>Whether it has a row in the legend: one it draws, or — while somebody is writing — any it names.</summary>
    public bool Listed { get; init; }

    protected override ContentNode Reshaped(ContentNode shape) => new RadarCurveNode(shape, this);
}

/// <summary>The option the graticule is shaped by — <c>graticule</c>, or else <c>ticks</c> — which is what a press on a ring means.</summary>
public sealed class RadarShapingNode : ContentNode
{
    internal RadarShapingNode(ContentNode written) : base(written) { }

    protected override ContentNode Reshaped(ContentNode shape) => new RadarShapingNode(shape);
}
