using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Nexaflow.Markdown.Ast;
using Nexaflow.Markdown.Editing;
using Nexaflow.Markdown.Prose;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Visuals.Text.Markdown;
using Nexaflow.Visuals.Text.Markdown.Printing;

namespace Nexaflow.Tests.Visuals.Markdown.Printing;

/// <summary>
/// A document on paper: laid out as it is drawn, cut into sheets only where nothing is cut through — between lines, never
/// through a diagram, never just after a heading — what is too wide or too tall for a sheet drawn smaller on it and nothing
/// else with it, and every sheet painted in the light palette paper is printed in, whatever its surface is drawn in.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("markdown-paper")]
public class MarkdownPaperTests
{
    private static readonly Size Room = new(300, 240);

    private const double Hair = 0.5;

    [TestMethod]
    public void TheSheetsMeet_FromTheTopOfTheDocumentToItsFoot() => UiThread.Run(() =>
    {
        var laid = Laid(Prose(40));
        var pages = PaperPages.Cut(laid, Room);
        var bands = pages.SelectMany(page => page.Bands).ToList();

        Assert.IsTrue(pages.Count > 2, $"forty paragraphs on sheets {Room.Height} tall take more than two of them, not {pages.Count}");
        Assert.AreEqual(0, bands[0].Top);

        for (var at = 1; at < bands.Count; at++)
            Assert.AreEqual(bands[at - 1].Bottom, bands[at].Top, $"band {at + 1} starts where band {at} ends");

        Assert.IsTrue(pages[^1].Bottom >= Words(laid).Max(line => line.Bottom) - Hair, "the last sheet holds the last line");
        Assert.IsTrue(pages.All(page => page.Drawn <= Room.Height + Hair), "no sheet holds more than it has room for");
        Assert.IsTrue(bands.All(band => band.Scale == 1), "and prose is drawn at its size");
    });

    [TestMethod]
    public void ASheetEndsBetweenTwoLines_NeverThroughOne() => UiThread.Run(() =>
    {
        var laid = Laid(Prose(30) + "```csharp\n" + string.Join("\n", Enumerable.Range(0, 40).Select(n => $"var line{n} = {n};")) + "\n```\n");

        foreach (var height in new[] { 120.0, 173, 240, 301 })
        {
            var pages = PaperPages.Cut(laid, new Size(Room.Width, height));

            foreach (var line in Words(laid))
                Assert.IsFalse(Cuts(pages, line), $"on sheets {height} tall, the line at {line} is cut");
        }
    });

    [TestMethod]
    public void WhatAnotherLanguageDrew_GoesOnOneSheetWhole() => UiThread.Run(() =>
    {
        var laid = Laid(Prose(6) + "```mermaid\nflowchart TD\n  A --> B\n  B --> C\n```\n\n" + Prose(6));
        var diagram = Drawn(laid, "Diagram");

        foreach (var height in Enumerable.Range(0, 12).Select(step => 270.0 + (step * 9)))
        {
            var pages = PaperPages.Cut(laid, new Size(Room.Width, height));

            Assert.IsFalse(Cuts(pages, diagram), $"on sheets {height} tall, the diagram at {diagram} is cut");
            Assert.AreEqual(1.0, Holding(pages, diagram).Scale, "a diagram that fits a sheet is drawn at its size");
        }
    });

    [TestMethod]
    public void NoSheetEndsOnAHeading() => UiThread.Run(() =>
    {
        var laid = Laid(string.Concat(Enumerable.Range(0, 12).Select(n => $"## Section {n}\n\n{Paragraph}\n\n")));
        var lines = Words(laid).ToList();
        var headings = HeadingLines(laid).ToHashSet();

        Assert.IsTrue(headings.Count >= 12, "every section has its heading");

        foreach (var height in Enumerable.Range(0, 30).Select(step => 140.0 + (step * 7)))
        {
            var pages = PaperPages.Cut(laid, new Size(Room.Width, height));

            foreach (var page in pages.Take(pages.Count - 1))
            {
                var last = lines.Where(line => line.Bottom <= page.Bottom + Hair).MaxBy(line => line.Bottom);

                Assert.IsFalse(headings.Contains(last), $"on sheets {height} tall, the sheet ending at {page.Bottom} ends on a heading");
            }
        }
    });

    [TestMethod]
    public void OneThingTallerThanASheet_IsDrawnSmallerOnASheetOfItsOwn() => UiThread.Run(() =>
    {
        var flow = "```mermaid\nflowchart TD\n" + string.Join("\n", Enumerable.Range(0, 20).Select(n => $"  N{n} --> N{n + 1}")) + "\n```\n\n";
        var laid = Laid(Prose(2) + flow + Prose(2));
        var diagram = Drawn(laid, "Diagram");
        var pages = PaperPages.Cut(laid, Room);
        var holding = Holding(pages, diagram);
        var sheet = pages.Single(page => page.Bands.Contains(holding));

        Assert.IsTrue(diagram.Height > Room.Height, "the diagram is taller than a sheet");
        Assert.IsTrue(holding.Scale < 1, "so it is drawn smaller");
        Assert.IsTrue(sheet.Drawn <= Room.Height + Hair, "small enough to go on a sheet");
        Assert.IsFalse(Words(laid).Where(line => !diagram.Contains(line)).Any(line => sheet.Top <= line.Top + Hair && line.Bottom <= sheet.Bottom + Hair),
                       "which it has to itself");
        Assert.IsTrue(pages.SelectMany(page => page.Bands).Where(band => band != holding).All(band => band.Scale == 1),
                      "and nothing else is drawn smaller for it");
    });

    [TestMethod]
    public void WhatIsWiderThanASheet_IsDrawnSmallerToFitIt_AndTheLinesRoundItAreNot() => UiThread.Run(() =>
    {
        var columns = Enumerable.Range(0, 30).ToList();
        var table = "| " + string.Join(" | ", columns.Select(n => $"column{n}")) + " |\n|" + string.Concat(columns.Select(_ => "---|")) + "\n| "
                  + string.Join(" | ", columns.Select(n => $"v{n}")) + " |\n";
        var laid = Laid(Prose(2) + table + "\n" + Prose(4));
        var pages = PaperPages.Cut(laid, Room);
        var wide = Words(laid).Where(line => line.Right > Room.Width).ToList();

        Assert.IsTrue(wide.Count > 0, "the table is wider than a sheet");
        Assert.AreEqual(1, wide.Select(line => Holding(pages, line).Scale).Distinct().Count(), "the table is drawn at one size, its columns under each other");

        foreach (var line in Words(laid))
        {
            var band = Holding(pages, line);

            if (wide.Contains(line)) Assert.IsTrue(line.Right * band.Scale <= Room.Width + Hair, $"the table's line at {line} is drawn small enough to fit");
            else if (wide.All(cell => line.Bottom <= cell.Top + Hair || line.Top >= cell.Bottom - Hair) && line.Right <= Room.Width)
                Assert.AreEqual(1.0, band.Scale, $"the line at {line}, outside the table, is drawn at its size");
        }
    });

    [TestMethod]
    public void NothingWritten_IsOneBlankSheet() => UiThread.Run(() =>
    {
        var paper = new MarkdownPaginator(string.Empty, new Size(816, 1056), StyleFormat.Light);

        Assert.AreEqual(1, paper.PageCount);
        Assert.AreNotSame(DocumentPage.Missing, paper.GetPage(0));
    });

    [TestMethod]
    public void EverySheetIsPaintedAtTheSizeOfTheSheet_TheDocumentInsideItsMargins() => UiThread.Run(() =>
    {
        var sheet = new Size(Room.Width + (2 * MarkdownPaginator.Margin), Room.Height + (2 * MarkdownPaginator.Margin));
        var inside = new Rect(new Point(MarkdownPaginator.Margin, MarkdownPaginator.Margin), Room);
        var paper = new MarkdownPaginator(Prose(20), sheet, StyleFormat.Light);

        Assert.IsTrue(paper.IsPageCountValid);
        Assert.AreEqual(paper.Pages.Count, paper.PageCount);
        Assert.AreEqual(Room, paper.Room);
        Assert.AreSame(DocumentPage.Missing, paper.GetPage(paper.PageCount));

        for (var at = 0; at < paper.PageCount; at++)
        {
            var page = paper.GetPage(at);

            Assert.AreEqual(sheet, page.Size);
            Assert.AreEqual(inside, page.ContentBox);
            Assert.IsTrue(Colours(page.Visual, inside).Any(colour => colour.A > 0), $"sheet {at + 1} has the document on it");
        }
    });

    [TestMethod]
    public void ASurfacePrintsOnWhitePaper_WhateverItIsDrawnIn() => UiThread.Run(() =>
    {
        var surface = new MarkdownSurface { Palette = StyleFormat.Dark, Markdown = Prose(3) };
        var sheet = new Size(Room.Width + (2 * MarkdownPaginator.Margin), Room.Height + (2 * MarkdownPaginator.Margin));
        var ink = Colours(surface.Paginator(sheet).GetPage(0).Visual, new Rect(sheet)).Where(colour => colour.A > 200).ToList();

        Assert.IsTrue(ink.Count > 0, "the sheet has words on it");
        Assert.IsTrue(ink.Average(colour => (colour.R + colour.G + colour.B) / 3.0) < 128, "and they are dark, as words on white paper are");
    });

    // ── The document, and what is on it ─────────────────────────────────────

    private const string Paragraph =
        "A paragraph long enough to wrap onto several lines at the width of the sheet it is printed on, so that a sheet can end between two of them.";

    private static string Prose(int paragraphs) => string.Concat(Enumerable.Range(0, paragraphs).Select(n => $"Paragraph {n}: {Paragraph}\n\n"));

    private static Laid Laid(string markdown) =>
        new ContentEngine().Lay(null, EditState.For(markdown), StyleFormat.Light, Room.Width, readOnly: true);

    /// <summary>Every line of words, where it is on the page.</summary>
    private static IEnumerable<Rect> Words(Laid laid) =>
        laid.Root.Placed().Where(placed => placed.Piece.Words is not null && !placed.Where.IsEmpty).Select(placed => placed.Where);

    /// <summary>The lines of words drawn from a heading.</summary>
    private static IEnumerable<Rect> HeadingLines(Laid laid) =>
        laid.Root.Placed()
            .Where(placed => placed.Piece.Words is not null && placed.Piece.Naming() is ContentPart part
                             && (part.Kind == MarkdownKinds.Heading || part.Ancestors().Any(holder => holder.Kind == MarkdownKinds.Heading)))
            .Select(placed => placed.Where);

    /// <summary>Where the one piece of <paramref name="kind"/> is drawn.</summary>
    private static Rect Drawn(Laid laid, string kind) => laid.Root.Placed().Single(placed => placed.Piece.Kind == kind).Where;

    /// <summary>Whether any band starts part-way through <paramref name="drawn"/>.</summary>
    private static bool Cuts(IReadOnlyList<PaperPage> pages, Rect drawn) =>
        pages.SelectMany(page => page.Bands).Any(band => band.Top > drawn.Top + Hair && band.Top < drawn.Bottom - Hair);

    /// <summary>The band that holds all of <paramref name="drawn"/>.</summary>
    private static PaperBand Holding(IReadOnlyList<PaperPage> pages, Rect drawn) =>
        pages.SelectMany(page => page.Bands).Single(band => band.Top <= drawn.Top + Hair && drawn.Bottom <= band.Bottom + Hair);

    // ── What a sheet is painted with ─────────────────────────────────────────

    private static List<Color> Colours(Visual visual, Rect within)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(within.Right), (int)Math.Ceiling(within.Bottom), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        var colours = new List<Color>();

        for (var y = (int)within.Top; y < bitmap.PixelHeight; y++)
            for (var x = (int)within.Left; x < bitmap.PixelWidth; x++)
            {
                var at = (y * stride) + (x * 4);
                colours.Add(Color.FromArgb(pixels[at + 3], pixels[at + 2], pixels[at + 1], pixels[at]));
            }

        return colours;
    }
}
