using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Class;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Class;

/// <summary>What an edit means in a class diagram: what an id cannot hold is dropped. Every other key does what it does anywhere.</summary>
internal sealed class ClassEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit)
    {
        if (Braced(edit) is { } held) return held;

        var change = DiagramWriting.Typed(edit, Escaping) ?? OrdinaryEdits.Keyed(edit);

        return change is null ? null : DiagramRenames.AtEveryMention(edit, change, ClassRoles.Id);
    }

    /// <summary>
    /// A brace typed among a class's members — past the line opening them and before the brace closing them — which closes them wherever
    /// it stands, as Mermaid reads a body: nothing is written, so what is being written stays the one body. Null where the edit is not one.
    /// </summary>
    private static ContentChange? Braced(ContentEdit edit)
    {
        var state = edit.State;
        if (edit.Kind != EditKind.Typing || edit.Text is not ("{" or "}") || state.HasSelection) return null;

        var caret = state.Caret;
        var body = edit.Root.SelfAndDescendants().FirstOrDefault(part => part.Kind == ClassKinds.Body && part.Start < caret && caret <= part.End);
        if (body?.Children.FirstOrDefault() is not { } opened || caret < opened.End) return null;
        if (body.Children.LastOrDefault(child => child.Kind == ClassKinds.Shut) is { } shut && caret > shut.Start) return null;

        return ContentChange.Write(caret, 0, string.Empty, caret, null);
    }

    /// <summary>
    /// A member and what is written on a relation run to the end of their line and hold anything but a comment. An id and a class
    /// are written bare — in backticks, where one holds what a bare id cannot — and a way and a target are words.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        if (part.Role is ClassRoles.Id or ClassRoles.Class)
            return Backed(part)
                ? MermaidWriting.Only(caret, text, character => character != '`')
                : MermaidWriting.Only(caret, text, ClassParser.Bare);

        if (part.Role is ClassRoles.Space) return MermaidWriting.Only(caret, text, ClassParser.Dotted);

        // A brace closes a class's members wherever it stands, as Mermaid reads a body, so a member or an annotation cannot hold one.
        if (part.Role is ClassRoles.Member or ClassRoles.Kind) return MermaidWriting.Only(caret, text, character => character is not ('{' or '}'));

        // A function named after call is written bare, and the quotes past it open what the class says while pointed at.
        if (part.Role is ClassRoles.Call) return MermaidWriting.Only(caret, text, character => character != '"');
        if (part.Role is ClassRoles.Towards or ClassRoles.Target) return MermaidWriting.Only(caret, text, char.IsAsciiLetter);

        return null;
    }

    /// <summary>Whether a name is written between backticks, which hold anything a bare id cannot.</summary>
    private static bool Backed(ContentPart part) =>
        part.Parent?.Children.Any(child => child.Role == Roles.Open && child.Text == ClassParser.Backtick) ?? false;
}
