using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Markdown.Layout.Prose;

/// <summary>
/// The room a markdown document leaves between two of its blocks — what the document's builder leaves between blocks it lays, and
/// what the engine leaves at the seam between two units of a long document laid apart, so the two are the same gap.
/// </summary>
public static class MarkdownGaps
{
    /// <summary>
    /// The room between two blocks written one after the other, set in <paramref name="style"/>: what the look of the one above asks
    /// for after it and the look of the one below before it — or the usual gap between paragraphs, where neither asks.
    /// </summary>
    public static double Between(ContentPart above, ContentPart below, LayoutStyle style)
    {
        var after = (above.Node.HeldAs(MarkdownRoles.Typeset) as ParagraphLook)?.After;
        var before = (below.Node.HeldAs(MarkdownRoles.Typeset) as ParagraphLook)?.Before;

        return after is null && before is null ? Usual(style) : ((after ?? 0) + (before ?? 0)) * style.TextSize;
    }

    /// <summary>The usual gap between two paragraphs, in <paramref name="style"/>.</summary>
    public static double Usual(LayoutStyle style) => style.TextSize * 0.72;
}
