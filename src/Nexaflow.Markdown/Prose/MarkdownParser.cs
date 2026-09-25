using System;
using System.Collections.Generic;

using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.Tables;
using Markdig.Renderers.Html;
using Markdig.Syntax;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;
using Nexaflow.Markdown.Prose.Stages;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// A markdown document: the blocks it is written in, and nothing about what any of them says.
///
/// <para>
/// <strong>A document is a list of blocks, and each block is its own content.</strong> A paragraph, a table, a
/// quote and a fenced tune are four languages, read by four parsers into four trees — so this one only has to
/// say where each block starts and which of them it is. What a block holds is settled by whoever reads that
/// kind, when it is read (<see cref="Stages.WithBlocks"/>), which is what lets a keystroke re-read one paragraph
/// rather than a thousand-line file, and what lets a kind nothing can read yet be shown exactly as it was typed.
/// </para>
/// <para>
/// Markdig decides where the boundaries are and nothing else. Where one block stops and the next begins is a
/// question with a standard answer and a decade of corner cases behind it, and there is no version of that worth
/// writing twice.
/// </para>
/// <para>
/// <strong>Every character comes from the source, not from Markdig.</strong> Its tree holds text it has already
/// read — an entity decoded, an escape taken off, a line's indentation dropped — so nothing in it is copied
/// across. Only the <em>spans</em> are, and the characters are cut out of the source at them, with whatever
/// falls between two blocks kept as the trivia it is.
/// </para>
/// </summary>
public static class MarkdownParser
{
    /// <summary>
    /// The pipeline used where a caller names none: everything a document can hold.
    ///
    /// <para>
    /// Deliberately without Markdig's own trivia tracking, which turns a pipe table back into the paragraph it
    /// was read from. Nothing here needs it: only the spans are taken and every character is cut from the
    /// source, so what falls between two blocks is found by looking rather than by being told.
    /// </para>
    /// </summary>
    public static MarkdownPipeline Pipeline { get; } = Reading(new MarkdownPipelineBuilder()).Build();

    /// <summary>
    /// The stages a document is read by once its blocks are found: each block's body read by the parser its
    /// kind names. Here rather than at each surface, so an editor and a view read the same document the same
    /// way.
    /// </summary>
    public static AstPipeline Reader { get; } = new(new WithBlocks(), new WithGroups());

    /// <summary>
    /// A reader for one document, read again and again as it is written: what it read of a block last time is what it hands
    /// back for the same block this time, so a keystroke reads the block it was typed in rather than every block there is.
    /// One per document — what it keeps is that document's blocks.
    /// </summary>
    public static AstPipeline Rereading() => new(new WithBlocks(remembering: true), new WithGroups());

    /// <summary>
    /// The same options, so a host adding an extension of its own starts from what is already read. Every
    /// heading is given a GitHub-style id on the way, which is what an in-page link is resolved against.
    /// </summary>
    public static MarkdownPipelineBuilder Reading(MarkdownPipelineBuilder builder) =>
        builder
            .UseYamlFrontMatter()
            .UsePipeTables()
            .UseGridTables()
            .UseTaskLists()
            .UseEmphasisExtras()
            .UseAutoLinks()
            .UseDefinitionLists()
            .UseListExtras()
            .UseAbbreviations()
            .UseAlertBlocks()
            .UseFigures()
            .UseFooters()
            .UseCitations()
            .UseMathematics()
            .UseDiagrams()
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub);

    /// <summary><paramref name="source"/> as the blocks it is written in.</summary>
    public static ContentNode Read(string? source, MarkdownPipeline? pipeline = null)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return ContentNode.Branch(MarkdownKinds.Document, []);

        var read = new Cut(text);
        var parts = new List<ContentNode>();
        MarkdownDefinitions? defined = null;

        try
        {
            var document = Markdig.Markdown.Parse(text, pipeline ?? Pipeline);

            Split(document, parts, read);
            defined = MarkdownDefinitions.In(document, text);
        }
        catch
        {
            // A reader that threw has said nothing about the text, which is not the same as the text being
            // wrong. What is left of it is shown as it was typed.
            parts.Add(ContentNode.Shown(read.Rest()));
        }

        read.Gap(parts, read.Length);

        var whole = Checked(MarkdownKinds.Document, parts, text, Roles.Element);

        // Seen only by reading the whole document, and wanted by every block's words — see MarkdownDefinitions.
        return defined is null ? whole : whole.Holding(MarkdownKinds.Definitions, Roles.Derived, defined);
    }

    /// <summary>
    /// What is written inside a block that holds blocks — a quote's lines, an alert's body.
    ///
    /// <para>
    /// Nothing is stripped. The <c>&gt;</c> at the head of each line falls between the blocks the quote holds,
    /// and is kept as the trivia it is, so the quote prints back as exactly what somebody typed while a builder
    /// draws its blocks and skips its marks, neither of them knowing about the other.
    /// </para>
    /// <para>
    /// A block covering the whole of what was handed over is the block being read rather than something inside
    /// it, so the walk goes through it — twice over where it has to, because a definition list is one item and
    /// the item is the term and what it means. Stopping at the first would hand back the same stretch of source
    /// under a new name, which is a reading that learned nothing.
    /// </para>
    /// </summary>
    public static ContentNode Inside(string? source, MarkdownPipeline? pipeline = null)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return ContentNode.Branch(Kinds.Sequence, [], Roles.Body);

        var read = new Cut(text);
        var parts = new List<ContentNode>();

        try
        {
            ContainerBlock blocks = Markdig.Markdown.Parse(text, pipeline ?? Pipeline);

            while (blocks.Count == 1 && blocks[0] is ContainerBlock only && Whole(only, text)) blocks = only;

            // An alert says what it is with the first characters of its first line. Markdig reads them and keeps them out of
            // the words, but not out of the paragraph's span — so they are cut here, before the paragraph is.
            var first = blocks is Markdig.Extensions.Alerts.AlertBlock alert ? Called(alert, parts, read) : 0;

            Split(blocks, parts, read, first);
        }
        catch
        {
            parts.Add(ContentNode.Shown(read.Rest()));
        }

        read.Gap(parts, read.Length);

        return Checked(Kinds.Sequence, parts, text, Roles.Body);
    }

    /// <summary>Whether a block is the whole of what was handed over rather than a part of it.</summary>
    private static bool Whole(Block block, string text) =>
        block.Span.Start <= 0 && block.Span.End >= text.TrimEnd('\n', '\r', ' ', '\t').Length - 1;

    /// <summary>
    /// The <c>[!NOTE]</c> an alert opens with, cut into its marks and the name between them — which are one marker is the
    /// pipeline's to say (<see cref="Stages.WithGroups"/>) — and what follows it up to where the words under it start.
    /// Where it is all its paragraph held, the line it stands on is all of it.
    /// </summary>
    /// <returns>Which of the alert's blocks is the first still to be read.</returns>
    private static int Called(Markdig.Extensions.Alerts.AlertBlock alert, List<ContentNode> parts, Cut read)
    {
        if (alert.Count == 0 || alert[0] is not ParagraphBlock first) return 0;

        var from = read.Starts(first);
        var name = alert.Kind.ToString();

        if (name.Length == 0 || read.Between(from, from + name.Length + 3) != $"[!{name}]") return 0;

        read.Gap(parts, from);
        parts.Add(read.Take(from + 2, Roles.Open, Kinds.Token));
        parts.Add(read.Take(from + 2 + name.Length, Roles.Name, Kinds.Token));
        parts.Add(read.Take(from + 3 + name.Length, Roles.Close, Kinds.Token));

        if (first.Inline?.FirstChild is { } words)
        {
            read.Gap(parts, read.Starts(words));
            return 0;
        }

        read.Gap(parts, read.Closes(first, read.At));
        return 1;
    }

    /// <summary>The blocks a container holds, each as its own piece — from the <paramref name="first"/> of them.</summary>
    internal static void Split(ContainerBlock blocks, List<ContentNode> parts, Cut read, int first = 0)
    {
        for (var at = first; at < blocks.Count; at++)
            One(blocks[at], parts, read, at + 1 < blocks.Count ? blocks[at + 1].Span.Start : null);
    }

    /// <summary>
    /// One block, with whatever was written in front of it — ending where the next begins, whatever its span says. A pipe
    /// table written straight under a line of words is cut out of the paragraph it interrupted, and the paragraph's span is
    /// left reaching over the table.
    /// </summary>
    private static void One(Block block, List<ContentNode> parts, Cut read, int? next)
    {
        var from = read.Starts(block);
        var to = read.Closes(block, from);
        if (next is { } ceiling && ceiling > from && ceiling < to) to = ceiling;
        if (to == from) return;

        read.Gap(parts, from);
        parts.Add(Block(block, read.Text(to)));
    }

    /// <summary>
    /// The reading, where it is one.
    ///
    /// <para>
    /// Checked rather than trusted: a tree that does not print back as what it was read from is not a reading
    /// of it, whatever else it is. What is handed back instead is the source shown as it was typed — which is
    /// what the body held before anybody read it, and what every builder already knows how to draw.
    /// </para>
    /// </summary>
    internal static ContentNode Checked(string kind, IReadOnlyList<ContentNode> parts, string text, string role)
    {
        var node = ContentNode.Branch(kind, parts, role);

        return node.Print() == text ? node : ContentNode.Branch(kind, [ContentNode.Shown(text)], role);
    }

    /// <summary>
    /// One block: what kind it is, and its own source held as written. Nothing is read out of the body here,
    /// because what is inside is a different language with a different grammar, and reading it is its own
    /// parser's business — which happens a stage later, in <see cref="Stages.WithBlocks"/>.
    /// </summary>
    private static ContentNode Block(Block block, string source)
    {
        // A maths block IS a fenced block — Markdig derives one from the other — and it reads the same way: a
        // delimiter, a body in another language, a delimiter. What differs is only that nobody writes the
        // language after the fence, because the $$ is what says it.
        if (block is Markdig.Extensions.Mathematics.MathBlock maths) return Fenced(maths, source, MarkdownKinds.Math);

        if (block is FencedCodeBlock fence) return Fenced(fence, source, MarkdownKinds.Fence);

        var read = ContentNode.Branch(Kind(block), [ContentNode.Leaf(Kinds.Verbatim, source, Roles.Body)]);

        // The name a link can point at. Taken here rather than worked out later, because it is the reading of
        // the whole document that settles it: two headings saying the same thing are told apart by their
        // order, which nothing looking at one heading's characters can see.
        if (block is HeadingBlock heading && heading.TryGetAttributes()?.Id is { Length: > 0 } named)
            read = AstRewrite.Holding(read, MarkdownKinds.Anchor, Roles.Derived, named);

        // How deep a heading is, which the reader counted as it read the hashes or saw which character underlined it.
        if (block is HeadingBlock ranked)
            read = read.Saying(MarkdownKinds.Rank, MarkdownRoles.Rank, ranked.Level.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return read;
    }

    /// <summary>
    /// A fenced block, as the fence, the language it names itself and that language's own source. The language
    /// is a part rather than the kind because a writer typed it: a fence saying <c>mermaid</c> has those seven
    /// characters in the source and they are still in the tree, where a paragraph has nothing written anywhere
    /// that says it is one.
    /// </summary>
    private static ContentNode Fenced(FencedCodeBlock fence, string source, string kind)
    {
        var opens = 0;
        while (opens < source.Length && source[opens] == fence.FencedChar) opens++;

        if (opens == 0) return ContentNode.Branch(MarkdownKinds.Code, [ContentNode.Leaf(Kinds.Verbatim, source, Roles.Body)]);

        var named = opens;
        while (named < source.Length && !char.IsWhiteSpace(source[named])) named++;

        var line = source.IndexOf('\n', named);
        var body = line < 0 ? source.Length : line + 1;

        // The closing fence is whatever run of fence characters ends the block, with the line it stands on. The
        // line ending before it belongs to the body: it ends the last line somebody wrote in there.
        var shut = source.Length;
        while (shut > body && char.IsWhiteSpace(source[shut - 1])) shut--;

        var closes = shut;
        while (shut > body && source[shut - 1] == fence.FencedChar) shut--;

        if (shut == closes) shut = source.Length;
        else while (shut > body && (source[shut - 1] == ' ' || source[shut - 1] == '\t')) shut--;

        List<ContentNode> parts =
        [
            ContentNode.Leaf(Kinds.Token, source[..opens], Roles.Open),
        ];

        if (named > opens) parts.Add(ContentNode.Leaf(Kinds.Token, source[opens..named], Roles.Name));
        if (body > named) parts.Add(ContentNode.Leaf(Kinds.Space, source[named..body], Roles.Trivia));

        // Held as written and nothing read out of it: what is in there is a different language.
        if (shut > body) parts.Add(ContentNode.Leaf(Kinds.Verbatim, source[body..shut], Roles.Body));
        if (source.Length > shut) parts.Add(ContentNode.Leaf(Kinds.Token, source[shut..], Roles.Close));

        return ContentNode.Branch(kind, parts);
    }

    /// <summary>Which language reads a block's body.</summary>
    private static string Kind(Block block) => block switch
    {
        HeadingBlock => MarkdownKinds.Heading,
        ThematicBreakBlock => MarkdownKinds.Rule,
        Markdig.Extensions.Alerts.AlertBlock => MarkdownKinds.Alert,
        QuoteBlock => MarkdownKinds.Quote,
        ListBlock => MarkdownKinds.List,
        Table => MarkdownKinds.Table,
        Markdig.Extensions.Yaml.YamlFrontMatterBlock => MarkdownKinds.FrontMatter,
        Markdig.Extensions.Footnotes.FootnoteGroup => MarkdownKinds.Footnote,
        Markdig.Extensions.DefinitionLists.DefinitionList => MarkdownKinds.Definition,
        Markdig.Extensions.DefinitionLists.DefinitionTerm => MarkdownKinds.Term,
        Markdig.Extensions.DefinitionLists.DefinitionItem => MarkdownKinds.Described,
        Markdig.Extensions.Figures.FigureCaption => MarkdownKinds.Caption,
        Markdig.Extensions.Figures.Figure => MarkdownKinds.Figure,
        Markdig.Extensions.Footers.FooterBlock => MarkdownKinds.Footer,
        HtmlBlock => MarkdownKinds.Html,
        LinkReferenceDefinitionGroup => MarkdownKinds.Reference,
        CodeBlock => MarkdownKinds.Code,
        ParagraphBlock => MarkdownKinds.Paragraph,
        _ => Kinds.Verbatim,
    };
}
