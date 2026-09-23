using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using ContentElement = Nexaflow.Visuals.Text.Editing.ContentElement;

namespace Nexaflow.Tests.Visuals.Markdown;

/// <summary>
/// The buttons in a block's corner, as a document being written in shows them: offered for a block where the pointer
/// rests on one and nowhere else, saying what the block is when pressed, and making a picture of it that is what it draws
/// — without what is drawn only for whoever is writing in it.
/// </summary>
[TestClass]
[TestCategory("Desktop")]
[DoNotParallelize]
[CoversNode("markdown-block-picture")]
public class BlockCornerTests
{
    private const string Document = "Pets:\n\n```mermaid\npie showData\n  \"Dogs\" : 30\n  \"Cats\" : 10\n```\n\nThe end.\n";

    [TestMethod]
    public void OverADiagramItsCornerOffersToCopyItAndToKeepAPictureOfIt() => UiThread.Run(() =>
        MarkdownEditorHarness.Run(Document, editor =>
        {
            var offered = editor.Corner(Middle(Box(editor, 0))).Select(offer => offer.Verb).ToList();

            CollectionAssert.AreEqual(new[] { LayoutVerbs.Copy, LayoutVerbs.Save }, offered);
        }));

    [TestMethod]
    public void OffEveryBlockNothingIsOffered() => UiThread.Run(() =>
        MarkdownEditorHarness.Run(Document, editor =>
        {
            var below = new Point(4, editor.Shown.Laid.Size.Height + 40);

            Assert.AreEqual(0, editor.Corner(below).Count, "past everything written there is no block");
        }));

    [TestMethod]
    public void AFormulaOnALineOfItsOwnIsAPictureWorthKeeping_AndOneInASentenceIsTheSentences() => UiThread.Run(() =>
        MarkdownEditorHarness.Run("Area $\\pi r^2$ of a circle.\n\n$$\n\\frac{a}{b}\n$$\n", editor =>
        {
            var blocks = MarkdownEditorHarness.Blocks(editor);
            Assert.AreEqual(2, blocks.Count, "precondition: both formulas are drawn");

            var inline = editor.Corner(Middle(Box(editor, 0))).Select(offer => offer.Verb).ToList();
            var display = editor.Corner(Middle(Box(editor, 1))).Select(offer => offer.Verb).ToList();

            CollectionAssert.DoesNotContain(inline, LayoutVerbs.Save, "in a sentence, the corner is the sentence's, and prose is no picture");
            CollectionAssert.Contains(display, LayoutVerbs.Save, "on its own line, the formula is the block");
        }));

    [TestMethod]
    public void KeepingAPictureTellsTheHostWhichBlock_AndThePictureIsTheBlocksSize() => UiThread.Run(() =>
        MarkdownEditorHarness.Run(Document, editor =>
        {
            var asked = new List<LayoutAct>();
            editor.Host = new Keeper(asked);

            var box = Box(editor, 0);
            editor.Raise(new LayoutIntent(LayoutVerbs.Save), Middle(box));

            var act = asked.Single();
            Assert.AreEqual(MarkdownKinds.Fence, act.Node?.Kind, "the block it was pressed on");
            StringAssert.StartsWith(act.Node!.Print(), "```mermaid");

            var picture = editor.Picture(act.Node, Brushes.White)!;
            var scale = VisualTreeHelper.GetDpi(editor).PixelsPerDip;
            Assert.AreEqual(Math.Ceiling(box.Width * scale), picture.PixelWidth, 2, "as big as the block is drawn");
            Assert.AreEqual(Math.Ceiling(box.Height * scale), picture.PixelHeight, 2);
        }));

    [TestMethod]
    public void APictureLeavesOutWhatIsChosen() => UiThread.Run(() =>
        MarkdownEditorHarness.Run(Document, editor =>
        {
            var pie = MarkdownEditorHarness.Blocks(editor)[0];
            var plain = Pixels(editor.Picture(pie, Brushes.White)!);

            editor.Shown.Select(editor.Shown.Markdown.IndexOf("Dogs", StringComparison.Ordinal), 4);
            Assert.IsTrue(editor.Shown.SelectionLength > 0, "precondition: something is chosen");

            CollectionAssert.AreEqual(plain, Pixels(editor.Picture(MarkdownEditorHarness.Blocks(editor)[0], Brushes.White)!),
                "a selection is not in the picture");
        }));

    [TestMethod]
    public void APictureIsTheContentAsItReadsWithNobodyWritingInIt() => UiThread.Run(() =>
    {
        var writable = Shown(new ContentElement("x", StyleFormat.Dark, new Marked()));
        var reading = Shown(new ContentElement("x", StyleFormat.Dark, new Marked()) { IsReadOnly = true });

        var picture = writable.Picture(Brushes.White);

        Assert.IsTrue(Differing(picture, OnPage(reading)) < 0.005, "the picture is what the content draws for a reader");
        Assert.IsTrue(Differing(picture, OnPage(writable)) > 0.005, "and not the mark it draws only where it can be written in");
    });

    // ── Reading the answers ─────────────────────────────────────────────────

    /// <summary>Where the <paramref name="index"/>th block of another language came out on the page.</summary>
    private static Rect Box(MarkdownSurface editor, int index)
    {
        var block = MarkdownEditorHarness.Blocks(editor)[index];
        var rects = editor.Shown.Laid.Root.RangeRects(block.Start, block.Length);
        Assert.IsTrue(rects.Count > 0, "the block was drawn");

        return rects.Aggregate(Rect.Union);
    }

    private static Point Middle(Rect box) => new(box.X + (box.Width / 2), box.Y + (box.Height / 2));

    /// <summary>A host that answers every verb it is asked, and remembers them.</summary>
    private sealed class Keeper(List<LayoutAct> asked) : ILayoutActions
    {
        public bool Invoke(LayoutAct act)
        {
            asked.Add(act);

            return true;
        }

        public IReadOnlyList<LayoutIntent> Menu(LayoutAct act) => [];
    }

    /// <summary>Content that draws a block for everybody, and a second only where somebody can write in it.</summary>
    private sealed class Marked : IContent
    {
        public Laid Lay(EditState state, double room, bool readOnly)
        {
            var build = new LayoutBuilder();
            build.Open("content");
            build.Draw(new RuleMark(new Rect(0, 0, 40, 20), Brushes.Blue));
            if (!readOnly) build.Draw(new RuleMark(new Rect(50, 0, 20, 20), Brushes.Red));
            build.Close();

            return new Laid(build.Seal(), new Size(80, 20), []);
        }
    }

    /// <summary>An element measured and arranged, as it would be on a page.</summary>
    private static ContentElement Shown(ContentElement element)
    {
        element.Measure(new Size(200, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));
        return element;
    }

    /// <summary>The block as the page shows it, on white — caret, holes and all.</summary>
    private static BitmapSource OnPage(ContentElement element)
    {
        var scale = VisualTreeHelper.GetDpi(element).PixelsPerDip;
        var size = element.RenderSize;

        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(element) { Stretch = Stretch.None, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top },
                             null, new Rect(size));
        }

        var page = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale),
                                          96 * scale, 96 * scale, PixelFormats.Pbgra32);
        page.Render(drawing);
        return page;
    }

    /// <summary>What share of two pictures' pixels differ by more than antialiasing does — every pixel, where their sizes do.</summary>
    private static double Differing(BitmapSource one, BitmapSource other)
    {
        if (one.PixelWidth != other.PixelWidth || one.PixelHeight != other.PixelHeight) return 1;

        var (left, right) = (Pixels(one), Pixels(other));
        var differing = 0;
        for (var at = 0; at < left.Length; at += 4)
            if (Enumerable.Range(0, 3).Any(channel => Math.Abs(left[at + channel] - right[at + channel]) > 48)) differing++;

        return differing / (left.Length / 4.0);
    }

    private static byte[] Pixels(BitmapSource picture)
    {
        var pixels = new byte[picture.PixelWidth * picture.PixelHeight * 4];
        picture.CopyPixels(pixels, picture.PixelWidth * 4, 0);
        return pixels;
    }
}
