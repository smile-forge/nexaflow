using System.Linq;
using System.Windows.Controls;

using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Lines;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Markdown.Code;

/// <summary>
/// How content laid line by line is set: tabs reaching to stops a set number of characters apart, whitespace shown as faint marks that
/// stand for none of it, and a page that slides sideways for a line too long for it rather than cutting it off.
/// </summary>
[TestClass]
[DoNotParallelize]
[CoversNode("content-line-setting")]
public class LineSettingTests
{
    [TestMethod]
    public void ATab_ReachesTheNextStop_TabWidthCharactersApart() => UiThread.Run(() =>
    {
        foreach (var width in new[] { 2, 4, 8 })
        {
            var engine = Laid("a\tb", WpfStyles.Dark with { TabWidth = width });

            var one = engine.Laid.Root.CaretRect(1).X;
            var past = engine.Laid.Root.CaretRect(2).X;

            Assert.AreEqual(width, past / one, 0.01, $"with stops {width} apart, the tab did not reach the first stop");
        }
    });

    [TestMethod]
    public void ATab_IsOneCharacterACaretStandsEitherSideOf() => UiThread.Run(() =>
    {
        var engine = Laid("\tx", WpfStyles.Dark);

        var stops = engine.Laid.Stops;
        CollectionAssert.Contains(stops.ToList(), 0, "nowhere to stand in front of a tab at a line's start");
        CollectionAssert.Contains(stops.ToList(), 1, "nowhere to stand after the tab");
        Assert.IsTrue(engine.Laid.Root.CaretRect(1).X > engine.Laid.Root.CaretRect(0).X);
    });

    [TestMethod]
    public void HowFarATabReaches_ComesToTheNextStop()
    {
        Assert.AreEqual(40, LinedSetting.TabAcross(0, 10, 4), 1e-9);
        Assert.AreEqual(25, LinedSetting.TabAcross(15, 10, 4), 1e-9);
        Assert.AreEqual(40, LinedSetting.TabAcross(40, 10, 4), 1e-9, "a tab standing at a stop reaches the next");
        Assert.AreEqual(40, LinedSetting.TabAcross(39.9999999999, 10, 4), 1e-6, "a hair short of a stop, from adding up advances, is at it");
    }

    [TestMethod]
    public void WhitespaceShown_IsMarkedOver_AndChangesNothingACaretOrTheTextMeets() => UiThread.Run(() =>
    {
        const string text = "if (x)\n\treturn  y;\n";
        var plain = Laid(text, WpfStyles.Dark);
        var shown = Laid(text, WpfStyles.Dark with { ShowsWhitespace = true });

        Assert.IsTrue(shown.Laid.Root.SelfAndDescendants().Any(piece => piece.Kind == LinedSetting.WhitespaceKind), "nothing was marked");
        Assert.IsFalse(plain.Laid.Root.SelfAndDescendants().Any(piece => piece.Kind == LinedSetting.WhitespaceKind), "whitespace was marked unasked");

        CollectionAssert.AreEqual(plain.Laid.Stops.ToList(), shown.Laid.Stops.ToList(), "the marks moved where a caret can stand");
        for (var offset = 0; offset <= text.Length; offset++)
            Assert.AreEqual(plain.Laid.Root.CaretRect(offset).X, shown.Laid.Root.CaretRect(offset).X, 0.01, $"the caret at {offset} moved");
    });

    [TestMethod]
    [TestCategory("Desktop")]
    public void APageThatSlides_LaysInItsOwnWidth_AndReachesAsFarAsItsWidestLine() => UiThread.Run(() =>
    {
        var line = string.Concat(Enumerable.Repeat("long_identifier ", 40));

        MarkdownEditorHarness.Run(line + "\nshort\n", surface =>
        {
            var (_, _, viewport) = MarkdownEditorHarness.Scrolled(surface);

            Assert.IsTrue(surface.ExtentWidth > surface.ViewportWidth, "the long line was cut off at the page's edge");
            Assert.IsTrue(viewport > 0);
        }, surface =>
        {
            surface.WrittenIn = ContentLanguages.AsTyped;
            surface.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        });
    });

    private static ContentEngine Laid(string text, LayoutStyle style)
    {
        var engine = new ContentEngine();
        engine.Show(ContentLanguages.AsTyped, style);
        engine.Start(text);
        engine.Fit(400);

        return engine;
    }
}
