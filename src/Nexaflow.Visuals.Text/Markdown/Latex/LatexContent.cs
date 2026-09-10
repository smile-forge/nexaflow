using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Visuals.Text.Markdown.Latex;

/// <summary>
/// A formula as a piece of content: how it is built, and what writing into it means.
///
/// <para>
/// The two halves of knowing what this content <em>is</em>, and neither belongs to the element that
/// hosts it. Building it is the builder's; how an edit is transformed by what it lands in is
/// <see cref="IContent"/>'s. An element measures, paints, hit-tests and holds a caret — where a
/// control word's name stops has nothing to do with any of that, and it lived there only because there
/// was nowhere else to put it.
/// </para>
/// <para>
/// The parse tree is kept here for the same reason. Both handlers ask questions of it, and it has to be
/// the reading the layout was built from rather than a second opinion: parts are matched by identity, so
/// a tree re-read from the same source would answer about pieces that merely look right.
/// </para>
/// </summary>
internal sealed class LatexContent(double scale, bool inline) : IContent
{
    /// <summary>
    /// The formula as TeX sees it, remade beside the layout every time the source changes. Its reading is
    /// worked out only if something asks a question about the parse, so a keystroke that merely redraws
    /// pays nothing for it.
    /// </summary>
    private LatexTree _tree = new(string.Empty, Laid.Nothing, LatexBuilder.Draws);

    /// <summary>The map behind what is drawn — always there, because a builder always makes one.</summary>
    public LatexTree Tree => _tree;

    /// <summary>
    /// Typesets the whole formula, with the stretch being written set as the characters that were typed.
    ///
    /// <para>
    /// One layout over the real source, rather than a layout of the settled part with the raw characters
    /// painted over it afterwards. Painting over could only ever work while the stretch was the last
    /// thing in the formula: anywhere else it covered whatever followed, which is what un-rendering a
    /// fraction in the middle of an expression looked like. Set through the typesetter it takes up room
    /// like anything else, so the formula flows around it — and every offset the tree reports is an
    /// offset into the source the reader is editing, with no mapping in between.
    /// </para>
    /// </summary>
    public Laid Lay(EditState state, double room, double pixelsPerDip, bool readOnly)
    {
        var laid = LatexBuilder.Build(
            state.Source, scale, inline, shownAsWritten: state.Raw, placeholders: !readOnly,
            pixelsPerDip: pixelsPerDip);

        _tree = new LatexTree(state.Source, laid, LatexBuilder.Draws, state.Raw, !readOnly);
        return laid;
    }

    /// <summary>
    /// What typing means here, given what it landed in — LaTeX's two rules about how a formula is
    /// written: what the characters make of themselves, and what the structure makes of them.
    ///
    /// <para>
    /// A backslash opens a command and letters extend it, so <c>\alpha</c> shows as itself while it is
    /// being written rather than flickering through four failed parses; and a 3 typed just inside the
    /// exponent of <c>x^2</c> makes it twenty-three, where the same keystroke one mark to the right
    /// follows the whole script.
    /// </para>
    /// </summary>
    public EditState? Typing(Landing landing, string text) =>
        WriteThroughTree(landing, text)
        ?? (text.Length == 1 ? landing.State.Typing(text[0]) : null);

    /// <summary>
    /// Ends a stretch being shown as written, keeping the space that says where a control word stopped —
    /// see <see cref="LatexWriting.Settle"/>.
    /// </summary>
    public EditState Settle(Landing landing, string separator) => landing.State.Settle(separator);

    /// <summary>
    /// Lets the tree make the edit, when the caret is somewhere a construct has an opinion about — the 3
    /// after <c>x^2</c> belongs in the exponent. Null when the position belongs to no construct in
    /// particular and the caller should write the text itself.
    /// </summary>
    /// <remarks>
    /// Deliberately declined mid-command and mid-selection. A half-written command is being shown as the
    /// characters it is spelled with, so the layout is a step behind the source and the tree would be
    /// answering about a formula the reader is not looking at; a selection is a replacement, which is a
    /// different edit. Whitespace is declined too — a space is how you say "out of this script", so it
    /// must never be the thing that grows one.
    /// </remarks>
    private EditState? WriteThroughTree(Landing landing, string text)
    {
        var state = landing.State;

        if (state.HasSelection || state.Raw is not null) return null;
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Only from inside. A caret that has stepped out of a construct is standing against the construct
        // rather than against its contents — that is what a place means and the whole reason it is a
        // piece — so a 3 typed there follows `x^2` instead of joining its exponent.
        if (!landing.Innermost) return null;

        return _tree.Write(state.Caret, text) is { } written
            ? new EditState(written.Latex, written.Caret)
            : null;
    }
}
