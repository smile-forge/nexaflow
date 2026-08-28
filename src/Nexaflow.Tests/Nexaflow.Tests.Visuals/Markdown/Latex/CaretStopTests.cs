using System.Linq;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// Where a caret — and so a drop — is allowed to land.
///
/// <para>
/// Every boundary of a piece the parser recognised, and nothing in between. That reads as "every
/// character position" in <c>x^2 + 1</c> and is not: each of those characters really is a piece of its
/// own, and a caret between two of them is a real place to be. Somewhere the source has structure the
/// difference shows immediately — <c>\alpha</c> is six characters and one piece, so there is nowhere to
/// stand inside its spelling, and <c>\frac{a}{b}</c> offers six positions rather than twelve.
/// </para>
/// <para>
/// This matters most to a drag: a term dropped into the middle of <c>\alp|ha</c> would produce a
/// command nobody has heard of, and one dropped between a fraction's <c>}</c> and <c>{</c> would land
/// nowhere at all. Neither is offered, because neither is a boundary of anything.
/// </para>
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-caret")]
public class CaretStopTests
{
    [TestMethod]
    public void ACommandHasNowhereToStandInsideItsSpelling() => UiThread.Run(() =>
    {
        var stops = StopsIn(@"\alpha + \beta");

        // 0 · after \alpha · after the space · after the + · after the space · the end.
        CollectionAssert.AreEqual(new[] { 0, 6, 7, 8, 9, 14 }, stops.ToArray(),
            "six characters of \\alpha, one piece — a drop inside it would make a command nobody knows");
    });

    [TestMethod]
    public void AConstructOffersItsPartsAndNotItsPunctuation() => UiThread.Run(() =>
    {
        var stops = StopsIn(@"\frac{a}{b}");

        // Either end of the numerator and of the denominator, plus either end of the whole fraction.
        // Nothing between the } and the { — a term dropped there would belong to neither part.
        CollectionAssert.AreEqual(new[] { 0, 6, 7, 9, 10, 11 }, stops.ToArray());
    });

    [TestMethod]
    public void EveryCharacterCountsWhenEveryCharacterIsAPiece() => UiThread.Run(() =>
    {
        // The case that looks like the rule was abandoned and is the rule being followed: in an
        // ordinary run of terms each character is its own piece, so every boundary is one too.
        var stops = StopsIn("x^2 + 1");

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, stops.ToArray());
    });

    private static IReadOnlyList<int> StopsIn(string latex)
    {
        var layout = LatexLayout.Build(latex, 16);
        Assert.IsNotNull(layout, latex);
        return layout.Tree.CaretStops;
    }
}
