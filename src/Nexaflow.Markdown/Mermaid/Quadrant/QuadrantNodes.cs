using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Quadrant;

/// <summary>How a point is drawn, as its class and its own style write it — null for what neither writes.</summary>
public sealed record QuadrantStyle(double? Radius, string? Colour, string? StrokeColour, double? StrokeWidth)
{
    public static QuadrantStyle None { get; } = new(null, null, null, null);

    /// <summary>This style with what <paramref name="over"/> writes laid over it.</summary>
    public QuadrantStyle With(QuadrantStyle over) =>
        new(over.Radius ?? Radius, over.Colour ?? Colour, over.StrokeColour ?? StrokeColour, over.StrokeWidth ?? StrokeWidth);

    /// <summary>What a style's properties write, where none of them is wrong.</summary>
    internal static QuadrantStyle Of(ContentNode? properties)
    {
        var style = None;
        foreach (var property in properties?.Children.Where(child => child.Kind == MermaidKinds.Property) ?? [])
        {
            if (property.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Key) is not { } key
                || property.Children.FirstOrDefault(child => child.Kind == MermaidKinds.Setting) is not { Trouble: null, Width: > 0 } value)
                continue;

            style = key.Text.ToLowerInvariant() switch
            {
                "radius" => style with { Radius = MermaidNumber.Pixels(value.Text) is > 0 and var radius ? radius : style.Radius },
                "color" => style with { Colour = value.Text },
                "stroke-color" => style with { StrokeColour = value.Text },
                "stroke-width" => style with { StrokeWidth = MermaidNumber.Pixels(value.Text) },
                _ => style,
            };
        }

        return style;
    }
}

/// <summary>
/// A <c>quadrantChart</c> block as its stages leave it: what its front matter asks for, and whether the x-axis's words go over
/// the chart.
/// </summary>
public sealed class QuadrantBlockNode : ContentNode
{
    internal QuadrantBlockNode(ContentNode written, QuadrantConfig config, bool xAxisOnTop) : base(written) =>
        (this.Config, this.XAxisOnTop) = (config, xAxisOnTop);

    public QuadrantConfig Config { get; }

    /// <summary>Whether the x-axis's words go over the chart: as the front matter says, or else only where there are no points.</summary>
    public bool XAxisOnTop { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new QuadrantBlockNode(shape, this.Config, this.XAxisOnTop);
}

/// <summary>
/// A point as its stages leave it: where it stands, and how it is drawn — its class, wherever that is written, with its own
/// style laid over it.
/// </summary>
public sealed class QuadrantPointNode : ContentNode
{
    internal QuadrantPointNode(ContentNode written, double? x, double? y, QuadrantStyle style) : base(written) =>
        (this.X, this.Y, this.Style) = (x, y, style);

    /// <summary>How far across it stands, from 0 to 1 — or null where that is not written, or wrong.</summary>
    public double? X { get; }

    /// <summary>How far up it stands, from 0 to 1.</summary>
    public double? Y { get; }

    /// <summary>Whether it has somewhere to stand.</summary>
    public bool Placed => this.X is not null && this.Y is not null;

    public QuadrantStyle Style { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new QuadrantPointNode(shape, this.X, this.Y, this.Style);
}
