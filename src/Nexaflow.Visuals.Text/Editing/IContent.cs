using System;

namespace Nexaflow.Visuals.Text.Editing;

/// <summary>
/// What an edit lands in: the text as it stands, what is drawn, and which of the places the caret is
/// standing at.
/// </summary>
/// <param name="State">Source, caret, selection, and the stretch shown as its own characters.</param>
/// <param name="Laid">What the builder made of it — the tree every question about the picture is asked of.</param>
/// <param name="At">The caret's place, or -1 where it is standing at none of them.</param>
public readonly record struct Landing(EditState State, Laid Laid, int At)
{
    /// <summary>
    /// Whether the caret is at the innermost place at its offset — inside whatever ends there, rather
    /// than stepped out past it.
    ///
    /// <para>
    /// The one thing a place says that an offset cannot, and the reason an edit is handed where it landed
    /// rather than only what it says: a 3 typed just inside the exponent of <c>x^2</c> makes it
    /// twenty-three, and the same keystroke one mark to the right follows the whole script.
    /// </para>
    /// </summary>
    public bool Innermost => At < 0 || At == Laid.Root.StopAt(State.Caret);
}

/// <summary>
/// A kind of content, whole: how source becomes a picture, and what writing into that picture means.
///
/// <para>
/// <strong>One thing owns the chain.</strong> Reading the source, whatever passes are made over the
/// parse, engraving or typesetting it into a tree, and how an edit is transformed by what it lands in —
/// these are all the same knowledge, and they need each other's working. The formula's on-edit handler
/// asks the parse tree the layout was built from; asking a freshly read one would answer about pieces
/// that merely look right, because parts are matched by identity. So the chain is held together here and
/// each part is called with what it needs.
/// </para>
/// <para>
/// <strong>The element is not in it.</strong> An element measures, paints, hit-tests, holds a caret and a
/// selection, and forwards keys — none of which changes with the kind of content, and none of which has
/// any bearing on where a control word's name stops. Everything that differs is behind this interface,
/// which is what makes a thing nobody has thought of yet — notes inside a formula, drawn under a
/// barcode — cost one implementation and no other change.
/// </para>
/// <para>
/// Only <see cref="Lay"/> has to be written. Most content is typed into exactly as it reads, so both
/// writing rules have an ordinary answer already; a barcode and a tune take neither.
/// </para>
/// </summary>
public interface IContent
{
    /// <summary>
    /// Lays the source out to fit the room it is given.
    ///
    /// <para>
    /// The <em>state</em> rather than the string, because what is being typed changes what is drawn: a
    /// stretch shown as its own characters is set into the layout rather than painted over it, which is
    /// the only way the rest of the content can be laid out knowing it is there.
    /// </para>
    /// </summary>
    /// <param name="readOnly">
    /// Whether anybody can type into this. Content that offers a place to write — a formula's empty
    /// argument, waiting to be filled — only draws one where there is a reader to fill it.
    /// </param>
    Laid Lay(EditState state, double room, double pixelsPerDip, bool readOnly);

    /// <summary>
    /// What writing <paramref name="text"/> means here. Null leaves it to the element, which splices the
    /// characters in where the caret is.
    ///
    /// <para>
    /// An edit does not reach the source directly: it passes through here, where it can be transformed by
    /// the context it arrives in. A backslash opens a command and letters extend it; a 3 lands inside the
    /// exponent it was typed into.
    /// </para>
    /// <para>
    /// <strong>Not a pass over the source.</strong> A pass runs on every render, so it would rewrite a
    /// document nobody had edited — source arriving from a file included — and by the time it ran the
    /// caret would be gone, taking with it the only evidence of what was meant. What is in the source and
    /// what an edit meant are different questions.
    /// </para>
    /// </summary>
    EditState? Typing(Landing landing, string text) => null;

    /// <summary>
    /// What ending whatever is half-written means — space and Enter both arrive here.
    ///
    /// <para>
    /// Separate from <see cref="Typing"/> because it is a different edit rather than another way of
    /// saying the same one: Enter carries no character at all, so folding the two together would have it
    /// type a space. The ordinary answer writes the separator, which is what a space has always meant.
    /// </para>
    /// </summary>
    EditState Settle(Landing landing, string separator) => landing.State.Write(separator);
}

/// <summary>Content that is a builder and nothing else, which is most of it.</summary>
public sealed class Content(Func<EditState, double, double, Laid> lay) : IContent
{
    /// <summary>Wraps a builder as content, for a kind that has nothing to say about writing.</summary>
    public static IContent Of(Func<EditState, double, double, Laid> lay) => new Content(lay);

    /// <inheritdoc/>
    public Laid Lay(EditState state, double room, double pixelsPerDip, bool readOnly) =>
        lay(state, room, pixelsPerDip);
}
