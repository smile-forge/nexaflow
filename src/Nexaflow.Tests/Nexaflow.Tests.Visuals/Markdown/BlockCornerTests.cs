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
using Nexaflow.Tests.Visuals.Editing;
using Nexaflow.Visuals.Text.Markdown.Prose;
using Nexaflow.Markdown.Ast;

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
    public void OverABlockItsButtonsStandInItsCorner_AndPressingOneTellsTheHostWhichBlock() => UiThread.Run(() =>
    {
        var asked = new List<LayoutAct>();
        var engine = new ContentEngine();
        var element = new MarkdownElement(Document, StyleFormat.Dark, new Keeper(asked), engine);
        element.Measure(new Size(600, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));

        var pie = engine.Blocked(Document.IndexOf("pie", StringComparison.Ordinal))!;
        var box = engine.Where(pie);

        engine.Input(new ContentHover(Middle(box)));

        var buttons = engine.Corner!.Root.SelfAndDescendants().Where(piece => piece.Acts is not null).ToList();
        Assert.AreEqual(2, buttons.Count, "copying it and keeping a picture of it, as the pie says");
        Assert.IsTrue(buttons.All(button => button.Bounds.Top >= box.Top && button.Bounds.Bottom <= box.Bottom),
                      "standing at the top of the band of the page the block owns");
        Assert.IsTrue(buttons.All(button => engine.Blocked(Middle(button.Bounds))?.Start == pie.Start),
                      "and the block under them is the one they belong to");

        engine.Input(new ContentPress(Middle(buttons[1].Bounds)));

        var act = asked.Single();
        Assert.AreEqual(LayoutVerbs.Save, act.Intent.Verb);
        StringAssert.StartsWith(act.Node!.Print(), "```mermaid", "told which block it was pressed on");

        engine.Input(new ContentHover(null));
        Assert.IsNull(engine.Corner, "and gone once the pointer has left");
    });

    [TestMethod]
    public void OffEveryBlockNothingIsOffered() => UiThread.Run(() =>
        MarkdownEditorHarness.Run(Document, editor =>
        {
            var below = new Point(4, editor.Shown.Laid.Size.Height + 40);

            Assert.AreEqual(0, editor.Corner(below).Count, "past everything written there is no block");
        }));

    [TestMethod]
    public void ABlockWhoseLanguageFallsOverOnItsCornerLeavesTheDocumentDrawn() => UiThread.Run(() =>
    {
        // The corner is worked out inside laying out but past the catch that covers it, and worked out again on every
        // hover, out of code the language wrote. Moving the pointer must not be able to cost the reader the document.
        var document = $"Pets:\n\n```{HandLaid.Uncornering}\nx\n```\n\nThe end.\n";
        var engine = new ContentEngine();
        var element = new MarkdownElement(document, StyleFormat.Dark, engine: engine);
        element.Measure(new Size(600, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));

        var block = engine.Blocked(document.IndexOf(HandLaid.Uncornering, StringComparison.Ordinal))!;
        engine.Input(new ContentHover(Middle(engine.Where(block))));

        Assert.IsNull(engine.Corner, "no corner, because asking the language what it offers threw");
        Assert.IsFalse(engine.Laid.ShowsSource, "and the hover cost the document nothing");
    });

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
            Assert.AreEqual(Kinds.Block, act.Node?.Kind, "the block it was pressed on");
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
        var writable = Shown(HandLaid.Element("x", Marked));
        var reading = Shown(HandLaid.Element("x", Marked, readOnly: true));

        var picture = writable.Picture(Brushes.White);

        Assert.IsTrue(Differing(picture, OnPage(reading)) < 0.005, "the picture is what the content draws for a reader");
        Assert.IsTrue(Differing(picture, OnPage(writable)) > 0.005, "and not the mark it draws only where it can be written in");
    });

    [TestMethod]
    public void APointerCanTravelFromTheWordsToAButtonAndPressIt() => UiThread.Run(() =>
    {
        // A reader reaches a button by moving the pointer onto it, a few units at a time. Every point on that way is over
        // the block the corner belongs to or over the corner itself, so the corner has to survive all of them — arriving on
        // a button without having travelled there is not something a hand can do.
        var asked = new List<LayoutAct>();
        var engine = new ContentEngine();
        var element = new MarkdownElement(Document, StyleFormat.Dark, new Keeper(asked), engine);
        element.Measure(new Size(600, double.PositiveInfinity));
        element.Arrange(new Rect(element.DesiredSize));

        var pie = engine.Blocked(Document.IndexOf("pie", StringComparison.Ordinal))!;
        var box = engine.Where(pie);
        var from = Middle(box);

        engine.Input(new ContentHover(from));
        Assert.IsNotNull(engine.Corner, "precondition: the pie offers a corner where the pointer rests on it");

        var buttons = engine.Corner!.Root.SelfAndDescendants().Where(piece => piece.Acts is not null).ToList();
        var to = Middle(buttons[1].Bounds);

        foreach (var (at, step) in Walked(from, to))
        {
            engine.Input(new ContentHover(at));
            Assert.IsNotNull(engine.Corner,
                             $"the corner went {step} of the way from the words at {from} to the button at {to}, on reaching {at}");
        }

        engine.Input(new ContentPress(to));
        Assert.AreEqual(LayoutVerbs.Save, asked.Single().Intent.Verb, "and the button the pointer walked to answered the press");
    });

    [TestMethod]
    public void EveryPointInsideABlockBelongsToIt_SoItsCornerStaysWhileThePointerIsOnIt() => UiThread.Run(() =>
    {
        // The empty corners of a diagram's card — the ones a pie's disc never reaches — have nothing of the diagram drawn
        // in them, so the piece nearest them is the paragraph above or below. A reader crossing one of those corners on the
        // way to the buttons has not left the diagram, and must not lose them.
        var engine = new ContentEngine();
        var element = new MarkdownElement(Document, StyleFormat.Dark, engine: engine);
        element.Measure(new Size(900, double.PositiveInfinity));
        element.Arrange(new Rect(new Size(1168, element.DesiredSize.Height)));

        var pie = engine.Blocked(Document.IndexOf("pie", StringComparison.Ordinal))!;
        var box = engine.Where(pie);

        var map = new System.Text.StringBuilder();
        var missed = 0;

        for (var y = box.Y; y < box.Bottom; y += 8)
        {
            for (var x = box.X; x < box.Right; x += 8)
            {
                var found = engine.Blocked(new Point(x, y))?.Start == pie.Start;
                if (!found) missed++;

                map.Append(found ? '#' : '.');
            }

            map.AppendLine();
        }

        Assert.AreEqual(0, missed, $"inside the pie's own box, but answered with another block or none:\n{map}");
    });

    [TestMethod]
    public void TheButtonsStandInFromThePanelsRightEdge_WhateverWidthTheWordsCameOutAt() => UiThread.Run(() =>
    {
        // The scroller a surface keeps a document in cannot scroll sideways, so the element is measured with the panel's width
        // and arranged at that same width however narrow the content came out — which is what this does, so the room the
        // buttons are placed from here is the room they are placed from on a page. Placed from the widest line instead they
        // would stand wherever the longest paragraph happened to reach, and would move when one was typed into.
        const double Panel = 1000;

        var engine = new ContentEngine();
        var element = new MarkdownElement(Document, StyleFormat.Dark, engine: engine);
        element.Measure(new Size(Panel, double.PositiveInfinity));
        element.Arrange(new Rect(new Size(Panel, element.DesiredSize.Height)));

        Assert.IsTrue(engine.Laid.Size.Width < Panel - 100, "precondition: the words came out far narrower than the panel");

        var pie = engine.Blocked(Document.IndexOf("pie", StringComparison.Ordinal))!;
        engine.Input(new ContentHover(Middle(engine.Where(pie))));

        var buttons = engine.Corner!.Root.SelfAndDescendants().Where(piece => piece.Acts is not null).ToList();
        Assert.AreEqual(2, buttons.Count, "precondition: the pie offers both buttons");

        var right = buttons.Max(button => button.Bounds.Right);
        Assert.AreEqual(Panel - 12, right, 0.5, "in from the panel's right edge by the corner's own inset");
        Assert.IsTrue(right <= element.RenderSize.Width, "which is inside the element, all of it the reader can see");

        foreach (var button in buttons)
            Assert.AreEqual(pie.Start, engine.Blocked(Middle(button.Bounds))?.Start, "and the pie is the block under it");
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

    /// <summary>The points a pointer passes through on its way from one place to another, as a hand moves it: a few units at a time.</summary>
    private static IEnumerable<(Point At, string Step)> Walked(Point from, Point to)
    {
        var away = to - from;
        var steps = Math.Max(1, (int)Math.Ceiling(away.Length / 4));

        for (var step = 1; step <= steps; step++)
            yield return (new Point(from.X + (away.X * step / steps), from.Y + (away.Y * step / steps)), $"{step}/{steps}");
    }

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
    private static Laid Marked(EditState state, bool readOnly)
    {
        var build = new LayoutBuilder();
        build.Open("content");
        build.Draw(new RuleMark(new Rect(0, 0, 40, 20), Brushes.Blue));
        if (!readOnly) build.Draw(new RuleMark(new Rect(50, 0, 20, 20), Brushes.Red));
        build.Close();

        return new Laid(build.Seal(), new Size(80, 20), []);
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
