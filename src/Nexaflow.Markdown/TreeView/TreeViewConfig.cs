using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Markdown.TreeView;

/// <summary>
/// What a tree view's front matter asks for — <c>config: treeView:</c> and <c>themeVariables: treeView:</c> — with Mermaid's own
/// defaults where it asks for nothing.
/// </summary>
public sealed record TreeViewConfig
{
    public static TreeViewConfig Default { get; } = new();

    /// <summary>How much further in each level of the tree is set, past the air before a row's words.</summary>
    public double RowIndent { get; init; } = 10;

    /// <summary>The air either side of a row's words.</summary>
    public double PaddingX { get; init; } = 5;

    /// <summary>The air over and under a row's words.</summary>
    public double PaddingY { get; init; } = 5;

    /// <summary>How thick the lines joining a folder to what is in it are.</summary>
    public double LineThickness { get; init; } = 1;

    /// <summary>Whether every node is drawn with an icon — a folder's, a file's, or what the maps below say — and not only one naming its own.</summary>
    public bool ShowIcons { get; init; }

    /// <summary>
    /// The icon pack a bare icon name is an icon of — in an <c>icon()</c>, or either map below — where it has one of that name: a pack
    /// shipped with the app, such as <c>material-icon-theme</c>, by the prefix Iconify names it with.
    /// </summary>
    public string? DefaultIconPack { get; init; }

    /// <summary>The icon a file of each name is drawn with, where icons are shown.</summary>
    public IReadOnlyDictionary<string, string> FilenameIcons { get; init; } = new Dictionary<string, string>();

    /// <summary>The icon a file ending in each extension is drawn with, where icons are shown.</summary>
    public IReadOnlyDictionary<string, string> ExtensionIcons { get; init; } = new Dictionary<string, string>();

    /// <summary>How big a label is set.</summary>
    public double LabelFontSize { get; init; } = 16;

    public string? LabelColour { get; init; }
    public string? LineColour { get; init; }
    public string? IconColour { get; init; }
    public string? DescriptionColour { get; init; }
    public string? HighlightBackground { get; init; }
    public string? HighlightStroke { get; init; }

    /// <summary>Reads the YAML between a block's front-matter fences — see <see cref="MermaidBlock.Config"/>.</summary>
    public static TreeViewConfig Read(string? yaml) => From(MermaidConfig.Read(yaml));

    /// <summary>Reads a block's front matter, already read as config.</summary>
    public static TreeViewConfig From(MermaidConfig config)
    {
        var tree = config.Diagram("treeView");
        var theme = config.DiagramTheme("treeView");

        return new TreeViewConfig
        {
            RowIndent = Math.Max(0, tree.Number("rowIndent") ?? Default.RowIndent),
            PaddingX = Math.Max(0, tree.Number("paddingX") ?? Default.PaddingX),
            PaddingY = Math.Max(0, tree.Number("paddingY") ?? Default.PaddingY),
            LineThickness = Math.Max(0, tree.Number("lineThickness") ?? Default.LineThickness),
            ShowIcons = tree.Flag("showIcons") ?? Default.ShowIcons,
            DefaultIconPack = tree.Value("defaultIconPack") is { Length: > 0 } pack ? pack : null,
            FilenameIcons = tree.Section("filenameIcons")?.Values ?? Default.FilenameIcons,
            ExtensionIcons = tree.Section("extensionIcons")?.Values ?? Default.ExtensionIcons,

            LabelFontSize = theme.Size("labelFontSize") ?? Default.LabelFontSize,
            LabelColour = theme.Value("labelColor"),
            LineColour = theme.Value("lineColor"),
            IconColour = theme.Value("iconColor"),
            DescriptionColour = theme.Value("descriptionColor"),
            HighlightBackground = theme.Value("highlightBg"),
            HighlightStroke = theme.Value("highlightStroke"),
        };
    }
}
