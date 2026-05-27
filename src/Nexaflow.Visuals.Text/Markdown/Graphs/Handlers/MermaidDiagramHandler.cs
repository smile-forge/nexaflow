using Nexaflow.Visuals.Text.Markdown.Graphs.Layout;
using Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;
using Nexaflow.Visuals.Text.Markdown.Graphs.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Handlers;

/// <summary>
/// Handles all <c>mermaid</c> fenced code blocks.
///
/// Mermaid is a family of diagram types sharing one language tag.  This handler
/// reads the first keyword of the source to choose the correct sub-pipeline:
///   • <c>pie</c>              → <see cref="MermaidPieParser"/> + <see cref="WpfPieChartRenderer"/>
///   • <c>graph / flowchart</c> → <see cref="MermaidParser"/>    + Sugiyama + <see cref="WpfGraphRenderer"/>
///
/// Adding a new Mermaid diagram type means adding a branch in <see cref="SubtypeOf"/>
/// and a corresponding render path — no changes outside this class.
/// </summary>
public sealed class MermaidDiagramHandler : IDiagramHandler
{
    private static readonly MermaidParser    FlowParser = new();
    private static readonly MermaidPieParser PieParser  = new();

    public bool CanHandle(string language) =>
        language.Equals("mermaid", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source)
    {
        return SubtypeOf(source) switch
        {
            MermaidSubtype.Pie     => RenderPie(source),
            MermaidSubtype.Graph   => RenderGraph(source),
            _                      => RenderSourceText(source),
        };
    }

    // ── Subtype detection ──────────────────────────────────────────────────

    private enum MermaidSubtype { Graph, Pie, Unknown }

    /// <summary>
    /// Reads the first non-blank, non-comment keyword to identify the diagram
    /// family.  All content decisions are made here, not in the dispatcher.
    /// </summary>
    private static MermaidSubtype SubtypeOf(string source)
    {
        foreach (var raw in source.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("%%")) continue;

            // First real keyword determines the type
            var keyword = line.Split(' ', '\t')[0].ToLowerInvariant();
            return keyword switch
            {
                "pie"                              => MermaidSubtype.Pie,
                "graph" or "flowchart"             => MermaidSubtype.Graph,
                "sequencediagram"
                    or "classdiagram"
                    or "erdiagram"
                    or "gantt"
                    or "gitgraph"
                    or "mindmap"
                    or "timeline"
                    or "journey"
                    or "quadrantchart"
                    or "requirementdiagram"
                    or "c4context"
                    or "block-beta"
                    or "architecture-beta" => MermaidSubtype.Unknown,
                _                          => MermaidSubtype.Unknown,
            };
        }
        return MermaidSubtype.Graph;
    }

    // ── Sub-renderers ──────────────────────────────────────────────────────

    private static FrameworkElement RenderPie(string source)
    {
        var chart = PieParser.Parse(source);
        return WpfPieChartRenderer.Render(chart);
    }

    private static FrameworkElement RenderSourceText(string source) =>
        new Border
        {
            Background      = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x2E)),
            BorderBrush     = new SolidColorBrush(Color.FromRgb(0x45, 0x47, 0x5A)),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(4),
            Padding         = new Thickness(12),
            Child = new TextBlock
            {
                Text                = source,
                FontFamily          = new FontFamily("Cascadia Code, Consolas, Courier New"),
                FontSize            = 13,
                Foreground          = new SolidColorBrush(Color.FromRgb(0xCD, 0xD6, 0xF4)),
                TextWrapping        = TextWrapping.NoWrap,
                FontStyle           = FontStyles.Normal,
            },
        };

    private static FrameworkElement RenderGraph(string source)
    {
        var graph  = FlowParser.Parse(source);
        var layout = SugiyamaLayout.Compute(graph, preferredMaxWidth: 900);
        return WpfGraphRenderer.Render(layout);
    }
}
