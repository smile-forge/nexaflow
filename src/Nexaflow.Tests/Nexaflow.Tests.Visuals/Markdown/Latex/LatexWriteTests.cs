using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// Editing a formula as a formula rather than as a string that happens to be one.
///
/// <para>
/// The difference shows up the first time anyone types into an exponent. LaTeX lets a one-token
/// argument go unbraced, so <c>x^2</c> is x to the 2 — and a 3 spliced in after it writes <c>x^23</c>,
/// which says x squared followed by a 3. The characters are right and the formula is wrong. Only the
/// construct holding that position knows the difference, so the edit is expressed against the tree and
/// the tree re-braces.
/// </para>
/// <para>
/// The answer comes back as source, not as a tree, and deliberately: the tree <em>is</em> a reading of
/// the source, so changed source is a changed tree. Re-reading it is what lays the formula out again
/// and repaints it, which is how one call reaches the picture.
/// </para>
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-writable-ast")]
public class LatexWriteTests
{
    private const double Scale = 16;

    [TestMethod]
    public void WritingAfterAnUnbracedArgumentGoesIntoIt() => UiThread.Run(() =>
    {
        var written = Write("x^2", caret: 3, "3");

        Assert.IsNotNull(written, "the caret is in the exponent, so the exponent has an opinion");
        Assert.AreEqual("x^{23}", written.Value.Latex);
        Assert.AreEqual(5, written.Value.Caret, "after the 3, still inside the exponent");
    });

    [TestMethod]
    public void WritingAtTheFrontOfAnArgumentGoesIntoItToo() => UiThread.Run(() =>
    {
        // The same question from the other side, which a rule about "the argument that ends here"
        // would have missed entirely.
        var written = Write("x^2", caret: 2, "3");

        Assert.IsNotNull(written);
        Assert.AreEqual("x^{32}", written.Value.Latex);
        Assert.AreEqual(4, written.Value.Caret, "after the 3 it just wrote");
    });

    [TestMethod]
    public void AnArgumentAlreadyBracedIsLeftAlone() => UiThread.Run(() =>
    {
        // It can hold anything, so there is nothing structural to do and the caller writes the text.
        // Answering here anyway would nest a brace inside the argument on every keystroke.
        Assert.IsNull(Write("x^{23}", caret: 5, "4"));
        Assert.IsNull(Write(@"\sqrt{x^2+1}", caret: 11, "0"), "the radicand is braced and can grow");
    });

    [TestMethod]
    public void OneTokenStillFitsWithoutBraces() => UiThread.Run(() =>
    {
        // x^\alpha is perfectly good LaTeX: a control word is one token however many characters it
        // takes to spell. Bracing it would be noise in the source for no change on the page.
        Assert.IsNull(Write("x^", caret: 2, @"\alpha"));
    });

    [TestMethod]
    public void SomewhereNoConstructOwnsIsTheCallersToWrite() => UiThread.Run(() =>
    {
        // The 1 in "a + 1" is an element of a row, not an argument of anything — a 2 after it is
        // twelve. This is the case the first attempt got wrong, by asking "does this have a role"
        // when a row gives its elements a role too.
        Assert.IsNull(Write("a + 1", caret: 5, "2"));
        Assert.IsNull(Write("x + y", caret: 3, "z"));
    });

    [TestMethod]
    public void NothingToWriteIsNothingToDo() => UiThread.Run(() =>
    {
        Assert.IsNull(Write("x^2", caret: 3, string.Empty));
    });

    // ── Carrying a term somewhere else ──────────────────────────────────────

    [TestMethod]
    public void ATermDraggedIntoAnExponentBracesIt() => UiThread.Run(() =>
    {
        // "a + 3" with the 3 carried into the exponent of x^2. Dropped as characters it would read
        // x squared beside a 3; the exponent has to be wrapped to hold both.
        var moved = Move("x^2 + 3", [(6, 1)], to: 3);

        Assert.IsNotNull(moved);
        Assert.AreEqual("x^{23} + ", moved.Value.Latex);
    });

    [TestMethod]
    public void ACommandDraggedAgainstALetterKeepsItsName() => UiThread.Run(() =>
    {
        // The smart part of the merge: a control word runs on until a non-letter ends it, so \alpha
        // written straight against the y would silently become the unknown command \alphay.
        var moved = Move(@"y + \alpha", [(4, 6)], to: 0);

        Assert.IsNotNull(moved);
        Assert.AreEqual(@"\alpha y + ", moved.Value.Latex);
    });

    [TestMethod]
    public void AnEditSaysExactlyWhatItWrote() => UiThread.Run(() =>
    {
        // What was handed in is not what landed: wrapping the argument moved it along by a brace, and
        // a command written against a letter gains a space to keep its name. Only the edit knows, and
        // a drag draws that stretch in another colour to show what is being carried.
        var braced = Move("x^2 + 3", [(6, 1)], to: 3);
        Assert.IsNotNull(braced);
        Assert.AreEqual("3", braced.Value.Latex.Substring(braced.Value.Wrote.Start, braced.Value.Wrote.Length));

        var spaced = Move(@"y + \alpha", [(4, 6)], to: 0);
        Assert.IsNotNull(spaced);
        Assert.AreEqual(@"\alpha ", spaced.Value.Latex.Substring(spaced.Value.Wrote.Start, spaced.Value.Wrote.Length),
            "the space it had to add is part of what it wrote, not part of what came after");
    });

    [TestMethod]
    public void ATermDroppedOnItselfHasNotMoved() => UiThread.Run(() =>
    {
        // Cutting it first would leave nowhere to put it back, so this has to be recognised rather
        // than attempted.
        Assert.IsNull(Move("a + bc", [(4, 2)], to: 5));
    });

    [TestMethod]
    public void MovingSeveralStretchesTakesThemInOrder() => UiThread.Run(() =>
    {
        // A column of a matrix is three cells nowhere near each other in the source, so a move has to
        // handle a selection that is not one stretch.
        var moved = Move("abcd", [(0, 1), (2, 1)], to: 4);

        Assert.IsNotNull(moved);
        Assert.AreEqual("bdac", moved.Value.Latex);
    });

    [TestMethod]
    [CoversNode("latex-drag-move")]
    public void ADragMayPassOverEveryPositionInTheFormula() => UiThread.Run(() =>
    {
        // A drag asks this question once per mouse move, so every position between one end of the
        // formula and the other gets asked — including the ones no reader would ever let go on. Each
        // has to answer: the move, or nothing. Throwing is not one of the answers, and it was: an
        // offset shifted backwards past a stretch that straddled it, and the source was read from a
        // higher index to a lower one.
        const string latex =
            @"S (\omega)=\frac{\alpha g^2}{\omega^5} \, e ^{[-0.74\bigl\{\frac{\omega U_\omega 19.5}{g}\bigr\}^{-4}]}";

        var layout = LatexLayout.Build(latex, Scale);
        Assert.IsNotNull(layout);

        var start = latex.IndexOf(@"\omega^5", System.StringComparison.Ordinal);
        Assert.IsTrue(start > 0, "the denominator is in there to be dragged out of");
        (int, int)[] denominator = [(start, @"\omega^5".Length)];

        for (var to = 0; to <= latex.Length; to++)
        {
            var moved = layout.Tree.Move(denominator, to);
            if (moved is not { } write) continue;

            Assert.IsTrue(write.Caret >= 0 && write.Caret <= write.Latex.Length,
                $"the caret landed outside the formula it wrote (drop at {to})");
            Assert.IsTrue(write.Wrote.Start >= 0 && write.Wrote.End <= write.Latex.Length,
                $"and what it says it wrote is inside it (drop at {to})");
        }
    });

    [TestMethod]
    [CoversNode("latex-drag-move")]
    public void ATermDraggedOutOfADenominatorLeavesTheHoleBehind() => UiThread.Run(() =>
    {
        // The reported drag, at the position it was let go on. An emptied argument is not a failure:
        // {} parses to a placeholder, which is a real symbol standing exactly where the next one goes.
        const string latex = @"\frac{\alpha g^2}{\omega^5} e";

        var layout = LatexLayout.Build(latex, Scale);
        Assert.IsNotNull(layout);

        var start = latex.IndexOf(@"\omega^5", System.StringComparison.Ordinal);
        var moved = layout.Tree.Move([(start, @"\omega^5".Length)], to: latex.Length - 1);

        Assert.IsNotNull(moved);
        Assert.AreEqual(@"\frac{\alpha g^2}{} \omega^5e", moved.Value.Latex);
    });

    private static LatexWrite? Write(string latex, int caret, string text)
    {
        var layout = LatexLayout.Build(latex, Scale);
        Assert.IsNotNull(layout, latex);
        return layout.Tree.Write(caret, text);
    }

    private static LatexWrite? Move(string latex, (int Start, int Length)[] ranges, int to)
    {
        var layout = LatexLayout.Build(latex, Scale);
        Assert.IsNotNull(layout, latex);
        return layout.Tree.Move(ranges, to);
    }
}
