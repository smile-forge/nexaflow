using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Latex;

namespace Nexaflow.Tests.Visuals.Markdown.Latex;

/// <summary>
/// What a drag over a matrix selects, drawn.
///
/// <para>
/// Opt-in, and a picture rather than an assertion, because the rules are proved in
/// <c>ContentSelectionTests</c> over hand-built trees and what a picture answers is the other question:
/// that the runs the typesetter's tree carries are the ones a reader sees. A column selection that is
/// arithmetically perfect and lands one cell to the left is a passing test and a broken feature.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[NoCoverage("a picture to look at, not a check")]
[DoNotParallelize]
public class MatrixSelectionPicture
{
    private const string Latex = @"\begin{pmatrix} 1 & 2 & 3 \\ 4 & 5 & 6 \\ 7 & 8 & 9 \end{pmatrix}";

    [TestMethod]
    public void ShowWhatEachDragOverAMatrixSelects() => UiThread.Run(() =>
    {
        var into = Environment.GetEnvironmentVariable("NEXAFLOW_MATRIX_IMAGES");
        if (string.IsNullOrWhiteSpace(into)) { Assert.Inconclusive("set NEXAFLOW_MATRIX_IMAGES"); return; }

        var shots = new[]
        {
            ("down the middle column", Shot("2", "8")),
            ("across the middle row", Shot("4", "6")),
            ("corner to corner", Shot("1", "5")),
            // A formula places the caret on a press rather than selecting what was pressed, which is its own
            // behaviour and not the grid's — here so the difference is visible rather than surprising.
            ("a press, which puts the caret down", Shot("5", "5")),
        };

        var width = (int)shots.Max(s => s.Item2.Width) + 20;
        var height = shots.Sum(s => (int)s.Item2.Height + 26) + 10;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            double y = 6;

            foreach (var (what, shot) in shots)
            {
                dc.DrawText(
                    new FormattedText(what, System.Globalization.CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.DimGray, 1.0),
                    new Point(10, y));

                y += 20;
                dc.DrawImage(shot, new Rect(10, y, shot.Width, shot.Height));
                y += shot.Height + 6;
            }
        }

        var page = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        page.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(page));
        using var stream = File.Create(Path.Combine(into, "matrix-selection.png"));
        encoder.Save(stream);
    });

    private static RenderTargetBitmap Shot(string from, string to)
    {
        var formula = new FormulaElement(Latex, MarkdownPalette.Light, 26);
        formula.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        formula.Arrange(new Rect(formula.DesiredSize));

        formula.BeginPointerSelect(Middle(Cell(formula, from)));
        formula.ExtendPointerSelect(Middle(Cell(formula, to)));
        formula.EndPointerSelect();

        // The selection asked for a repaint and nothing has painted: off a live window there is no render
        // pass, so the bitmap would show the drawing made during Arrange, with nothing selected.
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);

        var shot = new RenderTargetBitmap((int)formula.DesiredSize.Width, (int)formula.DesiredSize.Height,
                                          96, 96, PixelFormats.Pbgra32);
        shot.Render(formula);
        return shot;
    }

    private static Piece Cell(FormulaElement formula, string digit) =>
        formula.Laid.Root.Leaves()
            .Single(n => n.Sits() is { Length: > 0 } at && Latex.Substring(at.Start, at.Length) == digit);

    private static Point Middle(Piece node) =>
        new(node.Bounds.X + (node.Bounds.Width / 2), node.Bounds.Y + (node.Bounds.Height / 2));
}
