using System;
using System.Linq;
using System.Windows;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// Dragging cells of a matrix, driven as a hand drives it: a sweep to pick out a column, a press on what was picked
/// out, and a drag onto somewhere else.
///
/// <para>
/// What the arithmetic comes to is settled in <c>TexMoveTests</c>, over every carry and every drop. These are the
/// other half, and the half that cannot be reasoned about: that the gesture reaches the formula at all — the press
/// becomes a carry rather than a new selection, the engine asks the language the carry was let go in
/// (<see cref="IOnMove"/>), and what it answers is written and read back.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("latex-grid-move")]
public class MatrixMoveTests
{
    private const string Matrix = @"\begin{matrix} 1 & 2 & 3 \\ 4 & 5 & 6 \\ 7 & 8 & 9 \end{matrix}";

    [TestMethod]
    public void AColumnSweptOutAndCarriedOntoAnotherReordersTheMatrix() => InFormula(Matrix, (editor, formula) =>
    {
        // Down the first column, which a grid selection gives back as one stretch per cell rather than everything
        // written between the top one and the bottom one.
        Sweep(formula, At(formula, "1"), At(formula, "7"));

        Assert.AreEqual(3, formula.Selection.Count, "three cells, three stretches");

        // Pressed on what is picked out and carried onto the last column: it goes in after that one.
        Sweep(formula, At(formula, "1"), At(formula, "3"));

        Assert.AreEqual(@"\begin{matrix} 2 & 3 & 1 \\ 5 & 6 & 4 \\ 8 & 9 & 7 \end{matrix}", formula.Latex);
    });

    [TestMethod]
    public void ARowSweptOutAndCarriedOntoAnotherReordersThem() => InFormula(Matrix, (editor, formula) =>
    {
        // Across a row, which is contiguous in the source and comes back as one stretch holding the & between the
        // cells — a different shape from a column, and the same move.
        Sweep(formula, At(formula, "1"), At(formula, "3"));
        Sweep(formula, At(formula, "2"), At(formula, "8"));

        Assert.AreEqual(@"\begin{matrix} 4 & 5 & 6 \\ 7 & 8 & 9 \\ 1 & 2 & 3 \end{matrix}", formula.Latex);
    });

    [TestMethod]
    public void AndCarryingACellAboutIsStillCharactersMoving() => InFormula(@"\frac{x^2}{2}", (editor, formula) =>
    {
        // Nothing of the formula's own: a term carried about is not cells of a table, so the engine's own answer
        // stands and the characters move.
        var was = formula.Latex;

        Sweep(formula, At(formula, "x"), At(formula, "x"));
        Assert.AreEqual("x", formula.SelectedText, "the x alone");

        Sweep(formula, At(formula, "x"), Edge(formula));

        Assert.AreNotEqual(was, formula.Latex, "it moved");
        Assert.AreEqual(was.Length, formula.Latex.Length, "and nothing was invented or lost doing it");
    });

    // ── Harness ─────────────────────────────────────────────────────────────

    /// <summary>Runs <paramref name="test"/> against a surface holding <paramref name="latex"/> as its one formula.</summary>
    private static void InFormula(string latex, Action<MarkdownSurface, DocumentBlock> test) =>
        UiThread.Run(() => MarkdownEditorHarness.Run(latex,
                                                     editor => test(editor, MarkdownEditorHarness.Block(editor)),
                                                     editor => editor.WrittenIn = "latex"));

    /// <summary>A press, a drag and a release — one gesture, whether it picks out or carries.</summary>
    private static void Sweep(DocumentBlock formula, Point from, Point to)
    {
        formula.BeginPointerSelect(from);
        formula.ExtendPointerSelect(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2));
        formula.ExtendPointerSelect(to);
        formula.EndPointerSelect();
        MarkdownEditorHarness.Pump();
    }

    /// <summary>The middle of the piece the formula drew <paramref name="said"/> as.</summary>
    private static Point At(DocumentBlock formula, string said)
    {
        var piece = formula.Laid.Root.Leaves()
            .First(leaf => leaf.Sits() is { Length: > 0 } sits
                           && formula.Latex.Substring(sits.Start - formula.Origin, sits.Length) == said);

        return new Point(piece.Bounds.X + (piece.Bounds.Width / 2), piece.Bounds.Y + (piece.Bounds.Height / 2));
    }

    /// <summary>The far right of what was drawn, which is somewhere to let something go that is not where it was.</summary>
    private static Point Edge(DocumentBlock formula) =>
        new(formula.Laid.Root.Bounds.Right - 1, formula.Laid.Root.Bounds.Y + (formula.Laid.Root.Bounds.Height / 2));
}
