using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Nexaflow.SiteGenerator;

/// <summary>A heading the page offers as a topic, and the anchor that reaches it.</summary>
internal sealed record Topic(string Id, string Text);

/// <summary>
/// Turns a help page into HTML. The pages are ordinary CommonMark but for three things the app understands and a
/// browser does not: <c>help:</c> links to another page, <c>locate:</c> links that lasso a control on screen, and
/// pictures stored beside the page. Each is rewritten before Markdig sees it, and only outside fenced samples —
/// the Markdown help pages are full of fences showing this very syntax, and those must survive as text.
/// </summary>
internal static class Render
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    /// <summary>The page's body, and its topics — the same list the app puts at the top of every help page.</summary>
    public static (string Html, IReadOnlyList<Topic> Topics) Page(HelpPage page, IReadOnlyDictionary<string, HelpPage> byTopic)
    {
        var document = Markdig.Markdown.Parse(Rewrite(page, byTopic), Pipeline);

        var topics = document.Descendants<HeadingBlock>()
            .Where(h => h.Level == 2)
            .Select(h => new Topic(h.GetAttributes().Id ?? string.Empty, TextOf(h.Inline)))
            .Where(t => t.Id.Length > 0)
            .ToList();

        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();

        return (writer.ToString(), topics);
    }

    private static string TextOf(ContainerInline? inline)
        => inline is null ? string.Empty : string.Concat(inline.Descendants<LiteralInline>().Select(l => l.Content.ToString()));

    /// <summary>A page's summary line as inline HTML, so its emphasis and code spans read as the page wrote them.</summary>
    public static string Inline(string markdown)
    {
        var html = Markdig.Markdown.ToHtml(markdown, Pipeline).Trim();

        return html.StartsWith("<p>", StringComparison.Ordinal) && html.EndsWith("</p>", StringComparison.Ordinal)
            ? html[3..^4]
            : html;
    }

    /// <summary>The same text with its emphasis markers taken off, for somewhere that can only hold plain text.</summary>
    public static string Plain(string markdown)
        => markdown.Replace("**", "").Replace("`", "").Replace("*", "");

    private static string Rewrite(HelpPage page, IReadOnlyDictionary<string, HelpPage> byTopic)
    {
        var body = new StringBuilder();
        string? fence = null;

        foreach (var line in page.Body.Split('\n'))
        {
            var run = FenceRun.Match(line.TrimStart());
            if (run.Success)
            {
                if (fence is null) fence = run.Value;
                else if (run.Value[0] == fence[0] && run.Value.Length >= fence.Length && line.Trim() == run.Value) fence = null;
                body.Append(line).Append('\n');
                continue;
            }

            body.Append(fence is null ? Links(line, page, byTopic) : line).Append('\n');
        }

        return body.ToString();
    }

    private static string Links(string line, HelpPage page, IReadOnlyDictionary<string, HelpPage> byTopic)
    {
        // "Show me" points at a control in the running app. On the web it cannot do anything, but dropping it
        // would lose the fact that the app can point the reader at the thing being described — so it stays,
        // inert, and says what it would have shown.
        line = LocateLink.Replace(line, m =>
        {
            var ids = string.Join(", ", m.Groups[2].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return $"<span class=\"locate\" title=\"In the app this points at {WebUtility.HtmlEncode(ids)}\">{m.Groups[1].Value}</span>";
        });

        line = HelpLink.Replace(line, m => Link(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, byTopic));
        line = FileLink.Replace(line, m => Link(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, byTopic));

        // A picture lives beside its page in the pack; on the site every pack's pictures are gathered under
        // help/_img/<project>/ so one file is stored once however many pages show it.
        line = ImageRef.Replace(line, m =>
            m.Groups[2].Value.Contains(':')
                ? m.Value                                                    // remote: left as the page wrote it
                : $"{m.Groups[1].Value}../_img/{page.Project}/{m.Groups[2].Value})");

        return line;
    }

    private static string Link(string text, string topic, string anchor, IReadOnlyDictionary<string, HelpPage> byTopic)
    {
        if (topic.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) topic = topic[..^3];
        topic = topic.Replace('\\', '/').Split('/')[^1];

        return byTopic.TryGetValue(topic, out var target)
            ? $"[{text}](../{target.Slug}/{anchor})"
            : text;   // the index, or a page the site does not carry: keep the words, drop the link
    }

    private static readonly Regex FenceRun = new(@"^(`{3,}|~{3,})", RegexOptions.Compiled);

    private static readonly Regex LocateLink =
        new(@"\[([^\]]*)\]\(\s*<?locate:([^)\s>]+)>?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HelpLink =
        new(@"\[([^\]]*)\]\(\s*<?help:([^)\s>#]+)(#[^)\s>]*)?>?\s*\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FileLink =
        new(@"\[([^\]]*)\]\(\s*<?(?!https?:)([A-Za-z0-9_.-]+\.md)(#[^)\s>]*)?>?\s*\)", RegexOptions.Compiled);

    private static readonly Regex ImageRef =
        new(@"(!\[[^\]]*\]\()\s*<?([^)\s>]+)>?\s*\)", RegexOptions.Compiled);
}
