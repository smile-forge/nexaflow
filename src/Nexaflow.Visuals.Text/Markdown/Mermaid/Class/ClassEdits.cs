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

        return change is null ? null : Renamed(edit, change);
    }

    /// <summary>
    /// A class's name changed wherever it is written, rather than only where a reader typed.
    ///
    /// <para>
    /// What a class is called is how every other line says which one it means: a relation joins two of them by name, a
    /// <c>note for</c> line says which one it is against, a <c>cssClass</c> line names the ones taking a class, and the
    /// class holding the members is itself named. Changed in one place alone, the rest would be about a class that is not
    /// there. So the change a reader made in one of them is made in all of them, as one edit and one undo.
    /// </para>
    /// <para>
    /// A name emptied is left alone: writing nothing into every mention would take the diagram apart just as surely, and
    /// what a reader means by backing over the last letter of a name is not something to guess at.
    /// </para>
    /// </summary>
    private static ContentChange Renamed(ContentEdit edit, ContentChange change)
    {
        if (OrdinaryEdits.Written(edit) is not { Role: ClassRoles.Id, Kind: Kinds.Words } named) return change;
        if (named.Text is not { Length: > 0 } was || Called(named, change) is not { Length: > 0 } now || now == was) return change;

        var writes = new List<ContentWrite>(change.Writes);
        var caret = change.Caret;

        foreach (var mention in edit.Root.SelfAndDescendants())
        {
            if (mention.Kind != Kinds.Words || mention.Role != ClassRoles.Id) continue;
            if (mention.Start == named.Start || mention.Derived || mention.Text != was) continue;

            writes.Add(new ContentWrite(mention.Start, mention.Length, now));

            // What is written before the caret moves it, and the caret is in the name the reader is typing.
            if (mention.Start < named.Start) caret += now.Length - was.Length;
        }

        return writes.Count == change.Writes.Count ? change : change with { Writes = writes, Caret = caret };
    }

    /// <summary>What a name says once a change is made, or null where the change writes nothing into it.</summary>
    private static string? Called(ContentPart named, ContentChange change)
    {
        var said = named.Text;
        var moved = 0;
        var written = false;

        foreach (var write in change.Writes.OrderBy(write => write.Start))
        {
            if (write.Start < named.Start || write.Start + write.Length > named.End) continue;

            var at = write.Start - named.Start + moved;

            said = said[..at] + write.Text + said[(at + write.Length)..];
            moved += write.Text.Length - write.Length;
            written = true;
        }

        return written ? said : null;
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
