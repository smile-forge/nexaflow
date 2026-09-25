using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Prose;

namespace Nexaflow.Tests.Visuals.Markdown.Prose;

/// <summary>
/// A long document is painted a screen at a time: only the blocks near what is shown are painted and kept, and a block
/// scrolled far away lets its picture go.
/// </summary>
[TestClass]
[CoversNode("markdown-laid-again")]
public class PaintingWhatIsShownTests
{
    [TestMethod]
    public void OnlyTheBlocksNearWhatIsShownArePaintedAndKept() => UiThread.Run(() =>
    {
        var laid = Long();

        Paint(laid, new Rect(0, 0, 480, 100));
        var blocks = Blocks(laid);

        Assert.IsTrue(blocks[0].Holds, "the first block is on screen");
        Assert.IsFalse(blocks[^1].Holds, "the last is nowhere near it");
    });

    [TestMethod]
    public void ScrollingFarAwayLetsWhatWasKeptGo() => UiThread.Run(() =>
    {
        var laid = Long();

        Paint(laid, new Rect(0, 0, 480, 100));
        Paint(laid, new Rect(0, laid.Size.Height - 100, 480, 100));
        var blocks = Blocks(laid);

        Assert.IsFalse(blocks[0].Holds, "the first block is far from the end of the document");
        Assert.IsTrue(blocks[^1].Holds, "and the last is what is shown now");
    });

    [TestMethod]
    public void WhereNothingSaysWhatIsShownEverythingIsPainted() => UiThread.Run(() =>
    {
        var laid = Long();

        Paint(laid, null);

        Assert.IsTrue(Blocks(laid).All(block => block.Holds));
    });

    private static Laid Long() =>
        Laying.Lay(null, string.Join("\n\n", Enumerable.Range(1, 200).Select(n => $"Paragraph {n} says something.")), 480);

    private static List<LayoutKept> Blocks(Laid laid) =>
        [.. laid.Root.SelfAndDescendants().Where(piece => piece.Kind == MarkdownPieces.Whole).Select(piece => piece.Painting!.Kept!)];

    private static void Paint(Laid laid, Rect? showing)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();

        LayoutPainter.Paint(dc, laid.Root, Brushes.Black, showing);
    }
}
