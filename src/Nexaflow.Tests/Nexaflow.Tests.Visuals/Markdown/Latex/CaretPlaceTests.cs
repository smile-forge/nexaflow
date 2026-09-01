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
/// next arrow left the formula (or, in a matrix, the cell) still wearing them. There was no way back down
/// to the line at all.
/// </para>
/// <para>
/// The other half of the rule is asked here too, because it is what stops the fix being worse than the
/// fault: where the two would be drawn in the same place there is one place, so the reader never presses
/// an arrow twice for a caret that does not appear to move.
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
    public void SpaceInTheSettingIsAPlaceEitherSideOfIt() => UiThread.Run(() =>
    {
        // Reported: in `6+5` the caret could be put before the 6, after the 6, after the + and after the
        // 5 — four marks where a reader sees six places, because it was always drawn hard against
        // whatever preceded it and the glue around the operator was left with no caret in it.
        var (tree, walked) = Walk("6+5");

        CollectionAssert.AreEqual(new[] { 0, 1, 1, 2, 2, 3 }, walked.Select(p => p.Offset).ToArray(),
            "six places over four offsets: the two the operator's glue sits at are each two");

        var marks = walked.Select(p => Math.Round(tree.CaretRect(p).X, 3)).ToList();
        Assert.AreEqual(6, marks.Distinct().Count(), "and every one of them is drawn somewhere else");
        CollectionAssert.AreEqual(marks.OrderBy(x => x).ToArray(), marks.ToArray(),
            "in the order the arrow key walks them, left to right");
    });

    [TestMethod]
    public void GlyphsSetAgainstEachOtherShareTheOnePlaceBetweenThem()
    {
        UiThread.Run(() =>
        {
            // The half of the rule that keeps the other half honest. Nothing separates the digits of
            // `123`, so "after the 1" and "before the 2" are the one mark a reader sees.
            var (_, walked) = Walk("123");

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, walked.Select(p => p.Offset).ToArray());
            Assert.IsTrue(walked.All(p => p.Level == 0), "no second place anywhere in a run of digits");
        });
    }

    [TestMethod]
    public void AnUnbracedExponentHasAPlacePastIt() => UiThread.Run(() =>
    {
        var (tree, walked) = Walk("x^2");

        var last = walked[^1];
        Assert.AreEqual(new CaretPlace(3, 1), last, "past the script, at the same character as inside it");

        var inside = tree.CaretRect(new CaretPlace(3, 0));
        var outside = tree.CaretRect(last);
        Assert.IsTrue(outside.Height > inside.Height,
            $"the caret comes back down to the line: {inside.Height} inside the exponent, {outside.Height} past it");
        Assert.AreEqual(tree.Root.Bounds.Height, outside.Height, 0.001,
            "and it is the height of the whole thing it stepped out of");
    });

    [TestMethod]
    public void ABracedOneAlreadyHasOne() => UiThread.Run(() =>
    {
        // `x^{2}` closes the exponent with a brace, so the source has an offset of its own for "past the
        // script" and there is nothing to invent. The rule must not fire where it is not needed, or
        // every construct in the formula would grow a second place that goes nowhere.
        var (tree, walked) = Walk(@"x^{2}");

        Assert.IsTrue(walked.All(p => p.Level == 0), "one place per offset");
        Assert.AreEqual(tree.CaretStops.Count, walked.Count);
    });

    [TestMethod]
    public void AScriptEndingACellIsSteppedOutOfBeforeTheArrowLeavesTheCell() => UiThread.Run(() =>
    {
        // The reported case, and the one that made it matter: in a matrix there is somewhere for the
        // arrow to go, so a trailing exponent was not merely awkward to leave — it took the caret into
        // the next cell still raised and half-height, with no way back down at all.
        const string latex = @"A = \begin{pmatrix} a & 4b^{2}+3 \\ c^4 & d+3i \end{pmatrix}";
        var (tree, _) = Walk(latex);

        var afterTheFour = latex.IndexOf(@"c^4", StringComparison.Ordinal) + 3;
        var inside = new CaretPlace(afterTheFour, 0);

        var stepped = tree.Step(inside, forward: true);
        Assert.AreEqual(new CaretPlace(afterTheFour, 1), stepped,
            "the first arrow steps out of the script, not across to the next cell");
        Assert.IsTrue(tree.CaretRect(stepped!.Value).Height > tree.CaretRect(inside).Height,
            "and lands on the cell's own line");

        var onwards = tree.Step(stepped.Value, forward: true);
        Assert.IsTrue(onwards!.Value.Offset > afterTheFour, "the second one leaves for the next cell");
    });

    [TestMethod]
    public void PressingInTheSpaceLandsInIt() => UiThread.Run(() =>
    {
        // A place the arrow key can reach and the pointer cannot would be a second way of disagreeing
        // about where the caret goes — press in the glue around the operator and the caret snapped back
        // against the 6, having been offered a mark in the glue it could not be put in.
        var layout = LatexLayout.Build("6+5", 22);
        Assert.IsNotNull(layout);

        var againstTheSix = layout.Tree.CaretRect(new CaretPlace(1, 0));
        var againstThePlus = layout.Tree.CaretRect(new CaretPlace(1, 1));

        Assert.AreEqual(new CaretPlace(1, 1),
            layout.Tree.PlaceAt(new Point(againstThePlus.X - 0.2, againstThePlus.Y + 1)));
        Assert.AreEqual(new CaretPlace(1, 0),
            layout.Tree.PlaceAt(new Point(againstTheSix.X + 0.2, againstTheSix.Y + 1)));
    });

    [TestMethod]
    public void SteppingBackRetracesTheWayItCame() => UiThread.Run(() =>
    {
        // Two directions over one set of places, so they cannot disagree about how many there are.
        foreach (var latex in new[] { "6+5", "123", "x^2", @"x^{2}", @"\frac{a}{b}",
                                      @"A = \begin{pmatrix} a & 4b^{2}+3 \\ c^4 & d+3i \end{pmatrix}" })
        {
            var (tree, forwards) = Walk(latex);

            var backwards = new List<CaretPlace> { forwards[^1] };
            for (var place = forwards[^1]; tree.Step(place, forward: false) is { } previous; place = previous)
                backwards.Add(previous);

            backwards.Reverse();
            CollectionAssert.AreEqual(forwards, backwards, latex);
        }
    });

    /// <summary>Every place in a formula, in the order the right arrow visits them.</summary>
    private static (LatexTree Tree, List<CaretPlace> Places) Walk(string latex)
    {
        var layout = LatexLayout.Build(latex, 22);
        Assert.IsNotNull(layout, latex);

        var places = new List<CaretPlace> { new(0, 0) };
        for (var place = places[0]; layout.Tree.Step(place, forward: true) is { } next; place = next)
        {
            places.Add(next);
            Assert.IsTrue(places.Count < 500, "the walk must finish: " + latex);
        }

        return (layout.Tree, places);
    }
}
