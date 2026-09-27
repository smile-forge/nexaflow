using System;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Journey;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Journey;

/// <summary>What an edit means in a user journey: what would split or close a line goes in as its entity code. Every other key does what it does anywhere.</summary>
internal sealed class JourneyEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// Every colon splits a task from its score and its actors, a comma one actor from the next, and a <c>%%</c> closes the line, so
    /// each goes in as the entity code standing for it.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (part.Parent is not { Kind: JourneyKinds.Text or MermaidKinds.Name } || !text.Any(character => character is ':' or '%' or ',')) return null;

        var written = text.Replace(":", "#colon;", StringComparison.Ordinal)
                          .Replace("%", "#37;", StringComparison.Ordinal)
                          .Replace(",", "#44;", StringComparison.Ordinal);
        return new MermaidWriting(caret, caret, written, caret + written.Length);
    }
}
