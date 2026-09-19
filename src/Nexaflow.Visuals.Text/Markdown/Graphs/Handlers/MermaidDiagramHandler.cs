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
