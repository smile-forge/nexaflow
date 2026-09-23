using System;
using System.Collections.Generic;

using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Pipeline;

namespace Nexaflow.Markdown.Prose;

/// <summary>
/// The text of a block, read into the constructs a writer spelled with punctuation — emphasis, code spans,
/// links, the marks that tick an item off.
///
/// <para>
/// The parser a paragraph and a heading are read by, and the one a table's cell and a list item's words are
/// read by too: what is in them is the same language, whatever holds it. What makes a block that block — a
/// heading's hashes, an item's bullet, a cell's pipes — is simply the source that falls before the first thing
/// read, kept where it was written.
/// </para>
/// <para>
/// <strong>Every character comes from the source.</strong> Markdig's tree holds text it has already read, an
/// entity decoded and an escape taken off, so nothing in it is copied across: only the spans are, and the
/// characters are cut out at them. A stretch that does not print back as what it was cut from is shown as it
/// was typed rather than repaired into something nobody wrote.
/// </para>
/// </summary>
public static class MarkdownInline
{
    /// <summary>
    /// <paramref name="source"/> as the words and marks it is made of — read beside <paramref name="besides"/>, the
    /// definitions written elsewhere in the document it belongs to, so a <c>[text][name]</c> link and an abbreviation
    /// mean what the document says they do. Nothing of those is read into what comes back.
    /// </summary>
    public static ContentNode Read(string? source, MarkdownPipeline? pipeline = null, string? besides = null)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return ContentNode.Branch(MarkdownKinds.Words, []);

        var read = new Cut(text);
        var parts = new List<ContentNode>();

        try
        {
            var told = besides is { Length: > 0 } ? $"{text}\n\n{besides}\n" : text;

            Words(Markdig.Markdown.Parse(told, pipeline ?? MarkdownParser.Pipeline), parts, read);
        }
        catch
        {
            parts.Add(ContentNode.Shown(read.Rest()));
        }

        read.Gap(parts, read.Length);

        return MarkdownParser.Checked(MarkdownKinds.Words, parts, text, Roles.Element);
    }

    /// <summary>
    /// The text of every block under this one, in the order it was written. A block holding blocks is walked
    /// into rather than skipped, because the words of a list item are inside the item, inside the list.
    /// </summary>
    private static void Words(ContainerBlock blocks, List<ContentNode> parts, Cut read)
    {
        // What was read beside the text comes after it, and is never part of it.
        foreach (var block in blocks.TakeWhile(block => block.Span.Start < read.Length))
            switch (block)
            {
                case LeafBlock { Inline: not null } leaf: Inside(leaf.Inline, parts, read); break;
                case ContainerBlock inner: Words(inner, parts, read); break;
            }
    }

    private static void Inside(ContainerInline container, List<ContentNode> parts, Cut read)
    {
        foreach (var child in container)
        {
            read.Gap(parts, read.Starts(child));
            parts.Add(One(child, read));
        }
    }

    private static ContentNode One(Inline inline, Cut read)
    {
        var end = read.Ends(inline);

        switch (inline)
        {
            // Nothing inside a code span is read: an asterisk in there is an asterisk.
            case CodeInline:
                return Wrapped(read, end, MarkdownKinds.Code, Kinds.Verbatim, Roles.Body);

            // An address written bare is a link to itself just as one in angle brackets is.
            case AutolinkInline:
            case LinkInline { IsAutoLink: true }:
                return Wrapped(read, end, MarkdownKinds.Link, MarkdownKinds.Word, MarkdownRoles.Destination);

            case TaskList task:
                return Task(read, end, task.Checked);

            // What the abbreviation stands for is written on a line of its own somewhere else, so it is not in
            // these characters and cannot be cut from them — it is hung on instead.
            // The word is a part of its own under the abbreviation, because what is hung on a piece is hung beside
            // what it holds: a word hung with its meaning would be a piece holding nothing but the meaning.
            case Markdig.Extensions.Abbreviations.AbbreviationInline abbreviation:
                return ContentNode.Branch(MarkdownKinds.Abbreviation, [read.Take(end, Roles.Body, MarkdownKinds.Word)])
                    .Holding(MarkdownKinds.Abbreviation, Roles.Tip, abbreviation.Abbreviation.Text.ToString().Trim());

            case LiteralInline:
                return Escaped(read, end);

            case HtmlEntityInline:
                return read.Take(end, Roles.Element, MarkdownKinds.Entity);

            case HtmlInline:
                return read.Take(end, Roles.Element, MarkdownKinds.Html);

            // Whether the writer asked for the line to break is settled by what stands before the ending, which the reader
            // has already looked at; it is said here so nothing after has to look again.
            case LineBreakInline ending:
                return read.Take(end, ending.IsHard ? MarkdownRoles.Hard : Roles.Separator, MarkdownKinds.Break);

            case Markdig.Extensions.Mathematics.MathInline maths:
                return Formula(maths, read, end);
        }

        var parts = new List<ContentNode>();

        if (inline is ContainerInline container)
        {
            var body = new List<ContentNode>();
            Inside(container, body, read);

            if (body.Count > 0)
            {
                // What stands before the first thing inside is what opened the construct.
                if (body[0].Role == Roles.Trivia && body[0].IsLeaf)
                {
                    parts.Add(body[0].As(Roles.Open));
                    body.RemoveAt(0);
                }

                parts.Add(ContentNode.Branch(MarkdownKinds.Words, body, Roles.Body));
            }
        }

        if (end > read.At) parts.AddRange(Tail(inline, read, end));

        var node = parts.Count == 0
            ? read.Take(end, Roles.Element, Kind(inline))
            : ContentNode.Branch(Kind(inline), parts);

        // A link naming a definition written elsewhere has no address in its own characters; the definition's is hung on it.
        return inline is LinkInline { Url.Length: > 0 } link && MarkdownLinks.Goes(node) is null
            ? node.Holding(Kind(inline), MarkdownRoles.Destination, link.Url)
            : node;
    }

    /// <summary>
    /// A run of ordinary text, with any backslash escape in it taken as the piece it is.
    ///
    /// <para>
    /// <c>\*</c> is two characters that mean one, which is exactly what an entity is — so it is read as its
    /// own piece, and drawn as the character it stands for while still being the two that were typed. Split
    /// here rather than left to a builder, because where one token stops and the next begins is a fact about
    /// the text and no later stage can change it.
    /// </para>
    /// </summary>
    private static ContentNode Escaped(Cut read, int end)
    {
        var from = read.At;
        var text = read.Between(from, end);

        if (!text.Contains('\\')) return read.Take(end, Roles.Element, MarkdownKinds.Word);

        var parts = new List<ContentNode>();
        var taken = 0;

        for (var at = 0; at + 1 < text.Length; at++)
        {
            if (text[at] != '\\' || !Escapes(text[at + 1])) continue;

            if (at > taken) parts.Add(read.Take(from + at, Roles.Element, MarkdownKinds.Word));

            parts.Add(read.Take(from + at + 2, Roles.Element, MarkdownKinds.Escape));

            taken = at + 2;
            at++;
        }

        if (parts.Count == 0) return read.Take(end, Roles.Element, MarkdownKinds.Word);

        if (end > read.At) parts.Add(read.Take(end, Roles.Element, MarkdownKinds.Word));

        return ContentNode.Branch(Kinds.Sequence, parts, Roles.Element);
    }

    /// <summary>Which characters a backslash can take the meaning off — CommonMark's ASCII punctuation, and no others.</summary>
    private static bool Escapes(char what) =>
        what is (>= '!' and <= '/') or (>= ':' and <= '@') or (>= '[' and <= '`') or (>= '{' and <= '~');

    /// <summary>
    /// A task's box, <c>[x]</c>: its brackets, and between them the mark saying whether it is done — the one character a
    /// press on the box rewrites, so what a press means is settled here rather than by anything reading the brackets again.
    /// </summary>
    internal static ContentNode Task(Cut read, int end, bool done)
    {
        var mark = done ? MarkdownRoles.Done : MarkdownRoles.Todo;
        var at = read.At;

        if (end - at != 3) return ContentNode.Branch(MarkdownKinds.Task, [read.Take(end, mark, Kinds.Token)]);

        return ContentNode.Branch(MarkdownKinds.Task,
            [read.Take(at + 1, Roles.Open, Kinds.Token), read.Take(at + 2, mark, Kinds.Token), read.Take(end, Roles.Close, Kinds.Token)]);
    }

    /// <summary>
    /// A formula in the middle of a sentence, cut where its delimiters stop: the dollars that hold it, and the
    /// LaTeX between them.
    ///
    /// <para>
    /// Shaped like a fenced block for the same reason — what is between the delimiters is another language, and
    /// whoever reads that language wants the body and not the marks. Where the delimiters cannot be told from
    /// the body the whole of it is held as one, rather than guessed at.
    /// </para>
    /// </summary>
    private static ContentNode Formula(Markdig.Extensions.Mathematics.MathInline maths, Cut read, int end)
    {
        var from = read.At;
        var opens = Math.Clamp(maths.Content.Start, from, end);
        var shuts = Math.Clamp(maths.Content.End + 1, opens, end);

        if (shuts <= opens) return read.Take(end, Roles.Element, MarkdownKinds.Formula);

        List<ContentNode> parts = [];

        if (opens > from) parts.Add(read.Take(opens, Roles.Open, Kinds.Token));
        parts.Add(read.Take(shuts, Roles.Body, Kinds.Verbatim));
        if (end > shuts) parts.Add(read.Take(end, Roles.Close, Kinds.Token));

        return ContentNode.Branch(MarkdownKinds.Formula, parts);
    }

    /// <summary>
    /// What closes a construct, and whatever it says about itself after that: the marks, and — for a link —
    /// where it points and what it calls itself, each found in the source rather than taken from the reading.
    /// </summary>
    private static IEnumerable<ContentNode> Tail(Inline inline, Cut read, int end)
    {
        if (inline is LinkInline link)
        {
            if (read.Upto(']', end) is { } shut) yield return shut;

            if (link.Url is { Length: > 0 } url && read.Finds(url, end) is { } where)
            {
                if (where.Before is { } gap) yield return gap;
                yield return where.Found.As(MarkdownRoles.Destination);
            }

            if (link.Title is { Length: > 0 } title && read.Finds(title, end) is { } says)
            {
                if (says.Before is { } gap) yield return gap;
                yield return says.Found.As(MarkdownRoles.Title);
            }
        }

        if (end > read.At) yield return read.Take(end, Roles.Close, Kinds.Token);
    }

    /// <summary>A construct that is one stretch between two marks — a code span, a bracketed link.</summary>
    private static ContentNode Wrapped(Cut read, int end, string kind, string inner, string role)
    {
        var text = read.Between(read.At, end);

        var opens = 0;
        while (opens < text.Length && Marks(text[opens])) opens++;

        var shuts = text.Length;
        while (shuts > opens && Marks(text[shuts - 1])) shuts--;

        List<ContentNode> parts = [];
        if (opens > 0) parts.Add(read.Take(read.At + opens, Roles.Open, Kinds.Token));
        if (shuts > opens) parts.Add(read.Take(read.At + (shuts - opens), role, inner));
        if (end > read.At) parts.Add(read.Take(end, Roles.Close, Kinds.Token));

        return ContentNode.Branch(kind, parts);
    }

    private static bool Marks(char c) => c is '`' or '<' or '>' or '[' or ']';

    private static string Kind(Inline inline) => inline switch
    {
        EmphasisInline emphasis => Emphasised(emphasis),
        CodeInline => MarkdownKinds.Code,
        LinkInline { IsImage: true } => MarkdownKinds.Image,
        LinkInline or AutolinkInline => MarkdownKinds.Link,
        LineBreakInline => MarkdownKinds.Break,
        HtmlEntityInline => MarkdownKinds.Entity,
        HtmlInline => MarkdownKinds.Html,
        TaskList => MarkdownKinds.Task,
        Markdig.Extensions.Abbreviations.AbbreviationInline => MarkdownKinds.Abbreviation,
        Markdig.Extensions.Mathematics.MathInline => MarkdownKinds.Formula,
        _ => MarkdownKinds.Word,
    };

    private static string Emphasised(EmphasisInline emphasis) => (emphasis.DelimiterChar, emphasis.DelimiterCount) switch
    {
        ('"', _) => MarkdownKinds.Citation,
        ('~', 2) => MarkdownKinds.Strike,
        ('~', _) => MarkdownKinds.Sub,
        ('^', _) => MarkdownKinds.Sup,
        ('=', _) => MarkdownKinds.Mark,
        ('+', _) => MarkdownKinds.Insert,
        (_, 2) => MarkdownKinds.Strong,
        _ => MarkdownKinds.Emphasis,
    };
}
