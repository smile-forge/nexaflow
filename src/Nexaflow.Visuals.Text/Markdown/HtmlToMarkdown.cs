using System.Globalization;
using System.Text;
using HtmlAgilityPack;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// Converts an HTML fragment to markdown. Handles the common inline and block
/// constructs that show up on the clipboard / in drag payloads and in email bodies
/// (headings, emphasis, links, images, lists, code, quotes, tables). Unknown tags
/// degrade to their text content.
///
/// <para>
/// What a reader would not see is not converted: the head, styles and scripts, and anything its own style hides —
/// an email's preheader, a tracking pixel, a column kept for one mail client only. A table is converted as what it
/// is used for: one laying out a page reads as its content, in order, each piece once; only one holding data
/// becomes a markdown table.
/// </para>
///
/// Use <see cref="ConvertClipboardHtml"/> for the CF_HTML format that WPF puts on
/// the clipboard (it carries a byte-offset header before the markup).
/// </summary>
public static class HtmlToMarkdown
{
    /// <summary>Elements that are never content: what describes the page, styles it or runs in it.</summary>
    private static readonly HashSet<string> Unshown = new(StringComparer.OrdinalIgnoreCase)
    {
        "head", "title", "meta", "link", "style", "script", "template",
    };

    /// <summary>
    /// Elements that give a table cell structure of its own — so the table round it is laying a page out, not holding
    /// data. A paragraph or a division is not among them: a data cell's words are often wrapped in one.
    /// </summary>
    private static readonly HashSet<string> Structure = new(StringComparer.OrdinalIgnoreCase)
    {
        "table", "ul", "ol", "dl", "blockquote", "pre", "hr", "h1", "h2", "h3", "h4", "h5", "h6",
    };

    /// <summary>Elements that make blocks, which an inline element holding one has to be read as a container of.</summary>
    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "table", "ul", "ol", "dl", "blockquote", "pre", "hr", "h1", "h2", "h3", "h4", "h5", "h6",
        "p", "div", "section", "article", "main", "header", "footer", "nav", "aside", "center", "figure", "address", "form",
    };

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

    // ── What is not shown ────────────────────────────────────────────────────

    /// <summary>
    /// Whether a reader never sees this element: one that is never content, one marked <c>hidden</c>, or one whose own
    /// style takes it off the page — not displayed, invisible, transparent, kept for one mail client only (<c>mso-hide</c>),
    /// or squeezed to no height with its overflow cut off, which is how an email hides the line a mailbox previews.
    /// </summary>
    private static bool Hidden(HtmlNode node)
    {
        if (node.NodeType != HtmlNodeType.Element) return false;
        if (Unshown.Contains(node.Name) || node.Attributes.Contains("hidden")) return true;

        var style = node.GetAttributeValue("style", null);
        if (string.IsNullOrEmpty(style)) return false;

        var noHeight = false;
        var clipped = false;

        foreach (var (property, value) in Declarations(style))
        {
            switch (property)
            {
                case "display" when value == "none":
                case "visibility" when value is "hidden" or "collapse":
                case "mso-hide" when value == "all":
                case "opacity" when Length(value) == 0:
                    return true;

                case "max-height" or "height" when Length(value) == 0:
                    noHeight = true;
                    break;

                case "overflow" when value == "hidden":
                    clipped = true;
                    break;
            }
        }

        return noHeight && clipped;
    }

    /// <summary>A style attribute's declarations, each property and value lower-cased and without <c>!important</c>.</summary>
    private static IEnumerable<(string Property, string Value)> Declarations(string style)
    {
        foreach (var declaration in style.Split(';'))
        {
            var colon = declaration.IndexOf(':');
            if (colon <= 0) continue;

            var value = declaration[(colon + 1)..].Replace("!important", string.Empty, StringComparison.OrdinalIgnoreCase);

            yield return (declaration[..colon].Trim().ToLowerInvariant(), value.Trim().ToLowerInvariant());
        }
    }

    /// <summary>A CSS length or number as the number it starts with — <c>0</c>, <c>0px</c>, <c>1px</c> — or null where it is none.</summary>
    private static double? Length(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var text = value.Trim();
        var end = 0;
        while (end < text.Length && (char.IsAsciiDigit(text[end]) || text[end] == '.')) end++;

        return end > 0 && double.TryParse(text[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    /// <summary>
    /// Whether an image is too small to show anything: one declared a pixel or less each way, by its attributes or its
    /// style — the pixel a sender counts opens with, or a spacer.
    /// </summary>
    private static bool Pixel(HtmlNode image) => Declared(image) is { Width: <= 1, Height: <= 1 };

    /// <summary>
    /// The size an image is declared to be shown at, in pixels — by its <c>width</c> and <c>height</c>, or by its style, which
    /// wins. A size in anything but pixels (<c>100%</c>, <c>20em</c>) says nothing about the picture's own size and is not read.
    /// </summary>
    private static (double? Width, double? Height) Declared(HtmlNode image)
    {
        var width = Pixels(image.GetAttributeValue("width", null));
        var height = Pixels(image.GetAttributeValue("height", null));

        if (image.GetAttributeValue("style", null) is { Length: > 0 } style)
        {
            foreach (var (property, value) in Declarations(style))
            {
                if (property == "width") width = Pixels(value) ?? width;
                else if (property == "height") height = Pixels(value) ?? height;
            }
        }

        return (width, height);
    }

    /// <summary>A length in pixels — <c>120</c> or <c>120px</c> — or null for one in any other unit, or none.</summary>
    private static double? Pixels(string? value)
    {
        if (Length(value) is not { } number) return null;

        var unit = value!.Trim().TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.').Trim();

        return unit.Length == 0 || unit.Equals("px", StringComparison.OrdinalIgnoreCase) ? number : null;
    }

    /// <summary>
    /// An image's words for markdown, with the size it is shown at after a bar where its width is declared — written the way
    /// <see cref="MarkdownPictureSize"/> reads it, so the picture is drawn the size the sender laid it
    /// out at. Words that would read as a size of their own have their bar escaped.
    /// </summary>
    private static string Described(HtmlNode image)
    {
        var alt = NormalizeWhitespace(HtmlEntity.DeEntitize(image.GetAttributeValue("alt", "")) ?? string.Empty).Trim();
        if (MarkdownPictureSize.EndsLikeOne(alt)) alt = alt.Insert(alt.LastIndexOf('|'), @"\");

        var (width, height) = Declared(image);
        if (width is not > 0) return alt;

        return height is > 0 ? $"{alt}|{Number(width.Value)}x{Number(height.Value)}" : $"{alt}|{Number(width.Value)}";
    }

    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Whether an element holds any of <paramref name="kinds"/> anywhere under it that a reader would see.</summary>
    private static bool Holds(HtmlNode node, HashSet<string> kinds)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType != HtmlNodeType.Element || Hidden(child)) continue;
            if (kinds.Contains(child.Name) || Holds(child, kinds)) return true;
        }

        return false;
    }

    // ── Block-level ──────────────────────────────────────────────────────────

    private static void WriteBlock(HtmlNode node, StringBuilder sb, int listDepth)
    {
        if (Hidden(node)) return;

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
                WriteTable(node, sb, listDepth);
                EndBlock(sb);
                break;

            case "div": case "section": case "article": case "main": case "header":
            case "footer": case "nav": case "aside": case "center": case "figure": case "address": case "form":
            case "tbody": case "thead": case "tfoot": case "html": case "body":
                foreach (var c in node.ChildNodes) WriteBlock(c, sb, listDepth);
                break;

            default:
                // An inline element wrapped round blocks — a span round two paragraphs, a font round a table — is read as the
                // blocks it holds; flattened into one line they would lose every break between them. Not a link, which is one
                // thing to follow however much it holds, and would lose where it leads.
                if (!node.Name.Equals("a", StringComparison.OrdinalIgnoreCase) && Holds(node, Blocks))
                    foreach (var c in node.ChildNodes) WriteBlock(c, sb, listDepth);
                else
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
            if (!li.Name.Equals("li", StringComparison.OrdinalIgnoreCase) || Hidden(li)) continue;

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
                if (c.Name is "ul" or "ol" && !Hidden(c))
                    WriteList(c, sb, depth + 1);

            index++;
        }
    }

    /// <summary>
    /// A table, as what it is used for. Only its own rows and cells are read — a table inside a cell is that cell's
    /// content, read once, where it stands.
    /// </summary>
    private static void WriteTable(HtmlNode table, StringBuilder sb, int listDepth)
    {
        var rows = OwnRows(table)
            .Select(row => row.ChildNodes.Where(cell => cell.Name is "td" or "th" && !Hidden(cell)).ToList())
            .Where(cells => cells.Count > 0)
            .ToList();
        if (rows.Count == 0) return;

        if (HoldsData(table, rows))
        {
            WriteGrid(rows, sb);
            return;
        }

        // Laying a page out: each cell's content in order, as the blocks it is.
        foreach (var cell in rows.SelectMany(cells => cells))
        {
            foreach (var child in cell.ChildNodes) WriteBlock(child, sb, listDepth);
            EndBlock(sb);
        }
    }

    /// <summary>A table's own rows — straight under it, or under its head, body or foot — and none of a table inside one of its cells.</summary>
    private static IEnumerable<HtmlNode> OwnRows(HtmlNode table)
    {
        foreach (var child in table.ChildNodes)
        {
            if (Hidden(child)) continue;

            if (child.Name == "tr") yield return child;
            else if (child.Name is "thead" or "tbody" or "tfoot")
                foreach (var row in child.ChildNodes)
                    if (row.Name == "tr" && !Hidden(row)) yield return row;
        }
    }

    /// <summary>
    /// Whether a table holds data rather than laying a page out. Never one that says it is presentation, nor one with a
    /// cell holding structure of its own — a table, a list, a heading. Otherwise one with a caption, or with at least two
    /// rows of at least two columns: a single row or column is a stack of content, however it is marked up.
    /// </summary>
    private static bool HoldsData(HtmlNode table, List<List<HtmlNode>> rows)
    {
        if (table.GetAttributeValue("role", null) is { } role && (role.Equals("presentation", StringComparison.OrdinalIgnoreCase)
                                                                  || role.Equals("none", StringComparison.OrdinalIgnoreCase)))
            return false;

        if (rows.Any(cells => cells.Any(cell => Holds(cell, Structure)))) return false;

        if (table.ChildNodes.Any(child => child.Name == "caption" && !Hidden(child))) return true;

        return rows.Count >= 2 && rows.Max(cells => cells.Count) >= 2;
    }

    /// <summary>A data table as a markdown one: its first row names the columns.</summary>
    private static void WriteGrid(List<List<HtmlNode>> rows, StringBuilder sb)
    {
        // A markdown cell is one line, so whatever breaks a cell's words over lines is read as the space between them. A
        // cell spanning columns or rows stands in the first place it spans and leaves the others empty, so every row
        // keeps its columns.
        var matrix = rows.Select(_ => new List<string?>()).ToList();
        for (int r = 0; r < rows.Count; r++)
        {
            int c = 0;
            foreach (var cell in rows[r])
            {
                while (c < matrix[r].Count && matrix[r][c] is not null) c++;
                int across = Math.Clamp(cell.GetAttributeValue("colspan", 1), 1, 1000);
                int down = Math.Clamp(cell.GetAttributeValue("rowspan", 1), 1, rows.Count - r);
                string text = NormalizeWhitespace(InlineChildren(cell)).Trim().Replace("|", "\\|");
                for (int dr = 0; dr < down; dr++)
                {
                    var row = matrix[r + dr];
                    while (row.Count < c + across) row.Add(null);
                    for (int dc = 0; dc < across; dc++) row[c + dc] = dr == 0 && dc == 0 ? text : string.Empty;
                }
                c += across;
            }
        }

        int cols = matrix.Max(r => r.Count);
        var grid = matrix.Select(r => Enumerable.Range(0, cols).Select(i => i < r.Count ? r[i] ?? string.Empty : string.Empty).ToList()).ToList();

        sb.Append("| ").Append(string.Join(" | ", grid[0])).Append(" |\n");
        sb.Append("| ").Append(string.Join(" | ", Enumerable.Repeat("---", cols))).Append(" |\n");
        foreach (var r in grid.Skip(1))
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
        if (Hidden(node)) return string.Empty;

        switch (node.Name.ToLowerInvariant())
        {
            case "#text":
                return Visible(NormalizeWhitespace(HtmlEntity.DeEntitize(node.InnerText) ?? string.Empty));

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
                return string.IsNullOrEmpty(src) || Pixel(node) ? string.Empty : $"![{Described(node)}]({src})";

            // Blocks met inside a line keep a space either side, so the words of two of them never run together.
            case "p": case "div": case "li": case "td": case "th": case "tr": case "table":
                return $" {InlineChildren(node)} ";

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

    /// <summary>
    /// Text with every word made of nothing a reader can see taken out — the zero-width joiners and non-joiners an email
    /// pads its preview line with. A character like that inside a word is the word's own, and stays.
    /// </summary>
    private static string Visible(string text)
    {
        if (!text.Any(Invisible)) return text;

        var words = text.Split(' ').Where(word => word.Length == 0 || !word.All(Invisible));

        return NormalizeWhitespace(string.Join(' ', words));
    }

    /// <summary>A character that is drawn as nothing: a formatting mark, or the grapheme joiner that combines with none.</summary>
    private static bool Invisible(char character) =>
        CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format || character == '\u034F';

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
