using System;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Timeline;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Timeline;

/// <summary>What an edit means in a timeline: what would split or close a line goes in as its entity code. Every other key does what it does anywhere.</summary>
internal sealed class TimelineEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// Every colon splits a period from its events, and a <c>%%</c> closes the line, so both go in as the entity codes standing for
    /// them — which is how Mermaid writes a colon inside what something says.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (part.Parent is not { Kind: TimelineKinds.Text } || !text.Any(character => character is ':' or '%')) return null;

        var written = text.Replace(":", "#colon;", StringComparison.Ordinal).Replace("%", "#37;", StringComparison.Ordinal);
        return new MermaidWriting(caret, caret, written, caret + written.Length);
    }
}
