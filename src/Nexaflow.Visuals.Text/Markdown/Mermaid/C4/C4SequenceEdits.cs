using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.C4;

/// <summary>
/// What an edit means in a C4 dynamic diagram written as a sequence: what a C4 diagram's arguments hold, and on a sequence diagram's
/// own lines what they hold there. Every other key does what it does anywhere.
/// </summary>
internal sealed class C4SequenceEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>What a C4 diagram holds, and on a sequence diagram's line what a sequence diagram holds.</summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text) =>
        C4Edits.Escaping(part, caret, text, SequenceEdits.Escaping);
}
