using System.Linq;
using Nexaflow.Visuals.Text.Editing;
using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// Coverage for <see cref="FormulaElement"/> — the control that puts the map and the editing rules
/// together: click into a formula, drag across part of it, type into it, arrow out of it.
///
/// The rules themselves are asserted in <see cref="LatexEditStateTests"/> and the geometry in
/// <see cref="LatexLayoutTests"/>. What is left to prove here is that the control wires them to each
/// other — in particular that an offset means the same thing to a click, to the caret and to an edit,
/// which is exactly what goes wrong when a half-typed command shifts the source out from under the
/// layout.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-editing")]
public class FormulaElementTests
{
    private const double Scale = 20;

    private static FormulaElement Element(string latex) =>
        new(latex, MarkdownPalette.FromTheme(), Scale);

    private static FormulaElement Arranged(string latex)
    {
        var element = Element(latex);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));
        return element;
    }

    private static void Type(FormulaElement element, string text)
    {
        foreach (var character in text) element.Type(character);
    }

    // ── Typing ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void AHalfTypedCommandLeavesTheRestTypeset() => UiThread.Run(() =>
    {
        var element = Arranged("x+");
        element.TakeCaret(2);
        Type(element, @"\alp");

        Assert.AreEqual(@"x+\alp", element.Latex);
        Assert.IsFalse(element.HasError,
            "the formula around a command being written must stay typeset, not collapse to source");
        Assert.AreEqual((2, 4), element.ShownAsWritten,
            "and the part still being written is shown as the characters typed, in place");
    });

    [TestMethod]
    public void TypingPastAScriptFollowsItRatherThanJoiningIt() => UiThread.Run(() =>
    {
        // `x^2` finishes the exponent and the script at the same character, so which of the two the
        // caret is standing at is the whole of what a 3 typed there asks. It is why the second place
        // exists: without it there was only one answer available, and half the time it was the wrong one.
        var inside = Arranged("x^2");
        inside.TakeCaret(3);
        Type(inside, "3");
        Assert.AreEqual(@"x^{23}", inside.Latex,
            "in the exponent it is twenty-three, and the argument is braced so it can hold it");

        var outside = Arranged("x^2");
        outside.TakeCaret(3);
        Assert.IsTrue(outside.MoveCaret(forward: true), "there is a place past the script to step out to");
        Type(outside, "3");
        Assert.AreEqual("x^23", outside.Latex, "past the script it is x squared followed by a 3");
    });

    [TestMethod]
    public void BackspaceDoesNotCareWhichSideOfTheSpaceTheCaretIsOn() => UiThread.Run(() =>
    {
        // The caret has a place either side of the glue TeX sets around an operator, and backspace has
        // one meaning at both of them: the character before the offset they share. Two marks, one
        // deletion — asked for in as many words when the two places were.
        var element = Arranged("6+5");
        element.TakeCaret(1);
        Assert.IsTrue(element.MoveCaret(forward: true), "over to the far side of the glue");
        Assert.AreEqual(1, element.Caret, "which is the same character boundary");

        element.Backspace();
        Assert.AreEqual("+5", element.Latex);
    });

    [TestMethod]
    public void SpaceTypesetsWhatWasWritten() => UiThread.Run(() =>
    {
        var element = Arranged("x+");
        element.TakeCaret(2);
        Type(element, @"\alpha");
        element.Commit();

        Assert.AreEqual(@"x+\alpha ", element.Latex);
        Assert.AreEqual(@"x+\alpha ", element.Layout!.Latex, "all of it typesets now");
    });

    [TestMethod]
    public void BackspaceTakesASymbolWholeAndUnRendersAConstruct() => UiThread.Run(() =>
    {
        // A symbol is one thing on the page however many letters spelled it, so backspace over one
        // takes it. It used to show \alpha as text instead, on the theory that the reader might want to
        // edit the spelling — but by the time it renders the spelling is right, and what they pressed
        // backspace over was an α.
        var element = Arranged(@"x+\alpha");
        element.TakeCaret(8);
        element.Backspace();
        Assert.AreEqual("x+", element.Latex, "the whole symbol went, in one press");

        // A construct is different: it was written as a command and braces the reader can no longer
        // see, so there is source to go back to and backspace goes back to it.
        var fraction = Arranged(@"y+\frac{a}{b}");
        fraction.TakeCaret(13);
        fraction.Backspace();
        Assert.AreEqual(@"y+\frac{a}{b}", fraction.Latex, "nothing cut — it is showing what was written");
        Assert.AreEqual((2, 11), fraction.ShownAsWritten, "the fraction itself is what is shown as written");
        Assert.IsFalse(fraction.HasError, "and the y+ in front of it is still typeset around it");
    });

    [TestMethod]
    public void OneBackspaceRevealsTheConstructBehindTheCaret() => UiThread.Run(() =>
    {
        // The first press, not the second. Two formulas side by side is the ordinary case for this —
        // the one at the end is a construct, so backspace goes back to the source it was written as.
        var element = Arranged(@"\frac{a}{b} + \frac{c}{d}");
        element.TakeCaret(element.Latex.Length);

        element.Backspace();

        Assert.AreEqual(@"\frac{a}{b} + \frac{c}{d}", element.Latex, "nothing was cut");
        Assert.AreEqual((14, 11), element.ShownAsWritten,
            "and the second fraction is showing as what was written instead of as a fraction — the "
            + "first one, and the + between them, are untouched");
    });

    [TestMethod]
    public void UnRenderingNeverCostsACharacter() => UiThread.Run(() =>
    {
        // Reported from the app: backspace at the end of this did drop the construct into source, and
        // took the closing brace with it — leaving LaTeX that no longer parses. Un-rendering is a change
        // of what you are looking at, not an edit; it must never remove anything.
        //
        // A fraction whose denominator ends in another fraction is what it takes: the construct the
        // caret stands behind is the outer one, and the piece that ends nearest the caret is the inner.
        const string latex = @"\frac{1}{1 + \frac{1}{x}}";

        var element = Arranged(latex);
        element.TakeCaret(latex.Length);
        element.Backspace();

        Assert.AreEqual(latex, element.Latex, "nothing was cut");
        Assert.IsNotNull(element.ShownAsWritten, "and something is being shown as written instead");
    });

    [TestMethod]
    public void BackspaceNeverUnRendersAWholeRunOfThings() => UiThread.Run(() =>
    {
        // A row is not an item — it is however many items, each of which is one — so it is never "the
        // thing before the caret" however exactly it happens to end there. A caret sitting after
        // something that draws nothing (a thin space) finds no symbol ending where it stands, and the
        // search climbs until it reaches whatever contains them all: in a two-line align block that is
        // both equations, and one backspace un-rendered the lot.
        const string align = @"\begin{align*} a &= b \\ c &= d\, \end{align*}";

        var element = Arranged(align);
        element.TakeCaret(align.Length - 12);   // just after the \, and before \end{align*}
        element.Backspace();

        Assert.AreEqual(align.Length - 1, element.Latex.Length, "one character went");
        Assert.AreEqual(element.Latex, element.Layout!.Latex, "and all of it still typesets");
    });

    [TestMethod]
    public void ArrowsWalkIntoWhatBackspaceRevealed() => UiThread.Run(() =>
    {
        // The point of revealing it is to edit it, so the caret has to be able to get in. Stepping by
        // layout stops could not: every position inside the revealed stretch maps to the one point
        // where it sits in the typeset formula, so the caret jumped clean over the thing the reader
        // had just asked to see.
        var element = Arranged(@"\frac{a}{b} + \frac{c}{d}");
        element.TakeCaret(element.Latex.Length);
        element.Backspace();

        var end = element.Caret;
        element.MoveCaret(forward: false);
        Assert.AreEqual(end - 1, element.Caret, "one character back, into the revealed source");

        element.MoveCaret(forward: false);
        Assert.AreEqual(end - 2, element.Caret, "and again, so any of it can be reached");

        element.MoveCaret(forward: true);
        Assert.AreEqual(end - 1, element.Caret, "and forward the same way");
    });

    [TestMethod]
    public void EditsAreReportedToTheHost() => UiThread.Run(() =>
    {
        // The host owns the document; the element has to say when its source moved or the two drift.
        var element = Arranged("x");
        element.TakeCaret(1);

        var changes = 0;
        element.LatexChanged += (_, _) => changes++;

        Type(element, "+2");
        Assert.AreEqual(2, changes);
    });

    [TestMethod]
    public void AReadOnlyFormulaIgnoresTyping() => UiThread.Run(() =>
    {
        var element = new FormulaElement("x+2", MarkdownPalette.FromTheme(), Scale) { IsReadOnly = true };
        element.TakeCaret(1);
        Type(element, "z");

        Assert.AreEqual("x+2", element.Latex);
        Assert.IsFalse(element.HasCaret, "and shows no caret to invite it");
    });

    // ── Pointer ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void ClickingPutsTheCaretWhereYouClicked() => UiThread.Run(() =>
    {
        var element = Arranged(@"\frac{x^2}{2}");
        var exponent = element.Layout!.Tree.Root.Ink().Single(n => n.SourceStart == 8);

        element.BeginPointerSelect(new Point(
            exponent.Bounds.X + exponent.Bounds.Width * 0.75,
            exponent.Bounds.Y + exponent.Bounds.Height / 2));
        element.EndPointerSelect();

        Assert.AreEqual(9, element.Caret);
        Assert.IsTrue(element.HasCaret);
    });

    [TestMethod]
    public void DraggingSelectsWholeConstructs() => UiThread.Run(() =>
    {
        var element = Arranged(@"\frac{x^2}{2}");
        var baseGlyph = element.Layout!.Tree.Root.Ink().Single(n => n.SourceStart == 6);
        var exponent = element.Layout!.Tree.Root.Ink().Single(n => n.SourceStart == 8);

        element.BeginPointerSelect(new Point(baseGlyph.Bounds.X + 1, baseGlyph.Bounds.Y + baseGlyph.Bounds.Height / 2));
        element.ExtendPointerSelect(new Point(exponent.Bounds.Right - 1, exponent.Bounds.Y + exponent.Bounds.Height / 2));
        element.EndPointerSelect();

        Assert.AreEqual("x^2", element.SelectedText, "the drag took the whole script, not a half of it");
    });

    [TestMethod]
    public void TypingOverASelectionReplacesIt() => UiThread.Run(() =>
    {
        var element = Arranged(@"\frac{x^2}{2}");
        element.Select(6, 3);
        Type(element, "y");

        Assert.AreEqual(@"\frac{y}{2}", element.Latex);
    });

    // ── Leaving the formula ─────────────────────────────────────────────────

    [TestMethod]
    public void TheCaretHandsBackAtEitherEdge() => UiThread.Run(() =>
    {
        var element = Arranged("x+2");
        BlockExit? exit = null;
        element.Exited += (_, e) => exit = e;

        element.TakeCaret(0);
        Assert.IsFalse(element.MoveCaret(forward: false), "there is nothing before the formula");
        Assert.AreEqual(BlockExit.Before, exit);

        exit = null;
        element.TakeCaret(3);
        Assert.IsFalse(element.MoveCaret(forward: true));
        Assert.AreEqual(BlockExit.After, exit, "and the host is told which side it left by");
    });

    [TestMethod]
    public void ArrowingInsideTheFormulaStaysInside() => UiThread.Run(() =>
    {
        var element = Arranged(@"\frac{x^2}{2}");
        element.TakeCaret(0);

        Assert.IsTrue(element.MoveCaret(forward: true), "there is more formula to walk through");
        Assert.AreNotEqual(0, element.Caret);
    });
}
