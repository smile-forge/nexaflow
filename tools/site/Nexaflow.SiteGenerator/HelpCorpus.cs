using System.Text.RegularExpressions;

namespace Nexaflow.SiteGenerator;

/// <summary>One help page as it ships: where it came from, and what the site needs to lay it out.</summary>
internal sealed record HelpPage(
    string Project,
    string Topic,
    string Title,
    string Summary,
    string Slug,
    string Path,
    string Body,
    string Group,
    string? Node);

/// <summary>A section of the site: one page introducing a group of the app, with its own hand-written opening.</summary>
internal sealed record Section(string Group, string Slug, string Title);

/// <summary>Where a help page belongs on the site, and the product-tree node that describes its feature.</summary>
internal sealed record Placement(string Group, string? Node = null);

/// <summary>
/// The help pages the app ships, read straight from the feature projects so the site cannot drift from what a
/// reader sees in the app. A page that is not in <see cref="Catalogue"/> stops the build: a new help page has to
/// be given a home on the site deliberately, rather than quietly going missing from every index.
/// </summary>
internal sealed class HelpCorpus
{
    public required IReadOnlyList<HelpPage> Pages { get; init; }

    /// <summary>
    /// The sections of the site, in the order they are shown. "Getting around" has no section page of its own —
    /// it is one page about the help pane, and it belongs in the reference rather than on the front of the site.
    /// </summary>
    public static readonly Section[] Sections =
    [
        new("Open anything",    "open",     "Open anything"),
        new("Author & draw",    "author",   "Author & draw"),
        new("Run your machine", "system",   "Run your machine"),
        new("Keep track",       "track",    "Keep track"),
        new("With your AI",     "together", "With your AI"),
    ];

    public static readonly string[] GroupOrder =
        [.. Sections.Select(s => s.Group), "Getting around"];

    public static Section? SectionFor(string group) => Sections.FirstOrDefault(s => s.Group == group);

    /// <summary>
    /// Every help topic's home on the site, and the product-tree node whose description and concerns describe it.
    /// Only the page that leads a feature carries the node, so a feature is badged once rather than on every one
    /// of its pages.
    /// </summary>
    private static readonly Dictionary<string, Placement> Catalogue = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FileSystem"] = new("Open anything", "win-file-system"),
        ["Search"] = new("Open anything", "win-search"),
        ["Text"] = new("Open anything", "text-viewer"),
        ["Code"] = new("Open anything", "code"),
        ["Json"] = new("Open anything", "json"),
        ["Logs"] = new("Open anything", "log-viewer"),
        ["Tabular"] = new("Open anything", "tabular"),
        ["Notebook"] = new("Open anything", "notebook"),
        ["Html"] = new("Open anything", "web-viewer"),
        ["Pdf"] = new("Open anything", "pdf"),
        ["Images"] = new("Open anything", "images"),
        ["Svg"] = new("Open anything", "svg"),
        ["Font"] = new("Open anything", "font"),
        ["Audio"] = new("Open anything", "audio"),
        ["Video"] = new("Open anything", "video"),
        ["Model3D"] = new("Open anything", "model3d"),
        ["Dicom"] = new("Open anything", "dicom"),
        ["Email"] = new("Open anything", "email"),
        ["Hex"] = new("Open anything", "hex"),
        ["Compressed"] = new("Open anything", "compressed"),
        ["VirtualDisk"] = new("Open anything", "virtual-disk-viewer"),
        ["Executable"] = new("Open anything", "executable-inspector"),

        ["Markdown"] = new("Author & draw", "markdown"),
        ["MarkdownDiagrams"] = new("Author & draw"),
        ["DiagramsFlowAndStructure"] = new("Author & draw"),
        ["DiagramsPlanning"] = new("Author & draw"),
        ["DiagramsCharts"] = new("Author & draw"),
        ["MarkdownCodes"] = new("Author & draw"),
        ["MarkdownScience"] = new("Author & draw"),
        ["Solver"] = new("Author & draw", "solver"),

        ["Console"] = new("Run your machine", "console"),
        ["Processes"] = new("Run your machine", "processes"),
        ["ProcessDetail"] = new("Run your machine"),
        ["SystemInfo"] = new("Run your machine", "sysinfo"),
        ["SystemServices"] = new("Run your machine"),
        ["SystemEnvVars"] = new("Run your machine"),
        ["WindowsRegistry"] = new("Run your machine", "win-registry"),
        ["WindowsApps"] = new("Run your machine", "windowsapps"),
        ["Network"] = new("Run your machine", "network"),

        ["ProductManager"] = new("Keep track", "product"),
        ["ProductIntegrity"] = new("Keep track"),
        ["ProductSearch"] = new("Keep track"),
        ["GraphViewer"] = new("Keep track"),
        ["Projects"] = new("Keep track", "projects"),
        ["ProjectDetail"] = new("Keep track"),
        ["Scratchpad"] = new("Keep track", "scratchpad"),

        ["AIChat"] = new("With your AI", "aichat"),
        ["Conversation"] = new("With your AI"),

        ["Help"] = new("Getting around"),
    };

    public static HelpCorpus Read(string repo)
    {
        var pages = new List<HelpPage>();
        var unplaced = new List<string>();

        foreach (var project in ProjectDirs(repo))
        {
            var help = Path.Combine(project, "Localization", "en", "help");
            if (!Directory.Exists(help)) continue;

            foreach (var file in Directory.GetFiles(help, "*.md").OrderBy(f => f, StringComparer.Ordinal))
            {
                var topic = Path.GetFileNameWithoutExtension(file);
                if (topic.Equals("index", StringComparison.OrdinalIgnoreCase)) continue;

                if (!Catalogue.TryGetValue(topic, out var placement)) { unplaced.Add(file); continue; }

                var (title, summary, body) = Split(File.ReadAllText(file).Replace("\r\n", "\n"), topic);
                pages.Add(new HelpPage(
                    Path.GetFileName(project), topic, title, summary, Slug(topic), file, body,
                    placement.Group, placement.Node));
            }
        }

        if (unplaced.Count > 0)
            throw new InvalidOperationException(
                "these help pages have no section in HelpCorpus.Catalogue, so the site would drop them:\n  "
                + string.Join("\n  ", unplaced));

        return new HelpCorpus { Pages = pages.OrderBy(p => p.Title, StringComparer.OrdinalIgnoreCase).ToList() };
    }

    /// <summary>The same two depths LanguagePack.targets gathers from: src/&lt;name&gt;/ and src/&lt;group&gt;/&lt;name&gt;/.</summary>
    private static IEnumerable<string> ProjectDirs(string repo)
    {
        var src = Path.Combine(repo, "src");
        return Directory.GetDirectories(src)
            .Where(d => Path.GetFileName(d) != "Nexaflow.Tests")
            .SelectMany(d => Directory.GetDirectories(d).Prepend(d));
    }

    /// <summary>
    /// PascalCase to a readable url: MarkdownDiagrams becomes markdown-diagrams and AIChat becomes ai-chat. A
    /// digit never starts a new word, so Model3D stays model3d rather than breaking into model3-d.
    /// </summary>
    public static string Slug(string topic)
        => Regex.Replace(topic, "(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", "-").ToLowerInvariant();

    /// <summary>
    /// A help page opens with its title and a line saying what the page is for. Both become the page header on the
    /// site, so the body is what is left after them — otherwise the reader meets the same sentence twice.
    /// </summary>
    private static (string Title, string Summary, string Body) Split(string markdown, string fallback)
    {
        var lines = markdown.Split('\n');
        var at = Array.FindIndex(lines, l => l.StartsWith("# ", StringComparison.Ordinal));
        if (at < 0) return (fallback, string.Empty, markdown);

        var i = at + 1;
        while (i < lines.Length && lines[i].Trim().Length == 0) i++;

        var summary = new List<string>();
        while (i < lines.Length && lines[i].Trim() is { Length: > 0 } text && text != "---" && !text.StartsWith('#'))
        {
            summary.Add(text);
            i++;
        }

        return (lines[at][2..].Trim(), string.Join(' ', summary), string.Join('\n', lines[i..]));
    }
}
