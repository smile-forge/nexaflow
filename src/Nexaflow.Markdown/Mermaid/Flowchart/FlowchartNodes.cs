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

/// <summary>
/// One node of the chart as its stage leaves it (<see cref="Stages.ResolveChart"/>): what it is called, the shape it is drawn as,
/// the subgraph it belongs to, and which of the times it is written holds the words drawn on it. It prints as nothing — it stands
/// for the whole of what a chart's lines say of one node rather than for any of the characters.
/// </summary>
internal sealed class FlowchartGraphNode : ContentNode
{
    internal FlowchartGraphNode(ContentNode written, string id, MermaidShape shape, string? group, int? mention, bool meant) : base(written)
    {
        this.Id = id;
        this.Shape = shape;
        this.Group = group;
        this.Mention = mention;
        this.Meant = meant;
    }

    /// <summary>What it is called, which is what a link, a <c>class</c>, a <c>style</c> and a <c>click</c> name it by.</summary>
    public string Id { get; }

    public MermaidShape Shape { get; }

    /// <summary>The key of the subgraph it was first written in — where it stands among those opened — or null for one outside them all.</summary>
    public string? Group { get; }

    /// <summary>
    /// Which mention of it holds the words drawn, counted among the times its id is written, or null where no line wrote it and
    /// only an <c>id@{ … }</c> line made it. What is drawn stands for those characters, so a press on the words means them.
    /// </summary>
    public int? Mention { get; }

    /// <summary>Whether its words came from an <c>id@{ label: … }</c> line rather than from any mention of it.</summary>
    public bool Meant { get; }

    protected override ContentNode Reshaped(ContentNode shape) =>
        new FlowchartGraphNode(shape, this.Id, this.Shape, this.Group, this.Mention, this.Meant);
}

/// <summary>
/// One connection of the chart as its stage leaves it (<see cref="Stages.ResolveChart"/>): what the <c>linkStyle</c> lines
/// numbering it ask for it, the curve its metadata asks for, and which written link drew it — one link written between two sets
/// of nodes draws a connection for every pair, and they are all drawn as those same characters.
/// </summary>
internal sealed class FlowchartGraphLink : ContentNode
{
    internal FlowchartGraphLink(ContentNode written, MermaidStyle style, string? curve, int drawn) : base(written)
    {
        this.Style = style;
        this.Curve = curve;
        this.Drawn = drawn;
    }

    public MermaidStyle Style { get; }

    public string? Curve { get; }

    /// <summary>Where the link that drew it stands among the links written.</summary>
    public int Drawn { get; }

    protected override ContentNode Reshaped(ContentNode shape) => new FlowchartGraphLink(shape, this.Style, this.Curve, this.Drawn);
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
