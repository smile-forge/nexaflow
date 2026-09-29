using System;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sankey;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Sankey;

/// <summary>
/// What an edit means in a Sankey diagram: a name is put in quotes to hold a comma or a quote, and a value holds only a number.
/// Every other key does what it does anywhere.
/// </summary>
internal sealed class SankeyEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// A name holds anything but a comma and a quote as it is written; one given either is put in quotes, and a quote between those
    /// is written twice. A value is a number, so nothing else goes in one.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (part.Parent is { Kind: MermaidKinds.Amount })
            return MermaidWriting.Only(caret, text, character => char.IsAsciiDigit(character) || character is '.' or '-');
        if (part.Parent is not { } holder || part.Kind is not (Kinds.Words or Kinds.Hole)) return null;

        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var (before, after) = (said[..at] + text, said[at..]);

        // Inside quotes a quote is written twice and everything else goes in as it is.
        if (holder.Children.Any(child => child.Role == Roles.Open && child.Text == "\""))
        {
            if (!text.Contains('"')) return null;

            var doubled = text.Replace("\"", SankeyGrammar.Quoted, StringComparison.Ordinal);
            return new MermaidWriting(caret, caret, doubled, caret + doubled.Length);
        }

        if (!(before + after).Any(character => character is ',' or '"')) return null;

        // Written bare and no longer able to be: the whole name is written again, in quotes.
        var head = "\"" + before.Replace("\"", SankeyGrammar.Quoted, StringComparison.Ordinal);
        return new MermaidWriting(part.Start, part.Start + said.Length,
                                  head + after.Replace("\"", SankeyGrammar.Quoted, StringComparison.Ordinal) + "\"", part.Start + head.Length);
    }
}
