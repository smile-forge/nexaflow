using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Features.Pdf.Editing;
using Nexaflow.Features.Pdf.Rendering;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Pdf;

/// <summary>
/// Margins set on pages: each page's content found from a picture of it — whatever is not the paper's colour — and moved
/// as one block into the room the margins leave, made smaller only where it would not otherwise fit.
/// </summary>
[TestClass]
[CoversNode("pdf-page-margins")]
public class PdfMarginsTests
{
    private static readonly Size A4 = PdfPaper.A4.Size;

    [TestMethod]
    [TestCategory("Unit")]
    public void ContentThatFits_IsMovedIntoItsRoom_CentredAcross_AgainstTheTop_KeepingItsSize()
    {
        var margins = new PdfMargins(50, 60, 50, 60, PdfMarginAcross.Centre, PdfMarginDown.Top);
        var content = new Rect(10, 300, 200, 100);

        var placed = margins.Place(content, A4, margins.Shrink(content, A4)!.Value)!.Value.Apply(content);

        Assert.AreEqual(200, placed.Width, 0.01, "content that fits keeps its size");
        Assert.AreEqual((A4.Width - 200) / 2, placed.X, 0.01, "centred across");
        Assert.AreEqual(60, placed.Y, 0.01, "against the top margin");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ContentTooLargeForItsRoom_IsMadeSmallerInProportion_AgainstTheLeft_CentredDown()
    {
        var margins = new PdfMargins(72, 72, 72, 72, PdfMarginAcross.Left, PdfMarginDown.Centre);
        var content = new Rect(0, 0, A4.Width, A4.Height / 2);

        var scale = margins.Shrink(content, A4)!.Value;
        var placed = margins.Place(content, A4, scale)!.Value.Apply(content);

        Assert.AreEqual((A4.Width - 144) / A4.Width, scale, 1e-6, "as small as the room's width needs");
        Assert.AreEqual(72, placed.X, 0.01, "against the left margin");
        Assert.AreEqual(A4.Width - 144, placed.Width, 0.01);
        Assert.AreEqual(content.Height * scale, placed.Height, 0.01, "kept in proportion");
        Assert.AreEqual(72 + (A4.Height - 144 - placed.Height) / 2, placed.Y, 0.01, "centred down");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void WithOneScale_EveryPageIsMadeAsSmallAsTheSmallest_OtherwiseEachOnlyAsSmallAsItMust()
    {
        var margins = new PdfMargins(72, 72, 72, 72, PdfMarginAcross.Centre, PdfMarginDown.Top);
        var wide = new Rect(0, 0, A4.Width, 100);
        var narrow = new Rect(100, 100, 200, 100);
        (Rect, Size)[] pages = [(wide, A4), (narrow, A4)];

        var same = margins.Placements(pages, sameScale: true);
        var own = margins.Placements(pages, sameScale: false);

        Assert.AreEqual(same[0]!.Value.Apply(wide).Width / wide.Width, same[1]!.Value.Apply(narrow).Width / narrow.Width, 1e-6,
            "the text is one size on every page");
        Assert.AreEqual(200, own[1]!.Value.Apply(narrow).Width, 0.01, "a page that fits keeps its size");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MarginsThatLeaveNoRoom_PlaceNothing()
    {
        var margins = new PdfMargins(A4.Width / 2, 10, A4.Width / 2, 10, PdfMarginAcross.Centre, PdfMarginDown.Top);

        Assert.IsNull(margins.Room(A4));
        Assert.IsNull(margins.Placements([(new Rect(0, 0, 10, 10), A4)], sameScale: true)[0]);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void APagesContent_IsWhatIsNotThePapersColour_ABackgroundFilledWithThatColourNotCounted()
    {
        // A cream page, as a generated document fills its background, with a block of dark text on it.
        var cream = Color.FromRgb(250, 244, 226);
        var ink = PdfInk.Of(Page(100, 200, cream, new Int32Rect(20, 30, 40, 60), Colors.Black));

        var extent = ink.Extent()!.Value;

        Assert.AreEqual(0.20, extent.X, 1e-9);
        Assert.AreEqual(0.15, extent.Y, 1e-9);
        Assert.AreEqual(0.40, extent.Width, 1e-9);
        Assert.AreEqual(0.30, extent.Height, 1e-9);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void APageWithNothingOnIt_HasNoContent()
    {
        Assert.IsNull(PdfInk.Of(Page(50, 80, Colors.White, Int32Rect.Empty, Colors.Black)).Extent());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TheMarksAPageWasCutBy_AtItsEdges_AreNotItsContent_WhileArtworkToTheEdgeIs()
    {
        // A page cut from a sheet keeps the ends of the marks it was cut by: hairlines running in from its edges.
        var cut = PdfInk.Of(Page(200, 400, Colors.White, Colors.Black,
            new Int32Rect(40, 60, 120, 200), new Int32Rect(199, 0, 1, 20), new Int32Rect(0, 399, 25, 1)));
        var bleeding = PdfInk.Of(Page(200, 400, Colors.White, Colors.Black, new Int32Rect(40, 60, 120, 200), new Int32Rect(0, 0, 200, 40)));

        var content = cut.Extent()!.Value;
        Assert.AreEqual(0.20, content.X, 1e-9, "the marks are left out");
        Assert.AreEqual(0.15, content.Y, 1e-9);
        Assert.AreEqual(0.60, content.Width, 1e-9);
        Assert.AreEqual(0.50, content.Height, 1e-9);
        Assert.AreEqual(0, bleeding.Extent()!.Value.Y, 1e-9, "a band of artwork to the edge is content");
    }

    /// <summary>A picture of a page <paramref name="paper"/> coloured, with each of <paramref name="blocks"/> filled <paramref name="ink"/>.</summary>
    private static BitmapSource Page(int width, int height, Color paper, Color ink, params Int32Rect[] blocks)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var inked = blocks.Any(block => x >= block.X && x < block.X + block.Width && y >= block.Y && y < block.Y + block.Height);
            var c = inked ? ink : paper;
            var at = (y * width + x) * 4;
            (pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = (c.B, c.G, c.R, 255);
        }

        var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        picture.Freeze();
        return picture;
    }

    /// <summary>A picture of a page <paramref name="paper"/> coloured, with <paramref name="block"/> filled <paramref name="colour"/>.</summary>
    private static BitmapSource Page(int width, int height, Color paper, Int32Rect block, Color colour)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var inBlock = !block.IsEmpty && x >= block.X && x < block.X + block.Width && y >= block.Y && y < block.Y + block.Height;
            var c = inBlock ? colour : paper;
            var at = (y * width + x) * 4;
            pixels[at] = c.B;
            pixels[at + 1] = c.G;
            pixels[at + 2] = c.R;
            pixels[at + 3] = 255;
        }

        var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        picture.Freeze();
        return picture;
    }
}
