using System.Text;
using HtmlAgilityPack;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Converts an HTML fragment to markdown. Handles the common inline and block
/// constructs that show up on the clipboard / in drag payloads (headings,
/// emphasis, links, images, lists, code, quotes, tables-as-text). Unknown tags
/// degrade to their text content.
///
/// Use <see cref="ConvertClipboardHtml"/> for the CF_HTML format that WPF puts on
/// the clipboard (it carries a byte-offset header before the markup).
/// </summary>
public static class HtmlToMarkdown
{
    /// <summary>Strips the CF_HTML header (if present) and converts the fragment.</summary>
    public static string ConvertClipboardHtml(string cfHtml)
        => Convert(StripCfHtmlHeader(cfHtml));

    public static string Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var sb = new StringBuilder();
        var root = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;
        foreach (var child in root.ChildNodes)
            WriteBlock(child, sb, listDepth: 0);

        return Collapse(sb.ToString());
    }

    // ── CF_HTML header ──────────────────────────────────────────────────────

    private static string StripCfHtmlHeader(string cfHtml)
    {
        // CF_HTML wraps the markup in StartFragment/EndFragment comment markers.
        int start = cfHtml.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        int end   = cfHtml.IndexOf("<!--EndFragment-->",   StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start)
            return cfHtml.Substring(start + "<!--StartFragment-->".Length,
                                    end - start - "<!--StartFragment-->".Length);

        // Fall back to everything from the first real tag onward.
        int html = cfHtml.IndexOf('<');
        return html >= 0 ? cfHtml[html..] : cfHtml;
    }

    // ── Block-level ──────────────────────────────────────────────────────────

    private static void WriteBlock(HtmlNode node, StringBuilder sb, int listDepth)
    {
        switch (node.Name.ToLowerInvariant())
        {
            case "#text":
                var text = Inline(node);
                if (!string.IsNullOrWhiteSpace(text)) sb.Append(text);
                break;

            case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                int level = node.Name[1] - '0';
                EndBlock(sb);
                sb.Append(new string('#', level)).Append(' ').Append(InlineChildren(node).Trim());
                EndBlock(sb);
                break;

            case "p":
                EndBlock(sb);
                sb.Append(InlineChildren(node).Trim());
                EndBlock(sb);
                break;

            case "br":
                sb.Append("  \n");
                break;

            case "hr":
                EndBlock(sb);
                sb.Append("---");
                EndBlock(sb);
                break;

            case "blockquote":
                EndBlock(sb);
                var inner = new StringBuilder();
                foreach (var c in node.ChildNodes) WriteBlock(c, inner, listDepth);
                foreach (var line in Collapse(inner.ToString()).Split('\n'))
                    sb.Append("> ").Append(line).Append('\n');
                EndBlock(sb);
                break;

            case "pre":
                EndBlock(sb);
                var code = (HtmlEntity.DeEntitize(node.InnerText) ?? string.Empty).TrimEnd('\n');
                sb.Append("```\n").Append(code).Append("\n```");
                EndBlock(sb);
                break;

            case "ul": case "ol":
                EndBlock(sb);
                WriteList(node, sb, listDepth);
                EndBlock(sb);
                break;

            case "table":
                EndBlock(sb);
                WriteTable(node, sb);
                EndBlock(sb);
                break;

            case "div": case "section": case "article": case "main": case "header":
            case "footer": case "tbody": case "thead": case "html": case "body":
                foreach (var c in node.ChildNodes) WriteBlock(c, sb, listDepth);
                break;

            default:
                // Inline-level element appearing at block scope, or unknown tag.
                sb.Append(Inline(node));
                break;
        }
    }

    private static void WriteList(HtmlNode list, StringBuilder sb, int depth)
    {
        bool ordered = list.Name.Equals("ol", StringComparison.OrdinalIgnoreCase);
        int index = 1;
        var indent = new string(' ', depth * 2);

        foreach (var li in list.ChildNodes)
        {
            if (!li.Name.Equals("li", StringComparison.OrdinalIgnoreCase)) continue;

            var marker = ordered ? $"{index}. " : "- ";
            sb.Append(indent).Append(marker);

            // Inline content of the item (everything that isn't a nested list).
            var content = new StringBuilder();
            foreach (var c in li.ChildNodes)
            {
                if (c.Name is "ul" or "ol") continue;
                content.Append(Inline(c));
            }
            sb.Append(content.ToString().Trim()).Append('\n');

            // Nested lists.
            foreach (var c in li.ChildNodes)
                if (c.Name is "ul" or "ol")
                    WriteList(c, sb, depth + 1);

            index++;
        }
    }

    private static void WriteTable(HtmlNode table, StringBuilder sb)
    {
        var rows = table.Descendants("tr").ToList();
        if (rows.Count == 0) return;

        var matrix = rows
            .Select(r => r.ChildNodes
                .Where(c => c.Name is "td" or "th")
                .Select(c => InlineChildren(c).Trim().Replace("|", "\\|"))
                .ToList())
            .Where(r => r.Count > 0)
            .ToList();
        if (matrix.Count == 0) return;

        int cols = matrix.Max(r => r.Count);
        foreach (var r in matrix) while (r.Count < cols) r.Add(string.Empty);

        sb.Append("| ").Append(string.Join(" | ", matrix[0])).Append(" |\n");
        sb.Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", cols))).Append(" |\n");
        foreach (var r in matrix.Skip(1))
            sb.Append("| ").Append(string.Join(" | ", r)).Append(" |\n");
    }

    // ── Inline-level ─────────────────────────────────────────────────────────

    private static string InlineChildren(HtmlNode node)
    {
        var sb = new StringBuilder();
        foreach (var c in node.ChildNodes) sb.Append(Inline(c));
        return sb.ToString();
    }

    private static string Inline(HtmlNode node)
    {
        switch (node.Name.ToLowerInvariant())
        {
            case "#text":
                return NormalizeWhitespace(HtmlEntity.DeEntitize(node.InnerText) ?? string.Empty);

            case "strong": case "b":
                return $"**{InlineChildren(node).Trim()}**";

            case "em": case "i":
                return $"*{InlineChildren(node).Trim()}*";

            case "del": case "s": case "strike":
                return $"~~{InlineChildren(node).Trim()}~~";

            case "code":
                return $"`{HtmlEntity.DeEntitize(node.InnerText) ?? string.Empty}`";

            case "br":
                return "  \n";

            case "a":
                var href = node.GetAttributeValue("href", "");
                var label = InlineChildren(node).Trim();
                if (string.IsNullOrEmpty(label)) label = href;
                return string.IsNullOrEmpty(href) ? label : $"[{label}]({href})";

            case "img":
                var src = node.GetAttributeValue("src", "");
                var alt = node.GetAttributeValue("alt", "");
                return string.IsNullOrEmpty(src) ? string.Empty : $"![{alt}]({src})";

            // Block-ish tags that can show up inline — keep their text flowing.
            case "p": case "div": case "span": case "li": case "td": case "th":
            default:
                return InlineChildren(node);
        }
    }

    // ── Whitespace helpers ────────────────────────────────────────────────────

    private static string NormalizeWhitespace(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        bool lastSpace = false;
        foreach (var ch in s)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
            }
            else { sb.Append(ch); lastSpace = false; }
        }
        return sb.ToString();
    }

    private static void EndBlock(StringBuilder sb)
    {
        if (sb.Length == 0) return;
        while (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
        sb.Append('\n');
    }

    /// <summary>Collapse 3+ blank lines to one and trim outer whitespace.</summary>
    private static string Collapse(string s)
    {
        var lines = s.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        int blanks = 0;
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                if (++blanks <= 1) sb.Append('\n');
            }
            else { blanks = 0; sb.Append(line.TrimEnd()).Append('\n'); }
        }
        return sb.ToString().Trim();
    }
}
