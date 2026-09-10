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
    /// What typing means here, given what it landed in — LaTeX's rule about how a command is spelled.
    ///
    /// <para>
    /// A backslash opens one and letters extend it, so <c>\alpha</c> shows as itself while it is being
    /// written rather than flickering through four failed parses.
    /// </para>
    /// <para>
    /// It used to answer for the structure too, letting the parse tree place a 3 typed after <c>x^2</c>
    /// inside the exponent. That went with the tree: a properly nested layout offers a place inside the
    /// script and a place past it, so which one the caret is standing at already says where the 3 belongs,
    /// and a second opinion from the parse could only disagree with it.
    /// </para>
    /// </summary>
    public EditState? Typing(Landing landing, string text) =>
        text.Length == 1 ? landing.State.Typing(text[0]) : null;

    /// <summary>
    /// Ends a stretch being shown as written, keeping the space that says where a control word stopped —
    /// see <see cref="LatexWriting.Settle"/>.
    /// </summary>
    public EditState Settle(Landing landing, string separator) => landing.State.Settle(separator);
}
