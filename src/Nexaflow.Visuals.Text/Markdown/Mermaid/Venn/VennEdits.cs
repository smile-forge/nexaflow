using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Venn;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Venn;

/// <summary>What an edit means in a Venn diagram: a name is put in quotes to hold what a bare one cannot. Every other key does what it does anywhere.</summary>
internal sealed class VennEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// A bare name holds a word — letters, digits, <c>_</c> and <c>-</c>, and a set's starts with a letter or an underscore — and is
    /// put in quotes to hold anything else; see <see cref="MermaidWriting.Escape"/> for quotes and labels.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text) =>
        MermaidWriting.Escape(part, caret, text, (name, said) => VennGrammar.Bare(said, set: name.Parent?.Kind == VennKinds.Set));
}
