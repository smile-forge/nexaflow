using System.IO;

using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The name each heading answers to — what <c>#getting-started</c> leads to — worked out as GitHub works it out: the
/// heading's words without the marks round them, made into a name (lower case, a dash for a space, punctuation dropped), or
/// <c>section</c> where that leaves nothing, and a name an earlier heading already has made unique by a number after it.
///
/// <para>
/// <strong>Earlier means anywhere earlier in the document</strong>, not only in what one reading read. A long document read a
/// stretch at a time (<see cref="MarkdownParser.Windowed"/>) hands each stretch the names already given (<see cref="Taken"/>,
/// through the parser context), so the second "Notes" is <c>notes-1</c> in whichever stretch it is read. A reading handed none
/// starts the document's own, as a document read whole does.
/// </para>
/// </summary>
internal sealed class MarkdownHeadingNames : IMarkdownExtension
{
    /// <summary>Where a parser context holds the names already given: a set every heading read adds its own to.</summary>
    internal static readonly object Taken = new();

    /// <summary>Where a document holds the names it has given, where it was handed none.</summary>
    private static readonly object Given = new();

    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        if (pipeline.BlockParsers.Find<HeadingBlockParser>() is { } headings)
        {
            headings.Closed -= Closed;
            headings.Closed += Closed;
        }

        // A paragraph underlined with = or - becomes a heading as it closes.
        if (pipeline.BlockParsers.FindExact<ParagraphBlockParser>() is { } paragraphs)
        {
            paragraphs.Closed -= Closed;
            paragraphs.Closed += Closed;
        }
    }

    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    /// <summary>A heading is named once its words are read, which is after every block is.</summary>
    private static void Closed(BlockProcessor processor, Block block)
    {
        if (block is HeadingBlock heading) heading.ProcessInlinesEnd += Named;
    }

    private static void Named(InlineProcessor processor, Inline? inline)
    {
        if (processor.Block is not HeadingBlock { Inline: { } words } heading) return;

        var attributes = heading.GetAttributes();
        if (attributes.Id is not null) return;

        var taken = Names(processor);
        var said = LinkHelper.UrilizeAsGfm(Plain(words));
        var stem = string.IsNullOrEmpty(said) ? "section" : said;
        var name = stem;

        for (var again = 1; !taken.Add(name); again++) name = $"{stem}-{again}";

        attributes.Id = name;
    }

    /// <summary>The names already given in this document: what the reading was handed, or the document's own.</summary>
    private static HashSet<string> Names(InlineProcessor processor)
    {
        if (processor.Context?.Properties.TryGetValue(Taken, out var handed) == true && handed is HashSet<string> carried) return carried;

        if (processor.Document.GetData(Given) is HashSet<string> own) return own;

        var given = new HashSet<string>();
        processor.Document.SetData(Given, given);

        return given;
    }

    /// <summary>A heading's words as plain text: what they say, with no mark of how they are set and no address a link leads to.</summary>
    private static string Plain(ContainerInline words)
    {
        using var writer = new StringWriter();

        new HtmlRenderer(writer) { EnableHtmlForInline = false, EnableHtmlEscape = false }.Render(words);
        writer.Flush();

        return writer.ToString();
    }
}

/// <summary>
/// What a stretch of a markdown document read a stretch at a time hands on to the next (<see cref="Ast.ContentStretch"/>):
/// the link definitions read so far, and every name a heading has been given, in the order they were given. Compared by what
/// each holds.
/// </summary>
internal sealed record MarkdownCarried(MarkdownDefinitions? Defined, string[] Named)
{
    public bool Equals(MarkdownCarried? other) =>
        other is not null && Equals(Defined, other.Defined) && Named.AsSpan().SequenceEqual(other.Named);

    public override int GetHashCode() => HashCode.Combine(Defined, Named.Length);
}
