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
///   • <c>pie</c>              → <see cref="MermaidPieParser"/>      + <see cref="WpfPieChartRenderer"/>
///   • <c>quadrantChart</c>    → <see cref="MermaidQuadrantParser"/> + <see cref="WpfQuadrantChartRenderer"/>
///   • <c>sequenceDiagram</c>  → <see cref="MermaidSequenceParser"/> + <see cref="WpfSequenceDiagramRenderer"/>
///   • <c>gantt</c>            → <see cref="MermaidGanttParser"/>    + <see cref="WpfGanttRenderer"/>
///   • <c>graph / flowchart</c> → <see cref="MermaidParser"/>        + Sugiyama + <see cref="WpfGraphRenderer"/>
///
/// Adding a new Mermaid diagram type means adding a branch in <see cref="SubtypeOf"/>
/// and a corresponding render path — no changes outside this class.
/// </summary>
public sealed class MermaidDiagramHandler : IDiagramHandler
{
    private static readonly MermaidParser         FlowParser     = new();
    private static readonly MermaidPieParser      PieParser      = new();
    private static readonly MermaidQuadrantParser QuadrantParser = new();
    private static readonly MermaidSequenceParser SequenceParser = new();
    private static readonly MermaidGanttParser    GanttParser    = new();
    private static readonly MermaidGitGraphParser GitParser      = new();
    private static readonly MermaidMindmapParser  MindmapParser  = new();
    private static readonly MermaidStateParser    StateParser    = new();

    public bool CanHandle(string language) =>
        language.Equals("mermaid", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, MarkdownPalette palette)
    {
        // A leading `--- … ---` YAML front-matter block (title/config) is stripped here so every
        // sub-type sees only the diagram body; a front-matter title is applied to the parsed chart.
        var (body, title) = MermaidFrontmatter.Strip(source);

        return SubtypeOf(body) switch
        {
            MermaidSubtype.Pie      => RenderPie(body, title, palette),
            MermaidSubtype.Quadrant => RenderQuadrant(body, title, palette),
            MermaidSubtype.Sequence => RenderSequence(body, title, palette),
            MermaidSubtype.Gantt    => RenderGantt(body, title, palette),
            MermaidSubtype.Git      => RenderGit(body, title, palette),
            MermaidSubtype.Mindmap  => RenderMindmap(body, title, palette),
            MermaidSubtype.State    => RenderState(body, title, palette),
            MermaidSubtype.Graph    => RenderGraph(body, title, palette),
            _                       => RenderSourceText(body),
        };
    }

    /// <summary>Applies a front-matter title to a chart that doesn't already carry one.</summary>
    private static string Titled(string? existing, string? frontmatter) =>
        string.IsNullOrWhiteSpace(existing) && !string.IsNullOrWhiteSpace(frontmatter) ? frontmatter! : existing ?? string.Empty;

    // ── Subtype detection ──────────────────────────────────────────────────

    private enum MermaidSubtype { Graph, Pie, Quadrant, Sequence, Gantt, Git, Mindmap, State, Unknown }

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

            // gitGraph may carry an orientation/colon ("gitGraph TB:", "gitGraph:").
            if (keyword.StartsWith("gitgraph")) return MermaidSubtype.Git;
            // stateDiagram / stateDiagram-v2.
            if (keyword.StartsWith("statediagram")) return MermaidSubtype.State;

            return keyword switch
            {
                "pie"                              => MermaidSubtype.Pie,
                "quadrantchart"                    => MermaidSubtype.Quadrant,
                "sequencediagram"                  => MermaidSubtype.Sequence,
                "gantt"                            => MermaidSubtype.Gantt,
                "mindmap"                          => MermaidSubtype.Mindmap,
                "graph" or "flowchart"             => MermaidSubtype.Graph,
                "classdiagram"
                    or "erdiagram"
                    or "timeline"
                    or "journey"
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

    private static FrameworkElement RenderPie(string source, string? title, MarkdownPalette palette)
    {
        var chart = PieParser.Parse(source);
        chart.Title = Titled(chart.Title, title);
        return WpfPieChartRenderer.Render(chart, palette);
    }

    private static FrameworkElement RenderQuadrant(string source, string? title, MarkdownPalette palette)
    {
        var chart = QuadrantParser.Parse(source);
        chart.Title = Titled(chart.Title, title);
        return WpfQuadrantChartRenderer.Render(chart, palette);
    }

    private static FrameworkElement RenderSequence(string source, string? title, MarkdownPalette palette)
    {
        var diagram = SequenceParser.Parse(source);
        diagram.Title = Titled(diagram.Title, title);
        return WpfSequenceDiagramRenderer.Render(diagram, palette);
    }

    private static FrameworkElement RenderGantt(string source, string? title, MarkdownPalette palette)
    {
        var chart = GanttParser.Parse(source);
        chart.Title = Titled(chart.Title, title);
        return WpfGanttRenderer.Render(chart, palette);
    }

    private static FrameworkElement RenderGit(string source, string? title, MarkdownPalette palette)
    {
        var graph = GitParser.Parse(source);
        graph.Title = Titled(graph.Title, title);
        return WpfGitGraphRenderer.Render(graph, palette);
    }

    private static FrameworkElement RenderMindmap(string source, string? title, MarkdownPalette palette)
    {
        var map = MindmapParser.Parse(source);
        map.Title = Titled(map.Title, title);
        return WpfMindmapRenderer.Render(map, palette);
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

    private static FrameworkElement RenderGraph(string source, string? title, MarkdownPalette palette)
    {
        var graph  = FlowParser.Parse(source);
        graph.Title = Titled(graph.Title, title);
        var layout = SugiyamaLayout.Compute(graph, preferredMaxWidth: 900);
        return WpfGraphRenderer.Render(layout, palette);
    }

    private static FrameworkElement RenderState(string source, string? title, MarkdownPalette palette)
    {
        var graph  = StateParser.Parse(source);
        graph.Title = Titled(graph.Title, title);
        var layout = SugiyamaLayout.Compute(graph, preferredMaxWidth: 900);
        return WpfGraphRenderer.Render(layout, palette);
    }
}
