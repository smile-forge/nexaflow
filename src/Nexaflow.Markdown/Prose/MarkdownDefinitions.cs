using Markdig.Extensions.Abbreviations;
using Markdig.Syntax;

using Nexaflow.Markdown.Ast;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// What a document defines for the words written anywhere in it: where a <c>[text][name]</c> link goes, and what an
/// abbreviation stands for — each written on a line of its own, often blocks away from the words that use it.
///
/// <para>
/// A block's words are read on their own, so a definition three paragraphs up is not in front of the reader when a
/// sentence is read. The reading of the whole document is the one that sees every definition, so it writes them down
/// here, and each block's words are read with them beside it (<see cref="MarkdownBlocks"/>). They are the lines as
/// written — the reader of a block is told them the way it would have found them — and nothing of them is copied into
/// a block: they sit beside what is read, never in it.
/// </para>
/// </summary>
/// <param name="Text">Every definition line in the document, as written, one to a line.</param>
public sealed record MarkdownDefinitions(string Text)
{
    /// <summary>The definitions a document's reading hung on it, or null where it defines nothing.</summary>
    public static MarkdownDefinitions? Of(ContentNode document)
    {
        foreach (var child in document.Children)
            if (child.IsDerived && child.Kind == MarkdownKinds.Definitions)
                foreach (var held in child.Children)
                    if (held.Held is MarkdownDefinitions definitions) return definitions;

        return null;
    }

    /// <summary>
    /// Every definition <paramref name="document"/> was read to hold that starts before <paramref name="before"/>, cut from
    /// <paramref name="source"/> in the order they were written — so the definitions of two stretches of a document read one
    /// after the other join into what the whole document's reading says (<see cref="Joined"/>).
    /// </summary>
    internal static MarkdownDefinitions? In(MarkdownDocument document, string source, int before = int.MaxValue)
    {
        var lines = new List<(int At, string Line)>();

        foreach (var link in document.Descendants<LinkReferenceDefinition>()) Add(link);

        if (document.GetAbbreviations() is { } abbreviations)
            foreach (var abbreviation in abbreviations.Values) Add(abbreviation);

        return lines.Count == 0 ? null : new MarkdownDefinitions(string.Join('\n', lines.OrderBy(line => line.At).Select(line => line.Line)));

        void Add(Block block)
        {
            var start = Math.Clamp(block.Span.Start, 0, source.Length);
            if (start >= before) return;

            var end = Math.Clamp(block.Span.End + 1, start, source.Length);
            lines.Add((start, source[start..end].TrimEnd('\r', '\n')));
        }
    }

    /// <summary>What <paramref name="above"/> defines, then what <paramref name="below"/> does: two stretches of one document, the first written first.</summary>
    public static MarkdownDefinitions? Joined(MarkdownDefinitions? above, MarkdownDefinitions? below) =>
        above is null ? below : below is null ? above : new MarkdownDefinitions(above.Text + "\n" + below.Text);
}
