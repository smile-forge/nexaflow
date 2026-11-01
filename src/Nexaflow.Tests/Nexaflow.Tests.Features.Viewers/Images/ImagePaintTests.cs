using System.IO;
using System.Windows.Media;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Images;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Erasing;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Painting;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Restoring;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Features.Images.Services.Printing;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Theming;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// Painting, moving, transforming and printing: the round brush's dabs building to the stroke's opacity and no further, only
/// inside the selection; the eraser, a locked transparency and a mask painted in greys; the fill's reach; a stroke as one undo
/// step and the eyedropper's pick in the editor; a layer moved, what is selected lifted and dropped back, a transform flipping and
/// turning pixels and placing a picture or markup layer afresh; the Markup tool on a markup layer; a placement kept in .ora; and a
/// picture laid on paper.
/// </summary>
[TestClass]
public class ImagePaintTests
{
    private static readonly SKColor Red = new(220, 30, 30);

    private string _dir = "";
    private ViewerUiThread _ui = null!;
    private ImageEditingServices _services = null!;

    [TestInitialize]
    public void Make()
    {
        _dir = TestTemp.CreateSubdirectory("nexaflow-paint-").FullName;
        _ui = new ViewerUiThread();
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new ZipArchiveHandler());
        _services = new ImageEditingServices(vfs, new ImageRecipeStore(TimeSpan.FromHours(1)),
                                             new RestoredPictures(Path.Combine(_dir, "restored"), RestoredPictures.DefaultBudget),
                                             new CutOutMasks(Path.Combine(_dir, "cutouts"), CutOutMasks.DefaultBudget),
                                             new ErasedPictures(Path.Combine(_dir, "erased"), ErasedPictures.DefaultBudget));
    }

    [TestCleanup]
    public void Clean()
    {
        _ui.Dispose();
        _services.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static SKColor PixelOf(TiledPixels pixels, int x, int y)
    {
        var rgba = pixels.Read(new SKRectI(x, y, x + 1, y + 1));
        return new SKColor(rgba[0], rgba[1], rgba[2], rgba[3]);
    }

    private static TiledPixels Solid(int width, int height, SKColor colour, SKRectI where)
        => TiledPixels.Empty.Filled(TiledCoverage.FromBytes(Square(width, height, where), width, height), colour);

    private static byte[] Square(int width, int height, SKRectI where)
    {
        var bytes = new byte[width * height];
        for (int y = where.Top; y < where.Bottom; y++)
            for (int x = where.Left; x < where.Right; x++) bytes[y * width + x] = 255;
        return bytes;
    }

    // ── The engine ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-paint")]
    public void AStroke_BuildsToItsOpacity_AndNoFurther_OnlyInsideTheSelection()
    {
        var brush = new BrushSettings { Size = 10, Hardness = 1, Opacity = 0.5, Flow = 0.5, Spacing = 0.1, PressureSizes = false };
        var within = TiledCoverage.FromBytes(Square(60, 20, new SKRectI(0, 0, 30, 20)), 60, 20);
        var stroke = new PaintStroke(brush, PaintKind.Brush, 60, 20, within);
        var area = stroke.To(new SKPoint(5, 10), 1);
        for (int pass = 0; pass < 6; pass++)
        {
            area = SKRectI.Union(area, stroke.To(new SKPoint(55, 10), 1));
            area = SKRectI.Union(area, stroke.To(new SKPoint(5, 10), 1));
        }
        var drawn = stroke.Drawn(TiledPixels.Empty, TiledPixels.Empty, area, Red, alphaLocked: false, SKColors.Transparent);

        Assert.AreEqual(128, PixelOf(drawn, 15, 10).Alpha, 2, "passed over again and again, a stroke covers its opacity and no more");
        Assert.AreEqual(0, PixelOf(drawn, 45, 10).Alpha, "outside the selection, nothing");
        Assert.AreEqual(0, PixelOf(drawn, 15, 2).Alpha, "beyond the tip, nothing");
    }

    [TestMethod]
    [CoversNode("images-paint")]
    public void TheEraser_TakesAway_ALockedTransparencyIsKept_AndAMaskIsPaintedInGreys()
    {
        var full = new SKRectI(0, 0, 20, 20);
        var before = Solid(20, 20, Red, full);
        var brush = new BrushSettings { Size = 8, Hardness = 1, Opacity = 1, Flow = 1, PressureSizes = false };

        var eraser = new PaintStroke(brush, PaintKind.Eraser, 20, 20, null);
        var erased = eraser.Drawn(before, before, eraser.To(new SKPoint(10, 10), 1), Red, alphaLocked: false, SKColors.Transparent);
        Assert.AreEqual(0, PixelOf(erased, 10, 10).Alpha, "erased where it passed");
        Assert.AreEqual(255, PixelOf(erased, 1, 1).Alpha, "kept elsewhere");

        var half = Solid(20, 20, Red, new SKRectI(0, 0, 10, 20));
        var blue = new SKColor(20, 40, 230);
        var painter = new PaintStroke(brush with { Size = 20 }, PaintKind.Brush, 20, 20, null);
        var recoloured = painter.Drawn(half, half, painter.To(new SKPoint(10, 10), 1), blue, alphaLocked: true, SKColors.Transparent);
        AssertNear(blue, PixelOf(recoloured, 5, 10), because: "a locked layer is recoloured where it is painted");
        Assert.AreEqual(0, PixelOf(recoloured, 15, 10).Alpha, "and stays clear where it was clear");

        var mask = TiledCoverage.Covered(20, 20);
        var masking = new PaintStroke(brush, PaintKind.Brush, 20, 20, null);
        var greyed = masking.Drawn(mask, mask, masking.To(new SKPoint(10, 10), 1), grey: 0);
        Assert.AreEqual(0, greyed.Read(new SKRectI(10, 10, 11, 11))[0], "painted black, the mask hides there");
        var unmasking = new PaintStroke(brush, PaintKind.Eraser, 20, 20, null);
        var shown = unmasking.Drawn(greyed, greyed, unmasking.To(new SKPoint(10, 10), 1), grey: 0);
        Assert.AreEqual(255, shown.Read(new SKRectI(10, 10, 11, 11))[0], "the eraser shows the layer again");
    }

    [TestMethod]
    [CoversNode("images-paint")]
    public void TheFill_ReachesWhatIsNearTheColourClicked_JoinedToIt_OrAnywhere()
    {
        // Two red squares apart on clear pixels.
        var pixels = Solid(40, 20, Red, new SKRectI(0, 0, 10, 10));
        pixels = pixels.MergedWith(Solid(40, 20, new SKColor(225, 35, 30), new SKRectI(30, 0, 40, 10)));

        var joined = FloodFill.Reach(pixels, 40, 20, 2, 2, tolerance: 16, contiguous: true);
        Assert.AreEqual(255, joined.Read(new SKRectI(9, 9, 10, 10))[0]);
        Assert.AreEqual(0, joined.Read(new SKRectI(35, 5, 36, 6))[0], "contiguous: the square apart is not reached");
        Assert.AreEqual(0, joined.Read(new SKRectI(15, 15, 16, 16))[0], "nor the clear pixels round it");

        var anywhere = FloodFill.Reach(pixels, 40, 20, 2, 2, tolerance: 16, contiguous: false);
        Assert.AreEqual(255, anywhere.Read(new SKRectI(35, 5, 36, 6))[0], "anywhere: a near colour apart is reached");

        var tight = FloodFill.Reach(pixels, 40, 20, 2, 2, tolerance: 0, contiguous: false);
        Assert.AreEqual(0, tight.Read(new SKRectI(35, 5, 36, 6))[0], "with no tolerance, only the colour itself");
    }

    // ── In the editor ───────────────────────────────────────────────────────

    private ArtistViewModel Canvas(LayeredPicture picture, string name = "canvas.ora")
    {
        var path = Path.Combine(_dir, name);
        _services.WriteLayeredAsync(picture, path).GetAwaiter().GetResult();
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => _ui.Run(call.Arg<Action>()));
        var vm = _ui.Invoke(() => new ArtistViewModel(path, shell, _services, config: new ImagesConfig()));
        Until(() => vm.CurrentDocument is not null);
        Assert.IsNotNull(_ui.Invoke(() => vm.CurrentDocument), "the canvas was never read as a layered picture");
        OnUi(() => vm.Editor.InkColour = ColorSpec.Custom(Color.FromRgb(Red.Red, Red.Green, Red.Blue)));
        return vm;
    }

    private LayeredPicture Picture(ArtistViewModel vm) => _ui.Invoke(() => vm.CurrentDocument!.Picture);

    private void OnUi(Action action) => _ui.Run(action).GetAwaiter().GetResult();

    private void Until(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10) && !_ui.Invoke(done)) Thread.Sleep(10);
    }

    [TestMethod]
    [CoversNode("images-paint")]
    public void AStrokeInTheEditor_IsOneUndoStep_AndTheEyedropperPicksTheLayersColour()
    {
        var vm = Canvas(LayeredPicture.Blank(40, 30, LayerBackground.Transparent, "Layer 1"));
        OnUi(() =>
        {
            vm.Editor.Tool = EditTool.Paint;
            vm.Painting.Size = 6;
            vm.Painting.PressureSizes = false;
            Assert.IsTrue(vm.Painting.BeginStroke(new SKPoint(5, 15), 1));
            vm.Painting.ContinueStroke(new SKPoint(20, 15), 1);
            vm.Painting.ContinueStroke(new SKPoint(35, 15), 1);
            vm.Painting.EndStroke();
        });
        var pixels = ((PixelLayer)Picture(vm).Active!).Pixels;
        AssertNear(Red, PixelOf(pixels, 20, 15), because: "the stroke is painted along its way");

        OnUi(() =>
        {
            vm.Editor.InkColour = ColorSpec.Custom(Colors.White);
            vm.Painting.SampleMerged = false;
            vm.Painting.PickAt(new SKPoint(20, 15));
        });
        var picked = _ui.Invoke(() => vm.Editor.InkColour.Color);
        AssertNear(Red, new SKColor(picked.R, picked.G, picked.B), because: "picked from the active layer");

        OnUi(() => vm.UndoEditCommand.Execute(null));
        Assert.IsTrue(((PixelLayer)Picture(vm).Active!).Pixels.IsEmpty, "one undo takes the whole stroke back");
    }

    [TestMethod]
    [CoversNode("images-transform")]
    public void WhatIsSelected_IsLiftedAndMoved_ThenDroppedBack_AndOneUndoPutsItWhereItWas()
    {
        var blank = LayeredPicture.Blank(40, 30, LayerBackground.Transparent, "Layer 1");
        var vm = Canvas(blank with { Layers = [((PixelLayer)blank.Layers[0]) with { Pixels = Solid(40, 30, Red, new SKRectI(2, 2, 8, 8)) }] });
        OnUi(() => vm.Editor.RegionMarks.Add(new RegionRectangle(0, 0, 10, 10) { Mode = RegionMode.Replace }));
        OnUi(() =>
        {
            vm.Editor.Tool = EditTool.Move;
            Assert.IsTrue(vm.Arranging.BeginMove(new SKPoint(5, 5)));
            vm.Arranging.ContinueMove(new SKPoint(25, 15));
            vm.Arranging.EndMove();
        });
        Assert.IsTrue(_ui.Invoke(() => vm.Arranging.HasFloating), "what is selected floats above its layer");
        Assert.AreEqual(2, Picture(vm).Layers.Length);
        Assert.AreEqual(new SKRectI(20, 10, 30, 20), LayerEdits.CoveredArea(Picture(vm).Selection), "the selection goes with it");

        OnUi(() => vm.Arranging.Drop());
        var pixels = ((PixelLayer)Picture(vm).Layers.Single()).Pixels;
        AssertNear(Red, PixelOf(pixels, 25, 15), because: "dropped where it was moved to");
        Assert.AreEqual(0, PixelOf(pixels, 5, 5).Alpha, "gone from where it was lifted");

        OnUi(() => vm.UndoEditCommand.Execute(null));
        OnUi(() => vm.UndoEditCommand.Execute(null));
        var back = ((PixelLayer)Picture(vm).Layers.Single()).Pixels;
        AssertNear(Red, PixelOf(back, 5, 5), because: "undo takes back the drop, then the lift and the move together");
    }

    [TestMethod]
    [CoversNode("images-transform")]
    public void ATransform_TurnsAndFlipsPixels_AsOneStep_AndCancelledLeavesThemAsTheyWere()
    {
        var blank = LayeredPicture.Blank(40, 40, LayerBackground.Transparent, "Layer 1");
        var bar = Solid(40, 40, Red, new SKRectI(10, 18, 30, 22));
        var vm = Canvas(blank with { Layers = [((PixelLayer)blank.Layers[0]) with { Pixels = bar }] });

        OnUi(() =>
        {
            vm.Editor.Tool = EditTool.Move;
            vm.Arranging.Transform();
            vm.Arranging.RotateRightCommand.Execute(null);
        });
        var turned = ((PixelLayer)Picture(vm).Active!).Pixels.Bounds;
        Assert.IsTrue(turned.Width < 8 && turned.Height > 16, $"a quarter turn stands the bar up ({turned})");
        OnUi(() => vm.Arranging.Cancel());
        Assert.AreEqual(bar.Bounds, ((PixelLayer)Picture(vm).Active!).Pixels.Bounds, "cancelled, it lies as it was");
        Assert.IsFalse(_ui.Invoke(() => vm.UndoEditCommand.CanExecute(null)), "and nothing was added to undo");

        OnUi(() =>
        {
            vm.Arranging.Transform();
            vm.Arranging.FlipVerticalCommand.Execute(null);
            vm.Arranging.Apply();
        });
        Assert.AreEqual(bar.Bounds, ((PixelLayer)Picture(vm).Active!).Pixels.Bounds, "a bar flipped about its own middle lies where it was");
        Assert.IsTrue(_ui.Invoke(() => vm.UndoEditCommand.CanExecute(null)), "applied, it is one step");
    }

    [TestMethod]
    [CoversNode("images-transform")]
    public void AMarkupLayer_TakesNotesFromTheMarkupTool_AndATransformPlacesThemAfresh()
    {
        var notes = new MarkupLayer { Name = "Notes" };
        var blank = LayeredPicture.Blank(60, 40, LayerBackground.Transparent, "Layer 1");
        var vm = Canvas(blank with { Layers = [.. blank.Layers, notes], ActiveId = notes.Id });

        OnUi(() =>
        {
            vm.Editor.Tool = EditTool.Markup;
            vm.Editor.MarkupTool = MarkupTool.Arrow;
            vm.Editor.MarkupPress(new SKPoint(10, 10), 2);
            vm.Editor.MarkupDrag(new SKPoint(40, 30), 1);
            vm.Editor.MarkupRelease();
        });
        var layer = (MarkupLayer)Picture(vm).Active!;
        var arrow = (MarkupArrow)layer.Elements.Single();
        Assert.AreEqual((10.0, 10.0, 40.0, 30.0), (arrow.From.X, arrow.From.Y, arrow.To.X, arrow.To.Y), "drawn on the markup layer itself");

        OnUi(() =>
        {
            vm.Editor.Tool = EditTool.Move;
            vm.Arranging.Transform();
            vm.Arranging.FlipHorizontalCommand.Execute(null);
            vm.Arranging.Apply();
        });
        var flipped = (MarkupLayer)Picture(vm).Active!;
        Assert.AreEqual(arrow, flipped.Elements.Single(), "the notes are kept as drawn");
        Assert.IsTrue(flipped.Placement.ScaleX < 0, "the layer's placement takes the flip");
    }

    [TestMethod]
    [CoversNode("images-ora")]
    public async Task APlacement_FlipsAndAll_IsKeptInOpenRaster()
    {
        var placement = LayerPlacement.FromMatrix(SKMatrix.CreateScale(-1, 2).PostConcat(SKMatrix.CreateRotationDegrees(20)).PostConcat(SKMatrix.CreateTranslation(7, 3)));
        var notes = new MarkupLayer { Name = "Notes", Placement = placement, Elements = [new MarkupArrow { From = new(1, 1), To = new(9, 5) }] };
        var blank = LayeredPicture.Blank(30, 20, LayerBackground.Transparent, "Layer 1");
        var path = Path.Combine(_dir, "placed.ora");
        await _services.WriteLayeredAsync(blank with { Layers = [.. blank.Layers, notes] }, path);

        var read = await (await _services.LayeredFormatOfAsync(path))!.ReadAsync(path);
        var kept = read.Picture.Layers.OfType<MarkupLayer>().Single();
        Assert.AreEqual(placement, kept.Placement);
    }

    // ── Print ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-print")]
    public void APicture_IsLaidOnPaper_FittedFilledAtActualSizeOrAtAWidth()
    {
        var page = new SKSize(800, 1100);   // device-independent pixels
        var picture = new SKSizeI(4000, 3000);
        var fit = PicturePrintLayout.Tiles(picture, 300, new PrintOptions(PrintSize.Fit, 0, PrintOrientation.Auto, 0, Centre: true), page).Single();
        Assert.AreEqual(new SKRectI(0, 0, 4000, 3000), fit.Source);
        Assert.AreEqual((800f, 600f), (fit.Page.Width, fit.Page.Height), "as large as fits, whole");
        Assert.AreEqual(250, fit.Page.Top, 0.5, "centred down the page");

        var fill = PicturePrintLayout.Tiles(picture, 300, new PrintOptions(PrintSize.Fill, 0, PrintOrientation.Auto, 0, Centre: true), page).Single();
        Assert.AreEqual((800f, 1100f), (fill.Page.Width, fill.Page.Height), "the page filled");
        Assert.IsTrue(fill.Source.Width < 4000 && fill.Source.Height == 3000, "what overflows cropped away");

        // 4000 px at 100 dpi is 40 in: 3840 device-independent pixels wide, across five pages of 800.
        var actual = PicturePrintLayout.Tiles(picture, 100, new PrintOptions(PrintSize.Actual, 0, PrintOrientation.Auto, 0, Centre: true), page);
        Assert.AreEqual(5 * 3, actual.Count, "at actual size it runs across pages");
        Assert.AreEqual(0.96, PicturePrintLayout.Scale(actual), 1e-6, "a pixel at 100 dpi covers 0.96 device-independent pixels");

        var width = PicturePrintLayout.Tiles(picture, 100, new PrintOptions(PrintSize.Width, 100, PrintOrientation.Auto, 10, Centre: false), page).Single();
        Assert.AreEqual(100 / 25.4 * 96, width.Page.Width, 0.5, "at the width set");
        Assert.AreEqual(10 / 25.4 * 96, width.Page.Left, 0.5, "inside the margin, not centred");

        Assert.IsTrue(PicturePrintLayout.Landscape(picture, new PrintOptions(PrintSize.Fit, 0, PrintOrientation.Auto, 0, true)), "a wide picture turns the page");
        Assert.IsFalse(PicturePrintLayout.Landscape(picture, new PrintOptions(PrintSize.Fit, 0, PrintOrientation.Portrait, 0, true)), "unless portrait is chosen");
    }
}
