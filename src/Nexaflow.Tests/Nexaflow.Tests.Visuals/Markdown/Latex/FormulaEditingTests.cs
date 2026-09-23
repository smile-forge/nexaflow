using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// A formula written in: click into it, drag across part of it, type into it, arrow along it — on the surface the
/// Solver writes one on, where an offset has to mean the same thing to a click, to the caret and to an edit.
///
/// <para>
/// The rules themselves are asserted in <see cref="LatexEditStateTests"/> and the geometry in
/// <see cref="LatexLayoutTests"/>. What is left to prove here is that they are wired to each other — which is exactly
/// what goes wrong when a half-typed command shifts the source out from under the layout. Every offset below is counted
/// in the formula's own LaTeX.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("latex-editing")]
public class FormulaEditingTests
{
    // ── Typing ──────────────────────────────────────────────────────────────

    [TestMethod]
    public void AHalfTypedCommandLeavesTheRestTypeset() => InFormula("x+", (editor, formula) =>
    {
        At(formula, 2);
        MarkdownEditorHarness.Type(editor, @"\alp");

        Assert.AreEqual(@"x+\alp", formula.Latex);
        Assert.AreEqual(0, formula.Diagnostics.Count,
            "the formula around a command being written must stay typeset, not collapse to source");
        Assert.AreEqual((2, 4), formula.ShownAsWritten,
            "and the part still being written is shown as the characters typed, in place");
    });

    [TestMethod]
    public void BackspaceDoesNotCareWhichSideOfTheSpaceTheCaretIsOn() => InFormula("6+5", (editor, formula) =>
    {
        // The caret has a place either side of the glue TeX sets around an operator, and backspace has one meaning at
        // both of them: the character before the offset they share.
        At(formula, 1);
        Assert.IsTrue(formula.MoveCaret(forward: true), "over to the far side of the glue");
        Assert.AreEqual(1, Caret(formula), "which is the same character boundary");

        Back(editor);
        Assert.AreEqual("+5", formula.Latex);
    });

    [TestMethod]
    public void SpaceTypesetsWhatWasWritten() => InFormula("x+", (editor, formula) =>
    {
        At(formula, 2);
        MarkdownEditorHarness.Type(editor, @"\alpha");
        MarkdownEditorHarness.RaiseKey(editor, Key.Space);

        Assert.AreEqual(@"x+\alpha ", formula.Latex);
        Assert.IsNull(formula.ShownAsWritten, "all of it typesets now");
    });

    [TestMethod]
    public void BackspaceTakesASymbolWhole() => InFormula(@"x+\alpha", (editor, formula) =>
    {
        // A symbol is one thing on the page however many letters spelled it, so backspace over one takes it: by the
        // time it renders the spelling is right, and what the reader pressed backspace over was an α.
        At(formula, 8);
        Back(editor);

        Assert.AreEqual("x+", formula.Latex, "the whole symbol went, in one press");
    });

    [TestMethod]
    public void BackspaceUnRendersAConstruct() => InFormula(@"y+\frac{a}{b}", (editor, formula) =>
    {
        // A construct was written as a command and braces the reader can no longer see, so there is source to go back
        // to and backspace goes back to it.
        At(formula, 13);
        Back(editor);

        Assert.AreEqual(@"y+\frac{a}{b}", formula.Latex, "nothing cut — it is showing what was written");
        Assert.AreEqual((2, 11), formula.ShownAsWritten, "the fraction itself is what is shown as written");
        Assert.AreEqual(0, formula.Diagnostics.Count, "and the y+ in front of it is still typeset around it");
    });

    [TestMethod]
    public void OneBackspaceRevealsTheConstructBehindTheCaret() => InFormula(@"\frac{a}{b} + \frac{c}{d}", (editor, formula) =>
    {
        // The first press, not the second. The one at the end is a construct, so backspace goes back to the source it
        // was written as.
        At(formula, formula.Latex.Length);
        Back(editor);

        Assert.AreEqual(@"\frac{a}{b} + \frac{c}{d}", formula.Latex, "nothing was cut");
        Assert.AreEqual((14, 11), formula.ShownAsWritten,
            "and the second fraction is showing as what was written instead of as a fraction — the first one, and the "
            + "+ between them, are untouched");
    });

    [TestMethod]
    public void UnRenderingNeverCostsACharacter() => InFormula(@"\frac{1}{1 + \frac{1}{x}}", (editor, formula) =>
    {
        // Backspace at the end of this once dropped the construct into source and took the closing brace with it. A
        // fraction whose denominator ends in another fraction is what it takes: the construct the caret stands behind
        // is the outer one, and the piece that ends nearest the caret is the inner.
        const string latex = @"\frac{1}{1 + \frac{1}{x}}";

        At(formula, latex.Length);
        Back(editor);

        Assert.AreEqual(latex, formula.Latex, "nothing was cut");
        Assert.IsNotNull(formula.ShownAsWritten, "and something is being shown as written instead");
    });

    [TestMethod]
    public void BackspaceNeverUnRendersAWholeRunOfThings() => InFormula(@"\begin{align*} a &= b \\ c &= d\, \end{align*}", (editor, formula) =>
    {
        // A row is however many items, so it is never "the thing before the caret" however exactly it ends there. A
        // caret after something that draws nothing (a thin space) finds no symbol ending where it stands, and a search
        // that climbed until it reached what contains them all un-rendered both equations at once.
        const string align = @"\begin{align*} a &= b \\ c &= d\, \end{align*}";

        At(formula, align.Length - 12);   // just after the \, and before \end{align*}
        Back(editor);

        Assert.AreEqual(align.Length - 1, formula.Latex.Length, "one character went");
        Assert.AreEqual(0, formula.Diagnostics.Count, "and all of it still typesets");
    });

    [TestMethod]
    public void ArrowsWalkIntoWhatBackspaceRevealed() => InFormula(@"\frac{a}{b} + \frac{c}{d}", (editor, formula) =>
    {
        // The point of revealing it is to edit it, so the caret has to be able to get in: every position inside the
        // revealed stretch maps to the one point where it sits in the typeset formula, so stepping by layout stops
        // jumped clean over what the reader had just asked to see.
        At(formula, formula.Latex.Length);
        Back(editor);

        var end = Caret(formula);
        formula.MoveCaret(forward: false);
        Assert.AreEqual(end - 1, Caret(formula), "one character back, into the revealed source");

        formula.MoveCaret(forward: false);
        Assert.AreEqual(end - 2, Caret(formula), "and again, so any of it can be reached");

        formula.MoveCaret(forward: true);
        Assert.AreEqual(end - 1, Caret(formula), "and forward the same way");
    });

    [TestMethod]
    public void EditsAreReportedToTheHost() => InFormula("x", (editor, formula) =>
    {
        // The host owns what was written; the surface has to say when it moved or the two drift.
        At(formula, 1);

        var changes = 0;
        var said = DependencyPropertyDescriptor.FromProperty(MarkdownSurface.MarkdownProperty, typeof(MarkdownSurface));
        EventHandler counted = (_, _) => changes++;
        said.AddValueChanged(editor, counted);

        try { MarkdownEditorHarness.Type(editor, "+2"); }
        finally { said.RemoveValueChanged(editor, counted); }

        Assert.AreEqual(2, changes);
        Assert.AreEqual("x+2", editor.Markdown);
    });

    [TestMethod]
    public void AReadOnlyFormulaIgnoresTyping() => InFormula("x+2", (editor, formula) =>
    {
        editor.IsReadOnly = true;
        At(formula, 1);
        MarkdownEditorHarness.Type(editor, "z");

        Assert.AreEqual("x+2", formula.Latex);
    });

    // ── Pointer ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void ClickingAtTheEdgeOfSomethingPutsTheCaretThere() => InFormula(@"\frac{x^2}{2}", (editor, formula) =>
    {
        var exponent = Leaf(formula, 8);

        // Within a caret's reach of its right-hand edge: that is the place after it, not the thing itself.
        formula.BeginPointerSelect(new Point(exponent.Bounds.Right - 0.5, exponent.Bounds.Y + exponent.Bounds.Height / 2));
        formula.EndPointerSelect();

        Assert.AreEqual(9, Caret(formula));
        Assert.IsTrue(formula.HasCaret);
        Assert.AreEqual(0, formula.SelectionLength, "a place, so nothing is picked");
    });

    [TestMethod]
    public void ClickingSquarelyOnSomethingSelectsIt() => InFormula(@"\frac{x^2}{2}", (editor, formula) =>
    {
        // A single click on something that is not a stop is a selection of that thing. Here the exponent's 2, pressed
        // in its middle.
        var exponent = Leaf(formula, 8);

        formula.BeginPointerSelect(new Point(exponent.Bounds.X + exponent.Bounds.Width / 2,
                                             exponent.Bounds.Y + exponent.Bounds.Height / 2));
        formula.EndPointerSelect();

        Assert.AreEqual("2", formula.SelectedText);
    });

    [TestMethod]
    public void DraggingSelectsWholeConstructs() => InFormula(@"\frac{x^2}{2}", (editor, formula) =>
    {
        var baseGlyph = Leaf(formula, 6);
        var exponent = Leaf(formula, 8);

        formula.BeginPointerSelect(new Point(baseGlyph.Bounds.X + 1, baseGlyph.Bounds.Y + baseGlyph.Bounds.Height / 2));
        formula.ExtendPointerSelect(new Point(exponent.Bounds.Right - 1, exponent.Bounds.Y + exponent.Bounds.Height / 2));
        formula.EndPointerSelect();

        Assert.AreEqual("x^2", formula.SelectedText, "the drag took the whole script, not a half of it");
    });

    [TestMethod]
    public void TypingOverASelectionReplacesIt() => InFormula(@"\frac{x^2}{2}", (editor, formula) =>
    {
        formula.Select(formula.Origin + 6, 3);
        MarkdownEditorHarness.Type(editor, "y");

        Assert.AreEqual(@"\frac{y}{2}", formula.Latex);
    });

    // ── Walking it ──────────────────────────────────────────────────────────

    [TestMethod]
    public void AtEitherEdgeThereIsNowhereFurtherToGo() => InFormula("x+2", (editor, formula) =>
    {
        // The formula is the whole of what is written here, so an arrow off either end has nowhere to take the caret.
        At(formula, 0);
        MarkdownEditorHarness.RaiseKey(editor, Key.Left);
        Assert.AreEqual(0, Caret(formula), "there is nothing before the formula");

        At(formula, 3);
        MarkdownEditorHarness.RaiseKey(editor, Key.Right);
        Assert.AreEqual(3, Caret(formula), "nor after it, and the caret stays where it ran out");
        Assert.IsTrue(formula.HasCaret);
    });

    [TestMethod]
    public void ArrowingInsideTheFormulaStaysInside() => InFormula(@"\frac{x^2}{2}", (editor, formula) =>
    {
        At(formula, 0);

        Assert.IsTrue(formula.MoveCaret(forward: true), "there is more formula to walk through");
        Assert.AreNotEqual(0, Caret(formula));
    });

    // ── Harness ─────────────────────────────────────────────────────────────

    /// <summary>Runs <paramref name="test"/> against a surface holding <paramref name="latex"/> as its one formula.</summary>
    private static void InFormula(string latex, Action<MarkdownSurface, DocumentBlock> test) =>
        UiThread.Run(() => MarkdownEditorHarness.Run(latex,
                                                     editor => test(editor, MarkdownEditorHarness.Block(editor)),
                                                     editor => editor.SingleBlock = "latex"));

    /// <summary>The caret <paramref name="at"/> characters into the formula's LaTeX.</summary>
    private static void At(DocumentBlock formula, int at) => formula.TakeCaret(formula.Origin + at);

    /// <summary>Where the caret is, counted in the formula's LaTeX.</summary>
    private static int Caret(DocumentBlock formula) => formula.Caret - formula.Origin;

    private static void Back(MarkdownSurface editor) => MarkdownEditorHarness.RaiseKey(editor, Key.Back);

    /// <summary>The one leaf that sits <paramref name="at"/> characters into the formula's LaTeX.</summary>
    private static Piece Leaf(DocumentBlock formula, int at) =>
        formula.Laid.Root.Leaves().Single(leaf => leaf.Sits().Start == formula.Origin + at);
}
