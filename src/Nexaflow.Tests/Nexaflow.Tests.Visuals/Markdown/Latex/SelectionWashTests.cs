using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// What a selection looks like, asked of the drawing.
///
/// <para>
/// Reported from the app: selecting a lower-case <c>a</c> showed the wash through the counter of the
/// letter and almost nowhere else, and an <c>i</c> or an <c>l</c> as a stripe too narrow to notice. The
/// wash was exactly the glyph's box — its advance and its own height — and a box that size, painted
/// behind the very thing it marks, is hidden by it. Text does not do it that way either: a selected
/// character is washed over the whole line box, which is why a selected <c>i</c> reads as selected.
/// </para>
///
/// Needs an STA thread for WPF's font machinery. It opens no window and takes no focus.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("latex-selection-wash")]
public class SelectionWashTests
{
    [TestMethod]
    public void TheWashReachesPastTheGlyphItMarks() => UiThread.Run(() =>
    {
        // The three the report named, and a wide one beside them so the wash cannot simply be the
        // element: `i` and `l` for width, `a` for height.
        var formula = Measured("ailW");

        foreach (var offset in new[] { 0, 1, 2 })
        {
            formula.Select(offset, 1);

            var glyph = formula.Layout!.Tree.Root.Ink().Single(n => n.SourceStart == offset);
            var wash = RectanglesDrawnBy(formula)
                .Where(r => r.Contains(glyph.Bounds))
                .OrderBy(r => r.Width * r.Height)
                .First();

            var letter = formula.Latex.Substring(offset, 1);
            Assert.IsTrue(wash.Width < formula.RenderSize.Width,
                $"'{letter}': the wash marks the letter, not the formula");
            Assert.IsTrue(wash.Left < glyph.Bounds.Left && wash.Right > glyph.Bounds.Right,
                $"'{letter}': {wash.Width} of wash around {glyph.Bounds.Width} of glyph leaves nothing to see");
            Assert.IsTrue(wash.Top < glyph.Bounds.Top && wash.Bottom > glyph.Bounds.Bottom,
                $"'{letter}': {wash.Height} of wash around {glyph.Bounds.Height} of glyph leaves nothing to see");
        }
    });

    /// <summary>A formula laid out and measured, so it has a render size to draw into.</summary>
    private static FormulaElement Measured(string latex)
    {
        var formula = new FormulaElement(latex, MarkdownPalette.Dark, 22);
        formula.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        formula.Arrange(new Rect(formula.DesiredSize));
        return formula;
    }

    /// <summary>Every rectangle the element painted, in its own coordinates.</summary>
    private static IEnumerable<Rect> RectanglesDrawnBy(FormulaElement formula)
    {
        formula.InvalidateVisual();
        formula.UpdateLayout();

        var drawing = VisualTreeHelper.GetDrawing(formula);
        return drawing is null ? [] : Rectangles(drawing).ToList();
    }

    private static IEnumerable<Rect> Rectangles(DrawingGroup group)
    {
        foreach (var drawing in group.Children)
        {
            if (drawing is DrawingGroup nested)
                foreach (var rect in Rectangles(nested)) yield return rect;
            else if (drawing is GeometryDrawing { Geometry: RectangleGeometry geometry })
                yield return geometry.Rect;
        }
    }
}
