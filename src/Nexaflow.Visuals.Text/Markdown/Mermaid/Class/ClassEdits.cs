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
        var change = DiagramWriting.Typed(edit, Escaping) ?? OrdinaryEdits.Keyed(edit);

        return change is null ? null : DiagramRenames.AtEveryMention(edit, change, ClassRoles.Id);
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
                : MermaidWriting.Only(caret, text, ClassGrammar.Bare);

        if (part.Role is ClassRoles.Space) return MermaidWriting.Only(caret, text, ClassGrammar.Dotted);

        // A function named after call is written bare, and the quotes past it open what the class says while pointed at.
        if (part.Role is ClassRoles.Call) return MermaidWriting.Only(caret, text, character => character != '"');
        if (part.Role is ClassRoles.Towards or ClassRoles.Target) return MermaidWriting.Only(caret, text, char.IsAsciiLetter);

        return null;
    }

    /// <summary>Whether a name is written between backticks, which hold anything a bare id cannot.</summary>
    private static bool Backed(ContentPart part) =>
        part.Parent?.Children.Any(child => child.Role == Roles.Open && child.Text == ClassGrammar.Backtick) ?? false;
}
