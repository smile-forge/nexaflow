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
    {
        var graph  = _parser.Parse(source);
        var layout = SugiyamaLayout.Compute(graph, preferredMaxWidth: 900);
        return WpfGraphRenderer.Render(layout, palette);
    }
}
