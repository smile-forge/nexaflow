using System;
using System.Collections.Generic;

using Markdig;
using Markdig.Extensions.Tables;
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
    public static AstPipeline Reader { get; } = new(new WithBlocks());

    /// <summary>The same options, so a host adding an extension of its own starts from what is already read.</summary>
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
            .UseDiagrams();

    /// <summary><paramref name="source"/> as the blocks it is written in.</summary>
    public static ContentNode Read(string? source, MarkdownPipeline? pipeline = null)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return ContentNode.Branch(MarkdownKinds.Document, []);

        var read = new Cut(text);
        var parts = new List<ContentNode>();

        try
        {
            Split(Markdig.Markdown.Parse(text, pipeline ?? Pipeline), parts, read);
        }
        catch
        {
            // A reader that threw has said nothing about the text, which is not the same as the text being
            // wrong. What is left of it is shown as it was typed.
            parts.Add(ContentNode.Shown(read.Rest()));
        }

        read.Gap(parts, read.Length);

        return Checked(MarkdownKinds.Document, parts, text, Roles.Element);
    }

    /// <summary>
    /// What is written inside a block that holds blocks — a quote's lines, an alert's body.
    ///
    /// <para>
    /// Nothing is stripped. The <c>&gt;</c> at the head of each line falls between the blocks the quote holds,
    /// and is kept as the trivia it is, so the quote prints back as exactly what somebody typed while a builder
    /// draws its blocks and skips its marks, neither of them knowing about the other.
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
            foreach (var block in Markdig.Markdown.Parse(text, pipeline ?? Pipeline))
                if (block is ContainerBlock holds) Split(holds, parts, read);
                else One(block, parts, read);
        }
        catch
        {
            parts.Add(ContentNode.Shown(read.Rest()));
        }

        read.Gap(parts, read.Length);

        return Checked(Kinds.Sequence, parts, text, Roles.Body);
    }

    /// <summary>
    /// Every block of <paramref name="blocks"/>, in order, with whatever fell between two of them kept where
    /// the writer put it. The one move a markdown container makes, wherever it is — a document's blocks, a
    /// quote's, a list item's.
    /// </summary>
    internal static void Split(ContainerBlock blocks, List<ContentNode> parts, Cut read)
    {
        foreach (var block in blocks) One(block, parts, read);
    }

    /// <summary>One block, with whatever was written in front of it.</summary>
    private static void One(Block block, List<ContentNode> parts, Cut read)
    {
        var from = read.Starts(block);
        var to = read.Closes(block, from);
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
        if (block is FencedCodeBlock fence) return Fenced(fence, source);

        return ContentNode.Branch(Kind(block), [ContentNode.Leaf(Kinds.Verbatim, source, Roles.Body)]);
    }

    /// <summary>
    /// A fenced block, as the fence, the language it names itself and that language's own source. The language
    /// is a part rather than the kind because a writer typed it: a fence saying <c>mermaid</c> has those seven
    /// characters in the source and they are still in the tree, where a paragraph has nothing written anywhere
    /// that says it is one.
    /// </summary>
    private static ContentNode Fenced(FencedCodeBlock fence, string source)
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

        return ContentNode.Branch(MarkdownKinds.Fence, parts);
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
        HtmlBlock => MarkdownKinds.Html,
        LinkReferenceDefinitionGroup => MarkdownKinds.Reference,
        CodeBlock => MarkdownKinds.Code,
        ParagraphBlock => MarkdownKinds.Paragraph,
        _ => Kinds.Verbatim,
    };
}
