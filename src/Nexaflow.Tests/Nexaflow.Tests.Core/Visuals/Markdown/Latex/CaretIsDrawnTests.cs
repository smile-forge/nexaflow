using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Core.Visuals.Markdown.Latex;

/// <summary>
/// Whether a formula holding the caret actually draws one.
///
/// <para>
/// Asked of the drawing rather than of any state, because the state was right the whole time an empty
/// Latex tab showed no caret. The formula had it, the editor had the keyboard, and nothing was drawn —
/// <see cref="LatexLayout.Build"/> returns null for an empty formula, and the paint took that branch
/// and returned before it got to the caret. The first character typed looked like it summoned the
/// caret; what it really did was create the layout the caret was being drawn from.
/// </para>
/// <para>
/// So a dozen plausible focus fixes all failed, each for a good reason and none of them the reason.
/// This test asks the only question that distinguishes them.
/// </para>
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-caret")]
public class CaretIsDrawnTests
{
    [TestMethod]
    public void AnEmptyFormulaHoldingTheCaretDrawsOne() => UiThread.Run(() =>
    {
        // The Latex tab as it opens: nothing written yet, and everything to write.
        var formula = Measured(string.Empty);
        Assert.AreEqual(0, LinesDrawnBy(formula), "nothing has the caret yet");

        formula.TakeCaret(0);
        Assert.AreEqual(1, LinesDrawnBy(formula),
            "an empty formula is still a place being written in, so it still shows where");
    });

    [TestMethod]
    public void AFormulaWithSomethingInItDrawsOneToo() => UiThread.Run(() =>
    {
        // The case that always worked, kept alongside so the fix cannot be "draw it only when empty".
        var formula = Measured("x+1");
        formula.TakeCaret(3);

        Assert.AreEqual(1, LinesDrawnBy(formula));
    });

    [TestMethod]
    public void ReleasingItStopsDrawingIt() => UiThread.Run(() =>
    {
        var formula = Measured(string.Empty);
        formula.TakeCaret(0);
        formula.ReleaseCaret();

        Assert.AreEqual(0, LinesDrawnBy(formula), "one caret on the page, and only where it is");
    });

    /// <summary>A formula laid out and measured, so it has a render size to draw into.</summary>
    private static FormulaElement Measured(string latex)
    {
        var formula = new FormulaElement(latex, MarkdownPalette.Dark, 16);
        formula.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        formula.Arrange(new Rect(formula.DesiredSize));
        return formula;
    }

    /// <summary>
    /// How many straight lines the element painted. The caret is the only line an empty formula has,
    /// and one more than whatever a written one draws for its own sake — which is why each case counts
    /// from its own baseline.
    /// </summary>
    private static int LinesDrawnBy(FormulaElement formula)
    {
        formula.InvalidateVisual();
        formula.UpdateLayout();

        var drawing = VisualTreeHelper.GetDrawing(formula);
        return drawing is null ? 0 : Lines(drawing).Count();
    }

    private static IEnumerable<GeometryDrawing> Lines(DrawingGroup group)
    {
        foreach (var drawing in group.Children)
        {
            if (drawing is DrawingGroup nested)
                foreach (var line in Lines(nested)) yield return line;
            else if (drawing is GeometryDrawing { Geometry: LineGeometry } line2)
                yield return line2;
        }
    }
}
