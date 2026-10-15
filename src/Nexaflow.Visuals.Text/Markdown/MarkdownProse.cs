using System;
using System.Text;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Prose;

namespace Nexaflow.Visuals.Text.Markdown;

/// <summary>
/// The words of markdown's prose, read off the reading rather than laid out — so cheap enough to read a reply again at every
/// change as it streams, and on any thread. What a span of words shows is <see cref="Words"/>; how the blocks around them are
/// taken is each reader's own: <see cref="RunTogether"/> keeps only the prose, while help search keeps every line a page shows.
/// </summary>
public static class MarkdownProse
{
    /// <summary>
    /// <paramref name="markdown"/>'s prose on one line: the words of its paragraphs, headings, list items and quotes, one
    /// after another — its blocks stripped away, and with them what is not prose: code, what another language draws,
    /// tables, pictures, markup, and what never shows.
    /// </summary>
    public static string RunTogether(string? markdown)
    {
        var sb = new StringBuilder();
        Prose(ContentPart.Of(MarkdownParser.Worked(markdown ?? "")), sb);
        return sb.ToString();
    }

    private static void Prose(ContentPart holder, StringBuilder sb)
    {
        foreach (var part in holder.Children)
        {
            if (part.Derived || part.Role == Roles.Trivia) continue;
            if (part.Kind is MarkdownKinds.Code or Kinds.Block or MarkdownKinds.Table or MarkdownKinds.Html or MarkdownKinds.Rule
                          or MarkdownKinds.FrontMatter or MarkdownKinds.Reference or MarkdownKinds.Task or MarkdownKinds.Marker)
                continue;

            if (part.Part(Roles.Body) is { Kind: Kinds.Words } words) Append(sb, Words(words));
            else Prose(part.Part(Roles.Body) ?? part, sb);
        }
    }

    /// <summary>Adds <paramref name="words"/> to the line, one space between each and the next.</summary>
    private static void Append(StringBuilder sb, string words)
    {
        foreach (var word in words.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(word);
        }
    }
    /// <summary>
    /// The words an inline run shows, its marks taken off: a link's words and never its target, a code span's characters,
    /// an entity as the character it stands for — and no picture, markup or typeset formula, which are not words.
    /// </summary>
    public static string Words(ContentPart words)
    {
        var sb = new StringBuilder();
        Walk(words);
        return sb.ToString();

        void Walk(ContentPart part)
        {
            if (part.Derived) return;

            switch (part.Kind)
            {
                case MarkdownKinds.Image:                         return;   // a picture, not text
                case MarkdownKinds.Html:                          return;
                case MarkdownKinds.Task:                          return;
                case Kinds.Block or MarkdownKinds.Formula:        return;   // typeset, not text
                case MarkdownKinds.Entity:                        sb.Append(MarkdownEntities.Decoded(part.Text) ?? part.Text); return;
                case MarkdownKinds.Escape when part.Text.Length > 1: sb.Append(part.Text[1..]); return;
                case MarkdownKinds.Break:
                    if (!MarkdownLineEnds.Closes(part)) sb.Append(part.Role == MarkdownRoles.Hard ? '\n' : ' ');
                    return;
                case MarkdownKinds.Code when part.Part(Roles.Body) is { } code:
                    foreach (var leaf in code.Node.Leaves())
                        sb.Append(leaf.Role != Roles.Trivia ? leaf.Text : leaf.Text.Contains('\n') ? " " : "");
                    return;

                // A link to itself has its address for its words; any other link is its words, never its target.
                case Kinds.Link when part.Part(Roles.Body) is null:
                    sb.Append(MarkdownLinks.Goes(part.Node));
                    return;
            }

            if (part.Children.Count > 0)
            {
                foreach (var child in part.Children)
                    if (child.Role is not (Roles.Destination or MarkdownRoles.Title)) Walk(child);
                return;
            }

            if (part.Role is Roles.Open or Roles.Close or Roles.Name or Roles.Trivia or Roles.Silent) return;

            sb.Append(part.Text.Replace('\n', ' '));
        }
    }
}
