using System.Text;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Core.Help;

/// <summary>
/// What the assistant's Markdown can draw, and how to write each of it — read from the Markdown help the app ships,
/// where every kind of content has a section of its own with its examples. The help is the one place it is written
/// down, so what the assistant is told is what the user reads and what the renderer draws.
/// </summary>
internal sealed class MarkdownSyntaxTools(HelpLibrary library)
{
    /// <summary>The help pages that document what Markdown can hold: the Markdown feature's own.</summary>
    private const string Pages = "Nexaflow.Features.Markdown/";

    public IReadOnlyList<IClientTool> Tools =>
    [
        new DelegateClientTool(
            "get_markdown_syntaxes",
            "Lists every kind of content your Markdown replies can draw — tables, maths, callouts, each kind of diagram "
          + "and chart, codes, chemistry, music — by name, grouped as the help groups them. Takes no arguments.",
            [],
            ToolSafety.SafeOperation,
            ListAsync,
            parallelizable: true),

        new DelegateClientTool(
            "get_markdown_syntax",
            "How to write one kind of content: its help section in full, with examples. Give a name from get_markdown_syntaxes.",
            [new ClientToolParameter("name", "The kind of content, e.g. 'Pie chart', 'Math' or 'QR codes'.")],
            ToolSafety.SafeOperation,
            ReadAsync,
            parallelizable: true),
    ];

    private IEnumerable<(HelpTopic Page, IReadOnlyList<Section> Sections)> Documented()
        => library.Topics
                  .Where(t => t.LogicalName.StartsWith(Pages, StringComparison.Ordinal))
                  .Select(t => (t, Sections(library.Source(t) ?? "")));

    private Task<ToolResult> ListAsync(JsonObject args, CancellationToken ct)
    {
        var text = new StringBuilder();
        int count = 0;
        foreach (var (page, sections) in Documented())
        {
            if (sections.Count == 0) continue;
            text.Append(page.Title).Append(": ").Append(string.Join(", ", sections.Select(s => s.Name))).Append('\n');
            count += sections.Count;
        }
        return Task.FromResult(ToolResult.Ok($"{count} kinds of content", text.ToString().TrimEnd()));
    }

    private Task<ToolResult> ReadAsync(JsonObject args, CancellationToken ct)
    {
        var name = ((string?)args["name"])?.Trim() ?? "";
        var all  = Documented().SelectMany(d => d.Sections).ToList();
        var found = all.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                 ?? all.FirstOrDefault(s => name.Length > 0 && s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(found is null
            ? ToolResult.Error($"no '{name}'", $"No kind of content is called '{name}'. get_markdown_syntaxes lists them all.")
            : ToolResult.Ok($"how to write {found.Name}", found.Text));
    }

    /// <summary>A page's sections, one a <c>##</c> heading: its name and its text up to the next heading of that level or above.</summary>
    internal static IReadOnlyList<Section> Sections(string markdown)
    {
        var sections = new List<Section>();
        string? name = null;
        var text = new StringBuilder();
        bool fenced = false;
        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) fenced = !fenced;
            bool heading = !fenced && (line.StartsWith("## ", StringComparison.Ordinal) || line.StartsWith("# ", StringComparison.Ordinal));
            if (heading)
            {
                if (name is not null) sections.Add(new Section(name, text.ToString().Trim()));
                name = line.StartsWith("## ", StringComparison.Ordinal) ? line[3..].Trim() : null;
                text.Clear();
            }
            if (name is not null) text.Append(line).Append('\n');
        }
        if (name is not null) sections.Add(new Section(name, text.ToString().Trim()));
        return sections;
    }

    internal sealed record Section(string Name, string Text);
}
