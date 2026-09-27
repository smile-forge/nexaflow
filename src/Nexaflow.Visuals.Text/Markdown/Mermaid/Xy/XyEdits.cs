using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Xy;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Xy;

/// <summary>What an edit means in an XY chart: a word is put in quotes to hold what a bare one cannot. Every other key does what it does anywhere.</summary>
internal sealed class XyEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>A bare word holds no space, comma, bracket, quote or arrow, and is put in quotes to hold one; see <see cref="MermaidWriting.Escape"/>.</summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text) =>
        MermaidWriting.Escape(part, caret, text, (_, said) => said.Length > 0 && said.All(XyGrammar.Letter));
}
