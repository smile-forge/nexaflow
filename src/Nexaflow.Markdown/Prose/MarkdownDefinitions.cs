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
/// here, and each block's words are read with them beside it (<see cref="Stages.WithBlocks"/>). They are the lines as
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

    /// <summary>Every definition <paramref name="document"/> was read to hold, cut from <paramref name="source"/>.</summary>
    internal static MarkdownDefinitions? In(MarkdownDocument document, string source)
    {
        var lines = new List<string>();

        foreach (var link in document.Descendants<LinkReferenceDefinition>()) lines.Add(Cut(link));

        if (document.GetAbbreviations() is { } abbreviations)
            foreach (var abbreviation in abbreviations.Values) lines.Add(Cut(abbreviation));

        return lines.Count == 0 ? null : new MarkdownDefinitions(string.Join('\n', lines));

        string Cut(Block block)
        {
            var start = Math.Clamp(block.Span.Start, 0, source.Length);
            var end = Math.Clamp(block.Span.End + 1, start, source.Length);

            return source[start..end].TrimEnd('\r', '\n');
        }
    }
}
