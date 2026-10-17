using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using Nexaflow.Features.Pdf.Document;
using Nexaflow.Features.Pdf.Reading;
using Nexaflow.Features.Pdf.Rendering;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using UglyToad.PdfPig.Graphics.Core;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// A page read as objects and laid as a layout tree, from content streams written here so each test says exactly what
/// the page draws: a clip is a group its content sits in, painting order survives into the tree, the graphics state's
/// alpha reaches the paint, a hidden layer draws nothing, and the page's text is where its runs say.
/// </summary>
[TestClass]
[CoversNode("pdf-document-ast")]
public class PdfPageReadingTests
{
    /// <summary>The page every test draws on: 200 points square, so page space is the PDF's y turned over at 200.</summary>
    private const double Side = 200;

    [TestMethod]
    public void AClip_IsAGroupItsContentSitsIn_AndWhatComesAfterItsRestoreIsOutsideIt()
    {
        var page = Read("q 10 10 50 50 re W n 1 0 0 rg 0 0 200 200 re f Q 0 0 1 rg 0 0 20 20 re f");

        Assert.AreEqual(2, page.Content.Children.Count);
        var clip = (PdfGroupNode)page.Content.Children[0];
        Assert.AreEqual(PdfGroupRole.Clip, clip.Role);
        Assert.IsNotNull(clip.Clip);

        // 10..60 up from the bottom of a 200-point page is 140..190 down from its top.
        var kept = clip.Clip.Bounds(Matrix.Identity);
        Assert.AreEqual(10, kept.Left, 0.01);
        Assert.AreEqual(140, kept.Top, 0.01);
        Assert.AreEqual(50, kept.Width, 0.01);
        Assert.AreEqual(50, kept.Height, 0.01);

        var red = (PdfPathNode)clip.Children.Single();
        Assert.AreEqual(Colors.Red, ((PdfSolidPaint)red.Fill!).Colour);

        var blue = (PdfPathNode)page.Content.Children[1];
        Assert.AreEqual(Colors.Blue, ((PdfSolidPaint)blue.Fill!).Colour);
    }

    [TestMethod]
    public void ThePaintingOrder_SurvivesIntoTheLayoutTree()
    {
        // A fill before a clipped group and one after it: the tree must draw them in that order, which a piece painting
        // its own marks before its children would not.
        var page = Read("0 1 0 rg 0 0 200 200 re f q 0 0 100 100 re W n 1 0 0 rg 0 0 200 200 re f Q 0 0 1 rg 0 0 20 20 re f");
        var tree = PdfPageLayout.Lay(page, Brushes.White);

        var kinds = tree.Root.Children.Select(static c => c.Kind).ToList();
        CollectionAssert.AreEqual(new[] { PdfPieces.Ink, PdfPieces.Group, PdfPieces.Ink }, kinds);

        foreach (var piece in tree.Root.SelfAndDescendants())
            if (piece.Kind != PdfPieces.Text && piece.Kind != PdfPieces.Page)
                Assert.IsFalse(piece.Marks.Length > 0 && piece.Children.Count > 0,
                    $"{piece.Kind} both draws and holds pieces, so its marks would paint under its children");

        var picture = UiThreadPainted(tree, page);
        Assert.AreEqual(Colors.Blue, Pixel(picture, 5, 195), "the last fill is on top");
        Assert.AreEqual(Colors.Red, Pixel(picture, 50, 150), "the clipped fill is over the first");
        Assert.AreEqual(Colors.Lime, Pixel(picture, 150, 50), "outside the clip, the first fill shows");
    }

    [TestMethod]
    public void TheGraphicsStatesAlpha_ReachesThePaint()
    {
        var page = Read("/A gs 1 0 0 rg 0 0 100 100 re f", resources: "/ExtGState << /A << /ca 0.5 >> >>");

        var fill = (PdfSolidPaint)((PdfPathNode)page.Content.Children.Single()).Fill!;
        Assert.AreEqual(0.5, fill.Alpha, 0.01);
    }

    [TestMethod]
    public void AHiddenLayer_DrawsNothing()
    {
        var page = Read("/OC /hidden BDC 1 0 0 rg 0 0 100 100 re f EMC 0 0 1 rg 0 0 20 20 re f",
            resources: "/Properties << /hidden 8 0 R >>",
            catalog: "/OCProperties << /OCGs [8 0 R] /D << /OFF [8 0 R] >> >>",
            extra: [(8, "<< /Type /OCG /Name (Hidden) >>")]);

        var drawn = page.Content.Children.OfType<PdfPathNode>().ToList();
        Assert.AreEqual(1, drawn.Count, "only the shown layer's fill is drawn");
        Assert.AreEqual(Colors.Blue, ((PdfSolidPaint)drawn[0].Fill!).Colour);
    }

    [TestMethod]
    public void ThePagesText_IsWhereItsRunsSay_ALineBreakBetweenLines()
    {
        var page = Read("BT /F 12 Tf 20 150 Td (falcon) Tj 0 -30 Td (kestrel) Tj ET",
            resources: "/Font << /F << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");

        Assert.AreEqual("falcon\nkestrel", page.Text);

        var runs = Runs(page.Content).ToList();
        Assert.AreEqual(2, runs.Count);
        foreach (var run in runs)
            Assert.AreEqual(run.Text, page.Text.Substring(run.TextStart, run.Text.Length));

        Assert.IsTrue(runs[0].Glyphs.All(static g => g.Outline is { IsEmpty: false }),
            "a font the page only names is drawn in an installed face");
    }

    [TestMethod]
    public void ATextRun_IsSelectableWhereItsGlyphsAre()
    {
        var page = Read("BT /F 24 Tf 20 100 Td (peregrine) Tj ET",
            resources: "/Font << /F << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >>");
        var tree = PdfPageLayout.Lay(page, Brushes.White);

        var run = Runs(page.Content).Single();
        var middle = run.Glyphs[4].Box;
        var at = new Point((middle.Left + middle.Width / 2) * PdfPageLayout.DipsPerPoint,
                           (middle.Top + middle.Height / 2) * PdfPageLayout.DipsPerPoint);

        var offset = tree.Root.OffsetAt(at);
        Assert.IsTrue(offset is >= 3 and <= 5, $"a press on the fifth letter lands at {offset}, not on it");
    }

    // ── Writing and reading a one-page document ───────────────────────────────────

    [TestMethod]
    [CoversNode("pdf-links")]
    public void ALinkAnnotation_IsAPieceThatFollowsItsTarget()
    {
        var page = Read("0 0 1 rg 20 20 60 20 re f",
            page: "/Annots [5 0 R 6 0 R]",
            extra:
            [
                (5, "<< /Type /Annot /Subtype /Link /Rect [20 20 80 40] /A << /S /URI /URI (https://example.org/falcons) >> >>"),
                (6, "<< /Type /Annot /Subtype /Link /Rect [20 100 80 120] /Dest [3 0 R /XYZ 0 150 0] >>"),
            ]);

        Assert.AreEqual(2, page.Annotations.Count);
        var uri = (PdfUriTarget)page.Annotations[0].Link!;
        Assert.AreEqual("https://example.org/falcons", uri.Uri);
        var inside = (PdfPageTarget)page.Annotations[1].Link!;
        Assert.AreEqual(1, inside.PageNumber);
        Assert.AreEqual(150, inside.Top);

        // The link's rectangle, 20..40 up from the bottom, is 160..180 down from the top of the page.
        Assert.AreEqual(160, page.Annotations[0].Bounds.Top, 0.01);

        var tree = PdfPageLayout.Lay(page, Brushes.White);
        // A press means what the pieces placed under it answer to, as every host of a layout tree reads it.
        var at = new Point(50 * PdfPageLayout.DipsPerPoint, 170 * PdfPageLayout.DipsPerPoint);
        var answers = tree.Root.Placed()
            .Where(p => p.Where.Contains(at) && p.Piece.Acts?.For(LayoutGesture.Click) is not null)
            .Select(static p => p.Piece.Acts!.For(LayoutGesture.Click)!.Value)
            .ToList();
        Assert.AreEqual(1, answers.Count, "a press on the link lands on the piece that follows it");
        Assert.AreEqual(LayoutVerbs.Navigate, answers[0].Verb);
        Assert.AreEqual("https://example.org/falcons", answers[0].Target);

        Assert.IsTrue(PdfLinks.TryRead(PdfLinks.ToPage(inside), out var back));
        Assert.AreEqual(inside, back, "a link within the document round-trips through its target");
    }

    [TestMethod]
    [CoversNode("pdf-print")]
    public void APrintedPage_IsDrawnWholeOnTheSheet_AsLargeAsFits()
    {
        var path = TestTemp.CreateFile(".pdf");
        File.WriteAllBytes(path, Write("1 0 0 rg 0 0 200 200 re f", "", "", [], ""));
        try
        {
            UiThread.Run(() =>
            {
                using var scope = PdfDocumentScope.TryOpen(path, CancellationToken.None);
                Assert.IsNotNull(scope);
                var source = new PdfPageSource(new OpenScope(scope), scope.Pages.ReadDocument(CancellationToken.None), Brushes.White);
                var sheet = new Size(400, 600);
                var paginator = new PdfPaginator(source, sheet, CancellationToken.None);

                Assert.AreEqual(1, paginator.PageCount);
                var printed = paginator.GetPage(0);
                Assert.AreEqual(sheet, printed.Size);

                // A square page on a tall sheet: as wide as the sheet, centred down it.
                var bounds = VisualTreeHelper.GetDescendantBounds(printed.Visual);
                Assert.AreEqual(400, bounds.Width, 1);
                Assert.AreEqual(100, bounds.Top, 1);
            });
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>An open document handed straight to the code that reads it, as the reader hands it under its lock.</summary>
    private sealed class OpenScope(PdfDocumentScope scope) : IPdfDocumentAccess
    {
        public System.Threading.Tasks.Task<T?> ReadAsync<T>(Func<PdfDocumentScope, T> read, CancellationToken ct) =>
            System.Threading.Tasks.Task.FromResult<T?>(read(scope));
    }

    [TestMethod]
    [CoversNode("pdf-blend")]
    public void AMultipliedFill_DarkensWhatIsUnderIt_AndLeavesTheRestOfThePageAlone()
    {
        // Grey across the left half, then red multiplied across the bottom half: where they meet the red is darkened by
        // the grey; over white paper multiplying changes nothing, so the red there is red.
        var page = Read("0.5 g 0 0 100 200 re f /M gs 1 0 0 rg 0 0 200 100 re f",
            resources: "/ExtGState << /M << /BM /Multiply >> >>");
        Assert.IsFalse(page.Unsupported.Any(static u => u.Kind == PdfUnsupportedKind.BlendMode),
            "a blend on a page is composited, not reported");

        var tree = PdfPageLayout.Lay(page, Brushes.White);
        var picture = UiThreadPainted(tree, page);

        var multiplied = Pixel(picture, 50, 150);
        Assert.AreEqual(128, multiplied.R, 3, "red times grey");
        Assert.AreEqual(0, multiplied.G, 3);
        Assert.AreEqual(0, multiplied.B, 3);
        Assert.AreEqual(Colors.Red, Pixel(picture, 150, 150), "red times white is red");

        var grey = Pixel(picture, 50, 50);
        Assert.AreEqual(128, grey.R, 3, "above the red the grey is untouched");
        Assert.AreEqual(128, grey.G, 3);
    }

    [TestMethod]
    [CoversNode("pdf-blend")]
    public void AMultipliedFillOverAnother_IsTheProductOfBoth()
    {
        // Red multiplied across the bottom half, then grey multiplied across the left: where both lie, the grey's backdrop
        // is the red as it was composited, so the red is halved.
        var page = Read("/M gs 1 0 0 rg 0 0 200 100 re f 0.5 g 0 0 100 200 re f",
            resources: "/ExtGState << /M << /BM /Multiply >> >>");
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        var both = Pixel(picture, 50, 150);
        Assert.AreEqual(128, both.R, 3, "red times grey");
        Assert.AreEqual(0, both.G, 3);
        Assert.AreEqual(0, both.B, 3);
        Assert.AreEqual(Colors.Red, Pixel(picture, 150, 150), "the grey does not reach the right");

        var grey = Pixel(picture, 50, 50);
        Assert.AreEqual(128, grey.R, 3, "grey times white, above the red");
        Assert.AreEqual(128, grey.G, 3);
    }

    [TestMethod]
    [CoversNode("pdf-blend")]
    [Timeout(60_000)]
    public void BlendsOverBlends_AreEachCompositedOnce()
    {
        // Thirty Multiply fills, each over all the others. Each is composited against a backdrop holding the ones before
        // it; composited again inside every later backdrop that would be 2^30 composites, drawn as they were made it is 30.
        var fills = string.Concat(Enumerable.Repeat("0.98 g 0 0 200 200 re f ", 30));
        var page = Read("/M gs " + fills, resources: "/ExtGState << /M << /BM /Multiply >> >>");
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        // 0.98 is 250 of 255, and each composite rounds to a byte: 141 where the exact product is 139.
        var grey = Pixel(picture, 100, 100);
        Assert.AreEqual(141, grey.R, 4, "white multiplied by 0.98 thirty times");
        Assert.AreEqual(grey.R, grey.G);
        Assert.AreEqual(grey.R, grey.B);
    }

    [TestMethod]
    [CoversNode("pdf-blend")]
    public void LayingAPage_StopsWhenItIsCancelled()
    {
        var page = Read("/M gs 0.5 g 0 0 200 200 re f", resources: "/ExtGState << /M << /BM /Multiply >> >>");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(() => PdfPageLayout.Lay(page, Brushes.White, cancelled.Token));
    }

    [TestMethod]
    [CoversNode("pdf-blend")]
    public void ABlend_SeesWhatThePageHasPainted_AndNotItsPaper()
    {
        // The page is a group of its own, laid on its paper only once it is whole: Color keeps the lightness of what is
        // beneath, so over the white the left half paints it stays white, and over bare paper the fill is its own colour.
        var page = Read("1 g 0 0 100 200 re f /C gs 0.2 0.1 0.3 rg 0 0 200 200 re f",
            resources: "/ExtGState << /C << /BM /Color >> >>");
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        Assert.AreEqual(Colors.White, Pixel(picture, 50, 100), "over painted white, Color keeps white's lightness");

        var bare = Pixel(picture, 150, 100);
        Assert.AreEqual(51, bare.R, 3, "over the paper, nothing lies beneath the blend");
        Assert.AreEqual(26, bare.G, 3);
        Assert.AreEqual(77, bare.B, 3);
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void AnImagesOwnSoftMask_TakesThePlaceOfTheGraphicsStatesMask()
    {
        // The graphics state's soft mask is a group that paints nothing, so it hides whatever it masks; the image carries a
        // soft mask of its own, wholly opaque, which masks it instead. A path painted under the same state stays hidden.
        var page = Read("/M gs q 200 0 0 200 0 0 cm /Im Do Q 0 0 1 rg 0 0 50 50 re f",
            resources: "/XObject << /Im 5 0 R >> /ExtGState << /M << /SMask << /Type /Mask /S /Luminosity /G 7 0 R >> >> >>",
            extra:
            [
                (5, Stream("/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 6 0 R",
                    "FF0000FF0000FF0000FF0000>")),
                (6, Stream("/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8",
                    "FFFFFFFF>")),
                (7, "<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS /DeviceGray >> /Length 0 >>\nstream\n\nendstream"),
            ]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        Assert.AreEqual(Colors.Red, Pixel(picture, 100, 100), "the image is masked by its own soft mask alone");
        Assert.AreEqual(Colors.Red, Pixel(picture, 25, 175), "the path under the state's mask is hidden");
    }

    /// <summary>An image stream whose samples are written as hex, so the document stays text.</summary>
    private static string Stream(string dictionary, string hex) =>
        $"<< {dictionary} /Filter /ASCIIHexDecode /Length {hex.Length} >>\nstream\n{hex}\nendstream";

    [TestMethod]
    [CoversNode("pdf-blend")]
    public void TheBlendModes_FollowThePdfFormulas()
    {
        Span<double> into = stackalloc double[3];
        double[] backdrop = [0.2, 0.6, 1.0];
        double[] source = [0.5, 0.5, 0.25];

        PdfBlending.Blend(BlendMode.Multiply, backdrop, source, into);
        CollectionAssert.AreEqual(new[] { 0.1, 0.3, 0.25 }, Rounded(into));

        PdfBlending.Blend(BlendMode.Screen, backdrop, source, into);
        CollectionAssert.AreEqual(new[] { 0.6, 0.8, 1.0 }, Rounded(into));

        PdfBlending.Blend(BlendMode.Difference, backdrop, source, into);
        CollectionAssert.AreEqual(new[] { 0.3, 0.1, 0.75 }, Rounded(into));

        // Luminosity keeps the backdrop's hue and saturation and takes the source's luminosity.
        PdfBlending.Blend(BlendMode.Luminosity, backdrop, source, into);
        var lum = 0.3 * into[0] + 0.59 * into[1] + 0.11 * into[2];
        Assert.AreEqual(0.3 * 0.5 + 0.59 * 0.5 + 0.11 * 0.25, lum, 1e-6);
    }

    private static double[] Rounded(Span<double> values) => [Math.Round(values[0], 6), Math.Round(values[1], 6), Math.Round(values[2], 6)];

    private static IEnumerable<PdfTextNode> Runs(PdfGroupNode group) =>
        group.Children.SelectMany(static c => c switch
        {
            PdfTextNode text => [text],
            PdfGroupNode nested => Runs(nested),
            _ => Enumerable.Empty<PdfTextNode>(),
        });

    private static PdfPageNode Read(string content, string resources = "", string catalog = "",
        (int Number, string Body)[]? extra = null, string page = "")
    {
        var path = TestTemp.CreateFile(".pdf");
        File.WriteAllBytes(path, Write(content, resources, catalog, extra ?? [], page));
        try
        {
            using var scope = PdfDocumentScope.TryOpen(path, CancellationToken.None);
            Assert.IsNotNull(scope, "the document written here should open");
            return scope.Pages.ReadPage(1, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>One page of <paramref name="content"/>, with a cross-reference table that says where every object is.</summary>
    private static byte[] Write(string content, string resources, string catalog, (int Number, string Body)[] extra, string page)
    {
        var objects = new List<(int Number, string Body)>
        {
            (1, $"<< /Type /Catalog /Pages 2 0 R {catalog} >>"),
            (2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            (3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Side} {Side}] /Resources << {resources} >> /Contents 4 0 R {page} >>"),
            (4, $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"),
        };
        objects.AddRange(extra);
        objects.Sort(static (a, b) => a.Number.CompareTo(b.Number));

        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new SortedDictionary<int, int>();
        foreach (var (number, body) in objects)
        {
            offsets[number] = Encoding.ASCII.GetByteCount(pdf.ToString());
            pdf.Append($"{number} 0 obj\n{body}\nendobj\n");
        }

        var xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        var size = offsets.Keys.Max() + 1;
        pdf.Append($"xref\n0 {size}\n0000000000 65535 f \n");
        for (var n = 1; n < size; n++)
            pdf.Append(offsets.TryGetValue(n, out var at) ? $"{at:D10} 00000 n \n" : "0000000000 65535 f \n");
        pdf.Append($"trailer\n<< /Size {size} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    // ── Pixels ────────────────────────────────────────────────────────────────────

    /// <summary>The laid page painted at one pixel to a point, so a pixel's coordinates are the page's own.</summary>
    private static System.Windows.Media.Imaging.BitmapSource UiThreadPainted(LayoutTree tree, PdfPageNode page)
    {
        System.Windows.Media.Imaging.BitmapSource? picture = null;
        UiThread.Run(() =>
        {
            var size = new Size(page.Size.Width * PdfPageLayout.DipsPerPoint, page.Size.Height * PdfPageLayout.DipsPerPoint);
            picture = LayoutPictures.Painted(tree.Root, new Rect(size), 1 / PdfPageLayout.DipsPerPoint, Brushes.Black, Brushes.White);
        });
        Assert.IsNotNull(picture);
        return picture;
    }

    private static Color Pixel(System.Windows.Media.Imaging.BitmapSource picture, int x, int y)
    {
        var pixel = new byte[4];
        picture.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromRgb(pixel[2], pixel[1], pixel[0]);
    }
}
