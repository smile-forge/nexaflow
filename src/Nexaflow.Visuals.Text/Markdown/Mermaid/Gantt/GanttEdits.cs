using System;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Gantt;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Gantt;

/// <summary>What an edit means in a Gantt chart: a colon typed into a task's name is not written. Every other key does what it does anywhere.</summary>
internal sealed class GanttEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>A task's name runs to its colon, so a colon typed into one is not written; a link's quote is written as its entity code.</summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;
        if (part.Parent is not { Kind: GanttKinds.Text, Role: GanttRoles.Name } || !text.Contains(':')) return null;

        var kept = text.Replace(":", string.Empty, StringComparison.Ordinal);
        return new MermaidWriting(caret, caret, kept, caret + kept.Length);
    }
}
