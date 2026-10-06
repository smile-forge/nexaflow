using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.C4;
using Nexaflow.Markdown.Mermaid.Sequence;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.C4;

/// <summary>What an edit means in a C4 diagram: an argument holds what does not close it. Every other key does what it does anywhere.</summary>
internal sealed class C4Edits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit) => DiagramWriting.Typed(edit, Escaping);

    /// <inheritdoc cref="Escaping(ContentPart, int, string, DiagramEscaping?)"/>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text) => Escaping(part, caret, text, within: null);

    /// <summary>
    /// An argument in quotes holds anything but a quote; a bare one holds anything that does not close it; and a name holds what a
    /// name holds, since the same name is written in a native <c>note over</c> where it cannot be quoted at all. Anything else is
    /// what <paramref name="within"/> says, for a diagram written within another.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text, DiagramEscaping? within)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        // In quotes anything but a quote goes in as it is, and the quote has already been written as its entity code.
        if (part.Parent?.Children.Any(child => child.Role == Roles.Open && child.Text == "\"") ?? false) return null;

        var role = part.Kind == Kinds.Hole ? part.Parent?.Role ?? part.Role : part.Role;

        if (role is C4Roles.Value) return MermaidWriting.Only(caret, text, C4Grammar.Argued);
        if (role is C4Roles.Key or C4Roles.Macro) return MermaidWriting.Only(caret, text, MermaidLine.Letter);
        if (role is C4Roles.Aside) return null;

        // A name is written bare in a macro's argument list, so what neither that nor a native line can hold is dropped — the
        // same name is used in both, and it has to read in each.
        if (role is C4Roles.Alias or SequenceRoles.Id) return MermaidWriting.Only(caret, text, C4Grammar.Bare);

        return within?.Invoke(part, caret, text);
    }
}
