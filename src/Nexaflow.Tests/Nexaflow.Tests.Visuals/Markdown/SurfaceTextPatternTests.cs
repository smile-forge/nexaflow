using System.Linq;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Automation.Text;

using Nexaflow.Markdown.Editing;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>A surface's text as a screen reader reads it: a range at a time, by words and lines, found and picked out — never the whole of a long document at once.</summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("content-text-automation")]
public class SurfaceTextPatternTests
{
    private const string Text = "first line here\nsecond line\nthird";

    [TestMethod]
    public void TheDocumentRange_ReadsTheText_AndALineOfIt() => UiThread.Run(() => MarkdownEditorHarness.Run(Text, surface =>
    {
        var text = Provider(surface);

        Assert.AreEqual(Text, text.DocumentRange.GetText(-1));

        var line = text.DocumentRange.Clone();
        line.MoveEndpointByRange(TextPatternRangeEndpoint.End, line, TextPatternRangeEndpoint.Start);
        line.Move(TextUnit.Line, 1);
        Assert.AreEqual("second line\n", line.GetText(-1));
    }, AsTyped));

    [TestMethod]
    public void ARange_GrowsToTheWordItIsIn() => UiThread.Run(() => MarkdownEditorHarness.Run(Text, surface =>
    {
        var range = Provider(surface).DocumentRange;
        range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, 8);
        range.ExpandToEnclosingUnit(TextUnit.Word);

        Assert.AreEqual("line ", range.GetText(-1));
    }, AsTyped));

    [TestMethod]
    public void TextFound_IsARange_AndPickingItOut_PicksItOutInTheView() => UiThread.Run(() => MarkdownEditorHarness.Run(Text, surface =>
    {
        var found = Provider(surface).DocumentRange.FindText("SECOND", backward: false, ignoreCase: true);

        Assert.IsNotNull(found);
        found.Select();

        Assert.AreEqual("second", surface.Editor.SelectedText());
        Assert.AreEqual("second", Provider(surface).GetSelection().Single().GetText(-1));
    }, AsTyped));

    [TestMethod]
    public void ALongDocumentsValue_IsAsMuchAsIsHeldAsOneString() => UiThread.Run(() =>
    {
        var surface = new NexaTextSurface { WrittenIn = ContentLanguages.AsTyped, Document = new EditedDocument(new string('a', ContentDocument.RopeFrom * 2)) };
        var value = (IValueProvider)UIElementAutomationPeer.CreatePeerForElement(surface).GetPattern(PatternInterface.Value);

        Assert.AreEqual(ContentDocument.RopeFrom, value.Value.Length);
    });

    [TestMethod]
    public void ALongDocument_IsAPaneAScreenReaderScrolls_ByAPage_AndToTheEnd() => UiThread.Run(() => MarkdownEditorHarness.Run(
        string.Join("\n", Enumerable.Range(1, 400).Select(line => $"line {line}")), surface =>
    {
        var scroll = (IScrollProvider)UIElementAutomationPeer.CreatePeerForElement(surface).GetPattern(PatternInterface.Scroll);

        Assert.IsTrue(scroll.VerticallyScrollable);
        Assert.AreEqual(0, scroll.VerticalScrollPercent, 0.01);
        Assert.IsTrue(scroll.VerticalViewSize is > 0 and < 100, $"the view is {scroll.VerticalViewSize}% of the page");

        scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.LargeIncrement);
        MarkdownEditorHarness.Pump();
        Assert.AreEqual(surface.ViewportHeight, surface.VerticalOffset, 1, "a page on is the view's height on");

        scroll.SetScrollPercent(ScrollPatternIdentifiers.NoScroll, 100);
        MarkdownEditorHarness.Pump();
        Assert.AreEqual(100, scroll.VerticalScrollPercent, 0.5);
    }, AsTyped));

    [TestMethod]
    public void ContentShownWhole_IsNoPaneOfItsOwn() => UiThread.Run(() =>
    {
        var surface = new NexaTextSurface { WrittenIn = ContentLanguages.AsTyped, Document = new EditedDocument("short") };

        Assert.IsNull(UIElementAutomationPeer.CreatePeerForElement(surface).GetPattern(PatternInterface.Scroll), "whatever holds it scrolls it");
    });

    private static ITextProvider Provider(NexaTextSurface surface) =>
        (ITextProvider)UIElementAutomationPeer.CreatePeerForElement(surface).GetPattern(PatternInterface.Text);

    private static void AsTyped(NexaTextSurface surface) => surface.WrittenIn = ContentLanguages.AsTyped;
}
