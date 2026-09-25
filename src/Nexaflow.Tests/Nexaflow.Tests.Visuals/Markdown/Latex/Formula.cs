using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// A formula laid out as the engine lays one out: a tree of pieces. Everything about where things are, what a press means
/// and what a drag took is asked of that, and is the same code a tune and a barcode run.
/// </summary>
internal static class Formula
{
    /// <summary>The layout, which is the whole of what a builder returns.</summary>
    public static Laid Lay(string latex, double scale, bool placeholders = false,
                           RawZone? shownAsWritten = null) =>
        Laying.Formula(latex, scale, shownAsWritten: shownAsWritten, placeholders: placeholders);


}
