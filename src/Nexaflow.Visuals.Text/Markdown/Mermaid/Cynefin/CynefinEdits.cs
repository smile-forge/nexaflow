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
    /// In quotes, a quote is written as its entity code; bare text is put in quotes to hold a quote, a comment or an arrow, and to
    /// say a word that would otherwise open a domain or title the diagram rather than sit in it.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;
        if (part.Parent is not { Kind: CynefinKinds.Text } holder || holder.Children.Any(child => child.Role == Roles.Open)) return null;

        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var (before, after) = (said[..at] + text, said[at..]);
        var whole = before + after;

        return whole.Any(character => character is '"' or '%')
            || whole.Contains(CynefinGrammar.Arrow, StringComparison.Ordinal)
            || MermaidLine.Keyword(whole, MermaidLine.TitleWord) is not null
            || CynefinGrammar.Domains.Any(domain => whole.Trim().Equals(domain, StringComparison.OrdinalIgnoreCase))
                ? MermaidWriting.Quoting(part.Start, part.Start + said.Length, before, after)
                : null;
    }
}
