using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Mermaid.Flowchart;

/// <summary>
/// One join a link makes, as the stages leave it (<see cref="Stages.ResolveLinks"/>): the node — or the subgraph — it leaves and
/// the one it reaches, each by what it is called, and what the <c>linkStyle</c> lines numbering it ask for it.
/// </summary>
internal sealed record FlowchartJoin(string From, string To, MermaidStyle Style);

/// <summary>
/// A link as the stages leave it (<see cref="Stages.ResolveLinks"/>): every join it makes — each node written on its left to each
/// on its right, the nodes the link before it reached counting as its left — and the curve an <c>id@{ curve: … }</c> line asks
/// for it. What a link joins is a fact about its whole line, and which number a <c>linkStyle</c> calls it by a fact about every
/// line above it. It prints as the link written.
/// </summary>
internal sealed class FlowchartLinkNode : ContentNode
{
    internal FlowchartLinkNode(ContentNode written, IReadOnlyList<FlowchartJoin> joins, string? curve) : base(written)
    {
        this.Joins = joins;
        this.Curve = curve;
    }

    public IReadOnlyList<FlowchartJoin> Joins { get; }

    /// <summary>The curve its own metadata asks for, over whatever the front matter asks for every link.</summary>
    public string? Curve { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new FlowchartLinkNode(shape, this.Joins, this.Curve);
}

/// <summary>What an <c>id@{ … }</c> line is about: a node, a link, a subgraph — or nothing yet written, which it makes a node of.</summary>
internal enum FlowchartSaid { Node, Link, Group, New }

/// <summary>
/// An <c>id@{ … }</c> line as the stages leave it (<see cref="Stages.ResolveMetadata"/>): what it is about, which may be written
/// above it or below it, and what it says of a node — the shape it is drawn as, the words drawn on it, and the picture or icon
/// drawn instead. It prints as the line written.
/// </summary>
internal sealed class FlowchartMetadataNode : ContentNode
{
    internal FlowchartMetadataNode(ContentNode written, FlowchartSaid about, MermaidShape? shape, string? label, FlowchartPicture? picture) : base(written)
    {
        this.About = about;
        this.Shape = shape;
        this.Label = label;
        this.Picture = picture;
    }

    public FlowchartSaid About { get; }

    /// <summary>The shape <c>shape:</c> names — a rectangle for one Mermaid has none by — or null where it names none.</summary>
    public MermaidShape? Shape { get; }

    /// <summary>What <c>label:</c> or <c>title:</c> says it is drawn with, its quotes taken off and its entities read.</summary>
    public string? Label { get; }

    public FlowchartPicture? Picture { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new FlowchartMetadataNode(shape, this.About, this.Shape, this.Label, this.Picture);
}

/// <summary>
/// What a node drawn as a picture is drawn with — an <c>@{ img: … }</c> or an <c>@{ icon: … }</c> — and how: the frame an icon
/// stands in, whether its label goes above it, and the size it is asked for.
/// </summary>
/// <param name="Icon">The icon named, pack and all — or null for a picture.</param>
/// <param name="Form">What an icon stands in — <c>square</c>, <c>circle</c>, <c>rounded</c> — or null for nothing.</param>
/// <param name="Above">Whether the label goes above it, as <c>pos: t</c> asks, rather than below.</param>
/// <param name="Keeps">Whether a picture keeps its shape inside the size it is asked for, as <c>constraint: on</c> asks.</param>
internal sealed record FlowchartPicture(string? Icon, string? Form, bool Above, double? Width, double? Height, bool Keeps);
