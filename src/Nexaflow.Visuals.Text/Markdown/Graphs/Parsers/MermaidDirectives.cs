using System;
using System.Collections.Generic;
using System.Linq;

namespace Nexaflow.Visuals.Text.Markdown.Graphs.Parsers;

/// <summary>
/// Directives that mean the same thing in every mermaid diagram type, parsed once here rather than
/// re-implemented per parser.
/// </summary>
public static class MermaidDirectives
{
    /// <summary>
    /// Parses a mermaid <c>click</c> directive, in either accepted spelling:
    /// <code>
    /// click nodeId "https://example.com"
    /// click nodeId "https://example.com" "tooltip text"
    /// click nodeId href "https://example.com" "tooltip text"
    /// </code>
    /// <para>
    /// The <c>click id call fn()</c> form is recognised and rejected: it names a JavaScript callback,
    /// and this renderer has no script host to run one in. Returning false for it means the line is
    /// skipped as before rather than being turned into a link to a function name.
    /// </para>
    /// </summary>
    public static bool TryParseClick(string line, out string id, out string href, out string? tooltip)
    {
        id = href = string.Empty;
        tooltip = null;

        var trimmed = line.AsSpan().Trim();
        if (!trimmed.StartsWith("click ", StringComparison.OrdinalIgnoreCase)) return false;

        string rest = trimmed[6..].TrimStart().ToString();
        if (rest.Length == 0) return false;

        // The node id runs to the first space.
        int split = rest.IndexOf(' ');
        if (split <= 0) return false;

        id   = rest[..split].Trim();
        rest = rest[(split + 1)..].TrimStart();
        if (id.Length == 0) return false;

        // A callback binding, not a link — there is nothing to navigate to.
        if (rest.StartsWith("call ", StringComparison.OrdinalIgnoreCase)) return false;

        if (rest.StartsWith("href", StringComparison.OrdinalIgnoreCase))
            rest = rest[4..].TrimStart();

        var quoted = QuotedParts(rest).ToList();
        if (quoted.Count == 0)
        {
            // Unquoted single-token form: `click id https://example.com`
            string bare = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (bare.Length == 0) return false;
            href = bare;
            return true;
        }

        href    = quoted[0];
        tooltip = quoted.Count > 1 ? quoted[1] : null;
        return href.Length > 0;
    }

    /// <summary>The double-quoted runs in a line, in order.</summary>
    private static IEnumerable<string> QuotedParts(string text)
    {
        int index = 0;
        while (index < text.Length)
        {
            int open = text.IndexOf('"', index);
            if (open < 0) yield break;

            int close = text.IndexOf('"', open + 1);
            if (close < 0) yield break;

            yield return text[(open + 1)..close];
            index = close + 1;
        }
    }
}
