using System.Net;
using System.Text;

using Nexaflow.IO.Documents;
using Nexaflow.Markdown.Export;
using Nexaflow.Visuals.Text.Markdown.Export;

namespace Nexaflow.SiteGenerator;

/// <summary>A heading the page offers as a topic, and the anchor that reaches it.</summary>
internal sealed record Topic(string Id, string Text);

/// <summary>
/// Turns a help page into HTML: laid out as the app lays it, walked and written (<see cref="LayoutExport"/>), so the site
/// renders what the app renders. The pages are ordinary markdown but for three things the app understands and a browser
/// does not: <c>help:</c> links to another page, <c>locate:</c> links that lasso a control on screen, and pictures stored
/// beside the page. Each is what the page's writer makes of a link or a picture (<see cref="PageHtml"/>) — so a fence
/// showing this very syntax, which holds no link, is left as the code it is.
/// </summary>
internal static class Render
{
    /// <summary>
    /// What a page is laid out for: the width of the site's column, on a white page, and whatever another language draws kept
    /// as the picture it is drawn as, twice as fine as it is laid so it stays sharp on a fine screen.
    /// </summary>
    private static readonly ExportOccasion Occasion = new() { Width = 760, Pictures = true, Scale = 2 };

    private static readonly HtmlDocumentWriter Html = new(new HtmlWriting { Anchors = true });

    private static readonly TextDocumentWriter Text = new();

    /// <summary>The page's body, and its topics — the same list the app puts at the top of every help page.</summary>
    public static (string Html, IReadOnlyList<Topic> Topics) Page(HelpPage page, IReadOnlyDictionary<string, HelpPage> byTopic)
    {
        var document = LayoutExport.Exported(page.Body, Occasion);

        var topics = document.Blocks
            .OfType<ExportHeading>()
            .Where(heading => heading.Rank == 2 && heading.Anchor is { Length: > 0 })
            .Select(heading => new Topic(heading.Anchor!, Text.Written(heading.Words)))
            .ToList();

        return (new PageHtml(page, byTopic).Written(document), topics);
    }

    /// <summary>A page's summary line as inline HTML, so its emphasis and code spans read as the page wrote them.</summary>
    public static string Inline(string markdown) =>
        LayoutExport.Exported(markdown, Occasion) is { Blocks: [ExportParagraph only] } ? Html.Written(only.Words) : Loose(markdown).Trim();

    /// <summary>The same text with its marks taken off, on one line, for somewhere that can only hold plain text.</summary>
    public static string Plain(string markdown) =>
        Text.Written(LayoutExport.Exported(markdown, Occasion)).Trim().Replace('\n', ' ');

    /// <summary>A block of markdown as HTML — a section's opening prose, which carries no app link schemes.</summary>
    public static string Loose(string markdown) => Html.Written(LayoutExport.Exported(markdown, Occasion));

    /// <summary>
    /// A help page's HTML, with what the app means by its links and pictures said as the site says it: another page is the
    /// site's page for it, a control on screen is named rather than pointed at, and a picture stored beside the page is where
    /// the site gathers every pack's pictures.
    /// </summary>
    private sealed class PageHtml(HelpPage page, IReadOnlyDictionary<string, HelpPage> byTopic) : HtmlDocumentWriter(new HtmlWriting { Anchors = true })
    {
        protected override void WriteLink(ExportLink link, StringBuilder html)
        {
            var where = link.Destination;

            // "Show me" points at a control in the running app. On the web it cannot do anything, but dropping it
            // would lose the fact that the app can point the reader at the thing being described — so it stays,
            // inert, and says what it would have shown.
            if (where.StartsWith("locate:", StringComparison.OrdinalIgnoreCase))
            {
                var ids = string.Join(", ", where["locate:".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

                html.Append("<span class=\"locate\" title=\"In the app this points at ").Append(WebUtility.HtmlEncode(ids)).Append("\">");
                this.WriteWords(link.Words, html);
                html.Append("</span>");
                return;
            }

            if (Paged(where) is { } topic)
            {
                // The index, or a page the site does not carry: keep the words, drop the link.
                if (byTopic.TryGetValue(topic.Name, out var target))
                    base.WriteLink(link with { Destination = $"../{target.Slug}/{topic.Anchor}" }, html);
                else
                    this.WriteWords(link.Words, html);

                return;
            }

            base.WriteLink(link, html);
        }

        // A picture lives beside its page in the pack; on the site every pack's pictures are gathered under
        // help/_img/<project>/ so one file is stored once however many pages show it. A remote one is left as the page wrote it.
        protected override string Source(ExportImage image) =>
            image.Source.Contains(':', StringComparison.Ordinal) ? image.Source : $"../_img/{page.Project}/{image.Source}";

        /// <summary>The help page a link names — by <c>help:</c>, or as the file it is in — and where in it, or null for any other link.</summary>
        private static (string Name, string Anchor)? Paged(string where)
        {
            var hash = where.IndexOf('#');
            var (path, anchor) = hash < 0 ? (where, string.Empty) : (where[..hash], where[hash..]);

            if (path.StartsWith("help:", StringComparison.OrdinalIgnoreCase)) path = path["help:".Length..];
            else if (path.Contains(':', StringComparison.Ordinal) || !path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return null;

            if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) path = path[..^3];

            return (path.Replace('\\', '/').Split('/')[^1], anchor);
        }
    }
}
