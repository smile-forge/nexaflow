using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// The one rule LaTeX has about how it is written, which nothing else on the page shares.
///
/// <para>
/// Everything else about typing into a formula — what a selection is, what replacing it means, where the
/// caret lands, what backspace does behind something several characters produced — is
/// <see cref="EditState"/>'s, and is the same code for a tune and a barcode. This one is not: it is about
/// control words, which are a fact about TeX.
/// </para>
/// <para>
/// There is deliberately no "commit". Settling a half-written command is what typing a non-letter after
/// it already does, and a second way to say the same thing is a second thing to keep in step. Nor is
/// there anything here that writes a separator the reader did not type: where a control word ends is a
/// question about reading LaTeX, which is the builder's, and the builder is told which stretch is still
/// being written.
/// </para>
/// </summary>
internal static class LatexWriting
{
    /// <summary>
    /// What typing a character does, where LaTeX has something to say about it.
    ///
    /// <para>
    /// A backslash opens a stretch shown as itself, and letters extend it: that is TeX's own rule for a
    /// control word, and it is why <c>\alpha</c> shows as itself while you write it instead of flickering
    /// through four different failed parses. Anything that is not a letter ends the word — so
    /// <c>\alpha+</c> settles the command first and then types the plus, exactly as TeX would read it.
    /// </para>
    /// <para>
    /// Null everywhere else, which leaves the character to be typed as any character is.
    /// </para>
    /// </summary>
    public static EditState? Typing(this EditState state, char character)
    {
        if (state.Raw is { } zone && zone.Holds(state.Caret))
            return char.IsLetter(character)
                ? state.Write(character.ToString(), zone with { End = zone.End + 1 })
                : state.Settled().Write(character.ToString());

        return character == '\\'
            ? state.Write("\\", new RawZone(state.Caret, state.Caret + 1))
            : null;
    }

    /// <summary>
    /// Settles a stretch being shown as itself, keeping the space that ends a control word.
    ///
    /// <para>
    /// <strong>This should not exist, and it is here because of something else.</strong> Ending a
    /// half-written command is what typing a non-letter after it already does, so a separate settling step
    /// is a second way to say the same thing. And the space is a character the reader did not type, written
    /// to make the source agree with the parser: where a control word ends is a question about READING
    /// LaTeX, which belongs to whatever turns source into boxes.
    /// </para>
    /// <para>
    /// It cannot move there yet, because the LaTeX builder does not typeset — it captures a pass made by an
    /// engine that is handed a finished parse, so there is nowhere to say "the command stops here" except
    /// in the characters. Dropping the space without that costs four tests and a formula that reads
    /// <c>\alphax</c>. Fix the builder, then delete this.
    /// </para>
    /// </summary>
    public static EditState Settle(this EditState state, string separator = " ")
    {
        if (state.Raw is null) return state;

        var raw = state.RawText;
        var settled = state.Settled();

        return raw.Length > 1 && raw[0] == 0x5C && char.IsLetter(raw[^1]) ? settled.Write(separator) : settled;
    }
}
