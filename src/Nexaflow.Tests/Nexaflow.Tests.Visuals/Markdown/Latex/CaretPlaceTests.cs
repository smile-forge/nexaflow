using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// Where the caret goes when the arrow key is pressed, when an offset is not enough to say it.
///
/// <para>
/// Both cases were reported from the Solver's editor and both are the same shortfall: a place on the page
/// is not a place in the text. TeX sets space around a binary operator, so "after the 6" and "before the
/// +" of <c>6+5</c> are one character boundary drawn a hand's width apart — and a reader arrowing along
/// expects to visit both. LaTeX lets a one-token argument go unbraced, so the exponent of <c>x^2</c> and
/// the script holding it finish at the same character — and with only that character to name, there was
/// nowhere to stand past the script: the caret kept the exponent's height and its raised line, and the
/// next arrow left the formula (or, in a matrix, the cell) still wearing them.
/// </para>
/// <para>
/// So a place is a <em>piece</em> and one of its edges, the tree keeps them in the order an arrow visits
/// them, and a caret is an index into that. Both cases then fall out of what the builder made — the term
/// ends where its script ends, and each declares it — rather than out of a list of bars rebuilt per
/// offset with a walk through enclosures bolted on to invent the ones nobody declared.
/// </para>
/// <para>
/// The other half of the rule is asked here too, because it is what stops the fix being worse than the
/// fault: where two would be drawn in the same place there is one place, so the reader never presses an
/// arrow twice for a caret that does not appear to move.
/// </para>
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-caret-places")]
public class CaretPlaceTests
{
    [TestMethod]
    public void GlyphsSetAgainstEachOtherShareTheOnePlaceBetweenThem() => UiThread.Run(() =>
    {
        // The half of the rule that keeps the other half honest. Nothing separates the digits of `123`,
        // so "after the 1" and "before the 2" are the one mark a reader sees.
        var walked = Walk(Laid("123"));

        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, walked.Select(p => p.Offset).ToArray());
        Assert.AreEqual(walked.Count, walked.Select(p => p.Offset).Distinct().Count(),
            "no second place anywhere in a run of digits");
    });

    [TestMethod]
    public void TheArrowWalksExactlyThePlacesTheTreeDeclares() => UiThread.Run(() =>
    {
        // The invariant everything else rests on: stepping is a walk along the tree's own index, so there
        // is nowhere an arrow can reach that the builder did not put there, and nothing it puts that the
        // arrow skips.
        foreach (var latex in new[] { "6+5", "x^2", @"\frac{a}{b}", @"\sin x" })
            {
                var laid = Laid(latex);
                CollectionAssert.AreEqual(laid.Places.ToArray(), Walk(laid), latex);
            }
    });

    [TestMethod]
    public void AnUnbracedExponentHasAPlacePastIt() => UiThread.Run(() =>
    {
        var laid = Laid("x^2");
        var walked = Walk(laid);

        Assert.AreEqual(3, walked[^1].Offset, "past the script");
        Assert.AreEqual(3, walked[^2].Offset, "at the same character as the place inside it");

        var inside = walked[^2].CaretRect();
        var outside = walked[^1].CaretRect();

        Assert.IsTrue(outside.Height > inside.Height,
            $"the caret comes back down to the line: {inside.Height} inside the exponent, {outside.Height} past it");
        Assert.AreEqual(laid.Root.Bounds.Height, outside.Height, 0.001,
            "and it is the height of the whole thing it stepped out of");
    });

    [TestMethod]
    public void ABracedOneAlreadyHasOne() => UiThread.Run(() =>
    {
        // `x^{2}` closes the exponent with a brace, so the source has an offset of its own for "past the
        // script" and there is nothing to invent. The rule must not fire where it is not needed, or every
        // construct in the formula would grow a second place that goes nowhere.
        var laid = Laid(@"x^{2}");
                Assert.AreEqual(laid.Stops.Count, Walk(laid).Count, "one place per offset");
    });

    [TestMethod]
    public void AScriptEndingACellIsSteppedOutOfBeforeTheArrowLeavesTheCell() => UiThread.Run(() =>
    {
        // The reported case, and the one that made it matter: in a matrix there is somewhere for the
        // arrow to go, so a trailing exponent was not merely awkward to leave — it took the caret into
        // the next cell still raised and half-height, with no way back down at all.
        const string latex = @"A = \begin{pmatrix} a & 4b^{2}+3 \\ c^4 & d+3i \end{pmatrix}";
        var laid = Laid(latex);

        var afterTheFour = latex.IndexOf(@"c^4", StringComparison.Ordinal) + 3;
        var inside = laid.Root.StopAt(afterTheFour);
        Assert.IsTrue(inside >= 0, "the caret has somewhere to stand just inside the script");

        var stepped = laid.Step(inside, forward: true);
        Assert.IsNotNull(stepped);
        Assert.AreEqual(afterTheFour, laid.Places[stepped!.Value].Offset,
            "the first arrow steps out of the script, not across to the next cell");
        Assert.IsTrue(laid.Places[stepped.Value].CaretRect().Height > laid.Places[inside].CaretRect().Height,
            "and lands on the cell's own line");

        var onwards = laid.Step(stepped.Value, forward: true);
        Assert.IsTrue(laid.Places[onwards!.Value].Offset > afterTheFour, "the second one leaves for the next cell");
    });

    [TestMethod]
    public void PressingInTheSpaceLandsInIt() => UiThread.Run(() =>
    {
        // A place the arrow key can reach and the pointer cannot would be a second way of disagreeing
        // about where the caret goes — press in the glue around the operator and the caret snapped back
        // against the 6, having been offered a mark in the glue it could not be put in.
        var laid = Laid("6+5");

        var atOne = Enumerable.Range(0, laid.Places.Count).Where(at => laid.Places[at].Offset == 1).ToList();
        Assert.AreEqual(2, atOne.Count, "after the 6 and before the + are two places at one character");

        var againstTheSix = laid.Places[atOne[0]].CaretRect();
        var againstThePlus = laid.Places[atOne[1]].CaretRect();

        Assert.AreEqual(atOne[1], laid.StopNear(new Point(againstThePlus.X - 0.2, againstThePlus.Y + 1)));
        Assert.AreEqual(atOne[0], laid.StopNear(new Point(againstTheSix.X + 0.2, againstTheSix.Y + 1)));
    });

    [TestMethod]
    public void SteppingBackRetracesTheWayItCame() => UiThread.Run(() =>
    {
        // Two directions over one set of places, so they cannot disagree about how many there are.
        foreach (var latex in new[] { "6+5", "123", "x^2", @"x^{2}", @"\frac{a}{b}",
                                      @"A = \begin{pmatrix} a & 4b^{2}+3 \\ c^4 & d+3i \end{pmatrix}" })
        {
            var laid = Laid(latex);
            var forwards = Walk(laid);

            var backwards = new List<CaretPlace> { forwards[^1] };
            for (var at = laid.Places.Count - 1; laid.Step(at, forward: false) is { } previous; at = previous)
                backwards.Add(laid.Places[previous]);

            backwards.Reverse();
            CollectionAssert.AreEqual(forwards, backwards, latex);
        }
    });

    private static Laid Laid(string latex)
    {
        var laid = LatexBuilder.Build(latex, 22);
        Assert.IsNotNull(laid, latex);
        Assert.IsTrue(laid.Places.Count > 0, "a formula a caret can be in has somewhere to stand: " + latex);
        return laid;
    }

    /// <summary>Every place in a formula, in the order the right arrow visits them.</summary>
    private static List<CaretPlace> Walk(Laid laid)
    {

        var places = new List<CaretPlace> { laid.Places[0] };
        for (var at = 0; laid.Step(at, forward: true) is { } next; at = next)
        {
            places.Add(laid.Places[next]);
            Assert.IsTrue(places.Count < 500, "the walk must finish");
        }

        return places;
    }
}
