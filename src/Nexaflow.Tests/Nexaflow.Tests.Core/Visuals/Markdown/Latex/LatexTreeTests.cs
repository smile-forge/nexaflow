using System.Linq;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Latex;

/// <summary>
/// Coverage for <see cref="LatexTree"/> — the rules that turn a typeset formula's shape back into
/// meaning: what a click means, what a drag selected, where an arrow key goes, which command produced
/// the glyph behind the caret.
///
/// These need no typesetter, no window and no desktop. The tree below is a faithful miniature of a real
/// <c>\frac{x^2}{2}+\sqrt{y}</c> layout — the numbers came off the typesetter at scale 20 — so the rules
/// can be exercised as arithmetic, which is the point of keeping the tree apart from the fonts that
/// produce it. Every bug found by eye during this feature's development was in one of these rules, not in
/// the typesetting.
/// </summary>
[TestClass]
[CoversNode("latex-source-map")]
public class LatexTreeTests
{
    private const string Fraction = @"\frac{x^2}{2}+\sqrt{y}";
    //                               \frac{ = 0-5, x = 6, ^ = 7, 2 = 8, } = 9,
    //                               {  = 10, 2 = 11, } = 12, + = 13, \sqrt{ = 14-19, y = 20, } = 21

    /// <summary>
    /// The formula's layout as the capture builds it: containers holding the pieces they were laid out
    /// from, and ink only at the leaves of what the source names.
    /// <para>
    /// The fraction's bar and the radical's sign both name nothing. No character of <c>\frac{x^2}{2}</c>
    /// produced the bar; the typesetter does hand the <em>sign</em> the span of the whole <c>\sqrt{y}</c>,
    /// which the capture takes back off it, because the node holding the whole root already carries that
    /// span and two nodes naming the same thing is a link that has to be interpreted. Both are drawing
    /// their construct does for itself — covered when a selection passes over them, never selectable
    /// alone.
    /// </para>
    /// </summary>
    private static LayoutNode Tree()
    {
        var root = new LayoutNode(new Rect(0.0, 0.0, 78.7, 43.5), 0, 22, "HorizontalBox", isInk: false);

        var fraction = root.Add(new LayoutNode(new Rect(0.0, 0.0, 26.3, 43.5), 0, 13, "VerticalBox", isInk: false));
        var numerator = fraction.Add(new LayoutNode(new Rect(2.4, 0.0, 18.4, 16.3), 6, 3, "HorizontalBox", isInk: false));
        numerator.Add(new LayoutNode(new Rect(2.4, 7.7, 11.4, 8.6), 6, 1, "CharBox", isInk: true));      // x
        numerator.Add(new LayoutNode(new Rect(13.8, 0.0, 7.0, 9.0), 8, 1, "CharBox", isInk: true));      // 2, the exponent
        fraction.Add(new LayoutNode(new Rect(0.0, 20.0, 26.3, 2.0), 0, 0, "HorizontalRule", isInk: false));
        fraction.Add(new LayoutNode(new Rect(6.9, 30.6, 10.0, 12.9), 11, 1, "CharBox", isInk: true));    // 2, below the bar

        root.Add(new LayoutNode(new Rect(28.2, 18.1, 15.6, 13.3), 13, 1, "CharBox", isInk: true));       // +

        var radical = root.Add(new LayoutNode(new Rect(48.2, 13.6, 30.5, 24.0), 14, 8, "HorizontalBox", isInk: false));
        radical.Add(new LayoutNode(new Rect(48.2, 13.6, 20.0, 24.0), 14, 0, "CharBox", isInk: false));   // the sign
        radical.Add(new LayoutNode(new Rect(68.2, 21.2, 10.5, 12.5), 20, 1, "CharBox", isInk: true));    // y

        return root;
    }

    private static LatexTree Latex() => new(Fraction, Tree(), new Size(78.7, 43.5));

    // ── Where a caret may rest ──────────────────────────────────────────────

    [TestMethod]
    public void ACaretRestsOnlyWhereSomethingIsDrawn()
    {
        var stops = Latex().CaretStops.ToList();

        CollectionAssert.Contains(stops, 6, "before the numerator's x");
        CollectionAssert.Contains(stops, 9, "after the exponent");
        CollectionAssert.DoesNotContain(stops, 5,
            "inside \\frac{ is the same place on screen as before the x — an invisible stop");
        CollectionAssert.Contains(stops, 0, "and either end of the formula is always available");
        CollectionAssert.Contains(stops, Fraction.Length);
    }

    [TestMethod]
    public void TheCaretTakesTheShapeOfWhatItStandsBeside()
    {
        var tree = Latex();

        var exponent = tree.CaretRect(9);
        var denominator = tree.CaretRect(12);
        var whole = tree.CaretRect(0);

        Assert.IsTrue(exponent.Height < denominator.Height,
            $"a caret in an exponent is shorter than one in a denominator ({exponent.Height} vs {denominator.Height})");
        Assert.IsTrue(exponent.Y < denominator.Y, "and sits higher up");
        Assert.IsTrue(whole.Height > denominator.Height,
            "while one before the whole formula spans all of it");
    }

    [TestMethod]
    public void TheCaretBelongsToWhatPrecedesIt()
    {
        // Offset 9 ends the superscript and also opens the denominator's group. As in text, the caret
        // goes with what was just typed, so it stays up in the script rather than dropping below the bar.
        var tree = Latex();
        Assert.IsTrue(tree.CaretRect(9).Y < tree.CaretRect(12).Y);
    }

    [TestMethod]
    public void AnOffsetWithNoStopMovesToTheNearestOne() =>
        Assert.AreEqual(6, Latex().NearestStop(5));

    // ── Arrowing ────────────────────────────────────────────────────────────

    [TestMethod]
    public void ArrowingOffTheEndReportsNowhereLeftToGo()
    {
        var tree = Latex();

        Assert.IsNull(tree.Step(0, forward: false), "there is nothing before the formula");
        Assert.IsNull(tree.Step(Fraction.Length, forward: true), "nor anything after it");

        // Null is the signal the host needs: it is what hands the caret out into the surrounding prose.
        Assert.IsNotNull(tree.Step(0, forward: true));
    }

    [TestMethod]
    public void DownFromANumeratorLandsInItsOwnDenominator()
    {
        // The `+` beside the fraction starts fractionally lower than the numerator, so by pixels alone it
        // beats the denominator the reader means. Structure has to settle it.
        var tree = Latex();

        Assert.AreEqual(11, tree.StepVertical(6, up: false), "before the x → before the denominator");
        Assert.AreEqual(12, tree.StepVertical(7, up: false), "after the x → after it");
    }

    [TestMethod]
    public void UpFromADenominatorComesBack()
    {
        var tree = Latex();
        Assert.AreEqual(6, tree.StepVertical(11, up: true));
    }

    [TestMethod]
    public void DownFromAnExponentSkipsPastTheBase()
    {
        // An exponent's own line has nothing under it but the denominator of the fraction it sits in.
        var tree = Latex();
        Assert.AreEqual(12, tree.StepVertical(9, up: false));
    }

    [TestMethod]
    public void ThereIsNoVerticalMoveFromOutsideTheGlyphs()
    {
        // Offset 0 abuts nothing that was drawn — its caret spans every line, so it is on none of them.
        var tree = Latex();
        Assert.IsNull(tree.StepVertical(0, up: false));
        Assert.IsNull(tree.StepVertical(0, up: true));
    }

    // ── Clicking and selecting ──────────────────────────────────────────────

    [TestMethod]
    public void AClickLandsOnTheSymbolUnderThePointer()
    {
        // The right-hand side of the exponent's 2 puts the caret after it. The exponent sits inside the
        // script, the fraction and the whole formula, so this also proves that descending beats them all.
        Assert.AreEqual(9, Latex().OffsetAt(new Point(13.8 + 7.0 * 0.75, 4.5)));
        Assert.AreEqual(8, Latex().OffsetAt(new Point(13.8 + 7.0 * 0.25, 4.5)));
    }

    [TestMethod]
    public void PressingOnAConstructsOwnDrawingMeansTheConstruct()
    {
        // The radical's sign names nothing of its own, because the node holding the whole root already
        // names that span. A press on it therefore resolves upwards, to the root — which is what the
        // reader was pointing at.
        Assert.AreEqual(14, Latex().OffsetAt(new Point(52, 25)), @"the front of \sqrt{y}");
    }

    [TestMethod]
    public void AClickInEmptySpaceGoesToTheNearestSymbol() =>
        Assert.AreEqual(22, Latex().OffsetAt(new Point(200, 21)), "far to the right of everything");

    [TestMethod]
    public void DraggingAcrossAScriptTakesTheWholeScript()
    {
        // Stopping at the raw offsets would have selected `x^` and left the script half-taken.
        var (start, length) = Latex().SnapRange(6, 3);
        Assert.AreEqual("x^2", Fraction.Substring(start, length));
    }

    [TestMethod]
    public void DraggingFromANumeratorToADenominatorTakesTheFraction()
    {
        // The offsets alone give `1}{x` — braces closing something the selection never opened. Promotion
        // is what makes the answer a thing you could cut out: every piece of the fraction's ink is in the
        // drag, so the answer is the fraction.
        var (start, length) = Latex().SnapRange(6, 6);
        Assert.AreEqual(@"\frac{x^2}{2}", Fraction.Substring(start, length));
    }

    [TestMethod]
    public void SelectingInsideARootStaysInsideIt()
    {
        // A radical's sign is a single glyph whose source span is the WHOLE `\sqrt{y}`, drawn beside the y
        // rather than around it. Counting it as crossed made every selection anywhere inside a root widen
        // to the entire root — you could not select the y.
        var (start, length) = Latex().SnapRange(20, 1);
        Assert.AreEqual("y", Fraction.Substring(start, length));
    }

    [TestMethod]
    public void SelectingAllOfARootTakesTheRoot()
    {
        var (start, length) = Latex().SnapRange(14, 8);
        Assert.AreEqual(@"\sqrt{y}", Fraction.Substring(start, length));
    }

    [TestMethod]
    public void ARangeOverPunctuationSnapsToWhatIsActuallyDrawn()
    {
        // Offset 10 is the denominator's opening brace — a character with no glyph of its own.
        var (start, length) = Latex().SnapRange(10, 1);
        Assert.AreEqual("2", Fraction.Substring(start, length),
            "a selection is over what the reader can see, so it snaps to the glyph, not the brace");
    }

    [TestMethod]
    public void SelectingNothingWashesNothing() =>
        Assert.AreEqual(0, Latex().RangeRects(4, 0).Count);

    [TestMethod]
    public void AContiguousSelectionWashesAsOneRun()
    {
        // `\frac{x^2}{2}+` is one unbroken stretch of source, so it must read as one unbroken highlight —
        // not a block on the fraction, a gap, and another block on the plus.
        Assert.AreEqual(1, Latex().RangeRects(0, 14).Count);
    }

    [TestMethod]
    public void AFullySelectedFractionCoversItsBar()
    {
        // The bar comes from no character of the source, so washing only the glyphs would leave a
        // selected fraction looking like two separately selected numbers.
        var wash = Latex().RangeRects(0, 13).Single();

        Assert.IsTrue(wash.Top <= 7.7 + 0.5 && wash.Bottom >= 43.5 - 0.5,
            $"the wash {wash} should span the whole fraction, bar included");
    }

    [TestMethod]
    public void SelectionRectanglesNeverOverlap()
    {
        // They are painted translucent, so two rectangles over one glyph would show as a darker patch.
        var rects = Latex().RangeRects(0, Fraction.Length);

        Assert.AreNotEqual(0, rects.Count);
        for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
            {
                var overlap = Rect.Intersect(rects[i], rects[j]);
                Assert.IsTrue(overlap.IsEmpty || overlap.Width < 0.01 || overlap.Height < 0.01,
                    $"{rects[i]} and {rects[j]} overlap");
            }
    }

    // ── What backspace is standing behind ───────────────────────────────────
    //
    // Which piece, only. Whether that piece is made of parts is a question about the parse, and the
    // tree here is hand-built to exercise the geometry — its nodes stand for no parse at all. That half
    // is covered against a real one in FormulaElementTests.

    [TestMethod]
    public void BehindAStructureIsTheWholeStructure()
    {
        var symbol = Latex().SymbolBefore(13);   // just past the closing brace of the fraction
        Assert.IsNotNull(symbol);
        Assert.AreEqual(@"\frac{x^2}{2}", Fraction.Substring(symbol.SourceStart, symbol.SourceLength));
    }

    [TestMethod]
    public void BehindACommandIsTheWholeCommand()
    {
        var symbol = Latex().SymbolBefore(22);   // just past \sqrt{y}
        Assert.IsNotNull(symbol);
        Assert.AreEqual(@"\sqrt{y}", Fraction.Substring(symbol.SourceStart, symbol.SourceLength));
    }

    [TestMethod]
    public void BehindAPlainCharacterIsThatCharacter()
    {
        var symbol = Latex().SymbolBefore(7);
        Assert.IsNotNull(symbol);
        Assert.AreEqual(1, symbol.SourceLength, "one character of source for one glyph");
    }
}
