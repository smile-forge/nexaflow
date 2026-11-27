using System;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Cynefin;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Cynefin;

/// <summary>
/// What an edit means in a Cynefin framework: an item's words are put in quotes to hold what would otherwise read as something else.
/// Every other key does what it does anywhere.
/// </summary>
internal sealed class CynefinEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// In a string, what would end it is kept (<see cref="CynefinParser.Escaped"/>); bare text is put in double quotes to hold a quote, a
    /// comment or an arrow, and to say a word that would otherwise open a domain or title the diagram rather than sit in it — a backslash
    /// in it kept by another, since in quotes one starts an escape.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (CynefinParser.StringOf(part) is { } written && CynefinParser.Opened(written) is { } quote)
        {
            var kept = CynefinParser.Escaped(text, quote);
            return kept == text ? null : new MermaidWriting(caret, caret, kept, caret + kept.Length);
        }

        if (part.Parent is not { Kind: CynefinKinds.Text } holder || holder.Children.Any(child => child.Role == Roles.Open)) return null;

        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var (before, after) = (said[..at] + text, said[at..]);
        var whole = before + after;

        if (!(whole.Any(character => character is '"' or '%')
              || whole.Contains(CynefinParser.Arrow, StringComparison.Ordinal)
              || MermaidLine.Keyword(whole, MermaidLine.TitleWord) is not null
              || CynefinParser.Domains.Any(domain => whole.Trim().Equals(domain, StringComparison.OrdinalIgnoreCase))))
            return null;

        var head = "\"" + CynefinParser.Escaped(before, '"');
        return new MermaidWriting(part.Start, part.Start + said.Length, head + CynefinParser.Escaped(after, '"') + "\"", part.Start + head.Length);
    }
}
