using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;

using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The bars of content that scrolls itself stand for the engine's view over the page as it is laid now: a long document shows its bar as soon
/// as it is laid, before anything has moved, and a document cut short brings the view back inside it and puts the bar away.
/// <para>Interactive desktop only — the surface lays its document during a render pass.</para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("windowed-reading")]
public class SurfaceBarsTests
{
    private static readonly string Long = string.Join("\n", Enumerable.Range(1, 400).Select(line => $"line {line}"));

    [TestMethod]
    public void ALongDocument_ShowsItsBar_AsSoonAsItIsLaid() => UiThread.Run(() => MarkdownEditorHarness.Run(Long, surface =>
    {
        var bar = Bar(surface);

        Assert.AreEqual(Visibility.Visible, bar.Visibility, "nothing on the screen said the page runs on past it");
        Assert.AreEqual(surface.ScrollableHeight, bar.Maximum, 1, "the bar stands for a page of another length");
        Assert.AreEqual(0, surface.VerticalOffset, "laying it moved the view");
    }, AsTyped));

    [TestMethod]
    public void ADocumentCutShort_BringsTheViewBackInside_AndPutsTheBarAway() => UiThread.Run(() => MarkdownEditorHarness.Run(Long, surface =>
    {
        surface.ScrollToVerticalOffset(surface.ScrollableHeight);
        MarkdownEditorHarness.Pump();
        Assert.IsTrue(surface.VerticalOffset > 0);

        surface.Document.Load("one line");
        surface.UpdateLayout();
        MarkdownEditorHarness.Pump();

        Assert.AreEqual(0, surface.VerticalOffset, "the view was left past the end of what is there");
        Assert.AreEqual(Visibility.Collapsed, Bar(surface).Visibility, "the bar stayed for a page that fits");
    }, AsTyped));

    [TestMethod]
    public void ThePage_IsLaidInTheViewLessTheRoomKeptRoundIt_SoNothingIsLaidUnderTheBar() => UiThread.Run(() => MarkdownEditorHarness.Run(Prose, surface =>
    {
        Assert.AreEqual(Visibility.Visible, Bar(surface).Visibility);
        Assert.IsTrue(surface.ExtentWidth <= surface.ViewportWidth + 0.5,
                      $"the page, the room round it among it, was laid {surface.ExtentWidth:0} wide in a view {surface.ViewportWidth:0} wide: its last words stand past the view's edge");
    }, surface => surface.ContentPadding = new Thickness(30, 10, 30, 10)));

    /// <summary>Paragraphs enough to scroll, each long enough to wrap: what is laid to the page's width.</summary>
    private static readonly string Prose = string.Join("\n\n", Enumerable.Range(1, 120).Select(paragraph =>
        $"Paragraph {paragraph} runs on for long enough that it has to wrap, and wraps where the page ends rather than past it, under whatever stands at its edge."));

    /// <summary>The bar the view scrolls down by.</summary>
    private static ScrollBar Bar(NexaTextSurface surface) =>
        MarkdownEditorHarness.Drawn<ScrollBar>(surface) is { Orientation: System.Windows.Controls.Orientation.Vertical } bar
            ? bar
            : throw new AssertFailedException("the surface has no bar to scroll down by");

    private static void AsTyped(NexaTextSurface surface) => surface.WrittenIn = ContentLanguages.AsTyped;
}
