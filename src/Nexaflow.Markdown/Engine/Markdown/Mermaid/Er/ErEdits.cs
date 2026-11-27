using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Er;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Er;

/// <summary>What an edit means in an entity-relationship diagram: what a bare name cannot hold is dropped. Every other key does what it does anywhere.</summary>
internal sealed class ErEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit)
    {
        var change = DiagramWriting.Typed(edit, Escaping) ?? OrdinaryEdits.Keyed(edit);

        return change is null ? null : DiagramRenames.AtEveryMention(edit, change, ErRoles.Id);
    }

    /// <summary>
    /// What is written on a relationship and what an attribute says it is for hold anything but a comment. A name, a class, a type
    /// and a key are written bare — in quotes, where one holds what a bare name cannot — and a way is letters.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        // What a relationship is called, written bare, runs to the end of its line, and a quote typed into it would open a string
        // running on to the next quote in the block, as Mermaid's lexer reads one: the words are put in quotes to hold it.
        if (part.Parent?.Kind == ErKinds.Said && part.Kind is Kinds.Words or Kinds.Hole && text.Contains('"'))
        {
            var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
            var at = Math.Clamp(caret - part.Start, 0, said.Length);
            return MermaidWriting.Quoting(part.Start, part.Start + said.Length, said[..at] + text, said[at..]);
        }

        if (part.Role is ErRoles.Id or ErRoles.Class or ErRoles.Field) return MermaidWriting.Only(caret, text, ErParser.Bare);
        if (part.Role is ErRoles.Type) return MermaidWriting.Only(caret, text, ErParser.Typed);
        if (part.Role is ErRoles.Key or ErRoles.Towards) return MermaidWriting.Only(caret, text, char.IsAsciiLetter);

        // A subgraph's name is written in words, and the brackets past it open the label drawn instead of it.
        if (part.Role is ErRoles.Space) return MermaidWriting.Only(caret, text, character => character is not ('[' or ']'));

        return null;
    }
}
