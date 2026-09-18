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
    string Group);

/// <summary>
/// The help pages the app ships, read straight from the feature projects so the site cannot drift from what a
/// reader sees in the app. A page that is not in <see cref="Groups"/> stops the build: a new help page has to be
/// given a home on the site deliberately, rather than quietly going missing from every index.
/// </summary>
internal sealed class HelpCorpus
{
    public required IReadOnlyList<HelpPage> Pages { get; init; }

    /// <summary>The sections of the site, in the order they are shown.</summary>
    public static readonly string[] GroupOrder =
    [
        "Open anything",
        "Author & draw",
        "Run your machine",
        "Keep track",
        "With your AI",
        "Getting around",
    ];

    /// <summary>Which section each help topic belongs to. Every topic but the index must be here.</summary>
    private static readonly Dictionary<string, string> Groups = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FileSystem"] = "Open anything",
        ["Search"] = "Open anything",
        ["Text"] = "Open anything",
        ["Code"] = "Open anything",
        ["Json"] = "Open anything",
        ["Logs"] = "Open anything",
        ["Tabular"] = "Open anything",
        ["Notebook"] = "Open anything",
        ["Html"] = "Open anything",
        ["Pdf"] = "Open anything",
        ["Images"] = "Open anything",
        ["Svg"] = "Open anything",
        ["Font"] = "Open anything",
        ["Audio"] = "Open anything",
        ["Video"] = "Open anything",
        ["Model3D"] = "Open anything",
        ["Dicom"] = "Open anything",
        ["Email"] = "Open anything",
        ["Hex"] = "Open anything",
        ["Compressed"] = "Open anything",
        ["VirtualDisk"] = "Open anything",
        ["Executable"] = "Open anything",

        ["Markdown"] = "Author & draw",
        ["MarkdownDiagrams"] = "Author & draw",
        ["DiagramsFlowAndStructure"] = "Author & draw",
        ["DiagramsPlanning"] = "Author & draw",
        ["DiagramsCharts"] = "Author & draw",
        ["MarkdownCodes"] = "Author & draw",
        ["MarkdownScience"] = "Author & draw",
        ["Solver"] = "Author & draw",

        ["Console"] = "Run your machine",
        ["Processes"] = "Run your machine",
        ["ProcessDetail"] = "Run your machine",
        ["SystemInfo"] = "Run your machine",
        ["SystemServices"] = "Run your machine",
        ["SystemEnvVars"] = "Run your machine",
        ["WindowsRegistry"] = "Run your machine",
        ["WindowsApps"] = "Run your machine",
        ["Network"] = "Run your machine",

        ["ProductManager"] = "Keep track",
        ["ProductIntegrity"] = "Keep track",
        ["ProductSearch"] = "Keep track",
        ["GraphViewer"] = "Keep track",
        ["Projects"] = "Keep track",
        ["ProjectDetail"] = "Keep track",
        ["Scratchpad"] = "Keep track",

        ["AIChat"] = "With your AI",
        ["Conversation"] = "With your AI",

        ["Help"] = "Getting around",
    };

    public static HelpCorpus Read(string repo)
    {
        var pages = new List<HelpPage>();
        var ungrouped = new List<string>();

        foreach (var project in ProjectDirs(repo))
        {
            var help = Path.Combine(project, "Localization", "en", "help");
            if (!Directory.Exists(help)) continue;

            foreach (var file in Directory.GetFiles(help, "*.md").OrderBy(f => f, StringComparer.Ordinal))
            {
                var topic = Path.GetFileNameWithoutExtension(file);
                if (topic.Equals("index", StringComparison.OrdinalIgnoreCase)) continue;

                if (!Groups.TryGetValue(topic, out var group)) { ungrouped.Add(file); continue; }

                var (title, summary, body) = Split(File.ReadAllText(file).Replace("\r\n", "\n"), topic);
                pages.Add(new HelpPage(
                    Path.GetFileName(project), topic, title, summary, Slug(topic), file, body, group));
            }
        }

        if (ungrouped.Count > 0)
            throw new InvalidOperationException(
                "these help pages have no section in HelpCorpus.Groups, so the site would drop them:\n  "
                + string.Join("\n  ", ungrouped));

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
