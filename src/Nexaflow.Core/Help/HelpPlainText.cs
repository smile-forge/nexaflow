using System.Text;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Core.Help;

/// <summary>
/// A help page as the text a reader sees: one line per paragraph, heading, table cell and code line — the units the open
/// page's highlight matches a query against — so the Nth match the search index finds is, as near as the renderer allows, the
/// Nth one the page highlights. What renders as a picture (diagrams, formulas, scores, images) and what never renders (link
/// targets, HTML, front matter) is left out.
///
/// <para>
/// Read off the reading, not the layout: a search finds words, which the reading holds as they are shown, and a page is
/// indexed off the window's thread, where nothing can be laid out.
/// </para>
/// </summary>
internal static class HelpPlainText
{
    // Fences nothing has registered a language for that still render as something other than their text — the
    // maths the renderer typesets. A registered language answers for itself.
    private static readonly HashSet<string> TypesetFences = new(StringComparer.OrdinalIgnoreCase) { "math", "latex", "tex" };

    public static string Extract(string? markdown)
    {
        var sb = new StringBuilder();
        Blocks(ContentPart.Of(MarkdownParser.Worked(markdown ?? "")), sb);
        return sb.ToString();
    }

    private static void Blocks(ContentPart holder, StringBuilder sb)
    {
        foreach (var part in holder.Children)
        {
            if (part.Derived || part.Role == Roles.Trivia) continue;

            switch (part.Kind)
            {
                case MarkdownKinds.FrontMatter or MarkdownKinds.Html or MarkdownKinds.Reference or MarkdownKinds.Task or MarkdownKinds.Marker:
                    continue;

                // Code renders as the characters it is — unless its language draws a picture of what it says.
                case MarkdownKinds.Code:
                    Line(sb, Written(part));
                    continue;

                case Kinds.Block:
                    if (!IsTypeset((part.Node as BlockNode)?.Language)) Line(sb, Written(part));
                    continue;
            }

            // A block of words is a line of them; anything else is the blocks it holds.
            if (part.Part(Roles.Body) is { Kind: Kinds.Words } words) Line(sb, MarkdownProse.Words(words));
            else Blocks(part.Part(Roles.Body) ?? part, sb);
        }
    }

    /// <summary>
    /// Whether a fence draws as a picture rather than as its own words. A registered language says so itself —
    /// code colours what was typed and still shows it, a chart does not — and what none of them claims is
    /// only typeset if it is on the short list above.
    /// </summary>
    private static bool IsTypeset(string? info)
        => !string.IsNullOrWhiteSpace(info)
           && (ContentLanguages.For(info) is { } language && !language.Editing.ShowsWhatWasWritten
               || TypesetFences.Contains(info.Trim().Split(' ')[0]));

    /// <summary>What a block holds as written: its lines, without what a container put in front of each.</summary>
    private static string Written(ContentPart block)
    {
        if (block.Part(Roles.Body) is not { } body) return string.Empty;

        var sb = new StringBuilder();
        foreach (var leaf in body.Node.Leaves())
            if (leaf.Role != Roles.Trivia) sb.Append(leaf.Text);

        return sb.ToString();
    }

    private static void Line(StringBuilder sb, string text)
    {
        foreach (var line in text.Split('\n'))
            if (line.Trim() is { Length: > 0 } trimmed)
                sb.Append(trimmed).Append('\n');
    }
}
