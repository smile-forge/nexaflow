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
    /// Ends a stretch being shown as itself, keeping the space that says where a control word stopped.
    ///
    /// <para>
    /// <strong>An on-edit handler, and the right place for one.</strong> The reader pressed space or Enter;
    /// what that means depends on what it landed in, and this is what knows. The space it writes is not a
    /// character nobody typed — it is the character a person editing the source by hand would have typed
    /// there, and for the same reason: without it <c>\alpha</c> followed by a letter is the unknown command
    /// <c>\alphax</c>.
    /// </para>
    /// <para>
    /// This used to say it should move into a pipeline step that normalised the source before the builder
    /// read it. That was wrong twice over. A pipeline runs on every render, so it would write the space into
    /// a document nobody had edited — including source arriving from a file, which is the writer's and not
    /// ours to correct. And by the time a pipeline sees the text the intent is gone: <c>x^23</c> is only
    /// <em>text</em>, where an edit still knows the caret was inside the exponent. Where a control word ends
    /// is a question about the edit that ended it.
    /// </para>
    /// <para>
    /// Deliberately not the same handler as <see cref="Typing"/>, though both are on-edit. Typing carries a
    /// character and this does not: Enter settles a half-written command and adds no newline, so folding the
    /// two together would have Enter type a space into the formula.
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
