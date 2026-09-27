using System;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Mindmap;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Mindmap;

/// <summary>What an edit means in a mindmap: a title is put in quotes to hold what would end it. Every other key does what it does anywhere.</summary>
internal sealed class MindmapEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// A title in brackets is put in quotes to hold a quote, a bracket closing it or a comment; a bare id that is its own title is
    /// written as a title in quotes to hold anything an id cannot (<see cref="MermaidOutline.Escaping"/>). An icon's name runs to its
    /// bracket, so it cannot hold one.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        if (part.Role == MindmapRoles.Icon && text.Contains(')'))
        {
            var named = text.Replace(")", string.Empty, StringComparison.Ordinal);
            return new MermaidWriting(caret, caret, named, caret + named.Length);
        }

        return MermaidOutline.Escaping(part, caret, text, MindmapRoles.Id, Stops);
    }

    /// <summary>What a bare id cannot hold.</summary>
    private static bool Stops(char character) => character is '(' or '[' or ')' or '{' or '}' or '"' or '%';
}
