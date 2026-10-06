using System;
using System.Collections.Generic;
using System.Linq;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Mermaid;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// What writing <paramref name="text"/> at <paramref name="caret"/>, in <paramref name="part"/> — a part of the diagram, or a hole
/// standing where one goes — is written as, where it cannot go in as it is without the line saying something else. Null where it
/// goes in as it is.
/// </summary>
internal delegate MermaidWriting? DiagramEscaping(ContentPart part, int caret, string text);

/// <summary>
/// What typing in a diagram comes to where it comes to the same in every diagram that asks for it — a helper each diagram's own
/// handler is made of, and nobody's handler.
///
/// <para>
/// A character a place cannot hold as itself is written as the diagram says (<see cref="DiagramEscaping"/>) — a name put in quotes,
/// a quote written as its entity code — so a character typed never stops a line reading. What an entity code went into is shown as
/// written for as long as the caret is in it, so the caret has the characters it stands between; and what is shown as written stays
/// shown as written while it is written in, growing to hold what is typed at its end.
/// </para>
/// <para>
/// Nothing here says what Enter, backspace or delete mean: that is each diagram's own, and a diagram that says nothing leaves them
/// to do what they do anywhere.
/// </para>
/// </summary>
internal static class DiagramWriting
{
    /// <summary>
    /// Text typed — a character, a paste, a space — as the diagram writes it where the caret is; null for Enter and a key taking
    /// characters back, and wherever the text goes in as it is.
    /// </summary>
    public static ContentChange? Typed(ContentEdit edit, DiagramEscaping? escaping)
    {
        var state = edit.State;
        var text = edit.Text;
        if (edit.Kind is not (EditKind.Typing or EditKind.Settling) || text is "\n" or "" || state.HasSelection) return null;

        var caret = state.Caret;
        var part = WrittenIn(edit, caret);

        if (part is null || escaping?.Invoke(part, caret, text) is not { } writing)
            return state.Raw is { } shown && shown.Holds(caret) ? ContentChange.Typed(state, text, shown with { End = shown.End + text.Length }) : null;

        var source = state.Source[..writing.Start] + writing.Text + state.Source[writing.End..];
        var grown = writing.Text.Length - (writing.End - writing.Start);

        // Where what was written in now stands: the part typed into, or the stretch written over less the quotes put round it.
        var (start, end) = writing.Start == writing.End
            ? (part.Start, part.End + grown)
            : (writing.Start + 1, writing.Start + writing.Text.Length - 1);

        RawZone? raw = state.Raw is { } zone && zone.Holds(caret) ? new RawZone(zone.Start, zone.End + grown)
                     : MermaidText.Decode(source[start..end]) != source[start..end] ? new RawZone(start, end)
                     : null;

        return ContentChange.Write(writing.Start, writing.End - writing.Start, writing.Text, writing.Caret, raw);
    }

    /// <summary>
    /// What the caret is writing in: a hole it stands in, or a run of words drawn as they were written — the part itself, where what
    /// was drawn is a slice of it or a line of it wrapped — or null.
    /// </summary>
    private static ContentPart? WrittenIn(ContentEdit edit, int caret)
    {
        var laid = edit.Laid;

        if (laid.Holes.FirstOrDefault(hole => hole.Sits().Start == caret) is { Exists: true } hole && Of(hole.Part) is { } holding && edit.Holds(holding))
            return holding;

        return Of(laid.Root.WordsAt(caret).Part) is { } words && edit.Holds(words) ? words : null;

        static ContentPart? Of(ISourcePart? part) => part switch
        {
            ContentPart own => own,
            IStandsFor standing => Of(standing.Of),
            _ => null,
        };
    }
}
