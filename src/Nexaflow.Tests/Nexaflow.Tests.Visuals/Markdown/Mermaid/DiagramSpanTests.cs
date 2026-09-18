using System.Linq;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown.Mermaid;

namespace Nexaflow.Tests.Visuals.Markdown.Mermaid;

/// <summary>
/// A range read as something other than a plain one: logarithmic, square-rooted or reversed, and the
/// gridlines drawn at its ticks.
///
/// <para>
/// The distinction these are here to hold is between a value with no place and a value at nought. A log
/// scale has nothing to say about nought or less, and answering "nought" would draw those values in the
/// corner as though they belonged there.
/// </para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("mermaid-diagram-kit")]
public class DiagramSpanTests
{
    // ── Plain ───────────────────────────────────────────────────────────────

    [TestMethod]
    public void APlainSpanIsTheRangeOpenedOutToRoundNumbers()
    {
        var span = DiagramSpan.Of(3, 97);

        Assert.AreEqual(0, span.Min);
        Assert.AreEqual(100, span.Max);
        Assert.AreEqual(0.25, span.At(25));
    }

    [TestMethod]
    public void EndsTheBlockWroteAreTheEndsItGets()
    {
        // Opening them out to round numbers would be answering a question nobody asked.
        var span = DiagramSpan.Of(3, 97, widen: false);

        Assert.AreEqual(3, span.Min);
        Assert.AreEqual(97, span.Max);
    }

    // ── Logarithmic ─────────────────────────────────────────────────────────

    [TestMethod]
    public void ALogSpanRunsFromOnePowerToAnother()
    {
        var span = DiagramSpan.Of(3, 4200, DiagramTransform.Log10);

        Assert.AreEqual(1, span.Min);
        Assert.AreEqual(10000, span.Max);
    }

    [TestMethod]
    public void ALogSpanPutsEachPowerTheSameDistanceApart()
    {
        var span = DiagramSpan.Of(1, 1000, DiagramTransform.Log10);

        Assert.AreEqual(0, span.At(1)!.Value, 1e-9);
        Assert.AreEqual(1.0 / 3, span.At(10)!.Value, 1e-9);
        Assert.AreEqual(2.0 / 3, span.At(100)!.Value, 1e-9);
        Assert.AreEqual(1, span.At(1000)!.Value, 1e-9);
    }

    [TestMethod]
    public void ALogAxisIsNumberedInPowers()
    {
        var says = DiagramSpan.Of(1, 1000, DiagramTransform.Log10).Ticks().Select(tick => tick.Says).ToArray();

        CollectionAssert.AreEqual(new[] { "1", "10", "100", "1000" }, says);
    }

    [TestMethod]
    public void NoughtOrLessHasNoPlaceOnALogAxis()
    {
        // Null rather than nought, because nought is a real place and this is not one.
        var span = DiagramSpan.Of(1, 1000, DiagramTransform.Log10);

        Assert.IsNull(span.At(0));
        Assert.IsNull(span.At(-5));
    }

    [TestMethod]
    public void ALogSpanOfValuesReachingNoughtStartsAPowerBelowTheLargest()
    {
        // Rather than throwing, or quietly moving a value somewhere it is not.
        var span = DiagramSpan.Of(-4, 100, DiagramTransform.Log10);

        Assert.IsTrue(span.Min > 0);
        Assert.IsTrue(span.Max >= 100);
    }

    [TestMethod]
    public void LogTwoCountsInDoublings()
    {
        var says = DiagramSpan.Of(1, 8, DiagramTransform.Log2).Ticks().Select(tick => tick.Says).ToArray();

        CollectionAssert.AreEqual(new[] { "1", "2", "4", "8" }, says);
    }

    // ── Square root and reverse ─────────────────────────────────────────────

    [TestMethod]
    public void ASquareRootSpanSpreadsTheSmallValuesOut()
    {
        var span = DiagramSpan.Of(0, 100, DiagramTransform.Sqrt);

        // A quarter of the way up by value is half of the way along by root.
        Assert.AreEqual(0.5, span.At(25)!.Value, 1e-9);
    }

    [TestMethod]
    public void LessThanNoughtHasNoPlaceOnASquareRootAxis()
    {
        Assert.IsNull(DiagramSpan.Of(0, 100, DiagramTransform.Sqrt).At(-1));
    }

    [TestMethod]
    public void AReversedSpanRunsTheOtherWay()
    {
        var span = DiagramSpan.Of(0, 100, DiagramTransform.Reverse);

        Assert.AreEqual(1, span.At(0)!.Value, 1e-9);
        Assert.AreEqual(0.75, span.At(25)!.Value, 1e-9);
        Assert.AreEqual(0, span.At(100)!.Value, 1e-9);
    }

    // ── Gridlines ───────────────────────────────────────────────────────────

    [TestMethod]
    public void AGridlineIsDrawnAcrossThePanelAtEachTick() => UiThread.Run(() =>
    {
        var panel = new Rect(20, 10, 200, 100);
        var ticks = new[] { new DiagramTick(0, null), new DiagramTick(0.5, null), new DiagramTick(1, null) };

        var build = new LayoutBuilder();
        build.Open("Root");
        DiagramGrid.Draw(build, "Grid", panel, ticks, upright: true, new DiagramStroke(Brushes.Gray));
        build.Close();

        var grid = build.Seal().Root.SelfAndDescendants().Single(piece => piece.Kind == "Grid");

        // Across the whole panel, and no taller than the panel itself.
        Assert.AreEqual(panel.Left, grid.Bounds.Left, 0.5);
        Assert.AreEqual(panel.Right, grid.Bounds.Right, 0.5);
        Assert.AreEqual(panel.Height, grid.Bounds.Height, 0.5);
    });

    [TestMethod]
    public void AGridlineStandsForNothingAnybodyWrote() => UiThread.Run(() =>
    {
        var build = new LayoutBuilder();
        build.Open("Root");
        DiagramGrid.Draw(build, "Grid", new Rect(0, 0, 100, 100), [new DiagramTick(0.5, null)],
                         upright: false, new DiagramStroke(Brushes.Gray));
        build.Close();

        var grid = build.Seal().Root.SelfAndDescendants().Single(piece => piece.Kind == "Grid");

        Assert.IsNull(grid.Part, "nobody typed a gridline, so there is nowhere for a press on one to go");
        Assert.AreEqual(Stops.None, grid.Stops);
    });

    [TestMethod]
    public void NoTicksIsNoGridAtAll() => UiThread.Run(() =>
    {
        var build = new LayoutBuilder();
        build.Open("Root");
        DiagramGrid.Draw(build, "Grid", new Rect(0, 0, 100, 100), [], upright: false,
                         new DiagramStroke(Brushes.Gray));
        build.Close();

        Assert.IsFalse(build.Seal().Root.SelfAndDescendants().Any(piece => piece.Kind == "Grid"));
    });
}
