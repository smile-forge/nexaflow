using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Architecture;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Architecture;

/// <summary>
/// What an edit means in an architecture diagram: a title is put in quotes to hold what would end it, and what an id cannot hold is
/// dropped. Every other key does what it does anywhere.
/// </summary>
internal sealed class ArchitectureEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// A title and an icon are in brackets, and are put in quotes to hold a quote, a bracket closing them or a comment. An id, the
    /// group something is in and a side are written bare and cannot be quoted at all, so what they cannot hold is dropped — and a
    /// hole standing where one of those goes holds what it will hold.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        var role = part.Kind == Kinds.Hole ? part.Parent?.Role : part.Role;
        return role is ArchitectureRoles.Id or ArchitectureRoles.In or ArchitectureRoles.Side
            ? MermaidWriting.Only(caret, text, MermaidLine.Letter)
            : null;
    }
}
