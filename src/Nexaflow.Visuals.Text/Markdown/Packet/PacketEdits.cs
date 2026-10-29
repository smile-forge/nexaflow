using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Packet;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.Packet;

/// <summary>
/// What an edit means in a packet: what is typed into a label is kept by a backslash where it would end it, and a label still
/// to write is put in quotes as it is written into; a bit number takes only digits. Every other key does what it does anywhere.
/// </summary>
internal sealed class PacketEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// Text typed at <paramref name="caret"/> in <paramref name="part"/> as a packet writes it there — or null where it goes in as
    /// it is. Spelled as the parser spells a label (<see cref="PacketParser.Escaped"/>), so a keystroke and a paste agree.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (part.Kind == Kinds.Number || part.Parent is { Kind: MermaidKinds.Amount }) return MermaidWriting.Only(caret, text, char.IsAsciiDigit);

        if (PacketParser.LabelOf(part) is not { } label) return null;

        if (PacketParser.Opened(label) is { } quote)
        {
            var kept = PacketParser.Escaped(text, quote);
            return kept == text ? null : new MermaidWriting(caret, caret, kept, caret + kept.Length);
        }

        // Nothing is written after the colon yet: what is typed there is a label, so it goes in its quotes, the caret inside them.
        var opened = PacketParser.Opening + PacketParser.Escaped(text, PacketParser.Opening);
        return new MermaidWriting(caret, caret, opened + PacketParser.Opening, caret + opened.Length);
    }
}
