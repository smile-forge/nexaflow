using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Sankey;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Sankey;

/// <summary>
/// What an edit means in a Sankey diagram: a name is put in quotes to hold a comma or a quote, and a value holds only a number.
/// Every other key does what it does anywhere.
/// </summary>
internal sealed class SankeyEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit)
    {
        var change = DiagramWriting.Typed(edit, Escaping) ?? OrdinaryEdits.Keyed(edit);

        return change is null ? null : Renamed(edit, change);
    }

    /// <summary>
    /// A node's name changed on every flow that names it.
    ///
    /// <para>
    /// Nothing declares a sankey node: it is there because a flow is written between it and another. So the same node is
    /// where one flow comes from and where another goes, and is named under two roles rather than one. That is why this is
    /// its own sweep and not <c>DiagramRenames.AtEveryMention</c>, which follows a single role: a diagram that does not fit
    /// the common case says so itself, rather than the common case growing a parameter for it.
    /// </para>
    /// </summary>
    private static ContentChange Renamed(ContentEdit edit, ContentChange change)
    {
        if (OrdinaryEdits.Written(edit) is not { Kind: Kinds.Words } named || !Names(named.Role)) return change;
        if (named.Text is not { Length: > 0 } was || NameAfter(named, change) is not { Length: > 0 } now || now == was) return change;

        var writes = new List<ContentWrite>(change.Writes);
        var caret = change.Caret;

        foreach (var mention in edit.Root.SelfAndDescendants())
        {
            if (mention.Kind != Kinds.Words || !Names(mention.Role)) continue;
            if (mention.Start == named.Start || mention.Derived || mention.Text != was) continue;

            writes.Add(new ContentWrite(mention.Start, mention.Length, now));

            // What is written before the caret moves it, and the caret is in the name the reader is typing.
            if (mention.Start < named.Start) caret += now.Length - was.Length;
        }

        return writes.Count == change.Writes.Count ? change : change with { Writes = writes, Caret = caret };
    }

    /// <summary>Whether a role names a node, which either end of a flow does.</summary>
    private static bool Names(string? role) => role is SankeyRoles.Source or SankeyRoles.Target;

    /// <summary>What a name says once a change is made, or null where the change writes nothing into it.</summary>
    private static string? NameAfter(ContentPart named, ContentChange change)
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
    /// A name holds anything but a comma and a quote as it is written; one given either is put in quotes, and a quote between those
    /// is written twice. A value is a number, so nothing else goes in one.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (part.Parent is { Kind: MermaidKinds.Amount })
            return MermaidWriting.Only(caret, text, character => char.IsAsciiDigit(character) || character is '.' or '-');
        if (part.Parent is not { } holder || part.Kind is not (Kinds.Words or Kinds.Hole)) return null;

        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var (before, after) = (said[..at] + text, said[at..]);

        // Inside quotes a quote is written twice and everything else goes in as it is.
        if (holder.Children.Any(child => child.Role == Roles.Open && child.Text == "\""))
        {
            if (!text.Contains('"')) return null;

            var doubled = text.Replace("\"", SankeyGrammar.Quoted, StringComparison.Ordinal);
            return new MermaidWriting(caret, caret, doubled, caret + doubled.Length);
        }

        if (!(before + after).Any(character => character is ',' or '"')) return null;

        // Written bare and no longer able to be: the whole name is written again, in quotes.
        var head = "\"" + before.Replace("\"", SankeyGrammar.Quoted, StringComparison.Ordinal);
        return new MermaidWriting(part.Start, part.Start + said.Length,
                                  head + after.Replace("\"", SankeyGrammar.Quoted, StringComparison.Ordinal) + "\"", part.Start + head.Length);
    }
}
