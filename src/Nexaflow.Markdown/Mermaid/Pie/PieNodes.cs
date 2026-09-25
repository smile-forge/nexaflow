using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Pie;

/// <summary>
/// A <c>pie</c> block as its stages leave it: what it was written as, and what its front matter asks of the chart, with
/// Mermaid's own default wherever it asks nothing.
/// </summary>
public sealed class PieBlockNode : ContentNode
{
    internal PieBlockNode(ContentNode written, PieConfig config) : base(written) => this.Config = config;

    /// <summary>What the front matter asks of the chart.</summary>
    public PieConfig Config { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new PieBlockNode(shape, this.Config);
}

/// <summary>
/// A slice as its stages leave it: the line written for it, and what it comes to — the colour the front matter gives it,
/// whether it is picked out, its share of the whole, where it comes among the wedges, whether the legend lists it and
/// whether its row shows its value. None of that is in the line's characters, which it prints as all the same.
/// </summary>
public sealed class PieSliceNode : ContentNode
{
    internal PieSliceNode(ContentNode written) : this(written, written as PieSliceNode) { }

    private PieSliceNode(ContentNode shape, PieSliceNode? said) : base(shape)
    {
        if (said is null) return;

        this.Colour = said.Colour;
        this.Swatch = said.Swatch;
        this.Highlighted = said.Highlighted;
        this.Share = said.Share;
        this.Order = said.Order;
        this.Listed = said.Listed;
        this.ValueShown = said.ValueShown;
    }

    /// <summary>The colour the front matter writes for the slice's place in the order — null where it writes none, and the theme decides.</summary>
    public string? Colour { get; init; }

    /// <summary>
    /// Which of <c>pie1</c>…<c>pie12</c> <see cref="Colour"/> came from, by its number, which is where a restyle writes —
    /// the number rather than the key's name, so no slice pays for a string nothing may ever read.
    /// </summary>
    public int? Swatch { get; init; }

    /// <summary>Whether the config picks this slice out.</summary>
    public bool Highlighted { get; init; }

    /// <summary>Its share of the whole: its value against every slice worth a wedge — nought for one that is not.</summary>
    public double Share { get; init; }

    /// <summary>Where it comes among the slices worth a wedge, which is the colour the theme gives it — -1 for one that is not.</summary>
    public int Order { get; init; } = -1;

    /// <summary>Whether it has a wedge.</summary>
    public bool Drawn => this.Order >= 0;

    /// <summary>Whether it has a row in the legend.</summary>
    public bool Listed { get; init; }

    /// <summary>Whether its row shows its value.</summary>
    public bool ValueShown { get; init; }

    protected override ContentNode Reshaped(ContentNode shape) => new PieSliceNode(shape, this);
}
