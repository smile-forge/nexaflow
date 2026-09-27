using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Requirement;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Requirement;

/// <summary>What an edit means in a requirement diagram: what a bare name cannot hold is dropped. Every other key does what it does anywhere.</summary>
internal sealed class RequirementEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// A field's value runs to the end of its line and holds anything but a comment. A name and a class are written bare — in quotes,
    /// where one holds what a bare name cannot — and a way is letters.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        if (part.Role is RequirementRoles.Id or RequirementRoles.Class) return MermaidWriting.Only(caret, text, RequirementGrammar.Bare);
        if (part.Role is RequirementRoles.Towards) return MermaidWriting.Only(caret, text, char.IsAsciiLetter);

        return null;
    }
}
