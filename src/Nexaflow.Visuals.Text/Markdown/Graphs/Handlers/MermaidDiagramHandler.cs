using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
using Nexaflow.Visuals.Text.Markdown.Graphs.Layout;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;
using Nexaflow.Visuals.Text.Markdown.Mermaid;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Handlers;

/// <summary>
/// Handles all <c>mermaid</c> fenced code blocks.
///
/// Mermaid is a family of diagram types sharing one language tag. The block is read once, by
/// <see cref="MermaidParser"/>, and the diagram its header names chooses the sub-pipeline:
///   • a diagram named in <see cref="MermaidBuilders"/> → its grammar, its stages and its builder, on the shared layout
///     tree, in an element it can be selected and written in (docs/mermaid-diagrams.md)
///   • <c>sequenceDiagram</c>  → <see cref="MermaidSequenceParser"/> + <see cref="WpfSequenceDiagramRenderer"/>
///   • <c>classDiagram</c>     → <see cref="MermaidClassParser"/>   + Sugiyama + <see cref="WpfGraphRenderer"/>
///   • <c>requirementDiagram</c> → <see cref="MermaidRequirementParser"/> + Sugiyama + <see cref="WpfGraphRenderer"/>

///   • <c>erDiagram</c>        → <see cref="MermaidErParser"/>      + Sugiyama + <see cref="WpfGraphRenderer"/>

///   • <c>C4Context / …</c>    → <see cref="MermaidC4Parser"/> + <see cref="C4GraphProjector"/> + the graph family
///   • <c>C4Sequence</c>       → <see cref="MermaidC4Parser"/> + <see cref="C4SequenceProjector"/> + <see cref="WpfSequenceDiagramRenderer"/>
///   • <c>graph / flowchart</c> asking for nodes that open and close → <see cref="MermaidFlowchartParser"/> + Sugiyama +
///     <see cref="WpfGraphRenderer"/>; every other flowchart is drawn on the shared layout tree
///   • a header naming no type → <see cref="UnknownDiagramBuilder"/>, the block as written with the reason
///
/// Which keyword names which diagram is <see cref="MermaidDiagrams"/>; adding a type means naming it there and adding
/// its render path here.
/// </summary>
public sealed class MermaidDiagramHandler : IDiagramHandler
{
    private static readonly MermaidFlowchartParser FlowParser = new();
    private static readonly MermaidSequenceParser SequenceParser = new();
    private static readonly MermaidStateParser    StateParser    = new();
    private static readonly MermaidClassParser    ClassParser    = new();
    private static readonly MermaidRequirementParser RequirementParser = new();
    private static readonly MermaidErParser       ErParser       = new();
    private static readonly MermaidSwimlaneParser  SwimlaneParser = new();
    private static readonly MermaidC4Parser       C4Parser       = new();

    public bool CanHandle(string language) =>
        language.Equals("mermaid", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options)
    {
        // Every sub-type is handed the block past its front matter; the front matter's title is applied to the parsed
        // chart, and the diagrams that take a config read it from the block.
        var block = MermaidBlock.Read(source);
        var palette = options.Palette;

        // A diagram drawn on the shared layout tree is shown in an element it can be written in, and never drawn any other way —
        // unless it asks for nodes that open and close, which only the expandable view draws.
        if (!Explorable(block) && MermaidBuilders.Element(source, block.Diagram, options) is { } shared) return shared;

        return block.Diagram switch
        {
            MermaidDiagram.Sequence     => RenderSequence(block, palette),

            MermaidDiagram.State        => RenderGraphFamily(StateParser.Parse(block.Body), block, options, 900),
            MermaidDiagram.Class        => RenderClass(block, options),
            MermaidDiagram.Requirement  => RenderGraphFamily(RequirementParser.Parse(block.Body), block, options, 1100),

            MermaidDiagram.Er           => RenderEr(block, options),

            MermaidDiagram.Swimlane     => RenderSwimlane(block, palette),

            MermaidDiagram.C4           => RenderC4(block, options),
            MermaidDiagram.C4Sequence   => RenderC4Sequence(block, palette),
            MermaidDiagram.Flowchart    => RenderGraphFamily(FlowParser.Parse(block.Body), block, options, 900),
            _                           => UnknownDiagramBuilder.Element(source, options),
        };
    }

    /// <summary>Applies a front-matter title to a chart that doesn't already carry one.</summary>
    private static string Titled(string? existing, MermaidBlock block) =>
        string.IsNullOrWhiteSpace(existing) && block.FrontMatterTitleText is { } frontmatter ? frontmatter : existing ?? string.Empty;

    /// <summary>
    /// Whether the block asks for nodes that open and close — a <c>config: nexaflow:</c> block with anything in it. The shared
    /// layout tree draws what was written and nothing else, so a flowchart asking to be explored keeps the expandable graph view;
    /// this goes when opening and closing a node is something the layout tree can do.
    /// </summary>
    private static bool Explorable(MermaidBlock block) =>
        block.Diagram == MermaidDiagram.Flowchart && !NexaflowConfigParser.Parse(block.Config).IsEmpty;

    // ── Sub-renderers ──────────────────────────────────────────────────────

    private static FrameworkElement RenderSequence(MermaidBlock block, MarkdownPalette palette)
    {
        var diagram = SequenceParser.Parse(block.Body);
        diagram.Title = Titled(diagram.Title, block);
        return WpfSequenceDiagramRenderer.Render(diagram, palette);
    }

    private static FrameworkElement RenderEr(MermaidBlock block, DiagramRenderOptions options)
    {
        // ER entities are UML-style boxes, so they reuse the shared graph model + Sugiyama + WpfGraphRenderer
        // (like class / requirement diagrams). The er config is applied here: an inline `direction` wins, else
        // config layoutDirection; an explicit fill/stroke becomes the default for entities lacking a colour.
        var graph = ErParser.Parse(block.Body);

        var cfg = ErConfigParser.Parse(block.Config);
        bool inlineDir = block.Body.Split('\n').Any(l => l.TrimStart().StartsWith("direction ", StringComparison.OrdinalIgnoreCase));
        if (!inlineDir && cfg.LayoutDirection is GraphDirection d) graph.Direction = d;
        foreach (var node in graph.Nodes)
        {
            if (cfg.Fill   is string f && node.FillColor   is null) node.FillColor   = f;
            if (cfg.Stroke is string s && node.StrokeColor is null) node.StrokeColor = s;
        }

        return RenderGraphFamily(graph, block, options, 1100);
    }

    private static FrameworkElement RenderSwimlane(MermaidBlock block, MarkdownPalette palette)
    {
        var graph = SwimlaneParser.Parse(block.Body);
        graph.Title = Titled(graph.Title, block);
        return WpfSwimlaneRenderer.Render(graph, palette);
    }

    /// <summary>
    /// C4 structural diagrams reuse the shared graph model, layout and renderer — a C4 diagram is a
    /// node-and-edge graph with richer boxes, so it needs a parser and a projection, not a layout engine.
    /// </summary>
    private static FrameworkElement RenderC4(MermaidBlock block, DiagramRenderOptions options)
    {
        var diagram = C4Parser.Parse(block.Body);
        diagram.Title  = Titled(diagram.Title, block);
        diagram.Config = C4ConfigParser.Parse(block.Config);
        return RenderGraphFamily(C4GraphProjector.ToGraph(diagram), block, options, 1100);
    }

    /// <summary>
    /// A C4 sequence goes through the same renderer as a native sequenceDiagram — the participants just
    /// carry element cards instead of plain boxes. See <see cref="C4SequenceProjector"/>.
    /// </summary>
    private static FrameworkElement RenderC4Sequence(MermaidBlock block, MarkdownPalette palette)
    {
        var diagram = C4SequenceProjector.ToSequence(C4Parser.Parse(block.Body));
        diagram.Title = Titled(diagram.Title, block);
        return WpfSequenceDiagramRenderer.Render(diagram, palette);
    }

    // Class boxes are wide; allow more width before the layout starts compacting horizontal gaps.
    private static FrameworkElement RenderClass(MermaidBlock block, DiagramRenderOptions options)
        => RenderGraphFamily(ClassParser.Parse(block.Body), block, options, 1100);

    /// <summary>
    /// Every diagram that shares the graph model, layout and renderer — flowchart, state, class, ER,
    /// requirement — goes through one path, so expansion, the <c>config: nexaflow:</c> block and the
    /// viewport are properties of "a graph diagram" rather than of whichever one they were built for.
    /// </summary>
    /// <param name="fallbackWidth">Width to lay out for until the view knows its real one.</param>
    private static FrameworkElement RenderGraphFamily(
        Graph graph, MermaidBlock block, DiagramRenderOptions options, double fallbackWidth)
    {
        graph.Title = Titled(graph.Title, block);
        var cfg = NexaflowConfigParser.Parse(block.Config);
        return new GraphDiagramView(graph, cfg, options.Palette, options, fallbackWidth);
    }
}
