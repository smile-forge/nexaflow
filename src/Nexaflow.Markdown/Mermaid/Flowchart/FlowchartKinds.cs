namespace Nexaflow.Markdown.Mermaid.Flowchart;

/// <summary>
/// What a line of a flowchart says beyond the lines every diagram shares. What a line is made of — names, labels, styles —
/// is <see cref="MermaidKinds"/>'.
/// </summary>
public static class FlowchartKinds
{
    /// <summary>The way the chart is laid out, written after its keyword: the <c>TD</c> of <c>flowchart TD</c>.</summary>
    public const string Way = "flowchart-way";

    /// <summary>A <c>direction LR</c> line, which lays out the subgraph it is written in.</summary>
    public const string Direction = "flowchart-direction";

    /// <summary>The nodes and the links written on one line, in the order they are written.</summary>
    public const string Nodes = "flowchart-nodes";

    /// <summary>One node: its id, the label in the brackets that say its shape, and the class it is given.</summary>
    public const string Node = "flowchart-node";

    /// <summary>One link, with what is written on it.</summary>
    public const string Link = "flowchart-link";

    /// <summary>What is written on a link between its opening and the link closing it: <c>-- yes --&gt;</c>.</summary>
    public const string Saying = "flowchart-saying";

    /// <summary>A <c>subgraph</c>, which draws a box round every node written until the <c>end</c> closing it.</summary>
    public const string Opens = "flowchart-opens";

    /// <summary>The <c>end</c> that closes a subgraph.</summary>
    public const string Ends = "flowchart-ends";

    public const string ClassDef = "flowchart-class-def";

    public const string Class = "flowchart-class";

    public const string Style = "flowchart-style";

    /// <summary>A <c>linkStyle</c> line, which styles the links it numbers — or every link, where it says <c>default</c>.</summary>
    public const string LinkStyle = "flowchart-link-style";

    /// <summary>A <c>click</c> line: where pressing a node leads, and what it says while pointed at.</summary>
    public const string Click = "flowchart-click";

    /// <summary>An <c>id@{ … }</c> line, which says more about the node or the link it names than its brackets can.</summary>
    public const string Said = "flowchart-said";

    /// <summary>What the stages hang under a line: which subgraph it is in, and which one it opens.</summary>
    public const string Fact = "flowchart-fact";
}

/// <summary>What a piece of a flowchart's line is to the piece holding it.</summary>
public static class FlowchartRoles
{
    /// <summary>What a node is called, which is what a link, a <c>class</c>, a <c>style</c> and a <c>click</c> name it by.</summary>
    public const string Id = "flowchart-id";

    /// <summary>What is written on a node or on a link.</summary>
    public const string Label = "flowchart-label";

    /// <summary>The name of a class, declared by a <c>classDef</c> and given by a <c>class</c> line or by <c>:::</c>.</summary>
    public const string Class = "flowchart-class-name";

    /// <summary>The way a chart or a subgraph is laid out: <c>TD</c>, <c>LR</c>.</summary>
    public const string Towards = "flowchart-towards";

    /// <summary>The characters a link is drawn as, which say its heads, its line and how far it reaches.</summary>
    public const string Arrow = "flowchart-drawn";

    /// <summary>An id given to a link itself, so a later line can style it: the <c>e1</c> of <c>e1@--&gt;</c>.</summary>
    public const string Link = "flowchart-link-id";

    /// <summary>Which link a <c>linkStyle</c> line styles, counted in the order they are written.</summary>
    public const string Index = "flowchart-link-index";

    /// <summary>Where pressing a node leads.</summary>
    public const string Href = "flowchart-href";

    /// <summary>What a node says while it is pointed at.</summary>
    public const string Tip = "flowchart-tip";

    /// <summary>Where a link opens: <c>_blank</c>, <c>_self</c>.</summary>
    public const string Target = "flowchart-target";

    /// <summary>What a <c>click</c> line calls, which nothing here runs.</summary>
    public const string Call = "flowchart-call";

    /// <summary>The curve a <c>linkStyle</c> line interpolates its links along.</summary>
    public const string Curve = "flowchart-curve";

    /// <summary>The subgraph a line is written in (<see cref="MermaidNesting"/>).</summary>
    public const string Inside = "flowchart-inside";

    /// <summary>The subgraph a <c>subgraph</c> line opens.</summary>
    public const string Opened = "flowchart-opened";
}
