using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;
using Nexaflow.Markdown.Layout;
using Nexaflow.Markdown.Layout.Prose;
using Nexaflow.Visuals.Text.Painting;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// How much of a piece shows (<see cref="PortablePaint.Clip"/>, <see cref="PortablePaint.Opacity"/>,
/// <see cref="PortablePaint.Mask"/>) and how an outline's pen is shaped (<see cref="StrokeEnds"/>): painted into a picture the
/// way a document's picture is saved, and read back a pixel at a time; and pressed, where what a clip cuts away is not
/// there to be pressed.
///
/// <para>
/// The pieces stand away from the origin on purpose. The painter carries where a piece stands down to its marks rather
/// than pushing it, so a clip or a mask said in the piece's own frame lands in the right place only if the painter put the
/// frame there first.
/// </para>
/// </summary>
[TestClass]
[CoversNode("layout-paint")]
public class LayoutPaintTests
{
    /// <summary>The stretch of page every picture here is painted from.</summary>
    private static readonly Rect Page = new(0, 0, 120, 40);

    private static readonly TestPart CutPart = new(0, 5);
    private static readonly TestPart BesidePart = new(6, 5);
    private static readonly TestPart HiddenPart = new(12, 5);

    [TestMethod]
    public void AClippedPieceIsPaintedOnlyInsideItsClip_WhereverItStands() => UiThread.Run(() =>
    {
        // The bar runs from 20 to 100 across the page; its clip keeps its first 30, which is 20 to 50 on the page.
        var picture = Read(Painted(Bar(new PortablePaint(Clip: Clip(new Rect(0, 0, 30, 20)))), Brushes.White));

        Assert.IsTrue(IsRed(picture.At(35, 20)), "inside the clip, where the piece stands");
        Assert.IsTrue(IsWhite(picture.At(60, 20)), "past the clip, though the bar runs on there");
        Assert.IsTrue(IsWhite(picture.At(10, 20)), "and nothing before the bar");
    });

    [TestMethod]
    public void AClippedPieceKeepingItsPictureIsClippedJustTheSame() => UiThread.Run(() =>
    {
        var kept = new PortableKept();
        var bar = Bar(new PortablePaint(Kept: kept, Clip: Clip(new Rect(0, 0, 30, 20))));

        var first = Read(Painted(bar, Brushes.White));
        Assert.IsTrue(kept.Holds, "the picture was kept");

        var again = Read(Painted(bar, Brushes.White));

        foreach (var picture in new[] { first, again })
        {
            Assert.IsTrue(IsRed(picture.At(35, 20)));
            Assert.IsTrue(IsWhite(picture.At(60, 20)), "the clip is round the kept picture, not lost from it");
        }
    });

    [TestMethod]
    public void AFadedPieceShowsAtTheStrengthItAsksFor() => UiThread.Run(() =>
    {
        var half = Read(Painted(Bar(new PortablePaint(Opacity: 0.5)), ground: null)).At(50, 20);

        Assert.AreEqual(128, half.A, 2, "half its alpha");
        Assert.IsTrue(half.R > 240 && half.G < 15, "and still its own colour");

        var none = Read(Painted(Bar(new PortablePaint(Opacity: 0)), ground: null)).At(50, 20);
        Assert.AreEqual(0, none.A, "a piece that does not show at all is not there");
    });

    [TestMethod]
    public void AMaskedPieceShowsWhereItsMaskDoes() => UiThread.Run(() =>
    {
        // Opaque over the first half of what the piece draws and clear over the second: the bar runs 20 to 100 on the page,
        // so the mask's edge is at 60.
        var mask = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(Colors.Black, 0),
                new GradientStop(Colors.Black, 0.5),
                new GradientStop(Colors.Transparent, 0.5),
                new GradientStop(Colors.Transparent, 1),
            },
        };

        var picture = Read(Painted(Bar(new PortablePaint(Mask: Medium.Ink(Frozen(mask)))), ground: null));

        Assert.AreEqual(255, picture.At(40, 20).A, "where the mask is opaque");
        Assert.AreEqual(0, picture.At(85, 20).A, "and nothing where it is clear");
    });

    [TestMethod]
    public void InsideAClippedFrameABlockFarFromWhatIsShownStillLetsItsPictureGo() => UiThread.Run(() =>
    {
        // A clip puts the painter in the piece's own frame. What is near the screen has to move with it, or every block inside
        // a clipped piece is painted and kept however far down the page it is.
        var near = new PortableKept();
        var far = new PortableKept();

        var build = new PortableBuilder(WpfMedium.OnThisThread);
        build.Open("page");
        build.Open("cut", at: new LayoutPoint(0, 1000), paints: new PortablePaint(Clip: Clip(new Rect(0, 0, 100, 6000))));

        foreach (var (kept, y) in new[] { (near, 0d), (far, 5000d) })
        {
            build.Open("block", at: new LayoutPoint(0, y), paints: new PortablePaint(Kept: kept));
            build.Draw(new PortableRuleMark(new LayoutRect(0, 0, 50, 50), Medium.Ink(Brushes.Red)));
            build.Close();
        }

        build.Close();
        build.Close();
        var root = build.Seal().Root;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) WpfPainter.Paint(dc, root, Brushes.Black, new Rect(0, 1000, 200, 100));

        Assert.IsTrue(near.Holds, "the block on screen is painted and kept");
        Assert.IsFalse(far.Holds, "the one four thousand below it is not");
    });

    [TestMethod]
    public void AnOutlineIsDrawnWithThePenItAsksFor_BuiltOnce() => UiThread.Run(() =>
    {
        var stroke = (WpfStroke)Medium.Stroke(Medium.Ink(Brushes.Red), 2, [3, 1],
                                              new StrokeEnds(StrokeCap.Round, StrokeCap.Round, StrokeCap.Round, StrokeJoin.Bevel, 4, 1.5));

        var pen = stroke.PenFor(Brushes.Black);

        Assert.AreSame(Brushes.Red, pen.Brush);
        Assert.AreEqual(2, pen.Thickness);
        Assert.AreEqual(PenLineCap.Round, pen.StartLineCap);
        Assert.AreEqual(PenLineCap.Round, pen.EndLineCap);
        Assert.AreEqual(PenLineCap.Round, pen.DashCap);
        Assert.AreEqual(PenLineJoin.Bevel, pen.LineJoin);
        Assert.AreEqual(4, pen.MiterLimit);
        Assert.AreEqual(1.5, pen.DashStyle.Offset);
        CollectionAssert.AreEqual(new[] { 3d, 1d }, pen.DashStyle.Dashes.ToArray());
        Assert.IsTrue(pen.IsFrozen);

        Assert.AreSame(pen, stroke.PenFor(Brushes.Black), "built once and handed over from then on");
    });

    [TestMethod]
    public void AnOutlineWithNoPenOfItsOwnIsDrawnAsAPenDrawsIt() => UiThread.Run(() =>
    {
        var pen = ((WpfStroke)Medium.Stroke(Medium.Ink(Brushes.Red), 1, [2, 2])).PenFor(Brushes.Black);
        var plain = new Pen();

        Assert.AreEqual(plain.StartLineCap, pen.StartLineCap);
        Assert.AreEqual(plain.EndLineCap, pen.EndLineCap);
        Assert.AreEqual(plain.DashCap, pen.DashCap);
        Assert.AreEqual(plain.LineJoin, pen.LineJoin);
        Assert.AreEqual(plain.MiterLimit, pen.MiterLimit);
        Assert.AreEqual(0, pen.DashStyle.Offset);

        var defaults = new StrokeEnds();
        Assert.AreEqual(plain.StartLineCap, (PenLineCap)defaults.StartCap, "and a pen's defaults are the record's");
        Assert.AreEqual(plain.DashCap, (PenLineCap)defaults.DashCap);
        Assert.AreEqual(plain.LineJoin, (PenLineJoin)defaults.Join);
        Assert.AreEqual(plain.MiterLimit, defaults.MiterLimit);
    });

    [TestMethod]
    public void AnOutlineInNoColourOfItsOwnIsPennedInWhateverItIsHanded_AndACopyInItsOwnPen() => UiThread.Run(() =>
    {
        var shape = Medium.Line(new LayoutPoint(0, 0), new LayoutPoint(10, 10));
        var bevelled = Medium.Stroke(null, 1, ends: new StrokeEnds(Join: StrokeJoin.Bevel));
        var mark = new PortableGeometryMark(shape, null, bevelled);

        Assert.AreSame(Brushes.Black, ((WpfStroke)mark.Stroke!).PenFor(Brushes.Black).Brush);
        Assert.AreSame(Brushes.White, ((WpfStroke)mark.Stroke!).PenFor(Brushes.White).Brush, "a theme changing is a pen of the new colour");

        var rounded = mark with { Stroke = Medium.Stroke(null, 1, ends: new StrokeEnds(Join: StrokeJoin.Round)) };
        Assert.AreEqual(PenLineJoin.Round, ((WpfStroke)rounded.Stroke!).PenFor(Brushes.White).LineJoin, "a copy is drawn with its own pen");
        Assert.AreEqual(PenLineJoin.Bevel, ((WpfStroke)mark.Stroke!).PenFor(Brushes.White).LineJoin, "and the original keeps its");

        Assert.AreEqual(new PortableGeometryMark(shape, null, bevelled), mark, "a mark painted is still equal to the same mark never painted");
    });
    [TestMethod]
    public void APressOutsideAClipIsNotOnWhatItCuts()
    {
        var root = Pressed().Root;
        var bar = root.PieceAt(new LayoutPoint(30, 10));

        Assert.AreEqual(CutPart.Start, bar.Sits().Start, "inside the clip, the bar");
        Assert.IsTrue(bar.Squarely(new LayoutPoint(30, 10)));
        Assert.IsFalse(bar.Squarely(new LayoutPoint(105, 10)), "past the clip its box still reaches, and nothing of it is there");

        Assert.AreEqual(BesidePart.Start, root.PieceAt(new LayoutPoint(105, 10)).Sits().Start,
                        "on the bar's box past its clip is nearer the piece beside it than what is left of the bar");
        Assert.AreEqual(BesidePart.Start, root.PieceAt(new LayoutPoint(210, 10)).Sits().Start,
                        "and a piece its clip cuts away entirely is never the nearest");
    }

    [TestMethod]
    public void APictureDrawnSmallerAveragesThePixelsEachPixelShownCovers() => UiThread.Run(() =>
    {
        // A board of single black and white pixels, 64 across, drawn 25 across: each pixel shown covers about six and a half
        // of the board's, half of them black, so every one is grey. Sampled between neighbours instead, a pixel that lands
        // squarely on one of the board's comes out black or white, and the board aliases into speckle.
        const int Side = 64;
        var squares = new byte[Side * Side];
        for (var y = 0; y < Side; y++)
            for (var x = 0; x < Side; x++)
                squares[(y * Side) + x] = (byte)((x + y) % 2 == 0 ? 0 : 255);
        var board = Frozen(BitmapSource.Create(Side, Side, 96, 96, PixelFormats.Gray8, null, squares, Side));

        var build = new PortableBuilder(WpfMedium.OnThisThread);
        build.Open("page");
        build.Draw(new PortablePictureMark(Medium.Picture(board), new LayoutRect(10, 5, 25, 25)));
        build.Close();
        var picture = Read(Painted(build.Seal(), Brushes.White));

        for (var y = 7; y < 28; y++)
            for (var x = 12; x < 33; x++)
                Assert.AreEqual(128, picture.At(x, y).R, 40, $"({x}, {y}) is the board's average, not one of its squares");
    });

    /// <summary>
    /// A bar 100 wide, cut by the frame holding it to the 40 from 10 to 50 on the page; beside it at 130, an uncut box; and
    /// at 200, a box its own clip cuts away entirely.
    /// </summary>
    private static PortableTree Pressed()
    {
        var build = new PortableBuilder(WpfMedium.OnThisThread);
        build.Open("page");

        build.Open("cut", at: new LayoutPoint(10, 0), paints: new PortablePaint(Clip: Clip(new Rect(0, 0, 40, 20))));
        build.Open("bar", CutPart);
        build.Draw(new PortableRuleMark(new LayoutRect(0, 0, 100, 20), null));
        build.Close();
        build.Close();

        build.Open("beside", BesidePart, at: new LayoutPoint(130, 0));
        build.Draw(new PortableRuleMark(new LayoutRect(0, 0, 40, 20), null));
        build.Close();

        build.Open("hidden", HiddenPart, at: new LayoutPoint(200, 0),
                   paints: new PortablePaint(Clip: Clip(new Rect(100, 100, 5, 5))));
        build.Draw(new PortableRuleMark(new LayoutRect(0, 0, 20, 20), null));
        build.Close();

        build.Close();
        return build.Seal();
    }

    /// <summary>A page holding one red bar 80 by 20, standing at (20, 10) and painted as <paramref name="paints"/> says.</summary>
    private static PortableTree Bar(PortablePaint paints)
    {
        var build = new PortableBuilder(WpfMedium.OnThisThread);
        build.Open("page");
        build.Open("bar", at: new LayoutPoint(20, 10), paints: paints);
        build.Draw(new PortableRuleMark(new LayoutRect(0, 0, 80, 20), Medium.Ink(Brushes.Red)));
        build.Close();
        build.Close();
        return build.Seal();
    }

    /// <summary>The page of <paramref name="tree"/> painted into a picture, on <paramref name="ground"/> or on nothing.</summary>
    private static BitmapSource Painted(PortableTree tree, Brush? ground) =>
        WpfPainter.Painted(tree.Root, Page, 1, Brushes.Black, ground)!;

    /// <summary>What the pieces here are laid out in.</summary>
    private static WpfMedium Medium => WpfMedium.OnThisThread;

    /// <summary>A clip of <paramref name="box"/>, in the piece's own frame.</summary>
    private static LayoutShape Clip(Rect box) => Medium.Rectangle(box.ToLayout());

    /// <summary>A picture as the PNG it is saved as, read back.</summary>
    private static Pixels Read(BitmapSource picture)
    {
        var decoded = new PngBitmapDecoder(new MemoryStream(WpfPainter.Png(picture)), BitmapCreateOptions.PreservePixelFormat,
                                           BitmapCacheOption.OnLoad).Frames[0];
        var straight = new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0);

        var stride = straight.PixelWidth * 4;
        var bytes = new byte[stride * straight.PixelHeight];
        straight.CopyPixels(bytes, stride, 0);

        return new Pixels(bytes, stride);
    }

    private static bool IsRed(Color pixel) => pixel is { A: 255, R: > 240, G: < 15, B: < 15 };

    private static bool IsWhite(Color pixel) => pixel is { A: 255, R: > 240, G: > 240, B: > 240 };

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    /// <summary>A picture's pixels, blue, green, red and alpha, alpha not multiplied in.</summary>
    private sealed record Pixels(byte[] Bytes, int Stride)
    {
        public Color At(int x, int y)
        {
            var at = (y * Stride) + (x * 4);
            return Color.FromArgb(Bytes[at + 3], Bytes[at + 2], Bytes[at + 1], Bytes[at]);
        }
    }
}
