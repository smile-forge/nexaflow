using System;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Quadrant;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Quadrant;

/// <summary>What an edit means in a quadrant chart: text is put in quotes to hold what would end it. Every other key does what it does anywhere.</summary>
internal sealed class QuadrantEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// In quotes, a quote is written as its entity code; bare text is put in quotes to hold a colon, a quote or a bracket, which
    /// would end it; a bare class name is put in quotes to hold anything but a word.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text, (_, said) => QuadrantGrammar.Bare(said)) is { } escaped) return escaped;
        if (part.Parent is not { Kind: QuadrantKinds.Text } holder || holder.Children.Any(child => child.Role == Roles.Open)) return null;

        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var (before, after) = (said[..at] + text, said[at..]);

        return (before + after).Any(character => character is ':' or '"' or '[' or ']')
            ? MermaidWriting.Quoting(part.Start, part.Start + said.Length, before, after)
            : null;
    }
}
