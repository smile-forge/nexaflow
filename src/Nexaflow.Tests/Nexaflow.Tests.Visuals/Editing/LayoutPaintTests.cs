using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Text.Editing;

namespace Nexaflow.Tests.Visuals.Editing;

/// <summary>
/// How much of a piece shows (<see cref="LayoutPaint.Clip"/>, <see cref="LayoutPaint.Opacity"/>,
/// <see cref="LayoutPaint.Mask"/>) and how an outline's pen is shaped (<see cref="LayoutPen"/>): painted into a picture the
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
        var picture = Read(Painted(Bar(new LayoutPaint(Clip: Frozen(new RectangleGeometry(new Rect(0, 0, 30, 20))))), Brushes.White));

        Assert.IsTrue(IsRed(picture.At(35, 20)), "inside the clip, where the piece stands");
        Assert.IsTrue(IsWhite(picture.At(60, 20)), "past the clip, though the bar runs on there");
        Assert.IsTrue(IsWhite(picture.At(10, 20)), "and nothing before the bar");
    });

    [TestMethod]
    public void AClippedPieceKeepingItsPictureIsClippedJustTheSame() => UiThread.Run(() =>
    {
        var kept = new LayoutKept();
        var bar = Bar(new LayoutPaint(Kept: kept, Clip: Frozen(new RectangleGeometry(new Rect(0, 0, 30, 20)))));

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
        var half = Read(Painted(Bar(new LayoutPaint(Opacity: 0.5)), ground: null)).At(50, 20);

        Assert.AreEqual(128, half.A, 2, "half its alpha");
        Assert.IsTrue(half.R > 240 && half.G < 15, "and still its own colour");

        var none = Read(Painted(Bar(new LayoutPaint(Opacity: 0)), ground: null)).At(50, 20);
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

        var picture = Read(Painted(Bar(new LayoutPaint(Mask: Frozen(mask))), ground: null));

        Assert.AreEqual(255, picture.At(40, 20).A, "where the mask is opaque");
        Assert.AreEqual(0, picture.At(85, 20).A, "and nothing where it is clear");
    });

    [TestMethod]
    public void InsideAClippedFrameABlockFarFromWhatIsShownStillLetsItsPictureGo() => UiThread.Run(() =>
    {
        // A clip puts the painter in the piece's own frame. What is near the screen has to move with it, or every block inside
        // a clipped piece is painted and kept however far down the page it is.
        var near = new LayoutKept();
        var far = new LayoutKept();

        var build = new LayoutBuilder();
        build.Open("page");
        build.Open("cut", at: new Point(0, 1000), paints: new LayoutPaint(Clip: Frozen(new RectangleGeometry(new Rect(0, 0, 100, 6000)))));

        foreach (var (kept, y) in new[] { (near, 0d), (far, 5000d) })
        {
            build.Open("block", at: new Point(0, y), paints: new LayoutPaint(Kept: kept));
            build.Draw(new RuleMark(new Rect(0, 0, 50, 50), Brushes.Red));
            build.Close();
        }

        build.Close();
        build.Close();
        var root = build.Seal().Root;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) LayoutPainter.Paint(dc, root, Brushes.Black, new Rect(0, 1000, 200, 100));

        Assert.IsTrue(near.Holds, "the block on screen is painted and kept");
        Assert.IsFalse(far.Holds, "the one four thousand below it is not");
    });

    [TestMethod]
    public void AnOutlineIsDrawnWithThePenItAsksFor_BuiltOnce() => UiThread.Run(() =>
    {
        var mark = new GeometryMark(Geometry.Parse("M0,0 L10,0 L10,10"), null, Brushes.Red, 2)
        {
            Dashes = [3, 1],
            Outline = new LayoutPen(PenLineCap.Round, PenLineCap.Round, PenLineCap.Round, PenLineJoin.Bevel, 4, 1.5),
        };

        var pen = PenOf(mark, Brushes.Black);

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

        Assert.AreSame(pen, PenOf(mark, Brushes.Black), "built once and handed over from then on");
    });

    [TestMethod]
    public void AnOutlineWithNoPenOfItsOwnIsDrawnAsAPenDrawsIt() => UiThread.Run(() =>
    {
        var pen = PenOf(new GeometryMark(Geometry.Parse("M0,0 L10,0"), null, Brushes.Red, 1) { Dashes = [2, 2] }, Brushes.Black);
        var plain = new Pen();

        Assert.AreEqual(plain.StartLineCap, pen.StartLineCap);
        Assert.AreEqual(plain.EndLineCap, pen.EndLineCap);
        Assert.AreEqual(plain.DashCap, pen.DashCap);
        Assert.AreEqual(plain.LineJoin, pen.LineJoin);
        Assert.AreEqual(plain.MiterLimit, pen.MiterLimit);
        Assert.AreEqual(0, pen.DashStyle.Offset);

        var defaults = new LayoutPen();
        Assert.AreEqual(plain.StartLineCap, defaults.StartCap, "and a pen's defaults are the record's");
        Assert.AreEqual(plain.DashCap, defaults.DashCap);
        Assert.AreEqual(plain.LineJoin, defaults.Join);
        Assert.AreEqual(plain.MiterLimit, defaults.MiterLimit);
    });

    [TestMethod]
    public void AnOutlineInNoColourOfItsOwnIsPennedInWhateverItIsHanded_AndACopyInItsOwnPen() => UiThread.Run(() =>
    {
        var shape = Geometry.Parse("M0,0 L10,0 L10,10");
        var bevelled = new LayoutPen(Join: PenLineJoin.Bevel);
        var mark = new GeometryMark(shape, null, null, 1) { Outline = bevelled };

        Assert.AreSame(Brushes.Black, PenOf(mark, Brushes.Black).Brush);
        Assert.AreSame(Brushes.White, PenOf(mark, Brushes.White).Brush, "a theme changing is a pen of the new colour");

        var rounded = mark with { Outline = new LayoutPen(Join: PenLineJoin.Round) };
        Assert.AreEqual(PenLineJoin.Round, PenOf(rounded, Brushes.White).LineJoin, "a copy is drawn with its own pen");
        Assert.AreEqual(PenLineJoin.Bevel, PenOf(mark, Brushes.White).LineJoin, "and the original keeps its");

        Assert.AreEqual(new GeometryMark(shape, null, null, 1) { Outline = bevelled }, mark,
                        "a mark painted is still equal to the same mark never painted");
    });

    [TestMethod]
    public void APressOutsideAClipIsNotOnWhatItCuts()
    {
        var root = Pressed().Root;
        var bar = root.PieceAt(new Point(30, 10));

        Assert.AreEqual(CutPart.Start, bar.Sits().Start, "inside the clip, the bar");
        Assert.IsTrue(bar.Squarely(new Point(30, 10)));
        Assert.IsFalse(bar.Squarely(new Point(105, 10)), "past the clip its box still reaches, and nothing of it is there");

        Assert.AreEqual(BesidePart.Start, root.PieceAt(new Point(105, 10)).Sits().Start,
                        "on the bar's box past its clip is nearer the piece beside it than what is left of the bar");
        Assert.AreEqual(BesidePart.Start, root.PieceAt(new Point(210, 10)).Sits().Start,
                        "and a piece its clip cuts away entirely is never the nearest");
    }

    /// <summary>
    /// A bar 100 wide, cut by the frame holding it to the 40 from 10 to 50 on the page; beside it at 130, an uncut box; and
    /// at 200, a box its own clip cuts away entirely.
    /// </summary>
    private static LayoutTree Pressed()
    {
        var build = new LayoutBuilder();
        build.Open("page");

        build.Open("cut", at: new Point(10, 0), paints: new LayoutPaint(Clip: Frozen(new RectangleGeometry(new Rect(0, 0, 40, 20)))));
        build.Open("bar", CutPart);
        build.Draw(new RuleMark(new Rect(0, 0, 100, 20), null));
        build.Close();
        build.Close();

        build.Open("beside", BesidePart, at: new Point(130, 0));
        build.Draw(new RuleMark(new Rect(0, 0, 40, 20), null));
        build.Close();

        build.Open("hidden", HiddenPart, at: new Point(200, 0),
                   paints: new LayoutPaint(Clip: Frozen(new RectangleGeometry(new Rect(100, 100, 5, 5)))));
        build.Draw(new RuleMark(new Rect(0, 0, 20, 20), null));
        build.Close();

        build.Close();
        return build.Seal();
    }

    /// <summary>A page holding one red bar 80 by 20, standing at (20, 10) and painted as <paramref name="paints"/> says.</summary>
    private static LayoutTree Bar(LayoutPaint paints)
    {
        var build = new LayoutBuilder();
        build.Open("page");
        build.Open("bar", at: new Point(20, 10), paints: paints);
        build.Draw(new RuleMark(new Rect(0, 0, 80, 20), Brushes.Red));
        build.Close();
        build.Close();
        return build.Seal();
    }

    /// <summary>The page of <paramref name="tree"/> painted into a picture, on <paramref name="ground"/> or on nothing.</summary>
    private static BitmapSource Painted(LayoutTree tree, Brush? ground) =>
        LayoutPictures.Painted(tree.Root, Page, 1, Brushes.Black, ground)!;

    /// <summary>The pen a mark draws its outline with, painted with <paramref name="fallback"/> for the theme's colour.</summary>
    private static Pen PenOf(GeometryMark mark, Brush fallback)
    {
        var drawn = new DrawingGroup();
        using (var dc = drawn.Open()) mark.PaintOn(dc, fallback);

        return ((GeometryDrawing)drawn.Children[0]).Pen;
    }

    /// <summary>A picture as the PNG it is saved as, read back.</summary>
    private static Pixels Read(BitmapSource picture)
    {
        var decoded = new PngBitmapDecoder(new MemoryStream(LayoutPictures.Png(picture)), BitmapCreateOptions.PreservePixelFormat,
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
