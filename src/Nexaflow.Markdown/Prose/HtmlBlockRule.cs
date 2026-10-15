using System.Text.RegularExpressions;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// Raw HTML, in CommonMark's seven ways of starting it — and so of ending it: the first five at the line holding what
/// closes what opened them, the last two at a blank line. The seventh, a tag nothing else claims, cannot interrupt a
/// paragraph.
/// </summary>
internal sealed partial class HtmlBlockRule : MarkdownBlockRule
{
    private static readonly string[] Ends = ["", "", "-->", "?>", ">", "]]>"];

    private static readonly HashSet<string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        "address", "article", "aside", "base", "basefont", "blockquote", "body", "caption", "center", "col", "colgroup",
        "dd", "details", "dialog", "dir", "div", "dl", "dt", "fieldset", "figcaption", "figure", "footer", "form", "frame",
        "frameset", "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hr", "html", "iframe", "legend", "li", "link",
        "main", "menu", "menuitem", "nav", "noframes", "ol", "optgroup", "option", "p", "param", "search", "section",
        "summary", "table", "tbody", "td", "tfoot", "th", "thead", "title", "tr", "track", "ul",
    };

    private static readonly string[] Raw = ["pre", "script", "style", "textarea"];

    public override bool TakesLines => true;

    /// <summary>Where each line's markup starts, past its containers' marks.</summary>
    public override bool KeepsLines => true;

    public override bool MayStart(char first) => first == '<';

    public override Started TryStart(MarkdownScanner scan, ScannedBlock container)
    {
        if (scan.Indented || scan.Peek(scan.NextNonspace) != '<') return Started.No;

        var line = scan.Text.AsSpan(scan.NextNonspace, scan.Line.End - scan.NextNonspace);
        var type = TypeOf(line);

        if (type == 0) return Started.No;

        // A tag on its own cannot break into a paragraph, lazy or not.
        if (type == 7 && (container.Kind == ScannedKind.Paragraph || (!scan.AllMatched && !scan.Blank && scan.Tip.Kind == ScannedKind.Paragraph)))
            return Started.No;

        scan.Open(new ScannedHtml(this, scan.NextNonspace, type) { Line = scan.LineIndex });

        return Started.Leaf;
    }

    public override Continued Continues(MarkdownScanner scan, ScannedBlock block) =>
        scan.Blank && ((ScannedHtml)block).Type >= 6 ? Continued.Unmatched : Continued.Matched;

    public override void Added(MarkdownScanner scan, ScannedBlock block)
    {
        var html = (ScannedHtml)block;
        if (html.Type > 5) return;

        if (html.Taken is not var (from, to)) return;
        var line = scan.Text.AsSpan(from, to - from);

        var ended = false;

        if (html.Type == 1)
        {
            foreach (var name in Raw)
                ended |= line.Contains($"</{name}>", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            ended = line.Contains(Ends[html.Type], StringComparison.Ordinal);
        }

        // The line that opens it can close it too.
        if (ended) scan.Close(html);
    }

    /// <summary>Which of the seven ways the line starts raw HTML, or nought where it does not.</summary>
    private static int TypeOf(ReadOnlySpan<char> line)
    {
        foreach (var name in Raw)
            if (line.Length > name.Length && line[1..].StartsWith(name, StringComparison.OrdinalIgnoreCase)
                && (line.Length == name.Length + 1 || line[name.Length + 1] is ' ' or '\t' or '>'))
                return 1;

        if (line.StartsWith("<!--", StringComparison.Ordinal)) return 2;
        if (line.StartsWith("<?", StringComparison.Ordinal)) return 3;
        if (line.Length > 2 && line[1] == '!' && char.IsAsciiLetter(line[2])) return 4;
        if (line.StartsWith("<![CDATA[", StringComparison.Ordinal)) return 5;

        var name6 = line[1..];
        if (name6.StartsWith("/")) name6 = name6[1..];

        var length = 0;
        while (length < name6.Length && char.IsAsciiLetterOrDigit(name6[length])) length++;

        if (length > 0 && Named.Contains(name6[..length].ToString())
            && (length == name6.Length || name6[length] is ' ' or '\t' or '>' || name6[length..].StartsWith("/>")))
            return 6;

        return Tag().IsMatch(line) ? 7 : 0;
    }

    /// <summary>A whole opening or closing tag, and nothing after it on the line but space.</summary>
    [GeneratedRegex("""^(?:<[A-Za-z][A-Za-z0-9-]*(?:\s+[A-Za-z_:][A-Za-z0-9_.:-]*(?:\s*=\s*(?:[^"'=<>`\s]+|'[^']*'|"[^"]*"))?)*\s*/?>|</[A-Za-z][A-Za-z0-9-]*\s*>)\s*$""")]
    private static partial Regex Tag();
}
