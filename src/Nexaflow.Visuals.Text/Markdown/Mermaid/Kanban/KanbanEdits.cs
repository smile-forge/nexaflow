using System;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Kanban;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Kanban;

/// <summary>What an edit means on a Kanban board: a title is put in quotes to hold what would end it. Every other key does what it does anywhere.</summary>
internal sealed class KanbanEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// A title in brackets is put in quotes to hold a quote, a bracket closing it or a comment; a bare id that is its own title is
    /// written as a title in quotes to hold anything an id cannot (<see cref="MermaidOutline.Escaping"/>). An icon's name runs to its
    /// bracket and metadata to its brace, so neither can hold the one closing it.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        var closing = part.Role switch { KanbanRoles.Icon => ")", KanbanRoles.Data => "}", _ => null };
        if (closing is not null && text.Contains(closing, StringComparison.Ordinal))
        {
            var kept = text.Replace(closing, string.Empty, StringComparison.Ordinal);
            return new MermaidWriting(caret, caret, kept, caret + kept.Length);
        }

        return MermaidOutline.Escaping(part, caret, text, KanbanRoles.Id, Stops);
    }

    /// <summary>What a bare id cannot hold.</summary>
    private static bool Stops(char character) => character is '(' or '[' or ')' or '{' or '}' or '@' or '"' or '%';
}
