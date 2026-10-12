using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Tests.Fixtures;
using SkiaSharp;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The geometry a recipe describes — turns, flips, straighten, crop, aspect, resize — checked as arithmetic on the
/// one matrix the renderer and the crop handles share. What is worth pinning is the awkward part: turning a flipped
/// picture, the crop at an angle, and a level line drawn on a mirrored display.
/// </summary>
[TestClass]
public class ImageEditGeometryTests
{
    private static readonly SKSize Source = new(400, 300);

    private static IEnumerable<RecipeGeometry> EveryOrientation()
    {
        for (int turns = 0; turns < 4; turns++)
            foreach (var flipH in new[] { false, true })
                foreach (var flipV in new[] { false, true })
                    yield return new RecipeGeometry { QuarterTurns = turns, FlipHorizontal = flipH, FlipVertical = flipV };
    }

    private static SKPoint Shown(RecipeGeometry g, SKPoint p) => CropGeometry.Display(g, Source).MapPoint(p);

    // ── Turns and flips ─────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-rotate")]
    [CoversNode("images-flip")]
    public void TurningOrFlipping_AlwaysActsOnThePictureAsDisplayed()
    {
        var probe = new SKPoint(30, 20);
        foreach (var g in EveryOrientation())
        {
            // Clockwise on screen: whatever was displayed at (x, y) of a W×H display is now at (H − y, x).
            var before = Shown(g, probe);
            var shownSize = CropGeometry.Turned(Source, g.QuarterTurns);
            var turned = g.Turned(1);
            var after = CropGeometry.Display(turned, Source).MapPoint(probe);
            Assert.AreEqual(shownSize.Height - before.Y, after.X, 1e-3, $"turn of {g}");
            Assert.AreEqual(before.X, after.Y, 1e-3, $"turn of {g}");

            // A horizontal flip mirrors the display left-to-right, whatever was done before.
            var flipped = CropGeometry.Display(g.Flipped(horizontal: true), Source).MapPoint(probe);
            Assert.AreEqual(shownSize.Width - before.X, flipped.X, 1e-3, $"flip of {g}");
            Assert.AreEqual(before.Y, flipped.Y, 1e-3, $"flip of {g}");
        }
    }

    [TestMethod]
    [CoversNode("images-rotate")]
    [CoversNode("images-flip")]
    public void FourTurnsOrTwoFlips_AreNoEditAtAll_AndBothFlipsAreAHalfTurn()
    {
        var g = RecipeGeometry.Identity;
        Assert.IsTrue(g.Turned(1).Turned(1).Turned(1).Turned(1).IsIdentity);
        Assert.IsTrue(g.Turned(-1).Turned(1).IsIdentity);
        Assert.IsTrue(g.Flipped(true).Flipped(true).IsIdentity);

        var both = g.Flipped(true).Flipped(false);
        Assert.AreEqual(new RecipeGeometry { QuarterTurns = 2 }, both, "one spelling per picture");
    }

    // ── The whole matrix ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-render-geometry")]
    public void TheCropFillsTheCanvas_InEveryOrientation_AndMapsBack()
    {
        var crop = new CropRect(50, 40, 200, 120);
        foreach (var g in EveryOrientation())
        {
            var recipe = new ImageRecipe { Geometry = g with { Crop = crop } };
            var m = CropGeometry.FrameToCanvas(recipe, Source, RenderFrame.Output, 1, out var canvas);

            var expected = CropGeometry.Turned(new SKSize(200, 120), g.QuarterTurns);
            Assert.AreEqual((int)expected.Width, canvas.Width, $"{g}");
            Assert.AreEqual((int)expected.Height, canvas.Height, $"{g}");

            var mapped = m.MapRect(SKRect.Create(50, 40, 200, 120));
            Assert.AreEqual(0, mapped.Left, 1e-3);
            Assert.AreEqual(0, mapped.Top, 1e-3);
            Assert.AreEqual(canvas.Width, mapped.Right, 1e-3);
            Assert.AreEqual(canvas.Height, mapped.Bottom, 1e-3);

            Assert.IsTrue(m.TryInvert(out var back));
            var p = new SKPoint(123, 77);
            var round = back.MapPoint(m.MapPoint(p));
            Assert.AreEqual(p.X, round.X, 1e-3);
            Assert.AreEqual(p.Y, round.Y, 1e-3);
        }
    }

    [TestMethod]
    [CoversNode("images-edit-resize")]
    public void TheOutputSize_FollowsTheCropTurnAndResize()
    {
        var crop = new CropRect(0, 0, 200, 100);
        var g = new RecipeGeometry { Crop = crop };

        Assert.AreEqual(new SKSizeI(200, 100), CropGeometry.OutputSize(new ImageRecipe { Geometry = g }, Source));
        Assert.AreEqual(new SKSizeI(100, 200), CropGeometry.OutputSize(new ImageRecipe { Geometry = g with { QuarterTurns = 1 } }, Source));
        Assert.AreEqual(new SKSizeI(50, 25), CropGeometry.OutputSize(
            new ImageRecipe { Geometry = g, Resize = new RecipeResize { Mode = ResizeMode.Percent, Percent = 25 } }, Source));
        Assert.AreEqual(new SKSizeI(80, 40), CropGeometry.OutputSize(
            new ImageRecipe { Geometry = g, Resize = new RecipeResize { Mode = ResizeMode.Pixels, Width = 80, Height = 999, LockAspect = true } }, Source),
            "a locked height follows the crop, so a later re-crop never distorts");
        Assert.AreEqual(new SKSizeI(80, 90), CropGeometry.OutputSize(
            new ImageRecipe { Geometry = g, Resize = new RecipeResize { Mode = ResizeMode.Pixels, Width = 80, Height = 90, LockAspect = false } }, Source));
    }

    // ── Straightening ───────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-edit-straighten")]
    public void TheInscribedCrop_FitsAtEveryAngle_AndIsTheLargestThatDoes()
    {
        for (double deg = -45; deg <= 45; deg += 7.5)
        {
            foreach (var aspect in new[] { 0.5, 1.0, 4.0 / 3 })
            {
                var crop = CropGeometry.Inscribed(Source, deg, aspect);
                Assert.IsTrue(CropGeometry.Contains(Source, deg, crop), $"{deg}° {aspect}: inscribed crop must lie on the picture");
                Assert.AreEqual(aspect, crop.Width / crop.Height, 1e-6);

                var larger = new CropRect(crop.CentreX - crop.Width * 0.505, crop.CentreY - crop.Height * 0.505, crop.Width * 1.01, crop.Height * 1.01);
                Assert.IsFalse(CropGeometry.Contains(Source, deg, larger), $"{deg}° {aspect}: 1% larger must not fit");
            }
        }
    }

    [TestMethod]
    [CoversNode("images-edit-straighten")]
    public void StraighteningShrinksTheCropAboutItsCentre_KeepingItsShape()
    {
        var crop = new CropRect(100, 75, 200, 150);
        var fitted = CropGeometry.FitInside(Source, 30, crop);

        Assert.IsTrue(CropGeometry.Contains(Source, 30, fitted));
        Assert.AreEqual(crop.Width / crop.Height, fitted.Width / fitted.Height, 1e-6, "same shape");
        Assert.AreEqual(crop.CentreX, fitted.CentreX, 1e-6, "same centre");
        Assert.AreEqual(crop.CentreY, fitted.CentreY, 1e-6, "same centre");
        Assert.AreEqual(crop, CropGeometry.FitInside(Source, 0, crop), "a crop that fits is left alone");

        var lost = new CropRect(-500, -500, 100, 100);
        var recovered = CropGeometry.FitInside(Source, 10, lost);
        Assert.IsTrue(CropGeometry.Contains(Source, 10, recovered), "a centre off the picture is brought back");
    }

    [TestMethod]
    [CoversNode("images-edit-straighten")]
    public void WithNoCropChosen_TheDefaultFollowsTheAngle()
    {
        Assert.AreEqual(new CropRect(0, 0, 400, 300), CropGeometry.DefaultCrop(Source, 0));
        var tilted = CropGeometry.DefaultCrop(Source, 12);
        Assert.IsTrue(CropGeometry.Contains(Source, 12, tilted));
        Assert.AreEqual(400.0 / 300, tilted.Width / tilted.Height, 1e-6, "the picture's own shape");
    }

    // ── Crop frame ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-edit-crop")]
    public void ADraggedFrame_StopsAtThePicturesEdge()
    {
        var valid = new CropRect(100, 100, 100, 100);

        var dragged = CropGeometry.ClampTowards(Source, 0, valid, valid with { X = 380 });   // would hang 80 px off the right
        Assert.AreEqual(300, dragged.X, 0.01, "it stops flush with the edge");
        Assert.AreEqual(100, dragged.Width, 0.01);

        var tilted = CropGeometry.ClampTowards(Source, 20, CropGeometry.Inscribed(Source, 20, 1), new CropRect(-50, -50, 600, 500));
        Assert.IsTrue(CropGeometry.Contains(Source, 20, tilted), "at an angle too");

        var squashed = CropGeometry.ClampTowards(Source, 0, valid, valid with { Width = 1 });
        Assert.IsTrue(squashed.Width >= CropGeometry.MinCropEdge, "never thinner than the minimum");
    }

    [TestMethod]
    [CoversNode("images-edit-aspect")]
    public void APreset_ShapesTheCropAsDisplayed()
    {
        var square = CropGeometry.ApplyAspect(new RecipeGeometry { Aspect = AspectPreset.Square }, Source);
        Assert.AreEqual(new CropRect(50, 0, 300, 300), square);

        // Turned a quarter, a landscape 16:9 on screen is a portrait frame in the stored picture.
        var turned = new RecipeGeometry { Aspect = AspectPreset.SixteenNine, QuarterTurns = 1 };
        var crop = CropGeometry.ApplyAspect(turned, Source);
        Assert.AreEqual(9.0 / 16, crop.Width / crop.Height, 1e-6);
        var shown = CropGeometry.OutputSize(new ImageRecipe { Geometry = turned with { Crop = crop } }, Source);
        Assert.AreEqual(16.0 / 9, (double)shown.Width / shown.Height, 0.02, "landscape on screen");

        var portrait = CropGeometry.DisplayAspect(new RecipeGeometry { Aspect = AspectPreset.FourThree, AspectPortrait = true }, Source);
        Assert.AreEqual(3.0 / 4, portrait!.Value, 1e-6);
        Assert.IsNull(CropGeometry.DisplayAspect(RecipeGeometry.Identity, Source), "free has no aspect");
    }

    // ── Level tool ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-edit-level")]
    public void ALevelLine_StraightensTheWayTheDisplayNeeds()
    {
        var a = new SKPoint(0, 0);
        var tiltedDown = new SKPoint(100, (float)(100 * Math.Tan(5 * Math.PI / 180)));   // 5° clockwise of level

        Assert.AreEqual(-5, CropGeometry.LevelAngle(RecipeGeometry.Identity, a, tiltedDown), 1e-3);
        Assert.AreEqual(5, CropGeometry.LevelAngle(new RecipeGeometry { FlipHorizontal = true }, a, tiltedDown), 1e-3,
                        "through a mirror the same correction runs the other way");
        Assert.AreEqual(-5, CropGeometry.LevelAngle(new RecipeGeometry { QuarterTurns = 1 }, a, tiltedDown), 1e-3,
                        "turns do not change direction");

        var nearlyPlumb = new SKPoint((float)(-100 * Math.Tan(3 * Math.PI / 180)), 100);   // 93° from the x-axis
        Assert.AreEqual(-3, CropGeometry.LevelAngle(RecipeGeometry.Identity, a, nearlyPlumb), 1e-3, "a near-vertical line is made plumb");

        var already = new RecipeGeometry { StraightenDegrees = 40 };
        Assert.AreEqual(45, CropGeometry.LevelAngle(already, a, new SKPoint(100, -20)), 1e-6, "clamped to the range");
    }
}
