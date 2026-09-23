using System;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>Where an edit landed: state, layout, and which place the caret sits at.</summary>
/// <param name="State">Source, caret, selection, and the stretch shown as its own characters.</param>
/// <param name="Laid">What the builder made of it — the tree every question about the picture is asked of.</param>
/// <param name="At">The caret's place, or -1 where it is standing at none of them.</param>
public readonly record struct Landing(EditState State, Laid Laid, int At)
{
    /// <summary>True when the caret sits inside whatever ends at its offset, not stepped out past it — a 3 typed just inside the exponent of <c>x^2</c> makes it twenty-three; one mark to the right, it doesn't.</summary>
    public bool Innermost => At < 0 || At == Laid.Root.StopAt(State.Caret);
}

/// <summary>A content kind, end to end: how its source becomes a picture and how writing into that picture is interpreted. One interface owns the whole chain because the parts share state by identity — e.g. a formula's on-edit handler needs the exact parse tree its layout was built from, not a fresh reparse. The element itself (measuring, painting, hit-testing, caret, selection, keys) never changes with content kind. Only <see cref="Lay"/> must be implemented; the rest default to ordinary text-editing behavior.</summary>
public interface IContent
{
    /// <summary>
    /// Lays the source out to fit the given room, at its standard size. Takes the full edit state rather than a
    /// string because a stretch shown as its own characters must be set into the layout, not painted over it.
    ///
    /// <para>
    /// Nothing here knows the screen. A layout is in the content's own units and the element scales it as it
    /// paints, so the same source and the same room give the same tree on any display — which is also what makes
    /// a laid-out tree something a test can measure.
    /// </para>
    /// </summary>
    /// <param name="readOnly">Whether writable placeholders (e.g. a formula's empty argument) should be drawn.</param>
    Laid Lay(EditState state, double room, bool readOnly);

    /// <summary>What writing <paramref name="text"/> means here; null leaves it to the element, which splices at the caret. Routed through here rather than a background pass over the source, so context can transform it in place (a backslash opens a command, letters extend it) without losing the caret or touching unedited source.</summary>
    EditState? Typing(Landing landing, string text) => null;

    /// <summary>What ending whatever is half-written means — Space and Enter both arrive here. Kept separate from <see cref="Typing"/> because Enter carries no character; folding them together would type a space for Enter.</summary>
    EditState Settle(Landing landing, string separator) => landing.State.Write(separator);

    /// <summary>What taking back the character on one side of the caret means (before it for backspace, after it for delete); null leaves it to the element, which takes one character or a whole construct. Where there's nothing to take on that side — an empty placeholder, or just before the quote that closes a word — the ordinary answer takes the surrounding construct instead.</summary>
    /// <param name="forward">Delete rather than backspace.</param>
    EditState? Erasing(Landing landing, bool forward) => null;

    /// <summary>Final pass over an edit once the content has applied its own side effects (e.g. a rename propagated to every use). Called for every edit regardless of how it was made — typed, erased, pasted, or dragged.</summary>
    /// <param name="before">Where the edit landed — the state it was made to, and what was drawn of it.</param>
    /// <param name="after">The state it made.</param>
    EditState Edited(Landing before, EditState after) => after;
}

/// <summary>Content that is a builder and nothing else, which is most of it.</summary>
public sealed class Content(Func<EditState, double, Laid> lay) : IContent
{
    /// <summary>Wraps a builder as content, for a kind that has nothing to say about writing.</summary>
    public static IContent Of(Func<EditState, double, Laid> lay) => new Content(lay);

    /// <inheritdoc/>
    public Laid Lay(EditState state, double room, bool readOnly) => lay(state, room);
}
