using System.Linq;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// Coverage for <see cref="LayoutQuery"/> — what a pointer, a caret and a selection mean, answered by
/// descending a layout tree.
///
/// <para>
/// These trees are built by hand. No typesetter, no fonts, no STA thread, no desktop: the rules are
/// arithmetic over a tree, so they are asserted as arithmetic, and a failure names the rule rather than a
/// pixel. That is the whole reason the tree exists — the same rules were previously inferred from a flat
/// list of rectangles, could only be checked by sampling thousands of points across a rendered control,
/// and were wrong in a different way each time.
/// </para>
/// </summary>
[TestClass]
[CoversNode("latex-source-map")]
public class LayoutQueryTests
{
    /// <summary>
    /// <c>\frac{x^2}{2}+y</c>, laid out the way a typesetter would: a numerator above a rule above a
    /// denominator, then a binary operator and a term beside them.
    /// </summary>
    private static LayoutNode Fraction()
    {
        //                    \frac{x^2}{2}+y
        //  offsets            0    6 8  11 13 14
        var root = new LayoutNode(new Rect(0, 0, 100, 44), new TestPart(0, 15), "row", isInk: false);

        var frac = root.Add(new LayoutNode(new Rect(0, 0, 26, 44), new TestPart(0, 13), "fraction", isInk: false));
        var numerator = frac.Add(new LayoutNode(new Rect(2, 0, 20, 17), new TestPart(6, 3), "script", isInk: false));
        numerator.Add(new LayoutNode(new Rect(2, 8, 11, 9), new TestPart(6, 1), "char", isInk: true));    // x
        numerator.Add(new LayoutNode(new Rect(14, 0, 7, 9), new TestPart(8, 1), "char", isInk: true));    // 2 (exponent)
        // The bar names nothing: no character of \frac{x^2}{2} produced it, and the node holding the whole
        // fraction already carries that span. It is the fraction's own drawing.
        frac.Add(new LayoutNode(new Rect(2, 20, 22, 2), null, "rule", isInk: false));
        frac.Add(new LayoutNode(new Rect(7, 30, 10, 13), new TestPart(11, 1), "char", isInk: true));      // 2 (denominator)

        root.Add(new LayoutNode(new Rect(28, 18, 16, 13), new TestPart(13, 1), "char", isInk: true));     // +
        root.Add(new LayoutNode(new Rect(46, 18, 11, 13), new TestPart(14, 1), "char", isInk: true));     // y
        return root;
    }

    /// <summary>A 3x3 grid: rows of cells, the shape a matrix lays out as.</summary>
    private static LayoutNode Matrix()
    {
        var root = new LayoutNode(new Rect(0, 0, 70, 60), new TestPart(0, 9), "grid", isInk: false);
        for (var r = 0; r < 3; r++)
        {
            var row = root.Add(new LayoutNode(new Rect(0, r * 20, 70, 13), new TestPart(r * 3, 3), "row", isInk: false));
            for (var c = 0; c < 3; c++)
                row.Add(new LayoutNode(new Rect(c * 25, r * 20, 10, 13), new TestPart(r * 3 + c, 1), "char", isInk: true));
        }
        return root;
    }

    // ── Pointer ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void APressLandsOnTheDeepestInkUnderIt()
    {
        var root = Fraction();

        var hit = root.NodeAt(new Point(17, 4));   // inside the exponent
        Assert.IsNotNull(hit);
        Assert.AreEqual(8, hit.Sits().Start, "the exponent, not the script or the fraction that contain it");
    }

    [TestMethod]
    public void APressInASecondRowStaysInThatRow()
    {
        // The failure this replaces: a container spanning both rows contains points in both, so a press
        // in the lower one came back with an offset from the upper.
        var root = Matrix();

        var hit = root.NodeAt(new Point(5, 45));   // first cell of the third row
        Assert.IsNotNull(hit);
        Assert.AreEqual(6, hit.Sits().Start);
    }

    [TestMethod]
    public void APressOnBlankSpaceTakesTheNearestInk()
    {
        var root = Fraction();

        // Between the + and the y — no ink there, but a press must still mean something.
        var hit = root.NodeAt(new Point(45, 24));
        Assert.IsNotNull(hit);
        Assert.IsTrue(hit.Sits().Start is 13 or 14, $"expected the + or the y, got offset {hit.Sits().Start}");
    }

    [TestMethod]
    public void AContainerIsNeverWhatYouPressed()
    {
        // Every point in the formula is inside the root, and inside the fraction; neither is an answer.
        var root = Fraction();

        foreach (var point in new[] { new Point(7, 10), new Point(12, 35), new Point(30, 24) })
        {
            var hit = root.NodeAt(point);
            Assert.IsNotNull(hit);
            Assert.IsTrue(hit.IsInk, $"press at {point} resolved to the container {hit.Kind}");
        }
    }

    // ── Selection ───────────────────────────────────────────────────────────

    [TestMethod]
    public void SelectingEveryPartOfAConstructSelectsTheConstruct()
    {
        // The well-formedness guarantee: cover a fraction's numerator, rule and denominator and what you
        // have selected is the fraction — never those three pieces and the braces between them.
        var root = Fraction();
        var inside = root.Ink().Where(n => n.Sits().Start < 13).ToList();

        var promoted = LayoutQuery.Promote(inside);

        Assert.AreEqual(1, promoted.Count);
        Assert.AreEqual("fraction", promoted[0].Kind);
        Assert.AreEqual((0, 13), (promoted[0].Sits().Start, promoted[0].Sits().Length));
    }

    [TestMethod]
    public void SelectingPartOfAConstructSelectsOnlyThatPart()
    {
        var root = Fraction();
        var exponent = root.Ink().Single(n => n.Sits().Start == 8);

        var promoted = LayoutQuery.Promote([exponent]);

        Assert.AreEqual(1, promoted.Count);
        Assert.AreEqual((8, 1), (promoted[0].Sits().Start, promoted[0].Sits().Length));
    }

    [TestMethod]
    public void ARangeIsNeverCountedTwice()
    {
        // A node and its own child both selected must not yield two overlapping ranges.
        var root = Fraction();
        var frac = root.Children.First();
        var denominator = root.Ink().Single(n => n.Sits().Start == 11);

        var ranges = LayoutQuery.Ranges(LayoutQuery.Promote([frac, denominator]));

        Assert.AreEqual(1, ranges.Count);
        Assert.AreEqual((0, 13), ranges[0]);
    }

    [TestMethod]
    public void AColumnOfAGridIsSeveralRanges()
    {
        // A matrix column is a real selection and is not contiguous in the source, which is why a
        // selection is a set of ranges rather than one.
        var root = Matrix();
        var column = root.Ink().Where(n => n.Sits().Start % 3 == 1).ToList();

        var ranges = LayoutQuery.Ranges(LayoutQuery.Promote(column));

        CollectionAssert.AreEqual(new[] { (1, 1), (4, 1), (7, 1) }, ranges.ToArray());
    }

    [TestMethod]
    public void AWholeRowOfAGridIsTheRow()
    {
        var root = Matrix();
        var middleRow = root.Ink().Where(n => n.Sits().Start is 3 or 4 or 5).ToList();

        var promoted = LayoutQuery.Promote(middleRow);

        Assert.AreEqual(1, promoted.Count);
        Assert.AreEqual("row", promoted[0].Kind);
        Assert.AreEqual((3, 3), (promoted[0].Sits().Start, promoted[0].Sits().Length));
    }

    // ── Caret ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void TheCaretTakesTheShapeOfWhatItStandsBeside()
    {
        var root = Fraction();

        var exponent = root.CaretRect(9);      // after the 2 of x^2
        var denominator = root.CaretRect(12);  // after the 2 below the bar

        Assert.IsTrue(exponent.Height < denominator.Height,
            $"a caret in an exponent is shorter than one in a denominator ({exponent.Height} vs {denominator.Height})");
        Assert.IsTrue(exponent.Y < denominator.Y, "and sits higher up");
    }

    [TestMethod]
    public void ACaretBeforeAConstructIsAsTallAsTheConstruct()
    {
        // Offset 0 abuts no ink — the fraction starts there. It should stand as tall as the fraction it
        // precedes, not as tall as the first digit of its numerator.
        var root = Fraction();

        Assert.IsTrue(root.CaretRect(0).Height > root.CaretRect(9).Height);
    }

    [TestMethod]
    public void ACaretWithNothingAbuttingItStandsBesideTheNearestInk()
    {
        // The normal case straight after an edit: the caret lands wherever the text was cut, which need
        // not be a boundary of anything. Collapsing to the origin is what made Delete look like the caret
        // had jumped to the front of the line.
        var root = Fraction();

        var caret = root.CaretRect(7);   // between x and its exponent - no node begins or ends here
        Assert.AreNotEqual(0, caret.X, "the caret must not fall back to the origin");
        Assert.IsTrue(caret.X > 10 && caret.X < 25, $"expected it beside the script, got x={caret.X}");
    }

    [TestMethod]
    public void ArrowingOffAnEndReportsNowhereLeftToGo()
    {
        var root = Fraction();

        Assert.IsNull(root.Step(0, forward: false), "there is nothing before the content");
        Assert.IsNull(root.Step(15, forward: true), "nor anything after it");
        Assert.IsNotNull(root.Step(0, forward: true));
    }

    [TestMethod]
    public void TheCaretCrossesAFractionBarByStructure()
    {
        // By pixels alone the + starts fractionally lower than the numerator and would win. Asking which
        // ancestor has rows, and stepping within it, gives the denominator the reader meant.
        var root = Fraction();

        var down = root.StepVertical(6, up: false);
        Assert.AreEqual(11, down, "down from the numerator lands in the denominator");

        var up = root.StepVertical(11, up: true);
        Assert.AreEqual(6, up, "and back again");
    }

    [TestMethod]
    public void ThereIsNoVerticalMoveOffASingleRow()
    {
        var root = new LayoutNode(new Rect(0, 0, 30, 13), new TestPart(0, 3), "row", isInk: false);
        root.Add(new LayoutNode(new Rect(0, 0, 10, 13), new TestPart(0, 1), "char", isInk: true));
        root.Add(new LayoutNode(new Rect(10, 0, 10, 13), new TestPart(1, 1), "char", isInk: true));

        Assert.IsNull(root.StepVertical(0, up: false));
        Assert.IsNull(root.StepVertical(0, up: true));
    }

    // ── Structure ───────────────────────────────────────────────────────────

    [TestMethod]
    public void RowsAreFoundByWhatOverlapsVertically()
    {
        // Rows groups a node's own children, so the grid's are its three row nodes, and a row's are its
        // three cells.
        var root = Matrix();
        var rows = root.Rows();

        Assert.AreEqual(3, rows.Count);
        Assert.IsTrue(rows.All(r => r.Count == 1 && r[0].Kind == "row"));
        CollectionAssert.AreEqual(new[] { 6, 7, 8 }, rows[2][0].Rows().Single().Select(n => n.Sits().Start).ToArray());
    }

    [TestMethod]
    public void AnOrdinaryRunOfTermsIsOneRow()
    {
        var root = new LayoutNode(new Rect(0, 0, 40, 13), new TestPart(0, 4), "row", isInk: false);
        root.Add(new LayoutNode(new Rect(0, 0, 10, 13), new TestPart(0, 1), "char", isInk: true));
        root.Add(new LayoutNode(new Rect(12, 2, 10, 9), new TestPart(1, 1), "char", isInk: true));
        root.Add(new LayoutNode(new Rect(24, 0, 10, 13), new TestPart(2, 1), "char", isInk: true));

        Assert.AreEqual(1, root.Rows().Count);
    }

    [TestMethod]
    public void APieceDrawnFromNothingIsOnThePageAndNowhereInTheSource()
    {
        // The fraction's bar. No character of \frac{x^2}{2} produced it — it is the construct's own
        // drawing — so it was drawn from no part, and a piece with no part is visibly there without being
        // selectable: there is nothing an edit to it could mean. What it has instead of a stretch of
        // source is a point, borrowed from the thing it was drawn inside, which is enough to keep it in
        // order among the pieces that do stand for something and not enough to stand beside.
        var root = Fraction();
        var rule = root.SelfAndDescendants().Single(n => n.Kind == "rule");

        Assert.IsNull(rule.Part, "nobody wrote it");
        Assert.AreEqual(new SourcePlace(0, 0), rule.Sits(), "so it is a point where the fraction begins");
        Assert.IsFalse(rule.Stands(), "and there is nowhere in it for a caret to be");
        CollectionAssert.DoesNotContain(root.Ink().ToArray(), rule, "nor is it something a drag picks out");

        // Which is what makes pressing it mean the fraction: the press lands on the nearest thing above it
        // that somebody did write, rather than on a rule that could not answer for itself.
        var pressed = root.NodeAt(new Point(rule.Bounds.X + rule.Bounds.Width / 2,
                                            rule.Bounds.Y + rule.Bounds.Height / 2));

        Assert.AreEqual(new SourcePlace(0, 13), pressed!.Sits(), "the whole fraction");
    }
}
