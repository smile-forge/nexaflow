namespace Nexaflow.Markdown.Mermaid.Mindmap;

/// <summary>
/// What a <c>mindmap</c> block's front matter asks for: <c>config: mindmap:</c> <c>padding</c>, <c>maxNodeWidth</c> and
/// <c>useMaxWidth</c>, the <c>layout</c> it names, and its colours under <c>themeVariables:</c> — each branch's <c>cScale</c>,
/// <c>cScaleLabel</c> and <c>cScaleInv</c>, and the root's <c>git0</c> and <c>gitBranchLabel0</c>.
///
/// <para>A size nobody wrote is Mermaid's own, and a colour nobody wrote the theme's, so it is null here.</para>
/// </summary>
public sealed record MindmapConfig
{
    public static MindmapConfig Default { get; } = new();

    public double? Padding { get; init; }
    public double? MaxNodeWidth { get; init; }
    public bool UseMaxWidth { get; init; }

    /// <summary>The layout the front matter names — <c>tidy-tree</c>, <c>cose-bilkent</c> — read and kept; a mindmap here is drawn as a tidy tree.</summary>
    public string? Layout { get; init; }

    /// <summary>The <c>cScale</c>, <c>cScaleLabel</c> and <c>cScaleInv</c> colours written, by their number.</summary>
    public IReadOnlyDictionary<int, string> Scale { get; init; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<int, string> ScaleLabel { get; init; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<int, string> ScaleInverse { get; init; } = new Dictionary<int, string>();

    /// <summary>The root's fill and the ink of its words.</summary>
    public string? RootFill { get; init; }
    public string? RootTextFill { get; init; }

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static MindmapConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    public static MindmapConfig From(MermaidConfig config)
    {
        var mindmap = config.Diagram("mindmap");
        var theme = config.Theme;

        return new MindmapConfig
        {
            Padding = mindmap.Number("padding") is { } padding ? Math.Max(0, padding) : null,
            MaxNodeWidth = mindmap.Size("maxNodeWidth"),
            UseMaxWidth = mindmap.Flag("useMaxWidth") ?? false,
            Layout = config.Value("layout") ?? config.Shared.Value("layout"),
            Scale = theme.Swatches("cScale", 12, first: 0),
            ScaleLabel = theme.Swatches("cScaleLabel", 12, first: 0),
            ScaleInverse = theme.Swatches("cScaleInv", 12, first: 0),
            RootFill = theme.Value("git0"),
            RootTextFill = theme.Value("gitBranchLabel0"),
        };
    }
}
