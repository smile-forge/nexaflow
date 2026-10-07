using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Block;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Block;

/// <summary>
/// What an edit means in a block diagram: a label is put in quotes to hold what would end it, and what an id cannot hold is dropped.
/// Every other key does what it does anywhere.
/// </summary>
internal sealed class BlockEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit)
    {
        var change = DiagramWriting.Typed(edit, Escaping) ?? OrdinaryEdits.Keyed(edit);

        return change is null ? null : DiagramRenames.AtEveryMention(edit, change, BlockRoles.Id);
    }

    /// <summary>
    /// A label is put in quotes to hold a quote, a bracket closing it or a comment. An id, a class and a direction are written bare
    /// and cannot be quoted at all, so what they cannot hold is dropped; so is anything but a digit in a width.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        if (part.Role is BlockRoles.Id or BlockRoles.Class or BlockRoles.Direction) return MermaidWriting.Only(caret, text, BlockGrammar.Bare);
        if (part.Parent is { Kind: MermaidKinds.Amount }) return MermaidWriting.Only(caret, text, char.IsAsciiDigit);

        return null;
    }
}
