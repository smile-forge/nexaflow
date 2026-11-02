using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;
using System;
using System.Runtime.CompilerServices;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// A long document is painted a screen at a time: only the blocks near what is shown are painted and kept, and the picture of a
/// block scrolled far away is let go once nothing draws it.
/// </summary>
[TestClass]
[CoversNode("markdown-laid-again")]
public class PaintingWhatIsShownTests
{
    [TestMethod]
    public void OnlyTheBlocksNearWhatIsShownArePaintedAndKept() => UiThread.Run(() =>
    {
        var laid = Long();

        var shown = Paint(laid, new Rect(0, 0, 480, 100));
        var blocks = Blocks(laid);

        Assert.IsTrue(blocks[0].Holds, "the first block is on screen");
        Assert.IsFalse(blocks[^1].Holds, "the last is nowhere near it");
        GC.KeepAlive(shown);
    });

    [TestMethod]
    public void ScrollingFarAwayLetsWhatWasKeptGo() => UiThread.Run(() =>
    {
        var laid = Long();

        // The page drawn at the top, then at the end, which is all it draws now.
        PaintedAndLeft(laid, new Rect(0, 0, 480, 100));
        var shown = Paint(laid, new Rect(0, laid.Size.Height - 100, 480, 100));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var blocks = Blocks(laid);

        Assert.IsFalse(blocks[0].Holds, "the first block is far from the end of the document, and nothing draws it");
        Assert.IsTrue(blocks[^1].Holds, "and the last is what is shown now");
        GC.KeepAlive(shown);
    });

    [TestMethod]
    public void WhereNothingSaysWhatIsShownEverythingIsPainted() => UiThread.Run(() =>
    {
        var laid = Long();

        var shown = Paint(laid, null);

        Assert.IsTrue(Blocks(laid).All(block => block.Holds));
        GC.KeepAlive(shown);
    });

    private static Laid Long() =>
        Laying.Lay(null, string.Join("\n\n", Enumerable.Range(1, 200).Select(n => $"Paragraph {n} says something.")), 480);

    private static List<LayoutKept> Blocks(Laid laid) =>
        [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == LayoutPieces.Whole).Select(piece => piece.Painting!.Kept!)];

    /// <summary>The page drawn with <paramref name="showing"/> on screen — what holds the pictures it drew, as a page on screen does.</summary>
    private static DrawingVisual Paint(Laid laid, Rect? showing)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) WpfPainter.Paint(dc, laid.Root, Brushes.Black, showing);

        return visual;
    }

    /// <summary>The page drawn with <paramref name="showing"/> on screen and then drawn no more — in a frame of its own, so nothing still holds it once this returns.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PaintedAndLeft(Laid laid, Rect showing) => Paint(laid, showing);
}
