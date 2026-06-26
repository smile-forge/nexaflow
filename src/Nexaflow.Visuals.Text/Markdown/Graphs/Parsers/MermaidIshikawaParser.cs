using Nexaflow.Visuals.Text.Markdown.Graphs.Charts;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Parses Mermaid <c>ishikawa-beta</c> (alias <c>ishikawa</c>) fishbone diagrams.
///
/// Structure is purely indentation-based (like a mindmap):
/// <code>
/// ishikawa-beta
///     Blurry Photo           ← the effect / problem (first content line = the head)
///     Process                ← a category (main bone)
///         Out of focus       ← a cause (indented under the category)
///     Equipment
///         LENS               ← a cause
///             Dirty lens     ← a sub-cause (arbitrary nesting via deeper indentation)
/// </code>
/// The first content line after the keyword is the head; every later line attaches to the nearest
/// shallower line by relative leading-whitespace depth.  Labels are plain text (no quoting).
/// </summary>
public sealed class MermaidIshikawaParser
{
    public bool CanParse(string language) =>
        language.StartsWith("ishikawa", StringComparison.OrdinalIgnoreCase);

    public IshikawaDiagram Parse(string source)
    {
        var diagram = new IshikawaDiagram();
        try { ParseInto(source, diagram); }
        catch { /* never throw; return partial diagram */ }
        return diagram;
    }

    private static void ParseInto(string source, IshikawaDiagram diagram)
    {
        bool sawKeyword = false;
        var content = new List<(int indent, string text)>();

        foreach (var raw in source.Split('\n'))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith("%%")) continue;

            if (!sawKeyword)
            {
                sawKeyword = true;
                // The opening keyword line is consumed; anything else (defensive) is treated as content.
                if (trimmed.StartsWith("ishikawa", StringComparison.OrdinalIgnoreCase)) continue;
            }

            content.Add((LeadingWhitespace(raw), trimmed));
        }

        if (content.Count == 0) return;
        diagram.Head = content[0].text;

        // Build the bone tree from the remaining lines by relative indentation.
        var stack = new List<(int indent, IshikawaNode node)>();
        for (int i = 1; i < content.Count; i++)
        {
            var (indent, text) = content[i];
            while (stack.Count > 0 && stack[^1].indent >= indent) stack.RemoveAt(stack.Count - 1);

            var node = new IshikawaNode { Text = text };
            if (stack.Count == 0) diagram.Categories.Add(node);
            else stack[^1].node.Children.Add(node);

            stack.Add((indent, node));
        }
    }

    private static int LeadingWhitespace(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return i;
    }
}
