using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// Carrying a rename to every place a thing is named, for a diagram that names things by a role of its own.
///
/// <para>
/// A diagram's edit handler calls this; nothing calls the handler. It decides for itself whether a rename carries at
/// all, which role names the thing, and what else the change should hold — so a diagram whose names work some other way
/// simply does not call it, and one that needs two roles calls it twice.
/// </para>
/// </summary>
internal static class DiagramRenames
{
    /// <summary>
    /// <paramref name="change"/> with the name it writes written at every other place <paramref name="role"/> names the
    /// same thing, as one edit and so one undo.
    ///
    /// <para>
    /// What a thing is called is how every other line says which one it means. Changed in one place alone, the rest are
    /// about something that is not there — the links join nothing and the diagram comes apart.
    /// </para>
    /// <para>
    /// Three things are left alone. A change that writes nothing into the name: the reader is typing somewhere else. A
    /// name emptied: writing nothing into every mention would take the diagram apart just as surely, and what a reader
    /// means by backing over the last letter is not something to guess at. And a mention the diagram worked out rather
    /// than read, which is not written anywhere a write could reach.
    /// </para>
    /// </summary>
    /// <param name="role">The role the diagram names the thing by, such as <c>FlowchartRoles.Id</c>.</param>
    public static ContentChange AtEveryMention(ContentEdit edit, ContentChange change, string role)
    {
        
        // The run being written into, which is not always the one the piece under the caret was drawn from: a thing with
        // a label draws its label and never its name, so a reader typing in the name types where nothing is drawn.
        if (OrdinaryEdits.Written(edit) is not { Kind: Kinds.Words } named || named.Role != role) return change;
        if (named.Text is not { Length: > 0 } was || Called(named, change) is not { Length: > 0 } now || now == was) return change;

        var writes = new List<ContentWrite>(change.Writes);
        var caret = change.Caret;

        foreach (var mention in edit.Root.SelfAndDescendants())
        {
            if (mention.Kind != Kinds.Words || mention.Role != role) continue;
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
}
