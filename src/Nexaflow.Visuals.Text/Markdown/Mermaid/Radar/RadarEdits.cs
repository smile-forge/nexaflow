using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Radar;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Radar;

/// <summary>What an edit means in a radar chart: a name is put in quotes to hold what a bare one cannot. Every other key does what it does anywhere.</summary>
internal sealed class RadarEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// A bare name holds a word starting with a letter or an underscore, and is put in quotes to hold anything else; see
    /// <see cref="MermaidWriting.Escape"/> for quotes and labels.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text) =>
        MermaidWriting.Escape(part, caret, text, (_, said) => RadarGrammar.Bare(said));
}
