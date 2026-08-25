namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// What an edit expressed against the tree produced: the source it made, and where the caret sits in it.
/// <para>
/// Both together, because they cannot be worked out separately. Re-bracing an argument moves every
/// character after it, so a caller handed only the new source would have to re-derive where the caret
/// went from the shape of the change it did not make — and getting that wrong is invisible until the
/// next keystroke lands somewhere absurd.
/// </para>
/// <para>
/// The tree itself is not here, and does not need to be: it is a reading of the source, so the source
/// coming back changed <em>is</em> the tree changed. Re-reading it is what the caller does next, and is
/// how the layout and the picture follow from one edit.
/// </para>
/// </summary>
/// <param name="Latex">The whole formula, as it now reads.</param>
/// <param name="Caret">Where the caret belongs in it.</param>
/// <param name="Wrote">
/// The stretch of <paramref name="Latex"/> this edit put there — what to select, or to draw in another
/// colour while it is still being placed. Not the same as the length of what was handed in: an argument
/// that had to be wrapped moved it, and a command written against a letter gained a space to keep its
/// name. Only the edit knows what it really wrote.
/// </param>
public readonly record struct LatexWrite(string Latex, int Caret, LatexRange Wrote);
