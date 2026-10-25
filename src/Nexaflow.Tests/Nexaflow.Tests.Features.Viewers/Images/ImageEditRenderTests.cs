using Nexaflow.IO.Common;
using System.Collections.Immutable;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Tests.Fixtures;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The render pipeline on real pixels: where each quadrant of a test picture lands after a turn, a flip, a crop, an
/// EXIF orientation or a straighten; what the adjustment filter does to known colours; and how markup is painted and
/// hit. Rendering is Skia on the CPU, so all of it runs headless.
/// </summary>
[TestClass]
public class ImageEditRenderTests
{
    private static readonly ImageEditingServices Services = new(VirtualFileSystem.Instance);

    private static SKImage Render(DecodedImage source, ImageRecipe recipe, RenderRequest? request = null)
    {
        var pipeline = Services.Pipeline(source, cache: false);
        var picture = pipeline.Render(recipe, request ?? RenderRequest.Saved);
        // Copy out: the pipeline's picture is only valid until it is disposed.
        var copy = SKImage.FromBitmap(SKBitmap.FromImage(picture));
        pipeline.Dispose();
        return copy;
    }

    // ── Geometry on pixels ──────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-render-geometry")]
    public void AnUneditedRender_IsThePicture()
    {
        using var source = Decoded(Quadrants(40, 20));
        using var picture = Render(source, ImageRecipe.Identity);

        Assert.AreEqual(40, picture.Width);
        Assert.AreEqual(20, picture.Height);
        AssertNear(TopLeft, PixelAt(picture, 2, 2));
        AssertNear(BottomRight, PixelAt(picture, 37, 17));
    }

    [TestMethod]
    [CoversNode("images-rotate")]
    [CoversNode("images-flip")]
    public void TurnsAndFlips_MoveTheQuadrantsWhereTheyShould()
    {
        using var source = Decoded(Quadrants(40, 20));

        using var turned = Render(source, new ImageRecipe { Geometry = new RecipeGeometry { QuarterTurns = 1 } });
        Assert.AreEqual(20, turned.Width, "a quarter turn swaps the sides");
        Assert.AreEqual(40, turned.Height);
        AssertNear(TopLeft, PixelAt(turned, 17, 2), because: "clockwise: the top-left corner goes to the top-right");
        AssertNear(BottomLeft, PixelAt(turned, 2, 2), because: "and the bottom-left to the top-left");

        using var mirrored = Render(source, new ImageRecipe { Geometry = new RecipeGeometry { FlipHorizontal = true } });
        AssertNear(TopRight, PixelAt(mirrored, 2, 2));
        AssertNear(TopLeft, PixelAt(mirrored, 37, 2));

        using var upended = Render(source, new ImageRecipe { Geometry = new RecipeGeometry { FlipVertical = true } });
        AssertNear(BottomLeft, PixelAt(upended, 2, 2));
    }

    [TestMethod]
    [CoversNode("images-edit-crop")]
    [CoversNode("images-render-geometry")]
    public void ACrop_KeepsOnlyItsRegion()
    {
        using var source = Decoded(Quadrants(40, 20));
        using var picture = Render(source, new ImageRecipe { Geometry = new RecipeGeometry { Crop = new CropRect(20, 0, 20, 10) } });

        Assert.AreEqual(20, picture.Width);
        Assert.AreEqual(10, picture.Height);
        AssertNear(TopRight, PixelAt(picture, 2, 2));
        AssertNear(TopRight, PixelAt(picture, 17, 7));
    }

    [TestMethod]
    [CoversNode("images-edit-resize")]
    public void AResize_ScalesTheOutput()
    {
        using var source = Decoded(Quadrants(40, 20));
        using var picture = Render(source, new ImageRecipe { Resize = new RecipeResize { Mode = ResizeMode.Percent, Percent = 50 } });

        Assert.AreEqual(20, picture.Width);
        Assert.AreEqual(10, picture.Height);
        AssertNear(TopLeft, PixelAt(picture, 1, 1));
    }

    [TestMethod]
    [CoversNode("images-decode")]
    public void AnExifOrientation_IsAppliedByTheRender_NotTheDecode()
    {
        // Stored on its side: tag 6 means the picture is shown turned a quarter clockwise.
        using var source = Decoded(Quadrants(40, 20), SKEncodedOrigin.RightTop);
        Assert.AreEqual(new SKSize(20, 40), source.SourceSize, "recipes are written against the upright picture");

        using var picture = Render(source, ImageRecipe.Identity);
        Assert.AreEqual(20, picture.Width);
        Assert.AreEqual(40, picture.Height);
        AssertNear(TopLeft, PixelAt(picture, 17, 2), because: "the stored top-left is shown top-right");
    }

    [TestMethod]
    [CoversNode("images-edit-straighten")]
    public void AStraightenedPicture_HasNoEmptyCorners()
    {
        using var source = Decoded(Quadrants(400, 300));
        using var picture = Render(source, new ImageRecipe { Geometry = new RecipeGeometry { StraightenDegrees = 10 } });

        foreach (var (x, y) in new[] { (0, 0), (picture.Width - 1, 0), (0, picture.Height - 1), (picture.Width - 1, picture.Height - 1) })
            Assert.AreEqual(255, PixelAt(picture, x, y).Alpha, $"corner {x},{y} must be picture, not transparency");
    }

    [TestMethod]
    [CoversNode("images-render-geometry")]
    public void TheCropEditingFrame_ShowsTheWholeStraightenedPicture()
    {
        using var source = Decoded(Quadrants(400, 300));
        var recipe = new ImageRecipe { Geometry = new RecipeGeometry { StraightenDegrees = 10, Crop = new CropRect(150, 100, 50, 50) } };
        using var picture = Render(source, recipe, new RenderRequest(RenderFrame.CropEditing, 1));

        var bounds = CropGeometry.StraightenedBounds(new SKSize(400, 300), 10);
        Assert.AreEqual((int)Math.Round(bounds.Width), picture.Width, "uncropped: the frame is dragged over all of it");
        Assert.AreEqual(0, PixelAt(picture, 0, 0).Alpha, "outside the tilted picture is transparent");
    }

    // ── Adjustments ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-render-adjust")]
    [CoversNode("images-edit-adjust")]
    public void OneStopOfExposure_DoublesTheLight()
    {
        using var source = Decoded(Flat(8, 8, new SKColor(77, 77, 77)));   // sRGB 0.30 → linear 0.0742
        using var picture = Render(source, new ImageRecipe { Adjustments = new RecipeAdjustments { Exposure = 1 } });

        // Linear 0.1484 is sRGB ≈ 0.42 → 107.
        AssertNear(new SKColor(107, 107, 107), PixelAt(picture, 4, 4), tolerance: 3);
    }

    [TestMethod]
    [CoversNode("images-render-adjust")]
    [CoversNode("images-edit-adjust")]
    public void SaturationAndWhiteBalance_DoWhatTheirNamesSay()
    {
        using var colourful = Decoded(Flat(8, 8, new SKColor(200, 60, 60)));
        using var grey = Render(colourful, new ImageRecipe { Adjustments = new RecipeAdjustments { Saturation = -100 } });
        var g = PixelAt(grey, 4, 4);
        Assert.IsTrue(Math.Abs(g.Red - g.Green) <= 1 && Math.Abs(g.Green - g.Blue) <= 1, $"fully desaturated is grey, got {g}");

        using var neutral = Decoded(Flat(8, 8, new SKColor(128, 128, 128)));
        using var warm = Render(neutral, new ImageRecipe { Adjustments = new RecipeAdjustments { Temperature = 60 } });
        var w = PixelAt(warm, 4, 4);
        Assert.IsTrue(w.Red > w.Blue + 10, $"warmer is redder than it is blue, got {w}");
    }

    [TestMethod]
    [CoversNode("images-render-adjust")]
    public void ShadowsAndHighlights_LeaveBlackAndWhiteWhereTheyAre()
    {
        foreach (var (colour, adjustments) in new[]
        {
            (SKColors.Black, new RecipeAdjustments { Shadows = 100 }),
            (SKColors.White, new RecipeAdjustments { Highlights = -100 }),
        })
        {
            using var source = Decoded(Flat(4, 4, colour));
            using var picture = Render(source, new ImageRecipe { Adjustments = adjustments });
            AssertNear(colour, PixelAt(picture, 1, 1), tolerance: 2);
        }

        using var dark = Decoded(Flat(4, 4, new SKColor(50, 50, 50)));
        using var lifted = Render(dark, new ImageRecipe { Adjustments = new RecipeAdjustments { Shadows = 100 } });
        Assert.IsTrue(PixelAt(lifted, 1, 1).Red > 60, "shadows lifted");
    }

    [TestMethod]
    [CoversNode("images-edit-auto")]
    public void AutoEnhance_BrightensADarkPicture_AndLeavesAGoodOneAlmostAlone()
    {
        using var dark = SKImage.FromBitmap(Flat(64, 64, new SKColor(40, 40, 42)));
        var lift = AutoEnhance.Analyse(dark);
        Assert.IsTrue(lift.Exposure > 0.3, $"a dark picture is brightened, got {lift.Exposure}");

        using var good = SKImage.FromBitmap(Ramp(256, 16, (r, g, b) => (r, g, b)));
        var none = AutoEnhance.Analyse(good);
        Assert.IsTrue(Math.Abs(none.Exposure) < 0.25 && none.Contrast == 0 && none.Temperature == 0 && none.Tint == 0,
                      $"a full-range neutral picture is left almost alone, got {none}");
    }

    [TestMethod]
    [CoversNode("images-edit-auto")]
    public void AutoEnhance_CorrectsACast_OnlyFromWhatShouldBeGrey()
    {
        using var cool = SKImage.FromBitmap(Ramp(256, 16, (r, g, b) => ((byte)(r * 0.85), g, b)));
        Assert.IsTrue(AutoEnhance.Analyse(cool).Temperature > 5, "a grey with a blue cast is warmed");

        using var colourful = SKImage.FromBitmap(Quadrants(64, 64));
        var vivid = AutoEnhance.Analyse(colourful);
        Assert.AreEqual(0, vivid.Temperature, "nothing in a picture of saturated colours says what the light was");
        Assert.AreEqual(0, vivid.Tint);
    }

    /// <summary>A left-to-right grey ramp, each column's grey passed through <paramref name="tone"/>.</summary>
    private static SKBitmap Ramp(int width, int height, Func<byte, byte, byte, (byte R, byte G, byte B)> tone)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb()));
        for (int x = 0; x < width; x++)
        {
            byte v = (byte)(x * 255 / (width - 1));
            var (r, g, b) = tone(v, v, v);
            for (int y = 0; y < height; y++) bitmap.SetPixel(x, y, new SKColor(r, g, b));
        }
        return bitmap;
    }

    // ── Markup ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-render-markup")]
    [CoversNode("images-edit-markup-tools")]
    public void Markup_IsPaintedOverThePicture_AtSourcePositions()
    {
        using var source = Decoded(Flat(100, 100, SKColors.White));
        var arrow = new MarkupArrow { From = new MarkupPoint(10, 50), To = new MarkupPoint(90, 50), Color = 0xFF0000FF, StrokeWidth = 6 };
        var box = new MarkupBox { Corner = new MarkupPoint(10, 10), OppositeCorner = new MarkupPoint(40, 30), Color = 0xFF00AA00, StrokeWidth = 4 };
        using var picture = Render(source, new ImageRecipe { Markup = [arrow, box] });

        AssertNear(new SKColor(0, 0, 255), PixelAt(picture, 40, 50), because: "on the arrow's shaft");
        AssertNear(new SKColor(0, 170, 0), PixelAt(picture, 10, 20), because: "on the box's edge");
        AssertNear(SKColors.White, PixelAt(picture, 25, 20), because: "an unfilled box is clear inside");

        // Turned with the picture: the arrow's shaft is now vertical.
        using var turned = Render(source, new ImageRecipe { Markup = [arrow], Geometry = new RecipeGeometry { QuarterTurns = 1 } });
        AssertNear(new SKColor(0, 0, 255), PixelAt(turned, 50, 40));
    }

    [TestMethod]
    [CoversNode("images-render-markup")]
    public void AHighlighterCrossingItself_DoesNotDarkenTwice()
    {
        using var source = Decoded(Flat(100, 100, SKColors.White));
        var stroke = new MarkupStroke
        {
            Style = StrokeStyle.Highlighter, Color = 0x80FFD700, StrokeWidth = 16,
            Points = [new(10, 50), new(90, 50), new(90, 20), new(50, 20), new(50, 90)],
        };
        using var picture = Render(source, new ImageRecipe { Markup = [stroke] });

        var crossing = PixelAt(picture, 50, 50);
        var once = PixelAt(picture, 25, 50);
        AssertNear(once, crossing, tolerance: 2, because: "one stroke, one layer of ink");
        Assert.AreNotEqual(SKColors.White, once, "and it is inked");
    }

    [TestMethod]
    [CoversNode("images-edit-markup-select")]
    public void HitTesting_FindsWhatWasPainted()
    {
        var painter = Services.Markup;
        var arrow = new MarkupArrow { From = new MarkupPoint(0, 0), To = new MarkupPoint(100, 0), StrokeWidth = 4 };
        Assert.IsTrue(painter.HitTest(arrow, new SKPoint(50, 1), 2));
        Assert.IsFalse(painter.HitTest(arrow, new SKPoint(50, 20), 2));

        var text = new MarkupText { Anchor = new MarkupPoint(10, 10), Text = "Hello", FontSize = 20 };
        Assert.IsTrue(painter.HitTest(text, new SKPoint(20, 20), 0));
        Assert.IsFalse(painter.HitTest(text, new SKPoint(-30, -30), 0));

        var turned = text with { RotationDegrees = 90 };
        Assert.IsTrue(painter.HitTest(turned, new SKPoint(0, 30), 0), "turned text is hit where it is drawn");

        var moved = (MarkupArrow)arrow.Offset(10, 10);
        Assert.AreEqual(new MarkupPoint(110, 10), moved.To);
    }

    [TestMethod]
    [CoversNode("images-render-preview")]
    public void TheCachedPipeline_RedoesOnlyWhatChanged_AndMatchesAFreshRender()
    {
        using var source = Decoded(Quadrants(64, 48));
        using var pipeline = Services.Pipeline(source);
        var recipe = new ImageRecipe { Geometry = new RecipeGeometry { QuarterTurns = 1 } };
        var request = new RenderRequest(RenderFrame.Output, 1);

        var first = PixelAt(pipeline.Render(recipe, request), 5, 5);
        var adjusted = recipe with { Adjustments = new RecipeAdjustments { Exposure = 1 } };
        var second = PixelAt(pipeline.Render(adjusted, request), 5, 5);
        var back = PixelAt(pipeline.Render(recipe, request), 5, 5);

        Assert.AreNotEqual(first, second);
        AssertNear(first, back, tolerance: 0, because: "the cached geometry pass is reused, unchanged");
        using var fresh = Render(source, adjusted, request);
        AssertNear(PixelAt(fresh, 5, 5), second, tolerance: 0, because: "cached and uncached renders agree");
    }

    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// How long a live edit takes on a screen-sized preview — what decides whether a slider feels live. Logged, not
    /// asserted: a timing gate on a shared build machine fails for reasons that are not the code's.
    /// </summary>
    [TestMethod]
    [TestCategory("Performance")]
    [CoversNode("images-render-preview")]
    public void APreviewRerender_IsTimedForTheLog()
    {
        using var source = Decoded(Quadrants(2560, 1707));
        using var pipeline = Services.Pipeline(source);
        var straightened = new ImageRecipe { Geometry = new RecipeGeometry { StraightenDegrees = 3, QuarterTurns = 1 } };
        var request = new RenderRequest(RenderFrame.Output, 1);
        pipeline.Render(straightened, request);   // warm: the filter compiled, the geometry pass made

        var clock = System.Diagnostics.Stopwatch.StartNew();
        const int Ticks = 20;
        for (int i = 1; i <= Ticks; i++)
            pipeline.Render(straightened with { Adjustments = new RecipeAdjustments { Exposure = i / 20.0, Saturation = i } }, request);
        double slider = clock.Elapsed.TotalMilliseconds / Ticks;

        clock.Restart();
        pipeline.Render(straightened with { Geometry = straightened.Geometry with { StraightenDegrees = 4 } }, request);
        double geometry = clock.Elapsed.TotalMilliseconds;

        TestContext.WriteLine($"2560-px preview: a slider tick re-renders in {slider:F1} ms (tone pass only); a geometry change in {geometry:F1} ms.");
    }

    // ── Decoding ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-decode")]
    public void AJpegPreview_IsDecodedReduced_AndKnowsItsFullSize()
    {
        using var bitmap = Quadrants(1600, 1200);
        using var decoded = Services.Decoders.Decode(EncodeJpeg(bitmap), maxLongEdge: 400);

        Assert.AreEqual(ImageContainer.Jpeg, decoded.Container);
        Assert.IsTrue(Math.Max(decoded.Pixels.Width, decoded.Pixels.Height) <= 400);
        Assert.AreEqual(new SKSizeI(1600, 1200), decoded.StoredFullSize);
        Assert.AreEqual(4, decoded.Reduction, 0.01);
    }

    [TestMethod]
    [CoversNode("images-decode")]
    public void AJpegsExifOrientation_IsRead_AndStandsThePictureUpright()
    {
        using var decoded = Services.Decoders.Decode(OrientedPhotoBytes(), maxLongEdge: null);

        Assert.AreEqual((SKEncodedOrigin)OrientedPhotoSample.Orientation, decoded.Origin);
        Assert.AreEqual(new SKSize(OrientedPhotoSample.StoredHeight, OrientedPhotoSample.StoredWidth), decoded.SourceSize);

        using var picture = Render(decoded, ImageRecipe.Identity);
        AssertNear(Colour(OrientedPhotoSample.TopLeft), PixelAt(picture, picture.Width - 4, 4), tolerance: 12,
                   because: "the stored top-left is shown top-right");
    }

    [TestMethod]
    [CoversNode("images-decode")]
    public void ATiff_IsDecodedThroughWic()
    {
        using var bitmap = Quadrants(40, 20);
        using var picture = SKImage.FromBitmap(bitmap);
        var tiff = Services.Encoders.Encode(picture, ImageContainer.Tiff, new EncodeOptions(90), Nexaflow.Features.Images.Editing.Metadata.MetadataBlocks.None);

        using var decoded = Services.Decoders.Decode(tiff, maxLongEdge: null);
        Assert.AreEqual(ImageContainer.Tiff, decoded.Container);
        using var rendered = Render(decoded, ImageRecipe.Identity);
        AssertNear(TopLeft, PixelAt(rendered, 2, 2), tolerance: 1);
        AssertNear(BottomRight, PixelAt(rendered, 37, 17), tolerance: 1);
    }
}
