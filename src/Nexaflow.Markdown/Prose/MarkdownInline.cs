using System;
using System.Collections.Generic;

using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

using Nexaflow.Markdown.Ast;

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
    /// <summary><paramref name="source"/> as the words and marks it is made of.</summary>
    public static ContentNode Read(string? source, MarkdownPipeline? pipeline = null)
    {
        var text = source ?? string.Empty;
        if (text.Length == 0) return ContentNode.Branch(MarkdownKinds.Words, []);

        var read = new Cut(text);
        var parts = new List<ContentNode>();

        try
        {
            Words(Markdig.Markdown.Parse(text, pipeline ?? MarkdownParser.Pipeline), parts, read);
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
        foreach (var block in blocks)
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

            case AutolinkInline:
                return Wrapped(read, end, MarkdownKinds.Link, MarkdownKinds.Word, MarkdownRoles.Destination);

            case TaskList task:
                return ContentNode.Branch(MarkdownKinds.Task,
                    [read.Take(end, task.Checked ? MarkdownRoles.Done : MarkdownRoles.Todo, Kinds.Token)]);

            case LiteralInline:
                return read.Take(end, Roles.Element, MarkdownKinds.Word);

            case HtmlEntityInline:
                return read.Take(end, Roles.Element, MarkdownKinds.Entity);

            case HtmlInline:
                return read.Take(end, Roles.Element, MarkdownKinds.Html);

            case LineBreakInline:
                return read.Take(end, Roles.Separator, MarkdownKinds.Break);
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

        return parts.Count == 0
            ? read.Take(end, Roles.Element, Kind(inline))
            : ContentNode.Branch(Kind(inline), parts);
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
        _ => MarkdownKinds.Word,
    };

    private static string Emphasised(EmphasisInline emphasis) => (emphasis.DelimiterChar, emphasis.DelimiterCount) switch
    {
        ('~', 2) => MarkdownKinds.Strike,
        ('~', _) => MarkdownKinds.Sub,
        ('^', _) => MarkdownKinds.Sup,
        ('=', _) => MarkdownKinds.Mark,
        ('+', _) => MarkdownKinds.Insert,
        (_, 2) => MarkdownKinds.Strong,
        _ => MarkdownKinds.Emphasis,
    };
}
