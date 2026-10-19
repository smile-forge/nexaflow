using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
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
using UglyToad.PdfPig.PdfFonts;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// A page read as objects and laid as a layout tree, from content streams written here so each test says exactly what
/// the page draws: a clip is a group its content sits in, painting order survives into the tree, the graphics state's
/// alpha reaches the paint, a hidden layer draws nothing, and the page's text is where its runs say.
/// </summary>
[TestClass]
public class PdfPageReadingTests
{
    /// <summary>The page every test draws on: 200 points square, so page space is the PDF's y turned over at 200.</summary>
    private const double Side = 200;

    [TestMethod]
    [CoversNode("pdf-document-ast")]
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
    [CoversNode("pdf-render")]
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
    [CoversNode("pdf-document-ast")]
    public void TheGraphicsStatesAlpha_ReachesThePaint()
    {
        var page = Read("/A gs 1 0 0 rg 0 0 100 100 re f", resources: "/ExtGState << /A << /ca 0.5 >> >>");

        var fill = (PdfSolidPaint)((PdfPathNode)page.Content.Children.Single()).Fill!;
        Assert.AreEqual(0.5, fill.Alpha, 0.01);
    }

    [TestMethod]
    [CoversNode("pdf-document-ast")]
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
    [CoversNode("pdf-document-ast")]
    [CoversNode("pdf-render")]
    public void TwoLayersWithTheSameDictionary_AreShownAndHiddenApart()
    {
        // Objects 8 and 9 are the same dictionary, as a layout program's duplicated layer is; the configuration turns 9
        // off and leaves 8 on, so what is marked with 8 shows.
        var page = Read("/OC /shown BDC 1 0 0 rg 0 0 100 100 re f EMC /OC /hidden BDC 0 0 1 rg 0 0 20 20 re f EMC",
            resources: "/Properties << /shown 8 0 R /hidden 9 0 R >>",
            catalog: "/OCProperties << /OCGs [8 0 R 9 0 R] /D << /ON [8 0 R] /OFF [9 0 R] >> >>",
            extra: [(8, "<< /Type /OCG /Name (Layer) >>"), (9, "<< /Type /OCG /Name (Layer) >>")]);

        var drawn = page.Content.Children.OfType<PdfPathNode>().ToList();
        Assert.AreEqual(1, drawn.Count, "only the layer left on is drawn");
        Assert.AreEqual(Colors.Red, ((PdfSolidPaint)drawn[0].Fill!).Colour);
    }

    [TestMethod]
    [CoversNode("pdf-document-ast")]
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

    /// <summary>
    /// A font the page names but neither carries nor finds installed — a house face — is drawn in an installed face of the
    /// kind its descriptor asks for: a serif as Times is, a fixed-pitch face as Courier is, anything else as Helvetica is.
    /// The page gives its letters the widths those fonts' own metrics do, as a document set in them would.
    /// </summary>
    [TestMethod]
    [CoversNode("pdf-document-ast")]
    [CoversNode("pdf-render")]
    public void AFontFoundNowhere_IsDrawnInAFaceOfTheKindItsDescriptorAsksFor()
    {
        var kinds = new[]
        {
            (Flags: 34, Like: "Times-Roman", Widths: new[] { 722, 444, 778, 500, 500, 333, 500 }),
            (Flags: 33, Like: "Courier", Widths: new[] { 600, 600, 600, 600, 600, 600, 600 }),
            (Flags: 32, Like: "Helvetica", Widths: new[] { 722, 556, 833, 556, 556, 333, 556 }),
        };
        foreach (var (flags, like, widths) in kinds)
            CollectionAssert.AreEqual(Shapes(Named(like, null, widths)), Shapes(Named("HouseFace", flags, widths)),
                $"a house face of flags {flags} is drawn as {like} is");
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void AFontsKind_IsWhatItsDescriptorSays_OrItsName()
    {
        Assert.AreEqual(PdfFaceKind.Serif, PdfFaceKinds.Of(FontDescriptorFlags.Serif | FontDescriptorFlags.NonSymbolic, "Melior"));
        Assert.AreEqual(PdfFaceKind.Fixed, PdfFaceKinds.Of(FontDescriptorFlags.FixedPitch | FontDescriptorFlags.Serif, "Melior"),
            "a fixed-pitch serif is fixed pitch first");
        Assert.AreEqual(PdfFaceKind.Sans, PdfFaceKinds.Of(FontDescriptorFlags.NonSymbolic, "Melior"));
        Assert.AreEqual(PdfFaceKind.Serif, PdfFaceKinds.Of(null, "AGaramondPro-Regular"), "with no descriptor, its name says");
        Assert.AreEqual(PdfFaceKind.Fixed, PdfFaceKinds.Of(FontDescriptorFlags.NonSymbolic, "SourceCodeMono"),
            "a name saying fixed pitch is enough");
    }

    /// <summary>A page saying <c>Hamburg</c> in a Type 1 font of <paramref name="name"/> it does not carry, its letters as wide
    /// as <paramref name="widths"/> say in turn, with a descriptor of <paramref name="flags"/> or none.</summary>
    private static PdfPageNode Named(string name, int? flags, int[] widths)
    {
        const string word = "Hamburg";
        var each = Enumerable.Range(32, 95).Select(code => word.IndexOf((char)code) is var at and >= 0 ? widths[at] : 500);
        var descriptor = flags is null ? "" : " /FontDescriptor 11 0 R";
        return Read($"BT /F 24 Tf 20 100 Td ({word}) Tj ET", resources: "/Font << /F 10 0 R >>",
            extra: [(10, $"<< /Type /Font /Subtype /Type1 /BaseFont /{name} /FirstChar 32 /LastChar 126 /Widths [{string.Join(' ', each)}]{descriptor} >>"),
                    (11, $"<< /Type /FontDescriptor /FontName /{name} /Flags {flags ?? 32} /FontBBox [0 -200 1000 900] /ItalicAngle 0 "
                         + "/Ascent 900 /Descent -200 /CapHeight 700 /StemV 80 >>")]);
    }

    /// <summary>Every glyph a page draws, as its outline's coordinates.</summary>
    private static List<string> Shapes(PdfPageNode page) =>
        [.. Runs(page.Content).SelectMany(static r => r.Glyphs).Select(static g => g.Outline is { } outline
            ? string.Join(' ', outline.Coordinates.ToArray().Select(static c => c.ToString("0.###", CultureInfo.InvariantCulture)))
            : "none")];

    [TestMethod]
    [CoversNode("pdf-document-ast")]
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
    public void ABlendInsideABlendedGroup_IsCompositedOverWhatTheGroupHasPainted()
    {
        // A form multiplied onto the white page paints green, then multiplies grey across its left half: the grey's
        // backdrop is the green the form has painted, so the left half is a darker green, not grey.
        var page = Read("/M gs /F Do",
            resources: "/ExtGState << /M << /BM /Multiply >> >> /XObject << /F 5 0 R >>",
            extra:
            [
                (5, "<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency >> "
                    + "/Resources << /ExtGState << /M << /BM /Multiply >> >> >> /Length 54 >>\nstream\n"
                    + "0 1 0 rg 0 0 200 200 re f /M gs 0.5 g 0 0 100 200 re f\nendstream"),
            ]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        var both = Pixel(picture, 50, 100);
        Assert.AreEqual(0, both.R, 3, "green times grey");
        Assert.AreEqual(128, both.G, 3);
        Assert.AreEqual(0, both.B, 3);
        Assert.AreEqual(Colors.Lime, Pixel(picture, 150, 100), "the grey does not reach the right");
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

    [TestMethod]
    [CoversNode("pdf-render")]
    public void ASoftMaskFinerThanItsImage_IsDrawnAtItsOwnResolution()
    {
        // One red sample masked by two: the left one shows, the right one does not. Both are stretched over the same square,
        // so the left half of the page is red and the right half bare.
        var page = Read("q 200 0 0 200 0 0 cm /Im Do Q",
            resources: "/XObject << /Im 5 0 R >>",
            extra:
            [
                (5, Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMask 6 0 R",
                    "FF0000>")),
                (6, Stream("/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8",
                    "FF00>")),
            ]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        // Drawn larger, the picture is smoothed a little either side of the mask's edge; well inside each half it is plain.
        var shown = Pixel(picture, 50, 100);
        Assert.IsTrue(shown is { R: > 250, G: < 6, B: < 6 }, $"where the mask shows the sample: {shown}");
        var hidden = Pixel(picture, 150, 100);
        Assert.IsTrue(hidden is { R: > 249, G: > 249, B: > 249 }, $"where the mask hides it: {hidden}");
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    [DataRow("/DeviceRGB", "[0 0 0]", "000080" + "0000FF", 127, 127, 255, DisplayName = "blue, preblended with black")]
    [DataRow("/DeviceRGB", "[1 0 0]", "7F0080" + "0000FF", 127, 127, 255, DisplayName = "blue, preblended with red")]
    [DataRow("/DeviceGray", "[1]", "7F" + "00", 127, 127, 127, DisplayName = "black, preblended with white")]
    public void AnImagePreblendedWithItsMatte_DrawsItsTrueColourAtItsOpacity(string colourSpace, string matte, string hex, int r, int g, int b)
    {
        // The left sample is half opaque and was written mixed with the soft mask's matte by that half; the right is opaque.
        // Drawn as written over white paper, the left half would carry the matte as a fringe — blue over black, (127, 127, 191).
        var page = Read("q 200 0 0 200 0 0 cm /Im Do Q",
            resources: "/XObject << /Im 5 0 R >>",
            extra:
            [
                (5, Stream($"/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace {colourSpace} /BitsPerComponent 8 /SMask 6 0 R",
                    hex + ">")),
                (6, Stream($"/Type /XObject /Subtype /Image /Width 2 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Matte {matte}",
                    "80FF>")),
            ]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        AssertNear(Color.FromRgb((byte)r, (byte)g, (byte)b), Pixel(picture, 40, 100), "the half-opaque sample, its matte taken out");
        var opaque = colourSpace == "/DeviceGray" ? Colors.Black : Color.FromRgb(0, 0, 255);
        AssertNear(opaque, Pixel(picture, 160, 100), "the opaque sample, which no matte is mixed into");
    }

    private const string JpxImage = "/Type /XObject /Subtype /Image /Width 2 /Height 2";

    [TestMethod]
    [CoversNode("pdf-render")]
    [DataRow(1, 1, 177, DisplayName = "opacity, /SMaskInData 1")]
    [DataRow(2, 1, 227, DisplayName = "premultiplied opacity, /SMaskInData 1")]
    [DataRow(1, 2, 227, DisplayName = "opacity, /SMaskInData 2: the colour preblended")]
    public void AJpegTwoThousandImagesOwnOpacity_IsItsSoftMask(int opacityType, int mode, int halfShownGreen)
    {
        // The sample's four pixels over the page's quadrants, on white paper: red opaque, blue wholly transparent, green half
        // opaque — lighter where its colour was preblended and is divided back up — and grey opaque.
        var page = Read("q 200 0 0 200 0 0 cm /Im Do Q",
            resources: "/XObject << /Im 5 0 R >>",
            extra: [(5, JpxStream($"{JpxImage} /SMaskInData {mode}", Jpeg2000OpacitySample.Jp2((ushort)opacityType)))]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        AssertNear(Color.FromRgb(255, 0, 0), Pixel(picture, 50, 50), "the opaque red");
        AssertNear(Colors.White, Pixel(picture, 150, 50), "the transparent blue shows the paper");
        AssertNear(Color.FromRgb(127, (byte)halfShownGreen, 127), Pixel(picture, 50, 150), "the half-opaque green");
        AssertNear(Color.FromRgb(64, 64, 64), Pixel(picture, 150, 150), "the opaque grey");
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void WithoutSMaskInData_AJpegTwoThousandImagesOpacityIsLeftUnread_AndItsColourReadWhole()
    {
        // The opacity channel is neither the image's mask nor read as a fourth colour sample, which would put every pixel
        // after the first a sample out.
        var page = Read("q 200 0 0 200 0 0 cm /Im Do Q",
            resources: "/XObject << /Im 5 0 R >>",
            extra: [(5, JpxStream(JpxImage, Jpeg2000OpacitySample.Jp2(1)))]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        AssertNear(Color.FromRgb(255, 0, 0), Pixel(picture, 50, 50), "the red");
        AssertNear(Color.FromRgb(0, 0, 255), Pixel(picture, 150, 50), "the blue, opaque");
        AssertNear(Color.FromRgb(0, 100, 0), Pixel(picture, 50, 150), "the green, opaque");
        AssertNear(Color.FromRgb(64, 64, 64), Pixel(picture, 150, 150), "the grey");
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void ABareCodestreamsOpacity_IsTheComponentPastItsColourSpace()
    {
        // A codestream marks none of its components: under /DeviceRGB, the fourth is the one past the colour.
        var page = Read("q 200 0 0 200 0 0 cm /Im Do Q",
            resources: "/XObject << /Im 5 0 R >>",
            extra: [(5, JpxStream($"{JpxImage} /ColorSpace /DeviceRGB /BitsPerComponent 8 /SMaskInData 1", Jpeg2000OpacitySample.Codestream()))]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        AssertNear(Color.FromRgb(255, 0, 0), Pixel(picture, 50, 50), "the opaque red");
        AssertNear(Colors.White, Pixel(picture, 150, 50), "the transparent blue shows the paper");
        AssertNear(Color.FromRgb(127, 177, 127), Pixel(picture, 50, 150), "the half-opaque green");
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void AnSMask_TakesThePlaceOfTheOpacityInTheImagesData()
    {
        var page = Read("q 200 0 0 200 0 0 cm /Im Do Q",
            resources: "/XObject << /Im 5 0 R >>",
            extra:
            [
                (5, JpxStream($"{JpxImage} /SMaskInData 1 /SMask 6 0 R", Jpeg2000OpacitySample.Jp2(1))),
                (6, Stream("/Type /XObject /Subtype /Image /Width 2 /Height 2 /ColorSpace /DeviceGray /BitsPerComponent 8", "FFFFFFFF>")),
            ]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        AssertNear(Color.FromRgb(0, 0, 255), Pixel(picture, 150, 50), "the blue, shown by the wholly opaque /SMask");
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void TheOpacityInAnImagesData_TakesThePlaceOfTheGraphicsStatesMask()
    {
        // The graphics state's soft mask hides whatever it masks; the image's own opacity masks it instead. A path painted
        // under the same state stays hidden, over the image's half-opaque green.
        var page = Read("/M gs q 200 0 0 200 0 0 cm /Im Do Q 0 0 1 rg 0 0 50 50 re f",
            resources: "/XObject << /Im 5 0 R >> /ExtGState << /M << /SMask << /Type /Mask /S /Luminosity /G 7 0 R >> >> >>",
            extra:
            [
                (5, JpxStream($"{JpxImage} /SMaskInData 1", Jpeg2000OpacitySample.Jp2(1))),
                (7, "<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS /DeviceGray >> /Length 0 >>\nstream\n\nendstream"),
            ]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        AssertNear(Color.FromRgb(255, 0, 0), Pixel(picture, 50, 50), "the image is masked by its own opacity alone");
        AssertNear(Colors.White, Pixel(picture, 150, 50), "where its opacity is none");
        AssertNear(Color.FromRgb(127, 177, 127), Pixel(picture, 25, 175), "the path under the state's mask is hidden");
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void AShadingThatStopsAtItsEnds_ReachesAcrossThePage_HoweverSmallItsUnits()
    {
        // A red-to-blue axis up the middle of the page, said in units of a hundred-thousandth of a point (as a document laid
        // out in EMUs says it): the band between its ends runs right across the page, not just near the axis.
        var page = Read("q 0.00001 0 0 0.00001 0 0 cm /Sh sh Q",
            resources: "/Shading << /Sh << /ShadingType 2 /ColorSpace /DeviceRGB /Coords [10000000 0 10000000 20000000] "
                       + "/Function << /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >> /Extend [false false] >> >>");
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        foreach (var x in new[] { 5, 100, 195 })
        {
            var halfway = Pixel(picture, x, 100);
            Assert.AreEqual(128, halfway.R, 6, $"halfway up, at {x}, the shading is half red");
            Assert.AreEqual(0, halfway.G, 6);
            Assert.AreEqual(128, halfway.B, 6, "and half blue");
        }
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void AJoinPastTheMiterLimit_IsBevelled_AndOneWithinIt_KeepsItsMiter()
    {
        // One stroke 20 wide: up the left side, along the top, then back the way it came. Turning back at (170, 170) is a
        // join past any miter limit, which PDF bevels; cutting its miter off at the limit, as WPF would, leaves a spike
        // ten half-widths long running on to the right. The square corner at (30, 170) is within the limit: its miter
        // fills the corner out to (20, 180).
        var page = Read("20 w 0 J 0 j 10 M 30 30 m 30 170 l 170 170 l 30 170 l S");
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        Assert.AreEqual(Colors.White, Pixel(picture, 190, 30), "no spike runs on past where the stroke turns back");
        Assert.AreEqual(Colors.Black, Pixel(picture, 22, 22), "the square corner keeps its miter");
        Assert.AreEqual(Colors.Black, Pixel(picture, 100, 30), "the stroke itself is drawn");
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void APatternSteppedToShowOnce_IsDrawnAsSharpAsItsCell()
    {
        // A cell black on its left half, stepped 99999 apart so it shows once, as a picture placed as a pattern is. Drawn as
        // a tile of that step it would be squeezed into one small picture and come out a grey blur.
        var page = Read("/Pattern cs /P scn 0 0 200 200 re f",
            resources: "/Pattern << /P 5 0 R >>",
            extra:
            [
                (5, "<< /PatternType 1 /PaintType 1 /TilingType 1 /BBox [0 0 200 200] /XStep 99999 /YStep 99999 "
                    + "/Resources << >> /Length 20 >>\nstream\n0 g 0 0 100 200 re f\nendstream"),
            ]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        Assert.AreEqual(Colors.Black, Pixel(picture, 50, 100), "the cell's black half");
        Assert.AreEqual(Colors.Black, Pixel(picture, 97, 100), "right up to its edge");
        Assert.AreEqual(Colors.White, Pixel(picture, 103, 100), "and none past it");
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void AnIccBasedColour_IsDrawnThroughItsProfile()
    {
        // ProPhoto RGB's grey is lighter than sRGB's at the same numbers, its gamma being 1.8: drawn as its alternate,
        // DeviceRGB, it would be sRGB's 128.
        var prophoto = InstalledProfile("ProPhoto.icm");
        var page = Read("/P cs 0.5 0.5 0.5 sc 0 0 200 200 re f",
            resources: "/ColorSpace << /P [/ICCBased 5 0 R] >>",
            extra: [(5, Stream("/N 3 /Alternate /DeviceRGB", Convert.ToHexString(prophoto) + ">"))]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        var expected = ThroughWindows("ProPhoto.icm", 128);
        var drawn = Pixel(picture, 100, 100);
        Assert.IsTrue(expected > 140, $"ProPhoto's grey is lighter than sRGB's, not {expected}");
        Assert.AreEqual(expected, drawn.R, 2, "drawn as Windows converts it");
        Assert.AreEqual(expected, drawn.G, 2);
        Assert.AreEqual(expected, drawn.B, 2);
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void AnIccBasedImage_IsDrawnThroughItsProfile()
    {
        var prophoto = InstalledProfile("ProPhoto.icm");
        var page = Read("q 200 0 0 200 0 0 cm /Im Do Q",
            resources: "/XObject << /Im 6 0 R >>",
            extra:
            [
                (5, Stream("/N 3 /Alternate /DeviceRGB", Convert.ToHexString(prophoto) + ">")),
                (6, Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace [/ICCBased 5 0 R] /BitsPerComponent 8",
                    "808080>")),
            ]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        var expected = ThroughWindows("ProPhoto.icm", 128);
        Assert.AreEqual(expected, Pixel(picture, 100, 100).G, 2, "the image's samples go through its profile");
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void ASpotColoursLabAlternate_IsDrawnInLab()
    {
        // A spot colour as Pantone's are written: its tint a Lab colour, in an ICCBased space whose profile is Lab and which
        // gives no Range. A full tint is L* 50, a* and b* 0: sRGB's mid grey, 119.
        var page = Read("/S cs 1 sc 0 0 200 200 re f",
            resources: "/ColorSpace << /S [/Separation /Spot [/ICCBased 5 0 R] << /FunctionType 2 /Domain [0 1] "
                       + "/C0 [100 0 0] /C1 [50 0 0] /N 1 /Range [0 100 -128 127 -128 127] >>] >>",
            extra: [(5, Stream("/N 3", Convert.ToHexString(LabProfile()) + ">"))]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        var drawn = Pixel(picture, 100, 100);
        Assert.AreEqual(119, drawn.R, 2, "L* 50 is sRGB's 119");
        Assert.AreEqual(119, drawn.G, 2);
        Assert.AreEqual(119, drawn.B, 2);
    }

    /// <summary>
    /// A triangle mesh (type 4) — two triangles making the page, red along its foot and blue along its head — is drawn with
    /// each triangle's colour varying between its corners: red at the foot, blue at the head, purple between.
    /// </summary>
    [TestMethod]
    [CoversNode("pdf-render")]
    public void ATriangleMesh_IsShadedBetweenItsCorners()
    {
        var data = new List<byte>();
        void Vertex(int flag, double x, double y, byte r, byte b)
        {
            data.Add((byte)flag);
            data.AddRange(Coordinate(x));
            data.AddRange(Coordinate(y));
            data.AddRange([r, 0, b]);
        }

        Vertex(0, 0, 0, 255, 0);
        Vertex(0, Side, 0, 255, 0);
        Vertex(0, 0, Side, 0, 255);
        Vertex(1, Side, Side, 0, 255);

        AssertShadedFootToHead(Meshed($"/ShadingType 4 /BitsPerFlag 8 {MeshFormat}", data));
    }

    /// <summary>
    /// A Coons patch mesh (type 6) — one patch the page's square, red at its two foot corners and blue at its two head
    /// corners — is drawn as the gradient between them, as the triangle mesh of the same corners is.
    /// </summary>
    [TestMethod]
    [CoversNode("pdf-render")]
    public void ACoonsPatch_IsShadedBetweenItsCorners()
    {
        var data = new List<byte> { 0 };
        // The boundary from the first corner, up its left edge, along its head, down its right edge and back along its foot.
        (double X, double Y)[] boundary =
        [
            (0, 0), (0, Side / 3), (0, Side * 2 / 3), (0, Side), (Side / 3, Side), (Side * 2 / 3, Side),
            (Side, Side), (Side, Side * 2 / 3), (Side, Side / 3), (Side, 0), (Side * 2 / 3, 0), (Side / 3, 0),
        ];
        foreach (var (x, y) in boundary)
        {
            data.AddRange(Coordinate(x));
            data.AddRange(Coordinate(y));
        }

        data.AddRange([255, 0, 0, 0, 0, 255, 0, 0, 255, 255, 0, 0]);   // the corners at (0, 0), (0, 1), (1, 1) and (1, 0)

        AssertShadedFootToHead(Meshed($"/ShadingType 6 /BitsPerFlag 8 {MeshFormat}", data));
    }

    /// <summary>A line drawn far thinner than a pixel — a diagram scaled down to fit its page, its lines a hundredth of a point
    /// wide — is drawn no thinner than a device-independent pixel, as renderers draw a hairline, rather than fading to white.</summary>
    [TestMethod]
    [CoversNode("pdf-render")]
    public void ALineThinnerThanAPixel_IsDrawnAPixelWide()
    {
        var page = Read("q 0.01 0 0 0.01 0 0 cm 1 w 0 0 0 RG 1000 10000 m 19000 10000 l S Q");
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        var darkest = Enumerable.Range(97, 6).Min(y => (int)Pixel(picture, 100, y).R);
        Assert.IsTrue(darkest < 160, $"the line shows: its darkest is {darkest}, not near white");
    }

    /// <summary>An uncoloured tiling pattern (<c>/PaintType 2</c>) gives only shapes: it is painted in the colour the
    /// operands beside its name choose, here green, whatever its cell was drawn in.</summary>
    [TestMethod]
    [CoversNode("pdf-render")]
    public void AnUncolouredPattern_IsPaintedInTheColourItsOperandsChoose()
    {
        const string cell = "0 0 20 20 re f";
        var page = Read("/Cs cs 0 1 0 /P scn 0 0 200 200 re f",
            resources: "/ColorSpace << /Cs [/Pattern /DeviceRGB] >> /Pattern << /P 5 0 R >>",
            extra: [(5, $"<< /PatternType 1 /PaintType 2 /TilingType 1 /BBox [0 0 20 20] /XStep 40 /YStep 40 /Resources << >> "
                        + $"/Length {cell.Length} >>\nstream\n{cell}\nendstream")]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        Assert.IsFalse(page.Unsupported.Any(), "an uncoloured pattern is drawn, not reported");
        var inCell = Pixel(picture, 10, 189);
        var between = Pixel(picture, 30, 169);
        Assert.IsTrue(inCell.G > 240 && inCell.R < 15 && inCell.B < 15, $"the cell is green, not {inCell}");
        Assert.IsTrue(between.R > 240 && between.G > 240 && between.B > 240, $"between cells the page shows, not {between}");
    }

    /// <summary>Pictures laid edge to edge — a formula exported as strips of pictures a pixel tall, set between pixels — meet
    /// with no seam: their edges are their pixels', and the paper does not show through between them.</summary>
    [TestMethod]
    [CoversNode("pdf-render")]
    public void PicturesLaidEdgeToEdge_MeetWithNoSeam()
    {
        var strips = string.Concat(Enumerable.Range(0, 10).Select(i =>
            FormattableString.Invariant($"q 100 0 0 1 50 {100.5 + i} cm /Im Do Q ")));
        var page = Read(strips, resources: "/XObject << /Im 5 0 R >>",
            extra: [(5, Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "00>"))]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        for (var y = 92; y <= 97; y++)
            Assert.IsTrue(Pixel(picture, 100, y).R < 20, $"row {y} is the strips' black, not {Pixel(picture, 100, y)}");
    }

    /// <summary>How the test meshes write their vertices: 16-bit coordinates across the page, 8-bit RGB.</summary>
    private static readonly string MeshFormat =
        $"/ColorSpace /DeviceRGB /BitsPerCoordinate 16 /BitsPerComponent 8 /Decode [0 {Side} 0 {Side} 0 1 0 1 0 1]";

    private static byte[] Coordinate(double value)
    {
        var raw = (ushort)Math.Round(value / Side * ushort.MaxValue);
        return [(byte)(raw >> 8), (byte)raw];
    }

    /// <summary>A page that is nothing but the mesh shading of <paramref name="dictionary"/> and <paramref name="data"/>.</summary>
    private static PdfPageNode Meshed(string dictionary, List<byte> data) =>
        Read("/Sh sh", resources: "/Shading << /Sh 5 0 R >>",
            extra: [(5, Stream(dictionary, Convert.ToHexString([.. data]) + ">"))]);

    private static void AssertShadedFootToHead(PdfPageNode page)
    {
        Assert.IsFalse(page.Unsupported.Any(), "a mesh is drawn, not reported");
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        var foot = Pixel(picture, 100, 197);
        var middle = Pixel(picture, 100, 100);
        var head = Pixel(picture, 100, 2);
        Assert.IsTrue(foot.R > 240 && foot.B < 15, $"red at the foot, not {foot}");
        Assert.IsTrue(head.B > 240 && head.R < 15, $"blue at the head, not {head}");
        Assert.AreEqual(128, middle.R, 6, $"half red half way, not {middle}");
        Assert.AreEqual(128, middle.B, 6, $"half blue half way, not {middle}");
        Assert.AreEqual(0, middle.G, 2);
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void ALuminosityMasksBackdrop_IsInItsGroupsColourSpace()
    {
        // The mask's group paints nothing, so the mask is the luminosity of its backdrop alone: ProPhoto's mid grey, lighter
        // than the 128 the same numbers make in DeviceRGB. Red at that alpha over the white page leaves green and blue at
        // what the alpha does not cover.
        var prophoto = InstalledProfile("ProPhoto.icm");
        var page = Read("/M gs 1 0 0 rg 0 0 200 200 re f",
            resources: "/ExtGState << /M << /SMask << /Type /Mask /S /Luminosity /G 6 0 R /BC [0.5 0.5 0.5] >> >> >>",
            extra:
            [
                (5, Stream("/N 3 /Alternate /DeviceRGB", Convert.ToHexString(prophoto) + ">")),
                (6, "<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS [/ICCBased 5 0 R] >> "
                    + "/Length 0 >>\nstream\n\nendstream"),
            ]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        var luminosity = ThroughWindows("ProPhoto.icm", 128);
        Assert.AreEqual(255 - luminosity, Pixel(picture, 100, 100).G, 3, "the backdrop is ProPhoto's grey");
    }

    [TestMethod]
    [CoversNode("pdf-render")]
    public void AMasksTransferFunction_RemapsItsLevels_WhereItsGroupPaintsAndWhereItDoesNot()
    {
        // The mask's group paints its left half white, and its transfer function inverts: white masks out, and the black
        // backdrop everywhere the group paints nothing masks in. Red over the page shows on the right only.
        var page = Read("/M gs 1 0 0 rg 0 0 200 200 re f",
            resources: "/ExtGState << /M << /SMask << /Type /Mask /S /Luminosity /G 6 0 R /TR 7 0 R >> >> >>",
            extra:
            [
                (6, "<< /Type /XObject /Subtype /Form /BBox [0 0 200 200] /Group << /S /Transparency /CS /DeviceGray >> /Length 22 >>\nstream\n1 g 0 0 100 200 re f\nendstream"),
                (7, "<< /FunctionType 4 /Domain [0 1] /Range [0 1] /Length 13 >>\nstream\n{1 exch sub}\nendstream"),
            ]);
        var picture = UiThreadPainted(PdfPageLayout.Lay(page, Brushes.White), page);

        Assert.AreEqual(Colors.White, Pixel(picture, 50, 100), "the group's white, inverted, hides the red");
        Assert.AreEqual(Colors.Red, Pixel(picture, 150, 100), "the black backdrop, inverted, shows it");
        Assert.IsFalse(page.Unsupported.Any(), "a transfer function is drawn, not reported");
    }

    /// <summary>An image stream whose samples are written as hex, so the document stays text.</summary>
    private static string Stream(string dictionary, string hex) =>
        $"<< {dictionary} /Filter /ASCIIHexDecode /Length {hex.Length} >>\nstream\n{hex}\nendstream";

    /// <summary>A JPEG 2000 image stream: its data as it is, a byte a character, as <see cref="Write"/> writes the file.</summary>
    private static string JpxStream(string dictionary, byte[] data) =>
        $"<< {dictionary} /Filter /JPXDecode /Length {data.Length} >>\nstream\n{Encoding.Latin1.GetString(data)}\nendstream";

    /// <summary>Within a few levels of <paramref name="expected"/> in each channel, as a picture drawn larger and smoothed is.</summary>
    private static void AssertNear(Color expected, Color actual, string what) =>
        Assert.IsTrue(Math.Abs(expected.R - actual.R) <= 4 && Math.Abs(expected.G - actual.G) <= 4 && Math.Abs(expected.B - actual.B) <= 4,
            $"{what}: {actual}, not {expected}");

    /// <summary>A profile Windows installs, which a test of colour management converts through.</summary>
    private static byte[] InstalledProfile(string name)
    {
        var path = Path.Combine(Environment.SystemDirectory, "spool", "drivers", "color", name);
        if (!File.Exists(path)) Assert.Inconclusive($"Windows has no {name} here");
        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// A grey of <paramref name="level"/> in the RGB of the profile Windows installs as <paramref name="name"/>, in sRGB as
    /// WPF's own colour conversion makes it: one channel, the three being equal.
    /// </summary>
    private static byte ThroughWindows(string name, byte level)
    {
        var source = System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96, PixelFormats.Rgb24, null,
            new[] { level, level, level }, 3);
        var converted = new System.Windows.Media.Imaging.ColorConvertedBitmap(source,
            new ColorContext(new Uri(Path.Combine(Environment.SystemDirectory, "spool", "drivers", "color", name))),
            new ColorContext(PixelFormats.Bgra32), PixelFormats.Bgra32);
        var bgra = new byte[4];
        converted.CopyPixels(bgra, 4, 0);
        return bgra[1];
    }

    /// <summary>
    /// A Lab profile that leaves Lab as it is, as a Lab spot colour's alternate carries one: its header, a copyright, a
    /// description, the D50 white point, and one eight-bit table from Lab to Lab that serves both ways (ICC.1:2001, 6.5.7).
    /// </summary>
    private static byte[] LabProfile()
    {
        var lut = new byte[48 + 3 * 256 + 8 * 3 + 3 * 256];
        Encoding.ASCII.GetBytes("mft1").CopyTo(lut, 0);
        lut[8] = 3;
        lut[9] = 3;
        lut[10] = 2;
        for (var i = 0; i < 3; i++) BinaryPrimitives.WriteInt32BigEndian(lut.AsSpan(12 + 16 * i), 0x10000);
        for (var c = 0; c < 3; c++)
        for (var s = 0; s < 256; s++)
        {
            lut[48 + c * 256 + s] = (byte)s;
            lut[48 + 3 * 256 + 8 * 3 + c * 256 + s] = (byte)s;
        }
        for (var corner = 0; corner < 8; corner++)
        for (var c = 0; c < 3; c++)
            lut[48 + 3 * 256 + corner * 3 + c] = (byte)((corner >> (2 - c) & 1) * 255);

        var copyright = new byte[8 + 5];
        Encoding.ASCII.GetBytes("text").CopyTo(copyright, 0);
        Encoding.ASCII.GetBytes("none").CopyTo(copyright, 8);

        var description = new byte[12 + 4 + 4 + 4 + 2 + 1 + 67];
        Encoding.ASCII.GetBytes("desc").CopyTo(description, 0);
        BinaryPrimitives.WriteUInt32BigEndian(description.AsSpan(8), 4);
        Encoding.ASCII.GetBytes("Lab").CopyTo(description, 12);

        var white = new byte[20];
        Encoding.ASCII.GetBytes("XYZ ").CopyTo(white, 0);
        D50(white.AsSpan(8));

        (string Tag, byte[] Data)[] data = [("cprt", copyright), ("desc", description), ("wtpt", white), ("A2B0", lut)];
        var tags = new[] { "cprt", "desc", "wtpt", "A2B0", "B2A0" };
        var places = new Dictionary<string, (int Offset, int Length)>();
        var at = 128 + 4 + 12 * tags.Length;
        foreach (var (tag, bytes) in data)
        {
            places[tag] = (at, bytes.Length);
            at += (bytes.Length + 3) & ~3;
        }
        places["B2A0"] = places["A2B0"];

        var profile = new byte[at];
        BinaryPrimitives.WriteUInt32BigEndian(profile, (uint)at);
        profile[8] = 2;
        profile[9] = 0x10;
        Encoding.ASCII.GetBytes("spacLab Lab ").CopyTo(profile, 12);
        Encoding.ASCII.GetBytes("acsp").CopyTo(profile, 36);
        D50(profile.AsSpan(68));
        BinaryPrimitives.WriteUInt32BigEndian(profile.AsSpan(128), (uint)tags.Length);
        for (var i = 0; i < tags.Length; i++)
        {
            var entry = profile.AsSpan(132 + 12 * i);
            Encoding.ASCII.GetBytes(tags[i]).CopyTo(entry);
            BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)places[tags[i]].Offset);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)places[tags[i]].Length);
        }
        foreach (var (tag, bytes) in data) bytes.CopyTo(profile, places[tag].Offset);
        return profile;

        static void D50(Span<byte> xyz)
        {
            BinaryPrimitives.WriteInt32BigEndian(xyz, 63190);
            BinaryPrimitives.WriteInt32BigEndian(xyz[4..], 65536);
            BinaryPrimitives.WriteInt32BigEndian(xyz[8..], 54061);
        }
    }

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
            (4, $"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}\nendstream"),
        };
        objects.AddRange(extra);
        objects.Sort(static (a, b) => a.Number.CompareTo(b.Number));

        var pdf = new StringBuilder("%PDF-1.7\n");
        var offsets = new SortedDictionary<int, int>();
        foreach (var (number, body) in objects)
        {
            offsets[number] = Encoding.Latin1.GetByteCount(pdf.ToString());
            pdf.Append($"{number} 0 obj\n{body}\nendobj\n");
        }

        var xref = Encoding.Latin1.GetByteCount(pdf.ToString());
        var size = offsets.Keys.Max() + 1;
        pdf.Append($"xref\n0 {size}\n0000000000 65535 f \n");
        for (var n = 1; n < size; n++)
            pdf.Append(offsets.TryGetValue(n, out var at) ? $"{at:D10} 00000 n \n" : "0000000000 65535 f \n");
        pdf.Append($"trailer\n<< /Size {size} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(pdf.ToString());
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
