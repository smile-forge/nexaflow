using Nexaflow.Visuals.Text.Markdown.Graphs.Layout;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;
using System.Windows;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Handlers;

/// <summary>
/// Generic handler for any language whose parser implements <see cref="IGraphParser"/>
/// and whose output is a directed graph (Sugiyama layout + WPF graph renderer).
///
/// Use this to register new graph-based languages without writing a dedicated handler:
/// <code>new GraphDiagramHandler(new MyLanguageParser())</code>
/// </summary>
public sealed class GraphDiagramHandler : IDiagramHandler
{
    private readonly IGraphParser _parser;

    public GraphDiagramHandler(IGraphParser parser) =>
        _parser = parser;

    public bool CanHandle(string language) => _parser.CanParse(language);

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options)
    {
        // These languages have no front-matter, so there is nothing to configure — but they share the
        // graph model, so they get the same viewport and the same width-aware layout for free.
        var graph = _parser.Parse(source);
        return new GraphDiagramView(graph, new Charts.NexaflowGraphConfig(), options.Palette, options, 900);
    }
}
