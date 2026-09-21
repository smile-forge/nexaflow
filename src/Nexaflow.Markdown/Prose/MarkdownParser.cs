using System;
using System.Collections.Generic;

using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;

using Nexaflow.Markdown.Ast;

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

        var parts = new List<ContentNode>();
        var at = 0;

        try
        {
            foreach (var block in Markdig.Markdown.Parse(text, pipeline ?? Pipeline))
            {
                var from = Math.Clamp(block.Span.Start, at, text.Length);
                var to = Math.Clamp(Ends(block, text, from), from, text.Length);
                if (to == from) continue;

                if (from > at) parts.Add(Trivia(text[at..from]));

                parts.Add(Block(block, text[from..to]));
                at = to;
            }
        }
        catch
        {
            // A reader that threw has said nothing about the text, which is not the same as the text being
            // wrong. What is left of it is shown as it was typed.
            parts.Add(ContentNode.Shown(text[at..]));
            at = text.Length;
        }

        if (at < text.Length) parts.Add(Trivia(text[at..]));

        return ContentNode.Branch(MarkdownKinds.Document, parts);
    }

    /// <summary>
    /// One block: what kind it is, and its own source held as written. Nothing is read out of the body, because
    /// what is inside is a different language with a different grammar and reading it is its own parser's
    /// business.
    /// </summary>
    /// <summary>
    /// One past the last character a block stands for.
    ///
    /// <para>
    /// A span alone will not do. Markdig gives an unclosed fence a span of four characters, because there is
    /// no closing fence to measure to — so the lines it read are asked as well, and whichever reaches further
    /// wins. A block then takes the rest of the line it ends on, line ending and all, because a block-level
    /// construct never shares a line with the next one and the ending is what separates them.
    /// </para>
    /// </summary>
    private static int Ends(Block block, string text, int from)
    {
        var to = Math.Max(block.Span.End + 1, from);

        if (block is LeafBlock { Lines.Count: > 0 } leaf)
            to = Math.Max(to, leaf.Lines.Lines[leaf.Lines.Count - 1].Slice.End + 1);

        to = Math.Clamp(to, from, text.Length);

        while (to < text.Length && text[to] != '\n') to++;

        return to < text.Length ? to + 1 : to;
    }

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

    private static ContentNode Trivia(string text) =>
        ContentNode.Leaf(text.AsSpan().IsWhiteSpace() ? Kinds.Space : Kinds.Token, text, Roles.Trivia);

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
