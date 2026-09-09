using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// Two ways of asking for a formula, because there are two things to ask about.
///
/// <para>
/// <see cref="Lay"/> is what the builder makes and all it makes: a tree of pieces. Everything about
/// where things are, what a press means and what a drag took is asked of that, and is the same code a
/// tune and a barcode run.
/// </para>
/// <para>
/// <see cref="Read"/> adds the formula as TeX sees it — roles, matrices, what backspace un-renders —
/// which is a question about a parse tree and belongs to LaTeX alone. It reads the source itself,
/// exactly as the element does, which is why the reading it uses is the one the layout was built from
/// rather than a second opinion.
/// </para>
/// </summary>
internal static class Formula
{
    /// <summary>The layout, which is the whole of what a builder returns.</summary>
    public static Laid Lay(string latex, double scale, bool placeholders = false,
                           RawZone? shownAsWritten = null) =>
        LatexBuilder.Build(latex, scale, shownAsWritten: shownAsWritten, placeholders: placeholders);

    /// <summary>The layout, and the formula as TeX sees it.</summary>
    public static LatexTree Read(string latex, double scale, bool placeholders = false,
                                 RawZone? shownAsWritten = null) =>
        new(latex,
            Lay(latex, scale, placeholders, shownAsWritten),
            LatexBuilder.Draws,
            shownAsWritten,
            placeholders);
}
