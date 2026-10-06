using System;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Sequence;

/// <summary>What an edit means in a sequence diagram: what would end a name is dropped. Every other key does what it does anywhere.</summary>
internal sealed class SequenceEdits : IOnEdit
{
    /// <summary>What a name typed into never holds, since a name cannot be quoted and there would be nowhere to put it.</summary>
    private const string Never = ":,;<>+@-|/\\%";

    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <summary>
    /// A name holds anything that does not end one — the characters an arrow is written with, and what goes between a message's
    /// parts. What a message says, what a note says and what a frame holds under are written to the end of the line and hold
    /// anything at all.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        // In quotes anything but a quote goes in as it is, and the quote has already been written as its entity code.
        if (part.Parent?.Children.Any(child => child.Role == Roles.Open && child.Text == "\"") ?? false) return null;

        // A hole stands for what is not written yet, so what may go in it is what holds it.
        var role = part.Kind == Kinds.Hole ? part.Parent?.Role ?? part.Role : part.Role;

        if (role is SequenceRoles.Id) return Written(part, caret, text);
        if (role is SequenceRoles.Key or SequenceRoles.Type) return MermaidWriting.Only(caret, text, MermaidLine.Letter);
        if (role is SequenceRoles.Menu) return MermaidWriting.Only(caret, text, character => character != '@');
        if (role is SequenceRoles.Url) return MermaidWriting.Only(caret, text, SequenceGrammar.Linked);
        if (role is SequenceRoles.Colour) return MermaidWriting.Only(caret, text, Tinted);

        return null;
    }

    /// <summary>Whether a character may go in a name at all, wherever in one it is written.</summary>
    private static bool Held(char character) => !Never.Contains(character) && !char.IsControl(character);

    /// <summary>What a colour is written with: a name, a <c>#</c> and its digits, or a function and its parts.</summary>
    private static bool Tinted(char character) =>
        char.IsLetterOrDigit(character) || character is '#' or '(' or ')' or ',' or '.' or '%';

    /// <summary>
    /// A name typed into: the whole of what is typed goes in where the name still ends where it did, so a hyphen joins a name — and
    /// where it would not, what would end one is dropped instead.
    /// </summary>
    private static MermaidWriting? Written(ContentPart part, int caret, string text)
    {
        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var whole = said[..at] + text + said[at..];

        return SequenceGrammar.Ident(whole, 0) == whole.Length && !whole.Any(char.IsControl)
            ? null
            : MermaidWriting.Only(caret, text, Held);
    }
}
