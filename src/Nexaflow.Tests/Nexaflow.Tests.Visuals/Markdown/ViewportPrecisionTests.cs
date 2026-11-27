using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Markdown.PlainText;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The view of a page millions of pixels long: drawing takes its places in single precision, and a line handed over where it stands far
/// down the page comes out somewhere else, or not at all. So everything drawn is measured from the view — the last lines of a very long
/// file are drawn where they belong, nothing is drawn further than a screen or two from the view, and a press on them means them.
/// <para>Interactive desktop only — the surface lays its document during a render pass.</para>
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("windowed-reading")]
public class ViewportPrecisionTests
{
    /// <summary>Lines enough that the page runs to tens of millions of pixels — past where drawing loses its places.</summary>
    private const int Lines = 2_000_000;

    [TestMethod]
    public void AtTheEndOfAVeryLongFile_TheLastLinesAreDrawnInTheView() => UiThread.Run(() =>
    {
        ContentMarginTests.Shown(Numbered(Lines), surface =>
        {
            var lines = AtTheEnd(surface);

            Assert.IsTrue(surface.VerticalOffset > 1e7, $"the page is not long enough to test anything: the view stands {surface.VerticalOffset:0} down");
            Assert.AreEqual(Lines, lines[^1].Number, "the last line of the file is the last on the page");

            var drawn = Glyphs(surface.Shown);
            Assert.IsTrue(drawn.Count > 0, "nothing was drawn at the end of the file");

            var tall = surface.ViewportHeight;
            foreach (var run in drawn)
                Assert.IsTrue(run.Bounds.Top > -2 * tall && run.Bounds.Bottom < 3 * tall,
                              $"a line was drawn at {run.Bounds.Top:0}, not near a view {tall:0} tall");
        }, Typed);
    });

    [TestMethod]
    public void APressFarDownAVeryLongFile_MeansTheLineUnderIt() => UiThread.Run(() =>
    {
        ContentMarginTests.Shown(Numbered(Lines), surface =>
        {
            var lines = AtTheEnd(surface);
            var on = lines[lines.Count / 2];
            var point = surface.TranslatePoint(new Point(0, on.Top + (on.Height / 2)), surface.Shown) + new Vector(12, 0);

            Assert.IsTrue(surface.Shown.PointerDoubleClick(point), "the press landed on nothing");
            MarkdownEditorHarness.Pump();

            var state = surface.Engine.State;
            Assert.AreEqual(on.Number - 1, state.Document.LineOf(state.Caret), "the press meant another line than the one under it");
        }, Typed);
    });

    /// <summary>Shown as typed: plain text, its lines kept whole.</summary>
    private static void Typed(NexaTextSurface surface) => (surface.WrittenIn, surface.WrapsLines) = (TextParser.Language, false);

    /// <summary>The view scrolled to the end of the page, and the lines on it once what stands there is laid.</summary>
    private static IReadOnlyList<ShownLine> AtTheEnd(NexaTextSurface surface)
    {
        // Laying the end tells the page how tall it really is, which moves the end: scrolled to until it stays put.
        for (var tries = 0; tries < 20; tries++)
        {
            var was = surface.VerticalOffset;
            surface.ScrollToVerticalOffset(surface.ScrollableHeight);
            surface.UpdateLayout();
            MarkdownEditorHarness.Pump();

            if (tries > 0 && System.Math.Abs(surface.VerticalOffset - was) < 1) break;
        }

        var lines = surface.LinesShown();
        Assert.IsTrue(lines.Count > 0, "no lines stand on the page");

        return lines;
    }

    /// <summary><paramref name="count"/> lines, each saying which it is.</summary>
    private static string Numbered(int count)
    {
        var text = new StringBuilder(count * 14);
        for (var line = 1; line <= count; line++) text.Append("line ").Append(line).Append('\n');

        return text.ToString(0, text.Length - 1);
    }

    /// <summary>The runs of glyphs <paramref name="element"/> drew, where they stand on it.</summary>
    private static List<GlyphRunDrawing> Glyphs(UIElement element) => [.. Runs(VisualTreeHelper.GetDrawing(element))];

    private static IEnumerable<GlyphRunDrawing> Runs(Drawing? drawing) => drawing switch
    {
        GlyphRunDrawing run => [run],
        DrawingGroup group => group.Children.SelectMany(Runs),
        _ => [],
    };
}
