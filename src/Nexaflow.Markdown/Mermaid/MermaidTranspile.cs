using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;

namespace Nexaflow.Markdown.Mermaid;

/// <summary>
/// How a diagram writes back to its own source: what each place can hold, and how what it cannot hold is spelled so that
/// the line still reads.
/// </summary>
/// <remarks>
/// Only the places with a rule of their own are named. A run of words holds words, so what was typed into one goes in as
/// it was typed; that is the default and saying it again here would only be a way of getting it wrong.
/// </remarks>
public sealed class MermaidTranspile : ITranspile
{
    /// <inheritdoc/>
    public ContentChange? Write(ContentChange change)
    {
        if (!change.Writes.Any(write => write.Meant)) return change;

        var writes = new List<ContentWrite>(change.Writes.Count);
        var caret = change.Caret;
        var moved = 0;
        var grown = 0;

        foreach (var write in change.Writes.OrderBy(write => write.Start))
        {
            // Where the write stands once the ones before it have been made, and how far it moves what follows.
            var at = write.Start + moved;
            moved += write.Text.Length - write.Length;

            if (!write.Meant) { writes.Add(write); continue; }
            if (Spelled(write.Part!, write.Text) is not { } said) return null;

            writes.Add(new ContentWrite(write.Start, write.Length, said) { Part = write.Part });

            if (change.Caret >= at && change.Caret <= at + write.Text.Length)
                caret = at + grown + (Spelled(write.Part!, write.Text[..(change.Caret - at)])?.Length ?? said.Length);
            else if (change.Caret > at + write.Text.Length)
                caret += said.Length - write.Text.Length;

            grown += said.Length - write.Text.Length;
        }

        return change with { Writes = writes, Caret = caret };
    }

    /// <summary>
    /// What <paramref name="text"/> is written as where it is going — itself, wherever the place can hold it — or null
    /// where it cannot go there at all and nothing should be written.
    /// </summary>
    private static string? Spelled(ContentPart part, string text) => part.Kind switch
    {
        // In quotes a quote is its entity code, and a line break is the mark this language writes one with.
        MermaidKinds.Quoted => MermaidText.Quoted(text.ReplaceLineEndings(MermaidParser.LineBreak)),

        // A title is one line however many the reader pasted.
        MermaidKinds.Title => text.ReplaceLineEndings(" "),

        // A number holds a number. Read as this language writes one, which is not as a reader's own language would.
        MermaidKinds.Amount or Kinds.Number =>
            text.Length > 0 && double.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)
                ? text : null,

        _ => text,
    };
}
