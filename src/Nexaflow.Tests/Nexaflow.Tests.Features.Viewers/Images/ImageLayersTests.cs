using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Tests.Fixtures;
using SkiaSharp;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// A layered picture: pixels in shared tiles, the layer tree and its edits, what a history step costs, and the compositor —
/// every blend against Skia drawing the same, isolated and pass-through groups, masks on and off and on a group, clipping
/// to the layers beneath, and a composite drawn again over only what changed equal to one drawn whole.
/// </summary>
[TestClass]
public class ImageLayersTests
{
    private static readonly MarkupPainter Painter = new();

    /// <summary>No picture layers here: every one is left out.</summary>
    private sealed class NoPictures : IPictureLayerImages
    {
        public PictureLayerImage? ImageOf(PictureLayer layer, double scale) => null;
    }

    private static TiledCoverage All(int width, int height) => TiledCoverage.Covered(width, height);

    private static TiledCoverage Rect(int width, int height, double left, double top, double right, double bottom)
        => RegionRaster.Shape(new RegionRectangle(left, top, right, bottom), width, height, 1);

    private static PixelLayer Solid(int width, int height, SKColor colour, string name = "", LayerBlend? blend = null)
        => new() { Name = name, Pixels = TiledPixels.Empty.Filled(All(width, height), colour), Blend = blend ?? LayerBlend.Normal };

    private static LayeredPicture Picture(int width, int height, params Layer[] layers)
        => new() { Width = width, Height = height, Layers = [.. layers], Selection = TiledCoverage.Clear(width, height) };

    private static SKColor Pixel(SKImage image, int x, int y)
    {
        using var bitmap = SKBitmap.FromImage(image);
        return bitmap.GetPixel(x, y);
    }

    private static SKColor Composite(LayeredPicture picture, int x, int y)
    {
        using var compositor = new LayerCompositor(Painter, cache: false);
        using var image = compositor.Render(picture, 1, new NoPictures());
        return Pixel(image, x, y);
    }

    private static void Near(SKColor expected, SKColor actual, string because, int tolerance = 3)
        => Assert.IsTrue(Math.Abs(expected.Red - actual.Red) <= tolerance && Math.Abs(expected.Green - actual.Green) <= tolerance
                         && Math.Abs(expected.Blue - actual.Blue) <= tolerance && Math.Abs(expected.Alpha - actual.Alpha) <= tolerance,
                         $"{because}: expected {expected}, was {actual}");

    // ── Pixels ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-layers")]
    public void Pixels_AreHeldInTheTilesPainted_AndAChangeSharesTheRest()
    {
        var red = TiledPixels.Empty.Filled(Rect(1024, 1024, 10, 10, 20, 20), SKColors.Red);
        Assert.AreEqual(1, red.Tiles.Count(), "only the tile painted is held");
        Assert.AreEqual(new SKRectI(10, 10, 20, 20), red.Bounds);

        var more = red.Filled(Rect(1024, 1024, 600, 600, 610, 610), SKColors.Blue);
        Assert.IsTrue(more.SharesTile(red, new TileKey(0, 0)), "the first tile is shared, not copied");
        Assert.AreEqual(256L * 256 * 4, more.BytesNotIn(red), "the step costs the one tile it made");

        var cleared = more.Cleared(Rect(1024, 1024, 0, 0, 100, 100));
        Assert.IsFalse(cleared.Tiles.Contains(new TileKey(0, 0)), "a tile cleared to nothing is not held");
        Assert.AreEqual(1, cleared.Tiles.Count());
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public void AFill_LaysTheColourOver_AndWithItsTransparencyLocked_RecoloursOnlyWhatIsPainted()
    {
        var half = TiledPixels.Empty.Filled(Rect(20, 20, 0, 0, 10, 20), SKColors.Blue);
        var filled = half.Filled(All(20, 20), SKColors.Red, keepAlpha: true);
        using var bitmap = filled.ToBitmap(new SKRectI(0, 0, 20, 20));
        Assert.AreEqual(SKColors.Red, bitmap.GetPixel(5, 5), "what was painted takes the colour");
        Assert.AreEqual(0, bitmap.GetPixel(15, 5).Alpha, "what was clear stays clear");

        var over = half.Filled(Rect(20, 20, 0, 0, 20, 20), SKColors.Red.WithAlpha(128));
        using var mixed = over.ToBitmap(new SKRectI(0, 0, 20, 20));
        Assert.AreEqual(255, mixed.GetPixel(5, 5).Alpha);
        Assert.AreEqual(128, mixed.GetPixel(15, 5).Alpha, 1);
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public void KeptWhere_KeepsOnlyWhatIsSelected_AndNothingPastTheCanvas()
    {
        var pixels = TiledPixels.FromBitmap(BitmapOf(40, 40, SKColors.Green), -10, -10);
        var kept = pixels.KeptWhere(Rect(20, 20, 0, 0, 10, 20));
        Assert.AreEqual(new SKRectI(0, 0, 10, 20), kept.Bounds);
        Assert.AreEqual(0, kept.Alpha(20, 20)[15, 5]);
        Assert.AreEqual(255, kept.Alpha(20, 20)[5, 5]);
    }

    private static SKBitmap BitmapOf(int width, int height, SKColor colour)
    {
        var bitmap = new SKBitmap(width, height);
        bitmap.Erase(colour);
        return bitmap;
    }

    // ── The tree ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-layers")]
    public void Layers_AreInserted_Removed_AndMoved_AsTheTreeSays()
    {
        var bottom = Solid(8, 8, SKColors.Red, "bottom");
        var top = Solid(8, 8, SKColors.Blue, "top");
        var group = new GroupLayer { Name = "group" };
        var picture = Picture(8, 8, bottom, group, top) with { ActiveId = top.Id };

        var added = picture.Inserted(new PixelLayer { Name = "new" }, bottom.Id);
        CollectionAssert.AreEqual(new[] { "bottom", "new", "group", "top" }, added.Layers.Select(l => l.Name).ToArray());
        Assert.AreEqual("new", added.Active!.Name, "the layer added is active");

        var moved = picture.Moved(top.Id, group.Id, 0);
        Assert.AreEqual(group.Id, moved.Locate(top.Id)!.Value.Parent!.Id, "moved into the group");
        Assert.AreSame(moved, moved.Moved(group.Id, group.Id, 0), "a group is never moved into itself");

        var removed = picture.Without(top.Id);
        Assert.AreEqual(group.Id, removed.ActiveId, "the layer beneath becomes active");
        Assert.IsNull(removed.Find(top.Id));

        var renamed = picture.WithLayer(bottom.Id, l => l with { Name = "renamed" });
        Assert.AreSame(picture.Layers[2], renamed.Layers[2], "what is not changed is shared");
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public void AStep_CostsTheTilesItHoldsThatTheNextDoesNot()
    {
        var layer = Solid(512, 512, SKColors.Red);
        var before = Picture(512, 512, layer);
        var after = before.WithLayer(layer.Id, l => ((PixelLayer)l) with { Pixels = ((PixelLayer)l).Pixels.Cleared(Rect(512, 512, 0, 0, 10, 10)) });
        Assert.AreEqual(256L * 256 * 4, before.BytesNotIn(after), "the one tile the clear replaced");
        Assert.AreEqual(0L, before.BytesNotIn(before));
    }

    // ── Compositing ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-layers")]
    public void EveryBlend_CompositesAsSkiaDrawsIt()
    {
        var under = new SKColor(200, 120, 40);
        var over = new SKColor(60, 180, 220, 200);
        foreach (var (blend, skia) in LayerBlend.Offered)
        {
            var actual = Composite(Picture(4, 4, Solid(4, 4, under), Solid(4, 4, over, blend: blend)), 1, 1);
            using var surface = SKSurface.Create(new SKImageInfo(1, 1, SKImageInfo.PlatformColorType, SKAlphaType.Premul))!;
            surface.Canvas.Clear(under);
            using (var paint = new SKPaint { Color = over, BlendMode = skia }) surface.Canvas.DrawPaint(paint);
            using var reference = surface.Snapshot();
            Near(Pixel(reference, 0, 0), actual, blend.Op);
        }
        Assert.AreEqual(SKBlendMode.Exclusion, new LayerBlend("krita:exclusion").ToSkia(), "another application's blend is drawn by the Skia blend of its name");
        Assert.AreEqual(SKBlendMode.SrcOver, new LayerBlend("krita:dissolve").ToSkia(), "one Skia has none of is drawn as Normal");
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public void AnIsolatedGroup_BlendsWithinItself_APassThroughOne_WithWhatLiesBeneath()
    {
        var grey = Solid(4, 4, new SKColor(128, 128, 128));
        var red = Solid(4, 4, SKColors.Red, blend: new LayerBlend("svg:multiply"));
        Near(new SKColor(255, 0, 0), Composite(Picture(4, 4, grey, new GroupLayer { Children = [red], Isolated = true }), 1, 1),
             "isolated, the multiply has nothing beneath it in the group");
        Near(new SKColor(128, 0, 0), Composite(Picture(4, 4, grey, new GroupLayer { Children = [red], Isolated = false }), 1, 1),
             "passed through, it multiplies what lies beneath the group");
        var faded = Composite(Picture(4, 4, Solid(4, 4, SKColors.White), new GroupLayer { Children = [Solid(4, 4, SKColors.Black)], Isolated = false, Opacity = 0.5 }), 1, 1);
        Near(new SKColor(128, 128, 128), faded, "a pass-through group's opacity is multiplied into what it holds");
    }

    [TestMethod]
    [CoversNode("images-layer-masks")]
    public void AMask_ShowsItsLayerWhereWhite_UnlessItIsDisabled_OnAGroupToo()
    {
        var blue = Solid(10, 10, SKColors.Blue);
        var mask = new LayerMask(Rect(10, 10, 0, 0, 5, 10));
        var red = Solid(10, 10, SKColors.Red) with { Mask = mask };
        Near(SKColors.Red, Composite(Picture(10, 10, blue, red), 2, 5), "white shows the layer");
        Near(SKColors.Blue, Composite(Picture(10, 10, blue, red), 8, 5), "black hides it");
        Near(SKColors.Red, Composite(Picture(10, 10, blue, red with { Mask = mask with { Enabled = false } }), 8, 5), "a disabled mask hides nothing");

        var grouped = new GroupLayer { Children = [Solid(10, 10, SKColors.Red)], Mask = mask, Isolated = false };
        Near(SKColors.Blue, Composite(Picture(10, 10, blue, grouped), 8, 5), "a group's mask hides all it holds, even passed through");
        Near(SKColors.Red, Composite(Picture(10, 10, blue, grouped), 2, 5), "and shows it where white");
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public void ALayerClippedToThoseBeneath_ShowsOnlyWhereTheyAre_ItsBlendGoverningTheColour()
    {
        var square = new PixelLayer { Pixels = TiledPixels.Empty.Filled(Rect(10, 10, 3, 3, 7, 7), SKColors.White) };
        var red = Solid(10, 10, SKColors.Red) with { ClipsToBelow = true };
        Near(SKColors.Red, Composite(Picture(10, 10, square, red), 5, 5), "inside what lies beneath");
        Assert.AreEqual(0, Composite(Picture(10, 10, square, red), 1, 1).Alpha, "outside it, nothing");
        var grey = new PixelLayer { Pixels = TiledPixels.Empty.Filled(Rect(10, 10, 3, 3, 7, 7), new SKColor(128, 128, 128)) };
        Near(new SKColor(128, 0, 0), Composite(Picture(10, 10, grey, red with { Blend = new LayerBlend("svg:multiply") }), 5, 5),
             "the clip keeps the coverage, the blend the colour");
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public void TheBackground_ShowsBeneathEveryLayer_AndAnUndrawableLayerIsLeftOut()
    {
        var picture = Picture(4, 4, new UndrawableLayer { Kind = "applications:krita:levels" }) with { Background = LayerBackground.White };
        Near(SKColors.White, Composite(picture, 1, 1), "the background, and nothing from the layer no one can draw");
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public void ADrawingAgainOfWhatChanged_EqualsADrawingOfItAll()
    {
        var layer = Solid(700, 500, new SKColor(30, 90, 160));
        var first = Picture(700, 500, Solid(700, 500, SKColors.White), layer);
        var second = first.WithLayer(layer.Id, l => ((PixelLayer)l) with { Pixels = ((PixelLayer)l).Pixels.Filled(Rect(700, 500, 300, 200, 340, 260), SKColors.Orange) });

        var dirty = LayerCompositor.DirtyBetween(first, second, 0.5, new SKSizeI(350, 250));
        Assert.IsNotNull(dirty, "only pixels changed");
        Assert.IsTrue(dirty.Value.Width < 350 / 2, "only the tiles the fill touched are drawn again");

        using var kept = new LayerCompositor(Painter);
        kept.Render(first, 0.5, new NoPictures());
        using var again = SKBitmap.FromImage(kept.Render(second, 0.5, new NoPictures()));
        using var whole = new LayerCompositor(Painter, cache: false);
        using var image = whole.Render(second, 0.5, new NoPictures());
        using var reference = SKBitmap.FromImage(image);
        for (int y = 0; y < 250; y += 7)
            for (int x = 0; x < 350; x += 7)
                Assert.AreEqual(reference.GetPixel(x, y), again.GetPixel(x, y), $"({x}, {y})");
    }

    [TestMethod]
    [CoversNode("images-layer-masks")]
    public void AMaskShownAlone_IsWhiteWhereItShows_BlackWhereItHides()
    {
        var layer = Solid(10, 10, SKColors.Red) with { Mask = new LayerMask(Rect(10, 10, 0, 0, 5, 10)) };
        using var compositor = new LayerCompositor(Painter, cache: false);
        using var image = compositor.Render(Picture(10, 10, layer), 1, new NoPictures(), new CompositeOptions(MaskAlone: layer.Id));
        Near(SKColors.White, Pixel(image, 2, 5), "shown");
        Near(SKColors.Black, Pixel(image, 8, 5), "hidden");
    }
}
