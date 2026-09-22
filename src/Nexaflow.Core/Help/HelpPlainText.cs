using System.Text;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Yaml;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Core.Help;

/// <summary>
/// A help page as the text a reader sees: one line per rendered paragraph, heading, table cell and code line — the
/// units the open page's highlight matches a query against — so the Nth match the search index finds is, as near as
/// the renderer allows, the Nth one the page highlights. What renders as a picture (diagrams, formulas, scores,
/// images) and what never renders (link targets, HTML, front matter) is left out.
/// </summary>
internal static class HelpPlainText
{
    // Fences nothing has registered a language for that still render as something other than their text — the
    // maths the renderer typesets. A registered language answers for itself.
    private static readonly HashSet<string> TypesetFences = new(StringComparer.OrdinalIgnoreCase) { "math", "latex", "tex" };

    public static string Extract(string? markdown)
    {
        var sb = new StringBuilder();
        foreach (var block in Markdig.Markdown.Parse(markdown ?? "", MarkdownParser.Pipeline))
            Append(block, sb);
        return sb.ToString();
    }

    private static void Append(Block block, StringBuilder sb)
    {
        switch (block)
        {
            case YamlFrontMatterBlock:
            case HtmlBlock:
            case MathBlock:
                return;
            case FencedCodeBlock fenced when IsTypeset(fenced.Info):
                return;
            case CodeBlock code:
                for (var i = 0; i < code.Lines.Count; i++)
                    Line(sb, code.Lines.Lines[i].ToString());
                return;
            case LeafBlock { Inline: { } inline }:
                Line(sb, InlineText(inline));
                return;
            case ContainerBlock container:
                foreach (var child in container)
                    Append(child, sb);
                return;
        }
    }

    /// <summary>
    /// Whether a fence draws as a picture rather than as its own words. A registered language says so itself —
    /// code colours what was typed and still shows it, a chart does not — and what none of them claims is
    /// only typeset if it is on the short list above.
    /// </summary>
    private static bool IsTypeset(string? info)
        => !string.IsNullOrWhiteSpace(info)
           && (ContentLanguages.For(info) is { ShowsWhatWasWritten: false }
               || TypesetFences.Contains(info.Trim().Split(' ')[0]));

    private static void Line(StringBuilder sb, string text)
    {
        foreach (var line in text.Split('\n'))
            if (line.Trim() is { Length: > 0 } trimmed)
                sb.Append(trimmed).Append('\n');
    }

    private static string InlineText(ContainerInline root)
    {
        var sb = new StringBuilder();
        Walk(root);
        return sb.ToString();

        void Walk(Inline inline)
        {
            switch (inline)
            {
                case LiteralInline literal:            sb.Append(literal.Content.ToString()); break;
                case CodeInline code:                  sb.Append(code.Content); break;
                case LineBreakInline lineBreak:        sb.Append(lineBreak.IsHard ? '\n' : ' '); break;
                case HtmlEntityInline entity:          sb.Append(entity.Transcoded.ToString()); break;
                case LinkInline { IsImage: true }:     break;                       // a picture, not text
                case AutolinkInline autolink:          sb.Append(autolink.Url); break;   // its URL is its label
                case MathInline:                       break;                       // typeset, not text
                case HtmlInline:                       break;
                case ContainerInline container:        foreach (var child in container) Walk(child); break;
            }
        }
    }
}
