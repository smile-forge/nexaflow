using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;
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
///   • <c>classDiagram</c>     → <see cref="MermaidClassParser"/>   + Sugiyama + <see cref="WpfGraphRenderer"/>
///   • <c>requirementDiagram</c> → <see cref="MermaidRequirementParser"/> + Sugiyama + <see cref="WpfGraphRenderer"/>
///   • <c>kanban</c>           → <see cref="MermaidKanbanParser"/>  + <see cref="WpfKanbanRenderer"/>
///   • <c>xychart[-beta]</c>   → <see cref="MermaidXyChartParser"/> + <see cref="WpfXyChartRenderer"/>
///   • <c>radar-beta</c>       → <see cref="MermaidRadarParser"/>   + <see cref="WpfRadarRenderer"/>
///   • <c>ishikawa-beta</c>    → <see cref="MermaidIshikawaParser"/> + <see cref="WpfIshikawaRenderer"/>
///   • <c>sankey</c>           → <see cref="MermaidSankeyParser"/>  + <see cref="WpfSankeyRenderer"/>
///   • <c>erDiagram</c>        → <see cref="MermaidErParser"/>      + Sugiyama + <see cref="WpfGraphRenderer"/>
///   • <c>venn-beta</c>        → <see cref="MermaidVennParser"/>    + <see cref="WpfVennRenderer"/>
///   • <c>timeline</c>         → <see cref="MermaidTimelineParser"/> + <see cref="WpfTimelineRenderer"/>
///   • <c>journey</c>          → <see cref="MermaidJourneyParser"/>  + <see cref="WpfJourneyRenderer"/>
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
    private static readonly MermaidClassParser    ClassParser    = new();
    private static readonly MermaidRequirementParser RequirementParser = new();
    private static readonly MermaidKanbanParser   KanbanParser   = new();
    private static readonly MermaidXyChartParser  XyParser       = new();
    private static readonly MermaidRadarParser    RadarParser    = new();
    private static readonly MermaidIshikawaParser IshikawaParser = new();
    private static readonly MermaidSankeyParser   SankeyParser   = new();
    private static readonly MermaidErParser       ErParser       = new();
    private static readonly MermaidVennParser     VennParser     = new();
    private static readonly MermaidCynefinParser  CynefinParser  = new();
    private static readonly MermaidArchitectureParser ArchitectureParser = new();
    private static readonly MermaidSwimlaneParser  SwimlaneParser = new();
    private static readonly MermaidTimelineParser  TimelineParser = new();
    private static readonly MermaidJourneyParser   JourneyParser  = new();

    public bool CanHandle(string language) =>
        language.Equals("mermaid", StringComparison.OrdinalIgnoreCase);

    public FrameworkElement Render(string source, MarkdownPalette palette, Func<string, bool>? onNavigate = null)
        => Render(source, DiagramRenderOptions.For(palette, onNavigate));

    public FrameworkElement Render(string source, DiagramRenderOptions options)
    {
        // A leading `--- … ---` YAML front-matter block (title/config) is stripped here so every
        // sub-type sees only the diagram body; a front-matter title is applied to the parsed chart.
        var (body, title) = MermaidFrontmatter.Strip(source);
        var palette = options.Palette;

        return SubtypeOf(body) switch
        {
            MermaidSubtype.Pie      => RenderPie(body, title, palette),
            MermaidSubtype.Quadrant => RenderQuadrant(body, title, palette),
            MermaidSubtype.Sequence => RenderSequence(body, title, palette),
            MermaidSubtype.Gantt    => RenderGantt(body, title, palette),
            MermaidSubtype.Git      => RenderGit(body, title, palette),
            MermaidSubtype.Mindmap  => RenderMindmap(body, title, palette),
            MermaidSubtype.State       => RenderGraphFamily(StateParser.Parse(body), source, title, options, 900),
            MermaidSubtype.Class       => RenderClass(source, body, title, options),
            MermaidSubtype.Requirement => RenderGraphFamily(RequirementParser.Parse(body), source, title, options, 1100),
            MermaidSubtype.Kanban      => RenderKanban(body, title, palette),
            MermaidSubtype.XyChart     => RenderXyChart(source, body, title, palette),
            MermaidSubtype.Radar       => RenderRadar(source, body, title, palette),
            MermaidSubtype.Ishikawa    => RenderIshikawa(source, body, title, palette),
            MermaidSubtype.Sankey      => RenderSankey(source, body, title, palette),
            MermaidSubtype.Er          => RenderEr(source, body, title, options),
            MermaidSubtype.Venn        => RenderVenn(source, body, title, palette),
            MermaidSubtype.Cynefin      => RenderCynefin(source, body, title, palette),
            MermaidSubtype.Architecture => RenderArchitecture(source, body, title, palette),
            MermaidSubtype.Swimlane     => RenderSwimlane(body, title, palette),
            MermaidSubtype.Timeline     => RenderTimeline(source, body, title, palette),
            MermaidSubtype.Journey      => RenderJourney(source, body, title, palette),
            MermaidSubtype.Graph       => RenderGraphFamily(FlowParser.Parse(body), source, title, options, 900),
            _                       => RenderSourceText(body),
        };
    }

    /// <summary>Applies a front-matter title to a chart that doesn't already carry one.</summary>
    private static string Titled(string? existing, string? frontmatter) =>
        string.IsNullOrWhiteSpace(existing) && !string.IsNullOrWhiteSpace(frontmatter) ? frontmatter! : existing ?? string.Empty;

    // ── Subtype detection ──────────────────────────────────────────────────

    private enum MermaidSubtype { Graph, Pie, Quadrant, Sequence, Gantt, Git, Mindmap, State, Class, Requirement, Kanban, XyChart, Radar, Ishikawa, Sankey, Er, Venn, Cynefin, Architecture, Swimlane, Timeline, Journey, Unknown }

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
            // classDiagram / classDiagram-v2.
            if (keyword.StartsWith("classdiagram")) return MermaidSubtype.Class;
            // requirementDiagram.
            if (keyword.StartsWith("requirementdiagram")) return MermaidSubtype.Requirement;
            // xychart / xychart-beta (an orientation keyword may follow on the same line).
            if (keyword.StartsWith("xychart")) return MermaidSubtype.XyChart;
            // radar / radar-beta.
            if (keyword.StartsWith("radar")) return MermaidSubtype.Radar;
            // ishikawa / ishikawa-beta (fishbone).
            if (keyword.StartsWith("ishikawa")) return MermaidSubtype.Ishikawa;
            // sankey (CSV flow diagram).
            if (keyword.StartsWith("sankey")) return MermaidSubtype.Sankey;
            // erDiagram (entity-relationship).
            if (keyword.StartsWith("erdiagram")) return MermaidSubtype.Er;
            // venn-beta.
            if (keyword.StartsWith("venn")) return MermaidSubtype.Venn;
            // cynefin-beta (five-domain sense-making framework).
            if (keyword.StartsWith("cynefin")) return MermaidSubtype.Cynefin;
            // architecture-beta (grouped services + side-anchored edges).
            if (keyword.StartsWith("architecture")) return MermaidSubtype.Architecture;
            // swimlane-beta (flowchart whose top-level subgraphs are lanes).
            if (keyword.StartsWith("swimlane")) return MermaidSubtype.Swimlane;

            return keyword switch
            {
                "pie"                              => MermaidSubtype.Pie,
                "quadrantchart"                    => MermaidSubtype.Quadrant,
                "sequencediagram"                  => MermaidSubtype.Sequence,
                "gantt"                            => MermaidSubtype.Gantt,
                "mindmap"                          => MermaidSubtype.Mindmap,
                "kanban"                           => MermaidSubtype.Kanban,
                "graph" or "flowchart"             => MermaidSubtype.Graph,
                "timeline"                         => MermaidSubtype.Timeline,
                "journey"                          => MermaidSubtype.Journey,
                "c4context" or "block-beta"        => MermaidSubtype.Unknown,
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

    private static FrameworkElement RenderKanban(string source, string? title, MarkdownPalette palette)
    {
        var board = KanbanParser.Parse(source);
        board.Title = Titled(board.Title, title);
        return WpfKanbanRenderer.Render(board, palette);
    }

    private static FrameworkElement RenderXyChart(string source, string body, string? title, MarkdownPalette palette)
    {
        var chart = XyParser.Parse(body);
        chart.Title  = Titled(chart.Title, title);
        // The xychart is the one diagram that applies its front-matter config: block (parsed from the
        // original, pre-stripped source). A config chartOrientation overrides the declaration keyword.
        chart.Config = XyChartConfigParser.Parse(MermaidFrontmatter.RawBlock(source));
        if (chart.Config.Orientation is XyOrientation o) chart.Orientation = o;
        return WpfXyChartRenderer.Render(chart, palette);
    }

    private static FrameworkElement RenderRadar(string source, string body, string? title, MarkdownPalette palette)
    {
        var chart = RadarParser.Parse(body);
        chart.Title  = Titled(chart.Title, title);
        // Like xychart, radar applies its front-matter config: block (geometry, themeVariables, cScale palette).
        chart.Config = RadarConfigParser.Parse(MermaidFrontmatter.RawBlock(source));
        return WpfRadarRenderer.Render(chart, palette);
    }

    private static FrameworkElement RenderIshikawa(string source, string body, string? title, MarkdownPalette palette)
    {
        var diagram = IshikawaParser.Parse(body);
        diagram.Title  = Titled(diagram.Title, title);   // Ishikawa has no inline title; a front-matter title shows above.
        diagram.Config = IshikawaConfigParser.Parse(MermaidFrontmatter.RawBlock(source));
        return WpfIshikawaRenderer.Render(diagram, palette);
    }

    private static FrameworkElement RenderSankey(string source, string body, string? title, MarkdownPalette palette)
    {
        var diagram = SankeyParser.Parse(body);
        diagram.Title  = Titled(diagram.Title, title);   // Sankey has no inline title; a front-matter title shows above.
        diagram.Config = SankeyConfigParser.Parse(MermaidFrontmatter.RawBlock(source));
        return WpfSankeyRenderer.Render(diagram, palette);
    }

    private static FrameworkElement RenderEr(string source, string body, string? title, DiagramRenderOptions options)
    {
        // ER entities are UML-style boxes, so they reuse the shared graph model + Sugiyama + WpfGraphRenderer
        // (like class / requirement diagrams). The er config is applied here: an inline `direction` wins, else
        // config layoutDirection; an explicit fill/stroke becomes the default for entities lacking a colour.
        var graph = ErParser.Parse(body);

        var cfg = ErConfigParser.Parse(MermaidFrontmatter.RawBlock(source));
        bool inlineDir = body.Split('\n').Any(l => l.TrimStart().StartsWith("direction ", StringComparison.OrdinalIgnoreCase));
        if (!inlineDir && cfg.LayoutDirection is GraphDirection d) graph.Direction = d;
        foreach (var node in graph.Nodes)
        {
            if (cfg.Fill   is string f && node.FillColor   is null) node.FillColor   = f;
            if (cfg.Stroke is string s && node.StrokeColor is null) node.StrokeColor = s;
        }

        return RenderGraphFamily(graph, source, title, options, 1100);
    }

    private static FrameworkElement RenderVenn(string source, string body, string? title, MarkdownPalette palette)
    {
        var diagram = VennParser.Parse(body);
        diagram.Title  = Titled(diagram.Title, title);
        diagram.Config = VennConfigParser.Parse(MermaidFrontmatter.RawBlock(source));
        return WpfVennRenderer.Render(diagram, palette);
    }

    private static FrameworkElement RenderCynefin(string source, string body, string? title, MarkdownPalette palette)
    {
        var diagram = CynefinParser.Parse(body);
        diagram.Title  = Titled(diagram.Title, title);
        diagram.Config = CynefinConfigParser.Parse(MermaidFrontmatter.RawBlock(source));
        return WpfCynefinRenderer.Render(diagram, palette);
    }

    private static FrameworkElement RenderArchitecture(string source, string body, string? title, MarkdownPalette palette)
    {
        var diagram = ArchitectureParser.Parse(body);
        diagram.Title  = Titled(diagram.Title, title);
        diagram.Config = ArchitectureConfigParser.Parse(MermaidFrontmatter.RawBlock(source));
        return WpfArchitectureRenderer.Render(diagram, palette);
    }

    private static FrameworkElement RenderSwimlane(string source, string? title, MarkdownPalette palette)
    {
        var graph = SwimlaneParser.Parse(source);
        graph.Title = Titled(graph.Title, title);
        return WpfSwimlaneRenderer.Render(graph, palette);
    }

    private static FrameworkElement RenderTimeline(string source, string body, string? title, MarkdownPalette palette)
    {
        var diagram = TimelineParser.Parse(body);
        diagram.Title  = Titled(diagram.Title, title);
        diagram.Config = TimelineConfigParser.Parse(MermaidFrontmatter.RawBlock(source));
        return WpfTimelineRenderer.Render(diagram, palette);
    }

    private static FrameworkElement RenderJourney(string source, string body, string? title, MarkdownPalette palette)
    {
        var diagram = JourneyParser.Parse(body);
        diagram.Title  = Titled(diagram.Title, title);
        diagram.Config = JourneyConfigParser.Parse(MermaidFrontmatter.RawBlock(source));
        return WpfJourneyRenderer.Render(diagram, palette);
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

    // Class boxes are wide; allow more width before the layout starts compacting horizontal gaps.
    private static FrameworkElement RenderClass(string source, string body, string? title, DiagramRenderOptions options)
        => RenderGraphFamily(ClassParser.Parse(body), source, title, options, 1100);

    /// <summary>
    /// Every diagram that shares the graph model, layout and renderer — flowchart, state, class, ER,
    /// requirement — goes through one path, so expansion, the <c>config: nexaflow:</c> block and the
    /// viewport are properties of "a graph diagram" rather than of whichever one they were built for.
    /// </summary>
    /// <param name="fallbackWidth">Width to lay out for until the view knows its real one.</param>
    private static FrameworkElement RenderGraphFamily(
        Graph graph, string source, string? title, DiagramRenderOptions options, double fallbackWidth)
    {
        graph.Title = Titled(graph.Title, title);
        var cfg = NexaflowConfigParser.Parse(MermaidFrontmatter.RawBlock(source));
        return new GraphDiagramView(graph, cfg, options.Palette, options, fallbackWidth);
    }
}
