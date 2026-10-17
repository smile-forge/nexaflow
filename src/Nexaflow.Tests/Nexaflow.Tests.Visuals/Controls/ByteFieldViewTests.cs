using System.Windows;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Controls;

namespace Nexaflow.Tests.Visuals.Controls;

/// <summary>
/// The hex view's two promises: a point maps back to the byte drawn there, in either column, and a highlight or
/// selection placed out of view scrolls itself in.
/// <para>Interactive desktop only (WPF elements need an STA thread). Run with <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("vcommon-byte-field-view")]
public class ByteFieldViewTests
{
    static ByteFieldView Laid(int bytes, double height)
    {
        var view = new ByteFieldView { Bytes = [.. Enumerable.Range(0, bytes).Select(i => (byte)i)], FontSize = 12 };
        view.Measure(new Size(1000, height));
        view.Arrange(new Rect(0, 0, 1000, height));
        return view;
    }

    [TestMethod]
    public void EveryByteIsFoundWhereItIsDrawn_InBothColumns() => UiThread.Run(() =>
    {
        var view = Laid(40, 400);
        double rowHeight = view.ExtentHeight / 3;
        Assert.AreEqual(3, (int)Math.Round(view.ExtentHeight / rowHeight), "40 bytes at 16 a row is three rows");

        // Sweep the second row left to right: the offset column, then 16..31 in hex, then 16..31 as text.
        var runs = new List<int>();
        for (double x = 0; x < view.ExtentWidth; x += 0.5)
        {
            int offset = view.OffsetAt(new Point(x, rowHeight * 1.5));
            if (offset >= 0 && (runs.Count == 0 || runs[^1] != offset)) runs.Add(offset);
        }
        var expected = Enumerable.Range(16, 16).Concat(Enumerable.Range(16, 16)).ToList();
        CollectionAssert.AreEqual(expected, runs);

        Assert.AreEqual(-1, view.OffsetAt(new Point(1, rowHeight * 2.5)), "the offset column is no byte");
        Assert.AreEqual(-1, view.OffsetAt(new Point(view.ExtentWidth - 1, rowHeight * 2.5)), "past the last byte of a short last row");
    });

    [TestMethod]
    public void AHighlightOutOfView_ScrollsIn() => UiThread.Run(() =>
    {
        var view = Laid(16 * 50, 60);
        Assert.AreEqual(0, view.VerticalOffset);

        view.Highlight = new ByteSpan(16 * 40, 2);
        Assert.IsTrue(view.VerticalOffset > 0, "row 40 is far below a 60-pixel viewport");
        CollectionAssert.Contains(Visible(view), 16 * 40, "the highlighted bytes are now in view");

        view.Selection = new ByteSpan(0, 18);
        Assert.AreEqual(0, view.VerticalOffset, "a selection at the top scrolls back up");
        CollectionAssert.Contains(Visible(view), 0);
    });

    /// <summary>Every byte a point in the viewport lands on.</summary>
    static List<int> Visible(ByteFieldView view)
    {
        var seen = new HashSet<int>();
        for (double y = 0; y < view.ViewportHeight; y += 2)
            for (double x = 0; x < view.ViewportWidth; x += 2)
                if (view.OffsetAt(new Point(x, y)) is >= 0 and var offset) seen.Add(offset);
        return [.. seen];
    }
}
