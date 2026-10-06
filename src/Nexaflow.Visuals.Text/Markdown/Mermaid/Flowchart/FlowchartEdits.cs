using System;

using System.Collections.Generic;
using System.Linq;
using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Markdown.Mermaid.Flowchart;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid.Flowchart;

/// <summary>
/// What an edit means in a flowchart, and in a swimlane, which is a flowchart laid out in lanes: a label is put in quotes to hold
/// what would end it, and what an id cannot hold is dropped. Every other key does what it does anywhere.
/// </summary>
internal sealed partial class FlowchartEdits : IOnEdit
{
    /// <inheritdoc/>
    public ContentChange? Edit(ContentEdit edit)
    {
        // A choice is the ribbon's, and nothing the chart did not offer is anything to write.
        if (edit.Kind == EditKind.Choosing) return Chosen(edit) ?? ContentChange.Stay(edit.State);

        var change = DiagramWriting.Typed(edit, Escaping) ?? OrdinaryEdits.Keyed(edit);

        return change is null ? null : Renamed(edit, change);
    }

    /// <summary>
    /// A name changed wherever it is written, rather than only where a reader typed.
    ///
    /// <para>
    /// What a node or a subgraph is called is how every other line says which one it means: a link joins it by name, and a
    /// <c>class</c>, a <c>style</c>, a <c>click</c> line and an <c>id@{ … }</c> line are each about a name. Changed in one place
    /// alone, every one of those would be about something that is not there — the links would join nothing and the chart would
    /// come apart. So the change a reader made in one of them is made in all of them, as one edit, which is also one undo.
    /// </para>
    /// <para>
    /// A name emptied is left alone. Writing nothing into every mention of it would take the chart apart just as surely, and what
    /// a reader means by backing over the last letter of a name is not something to guess at.
    /// </para>
    /// </summary>
    private static ContentChange Renamed(ContentEdit edit, ContentChange change)
    {
        // The run being written into, which is not always the one the piece under the caret was drawn from: a node with a label
        // draws its label and never its name, so a reader typing in the name is typing where nothing is drawn.
        if (OrdinaryEdits.Written(edit) is not { Role: FlowchartRoles.Id, Kind: Kinds.Words } named) return change;
        if (named.Text is not { Length: > 0 } was || Called(named, change) is not { Length: > 0 } now || now == was) return change;

        var writes = new List<ContentWrite>(change.Writes);
        var caret = change.Caret;

        foreach (var mention in edit.Root.SelfAndDescendants())
        {
            if (mention.Kind != Kinds.Words || mention.Role != FlowchartRoles.Id) continue;
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
    /// A label is put in quotes to hold a quote, a bracket closing it or a comment; what is written on a link is quoted to hold
    /// whatever would close the link early. An id, a class and a way are written bare and cannot be quoted at all, so what they
    /// cannot hold is dropped, and nothing but a digit goes where a link is numbered.
    /// </summary>
    internal static MermaidWriting? Escaping(ContentPart part, int caret, string text)
    {
        if (part.Parent is { Kind: FlowchartKinds.Saying } saying) return Quoted(saying, part, caret, text);
        if (MermaidWriting.Escape(part, caret, text) is { } escaped) return escaped;

        if (part.Role is FlowchartRoles.Id or FlowchartRoles.Class or FlowchartRoles.Link)
            return Bared(part, caret, text, FlowchartGrammar.Subgraphed(part));
        if (part.Role is FlowchartRoles.Target or FlowchartRoles.Curve or FlowchartRoles.Call)
            return MermaidWriting.Only(caret, text, FlowchartGrammar.Bare);

        if (part.Role is FlowchartRoles.Towards) return MermaidWriting.Only(caret, text, char.IsAsciiLetter);
        if (part.Role is FlowchartRoles.Index) return MermaidWriting.Only(caret, text, char.IsAsciiDigit);

        return null;
    }

    /// <summary>
    /// Text written into a name that is written bare and cannot be quoted at all: a character goes in only where what the name would
    /// then say still reads as the name, and whatever would not is dropped, since there is nowhere to put it.
    ///
    /// <para>
    /// What follows the character is the rest of what is being written as well as the rest of the line. A name written in words
    /// takes a space at the end of it too, a word at a time being how one is written: the space belongs to the line until the next
    /// word arrives, and the line reads either way.
    /// </para>
    /// </summary>
    /// <param name="worded">Whether it is a name written in words — a subgraph's own — rather than an id, a class or a link's.</param>
    private static MermaidWriting? Bared(ContentPart part, int caret, string text, bool worded)
    {
        Func<string, int, int> ends = worded ? FlowchartGrammar.Titled : FlowchartGrammar.Ends;
        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var beyond = said[at..] + Following(part);
        var kept = said[..at];
        var written = string.Empty;

        for (var character = 0; character < text.Length; character++)
        {
            var tried = kept + text[character];
            var wanted = worded ? tried.TrimEnd(' ', '\t').Length : tried.Length;

            if (ends(tried + text[(character + 1)..] + beyond, 0) < wanted) continue;

            kept = tried;
            written += text[character];
        }

        return written == text ? null : new MermaidWriting(caret, caret, written, caret + written.Length);
    }

    /// <summary>What is written after a part, which is what a character typed at the end of it would run into.</summary>
    private static string Following(ContentPart part)
    {
        var top = part;
        while (top.Parent is { } holder) top = holder;

        // Print, not Text: a branch holds no text of its own, and what is written after a part is the source it stands in.
        var written = top.Print();
        var at = part.End - top.Start;

        return at >= 0 && at <= written.Length ? written[at..] : string.Empty;
    }

    /// <summary>
    /// What is typed into what is written on a link, put in quotes where it would otherwise close the link early — which is how
    /// Mermaid holds those characters there too.
    /// </summary>
    private static MermaidWriting? Quoted(ContentPart saying, ContentPart part, int caret, string text)
    {
        var said = part.Kind == Kinds.Hole ? string.Empty : part.Text;
        var at = Math.Clamp(caret - part.Start, 0, said.Length);
        var (before, after) = (said[..at] + text, said[at..]);
        var whole = before + after;

        var closes = whole.Contains("--", StringComparison.Ordinal) || whole.Contains("==", StringComparison.Ordinal)
                     || whole.Contains(".-", StringComparison.Ordinal) || whole.Contains("%%", StringComparison.Ordinal)
                     || whole.Contains('"');

        return closes ? MermaidWriting.Quoting(saying.Start, saying.End, before, after) : null;
    }
}
