using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;

using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Layout;
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

    [TestMethod]
    public void ALineGoneToHalfWayDownAVeryLongFile_StandsAtTheTop_AndTheLinesBesideThePageSaySo() => UiThread.Run(() =>
    {
        ContentMarginTests.Shown(Numbered(Lines), surface =>
        {
            // Gone to as a search goes to what it found: the caret on it, and the page with it at the top.
            const int line = Lines / 2;
            surface.Editor.MoveCaretTo(surface.Document.Current.OffsetOf(line - 1));
            surface.Editor.ScrollToLine(line - 1, ShownPlace.Top);

            for (var pass = 0; pass < 5; pass++)
            {
                surface.UpdateLayout();
                MarkdownEditorHarness.Pump();
            }

            var lines = surface.LinesShown();
            Assert.IsTrue(lines.Count > 0, "no lines stand on the page");

            
            var atTop = lines.Where(shown => shown.Top + shown.Height > 1).MinBy(shown => shown.Top)!;
            Assert.AreEqual(line, atTop.Number, $"the line at the top is said to be {atTop.Number}, standing at {atTop.Top:0}");
            Assert.IsTrue(atTop.Top is > -2 and < 2, $"line {atTop.Number} is said to stand at {atTop.Top:0}, not at the top");
        }, Typed);
    });

    [TestMethod]
    public void AfterTheEndWasShown_ALineGoneToHalfWayDown_IsMarkedForTheScreenAlone() => UiThread.Run(() =>
    {
        ContentMarginTests.Shown(Numbered(Lines), surface =>
        {
            // The end laid first, so that what is laid is far apart: what stands on the page, and the end of the file.
            AtTheEnd(surface);

            const int line = Lines / 2;
            surface.Editor.MoveCaretTo(surface.Document.Current.OffsetOf(line - 1));
            surface.Editor.ScrollToLine(line - 1, ShownPlace.Top);

            for (var pass = 0; pass < 5; pass++)
            {
                surface.UpdateLayout();
                MarkdownEditorHarness.Pump();
            }

            // Asked as the page asks while it paints: a screen above what is shown, what is shown, and a screen below it.
            var engine = surface.Engine;
            var shown = engine.OnScreen!.Value;
            var near = engine.NearScreen(new LayoutRect(-1e9, shown.Y - shown.Height, 2e9, shown.Height * 3));
            Assert.IsNotNull(near, "the page stands on lines laid, so marks are asked for some");

            var document = engine.State.Document;
            var (first, last) = (document.LineOf(near.Value.Start) + 1, document.LineOf(near.Value.End) + 1);
            Assert.IsTrue(first <= line && line <= last, $"marks are asked for lines {first}-{last}, which leave out line {line} at the top of the page");
            Assert.IsTrue(last - first < 200, $"marks are asked for lines {first}-{last}, for a page {surface.ViewportHeight:0} tall");
        }, Typed);
    });

    [TestMethod]
    public void DownThroughAFileLaidAtBothEnds_StepsALineAPress_WithoutStalling() => UiThread.Run(() =>
    {
        ContentMarginTests.Shown(Numbered(Lines), surface =>
        {
            // Laid at the end and half way down, so a step down has a long way laid around it to ask about.
            AtTheEnd(surface);

            const int line = Lines / 2;
            surface.Editor.MoveCaretTo(surface.Document.Current.OffsetOf(line - 1));
            surface.Editor.ScrollToLine(line - 1, ShownPlace.Top);

            for (var pass = 0; pass < 5; pass++)
            {
                surface.UpdateLayout();
                MarkdownEditorHarness.Pump();
            }

            var engine = surface.Engine;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (var press = 0; press < 40; press++)
            {
                engine.Input(new ContentKey(EditingKey.Down));
                surface.UpdateLayout();
            }

            clock.Stop();
            var state = engine.State;
            Assert.AreEqual(line - 1 + 40, state.Document.LineOf(state.Caret), "forty presses, forty lines down");
            Assert.IsTrue(clock.Elapsed.TotalSeconds < 5, $"forty steps down took {clock.Elapsed.TotalSeconds:0.0}s");
        }, Typed);
    });

    [TestMethod]
    public void ALineGoneToHalfWayDownAFileReadWhereItStands_StandsAtTheTop_AndTheLinesBesideThePageSaySo() => UiThread.Run(() =>
    {
        var path = System.IO.Path.Combine(TestTemp.Root, $"viewport-{System.Guid.NewGuid():N}.txt");
        System.IO.File.WriteAllText(path, Numbered(Lines), new UTF8Encoding(false));

        try
        {
            using var file = ContentFile.Open(path, new UTF8Encoding(false), 0, ContentFileAccess.ReadOnly);
            file.Scan();
            var document = new EditedDocument(ContentDocument.Of(file));

            var surface = new NexaTextSurface { IsReadOnly = true, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto };
            Typed(surface);
            surface.LeadingMargins.Add(new LineNumberMargin());
            var window = new Window { Width = 600, Height = 400, Content = surface, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -2000, Top = -2000 };

            try
            {
                window.Show();
                surface.Document = document;
                surface.UpdateLayout();
                MarkdownEditorHarness.Pump();

                const int line = Lines / 2;
                surface.Editor.MoveCaretTo(document.Current.OffsetOf(line - 1));
                surface.Editor.ScrollToLine(line - 1, ShownPlace.Top);

                for (var pass = 0; pass < 5; pass++)
                {
                    surface.UpdateLayout();
                    MarkdownEditorHarness.Pump();
                }

                var lines = surface.LinesShown();
                Assert.IsTrue(lines.Count > 0, "no lines stand on the page");

                var atTop = lines.Where(shown => shown.Top + shown.Height > 1).MinBy(shown => shown.Top)!;
                Assert.AreEqual(line, atTop.Number, $"the line at the top is said to be {atTop.Number}, standing at {atTop.Top:0}");
                Assert.IsTrue(atTop.Top is > -2 and < 2, $"line {atTop.Number} is said to stand at {atTop.Top:0}, not at the top");

                // And drawn so: a number beside every line on the page, not where the lines stood before the page moved.
                var margin = surface.LeadingMargins[0];
                var drawn = Glyphs(margin).Where(run => run.Bounds.Bottom > 0 && run.Bounds.Top < margin.ActualHeight).ToList();
                Assert.IsTrue(drawn.Count >= lines.Count - 1, $"{drawn.Count} numbers drawn beside the {lines.Count} lines on the page");
                Assert.IsTrue(drawn.Min(run => run.Bounds.Top) < atTop.Height, $"the first number is drawn {drawn.Min(run => run.Bounds.Top):0} down, not beside the top line");
            }
            finally { window.Close(); }
        }
        finally { System.IO.File.Delete(path); }
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
