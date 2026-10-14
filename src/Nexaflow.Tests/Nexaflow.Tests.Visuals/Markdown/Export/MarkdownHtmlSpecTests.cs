using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

using Nexaflow.IO.Documents;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Export;

namespace Nexaflow.Tests.Visuals.Markdown.Export;

/// <summary>
/// The HTML a document is written out as, held to the specifications' own: every example of CommonMark's specification and of
/// each extension's laid out, walked and written, and compared with the HTML the specification gives — written as a page that
/// runs scripts would have it, so a formula is the markup a typesetter reads. Through the HTML, this is what holds the
/// reading to the specifications: the blocks, the words, and what each extension makes of them.
///
/// <para>
/// Compared as CommonMark's own test runner compares, after normalising what no browser tells apart: the space between block
/// tags, the order of attributes, and how a character is escaped. An example written otherwise is a failure unless the
/// ledger beside this file (<c>markdown-html-differences.txt</c>) names it under a line saying why — a ratchet that only
/// shrinks: an example that comes right fails too, until it is taken out.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("markdown-export")]
public partial class MarkdownHtmlSpecTests
{
    private static string Ledger => Path.Combine(TestSampleData.RepoRoot, "src", "Nexaflow.Tests", "Nexaflow.Tests.Visuals", "Markdown", "Export",
                                                 "markdown-html-differences.txt");

    [TestMethod]
    public void EveryExampleIsWrittenAsItsSpecificationSays_ButWhereTheLedgerSays() => UiThread.Run(() =>
    {
        var writer = new HtmlDocumentWriter(new HtmlWriting { Scripts = true });
        var known = Known();
        var found = new List<string>();
        var report = new StringBuilder();

        foreach (var example in MarkdownSpecs.Names.SelectMany(MarkdownSpecs.Examples))
        {
            var written = writer.Written(LayoutExport.Exported(example.Markdown, new ExportOccasion()));
            if (Normalised(written) == Normalised(example.Html)) continue;

            found.Add(example.Id);
            report.Append("== ").Append(example.Id).Append(" (").Append(example.Section).Append(")\n")
                  .Append(example.Markdown).Append("-- expected\n").Append(example.Html).Append("-- written\n").Append(written).Append('\n');
        }

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "markdown-html-differences.found.txt"), report.ToString());

        var unexpected = found.Except(known).ToList();
        var mended = known.Except(found).ToList();

        Assert.AreEqual(0, unexpected.Count + mended.Count,
            $"written otherwise than its specification says, and not in the ledger: {string.Join(", ", unexpected)}\n"
            + $"in the ledger, and now written as its specification says — take them out: {string.Join(", ", mended)}\n"
            + $"every difference, with what was expected and what was written: {Path.Combine(AppContext.BaseDirectory, "markdown-html-differences.found.txt")}");
    });

    /// <summary>The examples the ledger names, each under a line saying why.</summary>
    private static HashSet<string> Known() =>
        File.Exists(Ledger)
            ? [.. File.ReadAllLines(Ledger).Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith('#'))]
            : [];

    // ── Normalising ─────────────────────────────────────────────────────────

    /// <summary>The tags whose surrounding space no browser renders — so space beside them is not compared.</summary>
    private static readonly HashSet<string> BlockTags =
    [
        "article", "header", "aside", "hgroup", "blockquote", "hr", "iframe", "body", "li", "map", "button", "object", "canvas", "ol",
        "caption", "output", "col", "p", "colgroup", "pre", "dd", "progress", "div", "section", "dl", "table", "td", "dt", "tbody",
        "embed", "textarea", "fieldset", "tfoot", "figcaption", "th", "figure", "thead", "footer", "tr", "form", "ul", "h1", "h2",
        "h3", "h4", "h5", "h6", "video", "script", "style",
    ];

    /// <summary>
    /// HTML as CommonMark's test runner compares it: every tag's name lower-cased and its attributes sorted, a self-closing tag
    /// the same as an open one, every character in text and attributes escaped one way, space in text outside <c>pre</c>
    /// collapsed, and the space beside a block tag gone.
    /// </summary>
    private static string Normalised(string html)
    {
        var said = new StringBuilder();
        var pre = 0;
        var lastBlock = true;

        foreach (Match piece in Pieces().Matches(html.Replace("\r\n", "\n")))
        {
            if (piece.Groups["tag"].Success)
            {
                var name = piece.Groups["name"].Value.ToLowerInvariant();
                var closing = piece.Groups["close"].Success;
                var block = BlockTags.Contains(name);

                if (block && said.Length > 0 && said[^1] == ' ') said.Length--;

                if (name == "pre") pre += closing ? -1 : 1;

                said.Append('<');
                if (closing) said.Append('/');
                said.Append(name);

                var attributes = Attributes().Matches(piece.Groups["attributes"].Value)
                    .Select(attribute => (Name: attribute.Groups["name"].Value.ToLowerInvariant(),
                                          Value: attribute.Groups["value"].Success ? Escaped(WebUtility.HtmlDecode(Unquoted(attribute.Groups["value"].Value))) : null))
                    .OrderBy(attribute => attribute.Name, StringComparer.Ordinal);

                foreach (var (attribute, value) in attributes)
                {
                    said.Append(' ').Append(attribute);
                    if (value is not null) said.Append("=\"").Append(value).Append('"');
                }

                said.Append('>');
                lastBlock = block;
                continue;
            }

            if (piece.Groups["comment"].Success)
            {
                said.Append(piece.Value);
                lastBlock = false;
                continue;
            }

            var text = Escaped(WebUtility.HtmlDecode(piece.Value));

            if (pre == 0)
            {
                text = Spaces().Replace(text, " ");
                if (lastBlock) text = text.TrimStart();
            }

            said.Append(text);
            lastBlock = false;
        }

        return said.ToString().Trim();
    }

    private static string Unquoted(string value) =>
        value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0] ? value[1..^1] : value;

    private static string Escaped(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    [GeneratedRegex(@"(?<comment><!--.*?-->|<![A-Z][^>]*>|<\?.*?\?>|<!\[CDATA\[.*?\]\]>)|(?<tag><(?<close>/)?(?<name>[A-Za-z][A-Za-z0-9-]*)(?<attributes>(?:\s+[^\s""'=<>`/]+(?:\s*=\s*(?:[^\s""'=<>`]+|'[^']*'|""[^""]*""))?)*)\s*/?>)|[^<]+|<", RegexOptions.Singleline)]
    private static partial Regex Pieces();

    [GeneratedRegex(@"(?<name>[^\s""'=<>`/]+)(?:\s*=\s*(?<value>[^\s""'=<>`]+|'[^']*'|""[^""]*""))?")]
    private static partial Regex Attributes();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
