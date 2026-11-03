using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Erasing;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Restoring;
using Nexaflow.Features.Images.Editing.SceneDepth;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Features.Images.Services;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The picture's scene depth in Artist: estimated once for a photo and kept by its content, so a focus moved or an aperture
/// dragged redraws the picture and never runs the model again; a lens blur sharp at its focus and blurred by distance from it,
/// haze thickening with distance, a selection by distance kept as its span, the depth as a layer or a mask, and the assistant's
/// tools, which name things by phrase and answer in near, middle and far.
/// <para>
/// Scene depth here is a stand-in whose depth runs from the nearest at the left edge to the farthest at the right
/// (<see cref="Ramp"/>); picture selection one that finds a fixed patch near the left (<see cref="Patch"/>). The photo is a fine
/// checkerboard, so blur shows as lost contrast.
/// </para>
/// </summary>
[TestClass]
public class ImageSceneDepthTests
{
    private const int Wide = 240, High = 160, Square = 2;

    private string _dir = "";
    private string _photo = "";
    private ViewerUiThread _ui = null!;
    private Ramp _depth = null!;
    private ImageEditingServices _services = null!;

    [TestInitialize]
    public void Make()
    {
        _dir = TestTemp.CreateSubdirectory("nexaflow-scene-depth-").FullName;
        _photo = Path.Combine(_dir, "checks.png");
        using (var bitmap = new SKBitmap(Wide, High))
        {
            for (int y = 0; y < High; y++)
                for (int x = 0; x < Wide; x++)
                    bitmap.SetPixel(x, y, (x / Square + y / Square) % 2 == 0 ? SKColors.White : SKColors.Black);
            File.WriteAllBytes(_photo, EncodePng(bitmap));
        }
        _ui = new ViewerUiThread();
        _depth = new Ramp();
        _services = new ImageEditingServices(VirtualFileSystem.Instance, new ImageRecipeStore(TimeSpan.FromHours(1)),
                                             new RestoredPictures(Path.Combine(_dir, "restored"), RestoredPictures.DefaultBudget),
                                             new CutOutMasks(Path.Combine(_dir, "cutouts"), CutOutMasks.DefaultBudget),
                                             new ErasedPictures(Path.Combine(_dir, "erased"), ErasedPictures.DefaultBudget),
                                             _depth, new SceneDepthMaps(Path.Combine(_dir, "depth"), SceneDepthMaps.DefaultBudget));
    }

    [TestCleanup]
    public void Clean()
    {
        _ui.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => _ui.Run(call.Arg<Action>()));
        return shell;
    }

    private ArtistViewModel Editing(IPictureSelection? selection = null)
    {
        var vm = new ArtistViewModel(_photo, Shell(), _services, selection: selection);
        Until(() => vm.Canvas is not null);
        Assert.IsNotNull(vm.Canvas, "the photo was never decoded and presented");
        OnUi(() => vm.Editor.Tool = EditTool.Adjust);
        return vm;
    }

    private void OnUi(Action action) => _ui.Run(action).GetAwaiter().GetResult();

    private void Until(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(15) && !_ui.Invoke(done)) Thread.Sleep(10);
    }

    /// <summary>How much a column's neighbouring pixels differ, on average, in a band across the middle: about 127 for a sharp
    /// checkerboard of squares two pixels wide, near 0 blurred.</summary>
    private static double Contrast(SKImage picture, int column)
    {
        using var bitmap = SKBitmap.FromImage(picture);
        double sum = 0;
        int n = 0;
        for (int y = picture.Height / 3; y < picture.Height * 2 / 3; y++)
            for (int x = column; x < column + 8; x++)
            {
                sum += Math.Abs(bitmap.GetPixel(x, y).Red - bitmap.GetPixel(x + 1, y).Red);
                n++;
            }
        return sum / n;
    }

    // ── The depth plane ───────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-scene-depth")]
    public void TheDisparity_SpreadOverNearness_NearestOneFarthestZero_AndKeptWhole()
    {
        var plane = SceneDepthPlane.FromDisparity([2f, 4f, 6f, 10f], 2, 2);
        CollectionAssert.AreEqual(new[] { 0f, 0.25f, 0.5f, 1f }, plane.Nearness.ToArray());
        Assert.AreEqual(0.2, plane.FarthestToNearest, 1e-6, "the farthest's disparity over the nearest's is kept");
        Assert.AreEqual(0f, plane.Distance(1f), 1e-6f, "the nearest part is at distance 0");
        Assert.AreEqual((float)(Math.Log(5) / Math.Log(SceneDepthPlane.DistanceRange)), plane.Distance(0f), 1e-6f,
                        "the farthest, five times as far, at the log of five on a scale of a thousand");
        Assert.AreEqual(1f, new SceneDepthPlane([0f, 1f], 2, 1).Distance(0f), "a sky, endlessly far, is as far as distance goes");

        using var kept = new MemoryStream();
        plane.Write(kept);
        var read = SceneDepthPlane.Read(kept.ToArray());
        Assert.AreEqual(2, read.Width);
        for (int i = 0; i < 4; i++) Assert.AreEqual(plane.Nearness[i], read.Nearness[i], 1e-4f);
        Assert.AreEqual(plane.FarthestToNearest, read.FarthestToNearest, "and read back");
        Assert.ThrowsExactly<InvalidDataException>(() => SceneDepthPlane.Read(kept.ToArray().AsSpan(0, 20)));
    }

    [TestMethod]
    [CoversNode("images-scene-depth")]
    public void ThePlane_LiesOnThePhotoCornerToCorner_AsTheMakersBringItToThePicture()
    {
        var plane = new SceneDepthPlane([0f, 1f, 0f, 1f], 2, 2);
        var photo = new SKSizeI(11, 5);
        Assert.AreEqual(0f, plane.At(0.5, 2.5, photo), 1e-6f, "the photo's first column is the plane's first");
        Assert.AreEqual(1f, plane.At(10.5, 2.5, photo), 1e-6f, "its last, the plane's last");
        Assert.AreEqual(0.5f, plane.At(5.5, 2.5, photo), 1e-6f, "its middle, between them");
        var full = plane.AtSize(photo);
        Assert.AreEqual(0.1f, full[1], 1e-6f, "at the photo's size every pixel is between, as PyTorch's corner-aligned resize puts it");

        var canvas = plane.OnCanvas(SKMatrix.CreateScale(2, 2), photo, new SKSizeI(22, 10));
        Assert.AreEqual(plane.At(5.5, 2.5, photo), canvas[5 * 22 + 11], 0.06f, "drawn twice the size, the canvas reads the photo's depth beneath each pixel");
    }

    // ── The passes ────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-scene-depth-lens-blur")]
    public void ALensBlur_KeepsWhatIsInFocusAsItWas_AndAFlatPictureFlat()
    {
        int w = 40, h = 30;
        var rgba = new float[w * h * 4];
        var nearness = new float[w * h];
        for (int i = 0; i < w * h; i++)
        {
            (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = (0.3f, 0.5f, 0.7f, 1f);
            nearness[i] = (i % w) / (float)(w - 1);
        }
        var blurred = (float[])rgba.Clone();
        LensBlur.Apply(blurred, nearness, w, h, sharpFrom: 1f, sharpTo: 1f, maxRadius: 8, BokehShape.Circle);
        for (int i = 0; i < w * h * 4; i++) Assert.AreEqual(rgba[i], blurred[i], 1e-4f, "blurring a flat picture leaves it flat: nothing lost at the layers' seams");

        rgba[(15 * w + 39) * 4] = 1f;   // a bright pixel in focus, at the near edge
        blurred = (float[])rgba.Clone();
        LensBlur.Apply(blurred, nearness, w, h, sharpFrom: 0.98f, sharpTo: 1f, maxRadius: 8, BokehShape.Hexagon);
        Assert.AreEqual(1f, blurred[(15 * w + 39) * 4], 1e-3f, "what is in focus stays sharp");
    }

    [TestMethod]
    [CoversNode("images-scene-depth-lens-blur")]
    public void ALensBlur_GrowsWithDistanceFromTheFocus_NotInTwoLevels()
    {

        int w = 120, h = 40;
        var rgba = new float[w * h * 4];
        var nearness = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float v = (x / 2 + y / 2) % 2 == 0 ? 1f : 0f;
                (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = (v, v, v, 1f);
                nearness[i] = 1 - x / (float)(w - 1);
            }
        var blurred = rgba;
        LensBlur.Apply(blurred, nearness, w, h, sharpFrom: 1f, sharpTo: 1f, maxRadius: 10, BokehShape.Circle);
        double Swing(int column)
        {
            double sum = 0;
            for (int y = 10; y < 30; y++) sum += Math.Abs(blurred[(y * w + column) * 4] - blurred[(y * w + column + 1) * 4]);
            return sum / 20;
        }
        double near = Swing(2), middle = Swing(40), far = Swing(100);
        Assert.IsTrue(near > middle && middle > far, $"contrast falls with distance from the focus: {near:0.00}, {middle:0.00}, {far:0.00}");
        Assert.IsTrue(middle > far * 1.5 || middle < near * 0.75, "the middle distance is blurred between the two, not as either");
    }

    [TestMethod]
    [CoversNode("images-scene-depth-lens-blur")]
    public void TheBand_IsToldInDistance_AndEverythingInItIsKeptSharp()
    {
        var plane = SceneDepthPlane.FromDisparity([10f, 5f, 1f, 0.1f, 0.01f], 5, 1);
        foreach (var distance in new[] { 0.0, 0.1, 0.25, 0.5, 0.75, 0.99 })
            Assert.AreEqual(distance, plane.Distance(plane.NearnessAt(distance)), 1e-4, "a distance turned to nearness and back is itself");
        Assert.AreEqual(1f, plane.NearnessAt(0), "the nearest part is the nearest");

        // A band across the middle of a ramp: everything in it, at either end, stays as it was.
        int w = 60, h = 20;
        var nearness = new float[w * h];
        var rgba = new float[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                nearness[i] = 1 - x / (float)(w - 1);
                float v = (x + y) % 2;
                rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = v;
                rgba[i * 4 + 3] = 1;
            }
        var blurred = (float[])rgba.Clone();
        LensBlur.Apply(blurred, nearness, w, h, sharpFrom: 0.4f, sharpTo: 0.6f, maxRadius: 8, BokehShape.Circle);
        double Swing(float[] p, int column) => Enumerable.Range(2, h - 4).Max(y => p[(y * w + column) * 4]) - Enumerable.Range(2, h - 4).Min(y => p[(y * w + column) * 4]);
        foreach (int column in new[] { 27, 30, 33 })
            Assert.AreEqual(1, Swing(blurred, column), 0.05, $"column {column}, in the band, keeps its checks");
        Assert.IsTrue(Swing(blurred, 3) < 0.5 && Swing(blurred, 56) < 0.5, "what is nearer and farther than the band is blurred");
    }

    [TestMethod]
    [CoversNode("images-scene-depth-lens-blur")]
    public async Task FocusingOnARectangleOrAnOutline_KeepsAllOfItSharp()
    {
        var vm = Editing();
        OnUi(() => vm.Depth.LensBlur = true);
        var session = vm.CurrentSession!;
        Until(() => session.SceneDepth is not null);
        var plane = session.SceneDepth!;
        var photo = new SKSizeI(Wide, High);
        double At(double x) => plane.Distance(plane.At(x, 80, photo));

        await _ui.Invoke(() => vm.Depth.FocusOnAsync(new RegionRectangle(100, 40, 160, 120)));
        var blur = session.Recipe.LensBlur!;
        Assert.IsTrue(blur.Near <= At(110) && At(150) <= blur.Far, $"the rectangle's middle is all in the band: {blur.Near:0.000}-{blur.Far:0.000}");
        Assert.IsTrue(At(40) < blur.Near && blur.Far < At(220), "and what lies well outside it is not");

        await _ui.Invoke(() => vm.Depth.FocusOnAsync(new RegionPolygon { Points = [new(20, 20), new(60, 20), new(60, 140), new(20, 140)] }));
        blur = session.Recipe.LensBlur!;
        Assert.IsTrue(blur.Near <= At(30) && At(50) <= blur.Far && blur.Far < At(120), $"an outline focuses on what it holds: {blur.Near:0.000}-{blur.Far:0.000}");
    }

    [TestMethod]
    [CoversNode("images-scene-depth")]
    public void TheStrip_TellsHowMuchOfThePictureLiesAtEachDistance()
    {
        var plane = SceneDepthPlane.FromDisparity([10f, 10f, 10f, 0.01f], 4, 1);
        var shares = plane.Shares();
        Assert.AreEqual(SceneDepthPlane.ShareSteps, shares.Length);
        Assert.AreEqual(1, shares[0], "the fullest step is 1: three of the four parts are the nearest");
        Assert.AreEqual(Math.Sqrt(1.0 / 3), shares[^1], 1e-9, "and the farthest holds a third as much, drawn by its square root to be seen");
        Assert.AreEqual(0, shares[SceneDepthPlane.ShareSteps / 2], "nothing lies between");
    }

    [TestMethod]
    [CoversNode("images-scene-depth-haze")]
    public void Haze_LeavesTheNearClear_AndFadesTheFarIntoItsColour()
    {
        var rgba = new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 0 };
        var distances = new float[] { 0f, 1f, 1f };
        DepthHaze.Apply(rgba, distances, 3, 1, density: 1, start: 0, (0.8f, 0.8f, 0.8f));
        Assert.AreEqual(0f, rgba[0], 1e-6f, "the nearest part is clear");
        Assert.AreEqual(0.8f * (1 - MathF.Exp(-DepthHaze.Thickest)), rgba[4], 1e-4f, "the farthest is nearly the haze");
        Assert.AreEqual(0f, rgba[8], 1e-6f, "haze lies over what is there, never over what is not");

        var colour = DepthHaze.FarthestColour([1, 0, 0, 1, 0, 0, 1, 1], [1f, 0f]);
        Assert.AreEqual((0f, 0f, 1f), colour, "a haze with no colour takes the colour of the picture's distance");
    }

    // ── In Artist ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-scene-depth-lens-blur")]
    public async Task ALensBlur_EstimatesTheDepthOnce_AndMovingTheFocusNeverEstimatesItAgain()
    {
        var vm = Editing();
        OnUi(() => vm.Depth.LensBlur = true);
        var session = vm.CurrentSession!;
        Until(() => session.SceneDepth is not null);
        Assert.IsNotNull(session.SceneDepth, "the depth is estimated and drawn from");
        Assert.AreEqual(1, _depth.Asks, "estimated once");

        OnUi(() => vm.Depth.Aperture = 90);
        OnUi(() => vm.Depth.FocusFar = 4);
        foreach (var x in new[] { 10.0, 120.0, 230.0, 60.0 })
            await _ui.Invoke(() => vm.Depth.FocusAtAsync(new MarkupPoint(x, 80)));
        Until(() => session.SceneDepth is not null);
        var plane = session.SceneDepth!;
        double clicked = plane.Distance(plane.FocusAt(60, 80, new SKSizeI(Wide, High)));
        var blur = session.Recipe.LensBlur!;
        Assert.IsTrue(blur.Near <= clicked && clicked <= blur.Far, $"the last click focuses: {clicked:0.000} in {blur.Near:0.000}-{blur.Far:0.000}");
        Assert.AreEqual(0.04, blur.Far - blur.Near, 1e-9, "a click moves the band, as wide as it was");
        Assert.AreEqual(1, _depth.Asks, "moving the focus and dragging the aperture draw the picture again, never the depth");

        // Another editor on the same photo finds the depth kept, by the photo's content.
        var again = Editing();
        OnUi(() => again.Depth.Haze = true);
        Until(() => again.CurrentSession!.SceneDepth is not null);
        Assert.AreEqual(1, _depth.Asks, "the photo's depth is kept on disk and found again");
    }

    [TestMethod]
    [CoversNode("images-scene-depth-lens-blur")]
    public async Task ALensBlur_IsSharpAtItsFocus_AndBlurredFarFromIt_OnScreenAndSaved()
    {
        var vm = Editing();
        OnUi(() => vm.Depth.LensBlur = true);
        var session = vm.CurrentSession!;
        Until(() => session.SceneDepth is not null);
        await _ui.Invoke(() => vm.Depth.FocusAtAsync(new MarkupPoint(8, 80)));
        OnUi(() => vm.Depth.Aperture = 100);

        // The screen draws from the photo as decoded for it, which may be smaller than the photo: its checks are judged against each other.
        using var shown = _ui.Invoke(() => session.Snapshot(session.Recipe, new RenderRequest(RenderFrame.Output, 1)))!;
        Assert.IsTrue(Contrast(shown, 4) > 3 * Contrast(shown, 220), "on screen, the near edge in focus keeps its checks and the far edge loses them");

        // Saved, against the same photo saved without the blur — the geometry pass's own resampling softens the checks a little.
        var png = Nexaflow.Features.Images.Editing.Codecs.ImageContainer.Png;
        var saved = await _ui.Invoke(() => _services.Exporter.ExportAsync(_photo, session.Recipe, png, made: session.Made));
        var plain = await _ui.Invoke(() => _services.Exporter.ExportAsync(_photo, session.Recipe with { LensBlur = null }, png));
        using var image = SKImage.FromEncodedData(saved);
        using var unblurred = SKImage.FromEncodedData(plain);
        double sharp = Contrast(image, 4), soft = Contrast(image, 220), checks = Contrast(unblurred, 4);
        Assert.IsTrue(sharp > 0.97 * checks, $"the near edge, in focus, keeps its checks ({sharp:0} of {checks:0})");
        Assert.IsTrue(soft < 0.2 * checks, $"the far edge is blurred ({soft:0} of {checks:0})");
    }

    [TestMethod]
    [CoversNode("images-scene-depth-haze")]
    public void Haze_IsARecipeSection_UndoneAsOneStep()
    {
        var vm = Editing();
        OnUi(() => vm.Depth.Haze = true);
        OnUi(() => vm.Depth.HazeDensity = 80);
        var session = vm.CurrentSession!;
        Assert.AreEqual(0.8, session.Recipe.DepthHaze!.Density, 1e-9);
        Assert.IsTrue(session.Recipe.AsksSceneDepth);
        OnUi(() => vm.UndoEditCommand.Execute(null));
        OnUi(() => vm.UndoEditCommand.Execute(null));
        Assert.IsNull(session.Recipe.DepthHaze, "taken back as the steps it was made in");
    }

    [TestMethod]
    [CoversNode("images-selection-distance")]
    public async Task SelectingByDistance_OnAPhoto_IsARegionStep_KeptAsItsSpan_AndMovingTheEndsSelectsAgain()
    {
        var vm = Editing();
        OnUi(() => vm.Editor.Tool = EditTool.Select);
        OnUi(() => vm.Editor.SelectBy = SelectionWay.Distance);
        Assert.IsTrue(_ui.Invoke(() => vm.Editor.SelectsByDistance));
        OnUi(() => { vm.Editor.RegionMarks.DistanceNear = 0; vm.Editor.RegionMarks.DistanceFar = 40; vm.Editor.RegionMarks.DistanceSoftness = 0; });
        await _ui.Invoke(() => vm.Editor.RegionMarks.SelectDistanceAsync(RegionMode.Replace));

        var step = _ui.Invoke(() => vm.Editor.RegionMarks.Region!.Steps.Single()) as RegionSceneDepth;
        Assert.IsNotNull(step);
        Assert.AreEqual(0.4, step.Far, 1e-9);
        var shown = _ui.Invoke(() => vm.Editor.RegionMarks.Shown)!;
        Assert.IsFalse(shown.IsEmpty, "what lies near is selected");

        OnUi(() => vm.Editor.RegionMarks.DistanceFar = 70);
        Until(() => (vm.Editor.RegionMarks.Region?.Steps.Single() as RegionSceneDepth)?.Far == 0.7);
        Assert.AreEqual(1, _ui.Invoke(() => vm.Editor.RegionMarks.Region!.Steps.Length), "moving the end selects again in the span's place, not beside it");

        var span = new RegionSceneDepth(0, 0.4, 0);
        var coverage = RegionSceneDepths.Span(new SceneDepthPlane([1f, 0f], 2, 1), new SKSizeI(10, 1), span.Near, span.Far, span.Softness);
        Assert.AreEqual(255, coverage[0], "the nearest pixel lies in a span from 0");
        Assert.AreEqual(0, coverage[9], "the farthest does not");
    }

    [TestMethod]
    [CoversNode("images-selection-distance")]
    public async Task ACutOutByDistance_NeedsSceneDepth_AndIsMadeFromTheKeptDepth()
    {
        var region = new RecipeRegion { Steps = [new RegionSceneDepth(0, 0.5, 0) { Mode = RegionMode.Replace }] };
        Assert.IsTrue(region.SelectsByDistance);
        var path = await _services.CutOuts.MakeAsync(_services, _photo, new RecipeCutOut { Region = region }, null, null, CancellationToken.None);
        var grey = CutOutMasks.ReadGrey(path, Wide, High);
        Assert.IsTrue(grey[High / 2 * Wide + 5] > 200, "the near left is kept");
        Assert.IsTrue(grey[High / 2 * Wide + Wide - 5] < 50, "the far right is not");
    }

    [TestMethod]
    [CoversNode("images-scene-depth-layers")]
    public async Task TheDepth_AsALayerAboveThePhoto_WhiteNear_BlackFar()
    {
        var vm = Editing();
        await _ui.Invoke(() => vm.Depth.DepthAsLayerCommand.ExecuteAsync(null));
        var document = vm.CurrentDocument;
        Assert.IsNotNull(document, "the photo became a layered picture");
        var layer = document.Picture.Active as PixelLayer;
        Assert.IsNotNull(layer, "the depth is a layer of its own, made active");
        var pixels = layer.Pixels.Read(new SKRectI(0, 0, Wide, High));
        Assert.IsTrue(pixels[(High / 2 * Wide + 2) * 4] > 230, "near is white");
        Assert.IsTrue(pixels[(High / 2 * Wide + Wide - 3) * 4] < 25, "far is black");

        OnUi(() => document.Commit(p => p with { ActiveId = p.All.OfType<PictureLayer>().Single().Id }));
        await _ui.Invoke(() => vm.Depth.DepthAsMaskCommand.ExecuteAsync(null));
        var masked = document.Picture.All.OfType<PictureLayer>().Single();
        Assert.IsNotNull(masked.Mask, "the photo's depth became its mask");
    }

    [TestMethod]
    [CoversNode("images-scene-depth")]
    public void TheRecipe_WithDepthEdits_IsWrittenAsVersion6_AndReadBackWhole()
    {
        var recipe = ImageRecipe.Identity with
        {
            LensBlur = new RecipeLensBlur { Near = 0.2, Far = 0.35, Aperture = 0.7, Shape = BokehShape.Hexagon },
            DepthHaze = new RecipeDepthHaze { Density = 0.3, Start = 0.2, Colour = 0xFF8090A0 },
        };
        Assert.AreEqual(6, recipe.WrittenVersion);
        Assert.IsFalse(recipe.IsIdentity);
        Assert.IsTrue(recipe.ChangesPixels);
        var read = ImageRecipeJson.Deserialize(ImageRecipeJson.Serialize(recipe));
        Assert.AreEqual(recipe, read.Recipe);

        var erasing = ImageRecipe.Identity with { Erasures = [new RecipeErase { Region = new RecipeRegion { Steps = [new RegionSceneDepth(0.1, 0.4, 0.05)] } }] };
        Assert.AreEqual(6, erasing.WrittenVersion, "a region by distance needs this build too");
        Assert.AreEqual(erasing, ImageRecipeJson.Deserialize(ImageRecipeJson.Serialize(erasing)).Recipe);
    }

    // ── For the assistant ─────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-edit-ai-describe-scene-depth")]
    public void TheDepthSummary_TellsNearMiddleAndFar_WithBoxes_NeverTheDepth()
    {
        // Nearness 1 at the nearest, 0.05 twenty times as far (the middle), 0 the sky.
        var plane = new SceneDepthPlane([1f, 1f, 0.05f, 0f, 1f, 1f, 0.05f, 0f], 4, 2);
        var bands = SceneDepthSummary.Bands(plane);
        Assert.AreEqual(0.5, bands[0].Share, 1e-9, "half of it is near");
        Assert.AreEqual(0, bands[0].Box!.Left, 1e-9);
        Assert.AreEqual(0.5, bands[0].Box!.Right, 1e-9);
        Assert.AreEqual(0.25, bands[1].Share, 1e-9, "twenty times as far is the middle: between ten and a hundred times");
        Assert.AreEqual(0.25, bands[2].Share, 1e-9, "the sky is far");
        var text = SceneDepthSummary.Describe("p.jpg", new SKSizeI(400, 200), bands,
                                              [new ThingDepth("dog", 0.9, new PictureBox(0.1, 0.2, 0.3, 0.9), new SubjectDepth(0.1, 0.05, 0.2))], ["cat"]);
        StringAssert.Contains(text, "near (0-0.33): 50%");
        StringAssert.Contains(text, "\"dog\" (0.90 sure) at [0.10, 0.20, 0.30, 0.90]: near, distance 0.10");
        StringAssert.Contains(text, "\"cat\": not found");
        Assert.IsTrue(text.Length < 1000, "a few lines, never the depth itself");
    }

    [TestMethod]
    [CoversNode("images-edit-ai-blur-background")]
    [CoversNode("images-edit-ai-add-haze")]
    [CoversNode("images-edit-ai-describe-scene-depth")]
    public async Task TheAssistant_BlursBehindAThingItNames_AndHazes_EachOneStep()
    {
        var vm = Editing(new Patch());
        OnUi(() => vm.Cutter.Follow(_photo));
        var described = await vm.GetClientTools().Single(t => t.Name == "describe_scene_depth")
                                .InvokeAsync(new JsonObject { ["things"] = new JsonArray("post") }, CancellationToken.None);
        StringAssert.Contains(described.ModelText, "\"post\"");
        StringAssert.Contains(described.ModelText, ": near");

        await vm.GetClientTools().Single(t => t.Name == "blur_background").InvokeAsync(new JsonObject { ["focus"] = "post", ["strength"] = 80 }, CancellationToken.None);
        var blur = vm.CurrentSession!.Recipe.LensBlur;
        Assert.IsNotNull(blur, "the blur is one of the photo's edits");
        Assert.AreEqual(0.8, blur.Aperture, 1e-9);
        Until(() => vm.CurrentSession!.SceneDepth is not null);
        var plane = vm.CurrentSession!.SceneDepth!;
        double post = plane.Distance(plane.At((Patch.Left + Patch.Right) / 2.0, 80, new SKSizeI(Wide, High)));
        Assert.IsTrue(blur.Near <= post && post <= blur.Far, $"focused on the thing named: {post:0.000} in {blur.Near:0.000}-{blur.Far:0.000}");

        var none = await vm.GetClientTools().Single(t => t.Name == "blur_background").InvokeAsync(new JsonObject { ["focus"] = "post", ["which"] = 2 }, CancellationToken.None);
        StringAssert.Contains(none.ModelText, "give 'which' from 1 to 1", "asked for a second of one, it says how many there are");
        Assert.AreEqual(0.8, vm.CurrentSession!.Recipe.LensBlur!.Aperture, 1e-9, "and changes nothing");

        await vm.GetClientTools().Single(t => t.Name == "add_haze").InvokeAsync(new JsonObject { ["density"] = 30, ["colour"] = "#8090A0" }, CancellationToken.None);
        Assert.AreEqual(0xFF8090A0u, vm.CurrentSession.Recipe.DepthHaze!.Colour);
        OnUi(() => vm.UndoEditCommand.Execute(null));
        Assert.IsNull(vm.CurrentSession.Recipe.DepthHaze, "one step, undone at once");
        Assert.IsNotNull(vm.CurrentSession.Recipe.LensBlur);
    }

    [TestMethod]
    [CoversNode("images-edit-ai-add-haze")]
    public async Task TheAssistant_ToldWhereToGetSceneDepth_WhereItIsNotHere()
    {
        _depth.State = ToolModelsState.NotDownloaded;
        var vm = Editing();
        var result = await vm.GetClientTools().Single(t => t.Name == "add_haze").InvokeAsync([], CancellationToken.None);
        StringAssert.Contains(result.ModelText, "Settings > Local > Tools");
        Assert.IsNull(vm.CurrentSession!.Recipe.DepthHaze);
    }

    /// <summary>Scene depth: the nearest at the left edge and a thousand times as far at the right, its disparity — and so its
    /// nearness — falling evenly across, at a small working size; counts its asks.</summary>
    private sealed class Ramp : ISceneDepth
    {
        private int _asks;

        public int Asks => _asks;
        public ToolModelsState State { get; set; } = ToolModelsState.Ready;
        public string ModelsVersion => "ramp-1";
        public long DownloadBytes => 0;
        public int DownloadPercent => 0;
        public string? DownloadFailure => null;
        public event EventHandler? Changed { add { } remove { } }
        public void Download() { }

        public Task<SceneDepthEstimate> EstimateAsync(SceneDepthRequest request, CancellationToken ct)
        {
            Interlocked.Increment(ref _asks);
            Assert.AreEqual(request.Width * request.Height * 4, new FileInfo(request.Pixels).Length, "the photo is handed over whole, as RGBA");
            const int w = 48, h = 32;
            var disparity = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++) disparity[y * w + x] = 10f * (1 - x / (float)(w - 1)) + 0.01f;
            File.WriteAllBytes(request.Into, MemoryMarshal.AsBytes(disparity.AsSpan()).ToArray());
            return Task.FromResult(new SceneDepthEstimate(w, h));
        }
    }

    /// <summary>Picture selection: finds a fixed patch near the left — "post" — and picks it wherever it is clicked.</summary>
    private sealed class Patch : IPictureSelection, IPictureSelectionSession
    {
        public const int Left = 10, Top = 40, Right = 40, Bottom = 120;

        public ToolModelsState State => ToolModelsState.Ready;
        public string ModelsVersion => "patch-1";
        public long DownloadBytes => 0;
        public int DownloadPercent => 0;
        public string? DownloadFailure => null;
        public event EventHandler? Changed { add { } remove { } }
        public void Download() { }
        public int Width { get; private set; }
        public int Height { get; private set; }

        public Task<IPictureSelectionSession> OpenAsync(ReadOnlyMemory<byte> rgba, int width, int height, CancellationToken ct)
        {
            (Width, Height) = (width, height);
            return Task.FromResult<IPictureSelectionSession>(this);
        }

        private PictureMask Mask(int width, int height)
        {
            double sx = (double)width / Width, sy = (double)height / Height;
            var pixels = new byte[width * height];
            for (int y = (int)(Top * sy); y < (int)(Bottom * sy); y++)
                for (int x = (int)(Left * sx); x < (int)(Right * sx); x++) pixels[y * width + x] = 255;
            return new PictureMask(pixels, width, height, new PictureBox(Left, Top, Right, Bottom));
        }

        public Task<PictureMask> SelectAsync(IReadOnlyList<PicturePoint> points, PictureBox? box, int maskWidth, int maskHeight, CancellationToken ct)
            => Task.FromResult(Mask(maskWidth, maskHeight));

        public Task<IReadOnlyList<PictureFind>> FindAsync(string phrase, IReadOnlyList<PictureExample> examples, int maskWidth, int maskHeight, CancellationToken ct)
        {
            if (phrase != "post") return Task.FromResult<IReadOnlyList<PictureFind>>([]);
            var mask = Mask(maskWidth, maskHeight);
            return Task.FromResult<IReadOnlyList<PictureFind>>([new PictureFind(0.9, mask.Bounds!, mask)]);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
