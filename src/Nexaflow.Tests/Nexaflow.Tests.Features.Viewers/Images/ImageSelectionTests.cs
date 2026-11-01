using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Erasing;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Restoring;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Imaging;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The selection drawn by hand: coverage in shared tiles, each shape rasterized anti-aliased and met with the selection
/// as its mode says, the refinements, the outline marching ants walk, and the region a recipe keeps (version 5) — then, in
/// the photo editor, a marquee cutting a photo out and erasing from it without picture selection, and meeting what picture
/// selection picked.
/// <para>
/// Picture selection here is a stand-in that picks a fixed square of the photo, so what a selection kept is plain to see.
/// </para>
/// </summary>
[TestClass]
public class ImageSelectionTests
{
    private static readonly SKColor Ground = new(100, 150, 200);
    private static readonly SKColor Spot = new(230, 30, 30);

    private string _dir = "";
    private string _photo = "";
    private ViewerUiThread _ui = null!;
    private ImageEditingServices _services = null!;

    [TestInitialize]
    public void Make()
    {
        _dir = TestTemp.CreateSubdirectory("nexaflow-selection-").FullName;
        _photo = Path.Combine(_dir, "wall.png");
        using (var bitmap = Flat(32, 24, Ground))
        {
            for (int y = 10; y < 14; y++)
                for (int x = 14; x < 18; x++) bitmap.SetPixel(x, y, Spot);
            File.WriteAllBytes(_photo, EncodePng(bitmap));
        }
        _ui = new ViewerUiThread();
        _services = new ImageEditingServices(VirtualFileSystem.Instance, new ImageRecipeStore(TimeSpan.FromHours(1)),
                                             new RestoredPictures(Path.Combine(_dir, "restored"), RestoredPictures.DefaultBudget),
                                             new CutOutMasks(Path.Combine(_dir, "cutouts"), CutOutMasks.DefaultBudget),
                                             new ErasedPictures(Path.Combine(_dir, "erased"), ErasedPictures.DefaultBudget));
    }

    [TestCleanup]
    public void Clean()
    {
        _ui.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ── Coverage ────────────────────────────────────────────────────────────

    private static TiledCoverage Rect(int width, int height, double left, double top, double right, double bottom)
        => RegionRaster.Shape(new RegionRectangle(left, top, right, bottom), width, height, 1);

    [TestMethod]
    [CoversNode("images-selection-marquee")]
    public void AShape_IsRasterizedAntiAliased_HoldingOnlyTheTilesItTouches()
    {
        var rect = Rect(600, 600, 10.5, 10, 20.5, 20);
        Assert.AreEqual(255, rect[15, 15], "inside is wholly covered");
        Assert.AreEqual(128, rect[10, 15], 2, "a pixel the edge halves is half covered");
        Assert.AreEqual(0, rect[25, 15], "outside is not covered");
        Assert.AreEqual(new SKRectI(10, 10, 21, 20), rect.Bounds);
        Assert.IsTrue(rect.SharesTile(TiledCoverage.Clear(600, 600), 1, 1), "a tile the shape does not touch is not held");
        Assert.IsFalse(rect.SharesTile(TiledCoverage.Clear(600, 600), 0, 0));

        var ellipse = RegionRaster.Shape(new RegionEllipse(0, 0, 100, 50), 100, 50, 1);
        Assert.AreEqual(255, ellipse[50, 25]);
        Assert.AreEqual(0, ellipse[2, 2], "an ellipse leaves its box's corners");

        var tooFew = RegionRaster.Shape(new RegionPolygon { Points = [new(0, 0), new(10, 10)] }, 20, 20, 1);
        Assert.IsTrue(tooFew.IsEmpty, "two points enclose nothing");
    }

    [TestMethod]
    [CoversNode("images-selection")]
    public void EachMode_MeetsTheSelection_AsItSays()
    {
        var left = Rect(20, 10, 0, 0, 10, 10);
        var right = Rect(20, 10, 5, 0, 15, 10);

        var added = left.Combined(right, RegionMode.Add);
        CollectionAssert.AreEqual(new[] { 255, 255, 255, 0 }, new int[] { added[2, 5], added[7, 5], added[12, 5], added[17, 5] });
        var subtracted = left.Combined(right, RegionMode.Subtract);
        CollectionAssert.AreEqual(new[] { 255, 0, 0 }, new int[] { subtracted[2, 5], subtracted[7, 5], subtracted[12, 5] });
        var intersected = left.Combined(right, RegionMode.Intersect);
        CollectionAssert.AreEqual(new[] { 0, 255, 0 }, new int[] { intersected[2, 5], intersected[7, 5], intersected[12, 5] });
        Assert.AreSame(right, left.Combined(right, RegionMode.Replace), "replacing is the shape itself");

        var half = TiledCoverage.FromBytes(Enumerable.Repeat((byte)128, 4).ToArray(), 2, 2);
        Assert.AreEqual(127, TiledCoverage.Covered(2, 2).Combined(half, RegionMode.Subtract)[0, 0], "subtracting leaves as much as the shape leaves uncovered");
        Assert.AreEqual(128, TiledCoverage.Clear(2, 2).Combined(half, RegionMode.Add)[1, 1]);
    }

    [TestMethod]
    [CoversNode("images-selection")]
    public void AChange_SharesEveryTileItLeavesAlone()
    {
        var first = Rect(1024, 1024, 10, 10, 100, 100);
        var second = first.Combined(Rect(1024, 1024, 800, 800, 900, 900), RegionMode.Add);
        Assert.IsTrue(second.SharesTile(first, 0, 0), "the first shape's tile is shared, not copied");
        Assert.IsFalse(second.SharesTile(first, 3, 3), "the tile the second shape touched is new");

        var wholeTile = Rect(1024, 1024, 256, 256, 512, 512);
        var again = Rect(1024, 1024, 256, 256, 512, 512);
        Assert.IsTrue(wholeTile.SharesTile(again, 1, 1), "a wholly covered tile is the one shared covered tile");
    }

    [TestMethod]
    [CoversNode("images-selection-refine")]
    public void Invert_TurnsEveryPixelOver()
    {
        var rect = Rect(300, 300, 0, 0, 10, 10);
        var inverted = rect.Inverted();
        CollectionAssert.AreEqual(new[] { 0, 255, 255 }, new int[] { inverted[5, 5], inverted[20, 20], inverted[299, 299] });
        Assert.AreEqual(255, inverted.Outside);
        var half = TiledCoverage.FromBytes([100], 1, 1).Inverted();
        Assert.AreEqual(155, half[0, 0]);
        Assert.IsTrue(TiledCoverage.Clear(5, 5).Inverted().Inverted().IsEmpty);
    }

    [TestMethod]
    [CoversNode("images-selection-refine")]
    public void Feather_FadesTheEdge_LeavingTheMiddle_AndTheCanvasEdgeCovered()
    {
        var feathered = Rect(200, 200, 50, 50, 150, 150).Feathered(10);
        Assert.AreEqual(255, feathered[100, 100], "the middle stays covered");
        Assert.AreEqual(128, feathered[50, 100], 20, "the edge is half covered");
        Assert.IsTrue(feathered[45, 100] is > 0 and < 128, "just outside fades in");
        Assert.AreEqual(0, feathered[20, 100], "far outside stays clear");
        for (int x = 30; x < 70; x++) Assert.IsTrue(feathered[x, 100] <= feathered[x + 1, 100], "the edge only rises inward");

        var atTheEdge = Rect(200, 200, 0, 0, 100, 200).Feathered(10);
        Assert.AreEqual(255, atTheEdge[0, 100], "the canvas's edge is no edge");
    }

    [TestMethod]
    [CoversNode("images-selection-refine")]
    public void GrowAndShrink_MoveTheEdgeByTheirAmount_RoundingCorners()
    {
        var grown = Rect(200, 200, 50, 50, 100, 100).Grown(10);
        CollectionAssert.AreEqual(new[] { 0, 128, 255 }, new int[] { grown[39, 75], grown[40, 75], grown[41, 75] }, "the edge is ten pixels out, anti-aliased");
        Assert.AreEqual(0, grown[42, 42], "a corner grows round, not square");

        var shrunk = Rect(200, 200, 50, 50, 100, 100).Grown(-10);
        CollectionAssert.AreEqual(new[] { 0, 128, 255 }, new int[] { shrunk[59, 75], shrunk[60, 75], shrunk[61, 75] }, "the edge is ten pixels in, anti-aliased");

        var atTheEdge = Rect(200, 200, 0, 0, 100, 200).Grown(-10);
        Assert.AreEqual(255, atTheEdge[0, 100], "nothing shrinks away from the canvas's edge");
        Assert.AreEqual(0, atTheEdge[95, 100]);
    }

    [TestMethod]
    [CoversNode("images-selection-refine")]
    public void Smooth_TakesOffWhatIsSmallerThanItsRadius_LeavingACrispEdge()
    {
        var spiked = Rect(200, 200, 50, 50, 150, 150).Combined(Rect(200, 200, 150, 99, 170, 101), RegionMode.Add);
        var smoothed = spiked.Smoothed(8);
        Assert.AreEqual(255, smoothed[100, 100]);
        Assert.AreEqual(0, smoothed[165, 100], "a spike narrower than the radius goes");
        Assert.AreEqual(0, smoothed[40, 100]);
        Assert.AreEqual(255, smoothed[55, 100], "the edge stays crisp, not feathered");
    }

    [TestMethod]
    [CoversNode("images-selection")]
    public void TheOutline_IsTracedRoundWhatIsHalfCovered_ALoopForEachEdge()
    {
        var one = CoverageOutline.Trace(Rect(40, 40, 10, 10, 20, 30));
        Assert.AreEqual(1, one.Count);
        Assert.AreEqual((10f, 10f, 20f, 30f), (one[0].Min(p => p.X), one[0].Min(p => p.Y), one[0].Max(p => p.X), one[0].Max(p => p.Y)),
                        "the outline runs between the covered pixels and the clear ones");

        var two = Rect(40, 40, 2, 2, 8, 8).Combined(Rect(40, 40, 20, 20, 30, 30), RegionMode.Add);
        Assert.AreEqual(2, CoverageOutline.Trace(two).Count, "two shapes apart are two loops");
        var ring = Rect(40, 40, 5, 5, 35, 35).Combined(Rect(40, 40, 15, 15, 25, 25), RegionMode.Subtract);
        Assert.AreEqual(2, CoverageOutline.Trace(ring).Count, "a ring has an outer loop and an inner one");
        Assert.AreEqual(1, CoverageOutline.Trace(TiledCoverage.Covered(10, 10)).Count, "everything selected is closed along the canvas's edge");
        Assert.AreEqual(0, CoverageOutline.Trace(TiledCoverage.Clear(10, 10)).Count);
    }

    [TestMethod]
    [CoversNode("images-selection")]
    public void ARegion_IsAppliedInOrder_TheSameAtAnyScale()
    {
        var region = new RecipeRegion
        {
            Steps =
            [
                new RegionRectangle(0, 0, 100, 100) { Mode = RegionMode.Replace },
                new RegionEllipse(20, 20, 80, 80) { Mode = RegionMode.Subtract },
                new RegionInvert(),
            ],
        };
        var full = RegionRaster.Apply(TiledCoverage.Clear(100, 100), region, 1);
        var half = RegionRaster.Apply(TiledCoverage.Clear(50, 50), region, 0.5);
        CollectionAssert.AreEqual(new[] { 255, 0 }, new int[] { full[50, 50], full[5, 5] }, "the ellipse cut out of the square, turned over");
        CollectionAssert.AreEqual(new[] { 255, 0 }, new int[] { half[25, 25], half[2, 2] }, "the same region at half the size");

        var started = RegionRaster.Apply(Rect(100, 100, 0, 0, 10, 10), new RecipeRegion { Steps = [new RegionRectangle(50, 50, 60, 60) { Mode = RegionMode.Add }] }, 1);
        CollectionAssert.AreEqual(new[] { 255, 255 }, new int[] { started[5, 5], started[55, 55] }, "a region starts from what it is given");
    }

    // ── The recipe ──────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-selection")]
    public void ARegion_IsWrittenAsVersion5_AndReadsBackTheSame()
    {
        var region = new RecipeRegion
        {
            Steps =
            [
                new RegionRectangle(1, 2, 30, 40) { Mode = RegionMode.Replace },
                new RegionEllipse(5, 5, 9, 9) { Mode = RegionMode.Add },
                new RegionPolygon { Points = [new(1, 1), new(9, 1), new(5, 8)], Mode = RegionMode.Subtract },
                new RegionInvert(),
                new RegionFeather(4),
                new RegionGrow(-2.5),
                new RegionSmooth(3),
            ],
        };
        var recipe = ImageRecipe.Identity with
        {
            CutOut = new RecipeCutOut { Clicks = [new RecipeCutClick(1, 2, true)], Region = region },
            Erasures = [new RecipeErase { Fill = EraseFill.ContentAware, Region = region }],
        };

        var json = ImageRecipeJson.Serialize(recipe);
        StringAssert.Contains(json, "\"version\": 5");
        StringAssert.Contains(json, "\"kind\": \"polygon\"");
        var read = ImageRecipeJson.Deserialize(json);
        Assert.AreEqual(RecipeReadStatus.Read, read.Status);
        Assert.AreEqual(recipe, read.Recipe, "every step reads back as it was written");
        Assert.IsTrue(read.Recipe!.Erasures[0].MarksAnything, "a region alone marks what to erase");
        Assert.IsFalse(read.Recipe.CutOut!.Region!.Equals(region with { Steps = region.Steps.RemoveAt(3) }));
    }

    [TestMethod]
    [CoversNode("images-selection")]
    public void ARecipeWithNoRegion_IsWrittenAsBefore_AndFiledAsBefore()
    {
        var erasing = ImageRecipe.Identity with { Erasures = [new RecipeErase { Fill = EraseFill.ContentAware, Strokes = [new RecipeEraseStroke { Points = [new(1, 1)], Width = 4 }] }] };
        StringAssert.Contains(ImageRecipeJson.Serialize(erasing), "\"version\": 4");

        var picked = new RecipeCutOut { Clicks = [new RecipeCutClick(1, 2, true)] };
        Assert.AreEqual("clicks:1,2,1|box:", picked.Describe(), "a cut-out without a region is filed as it always was, so what is kept is found");
        Assert.AreNotEqual(picked.Describe(), (picked with { Region = new RecipeRegion { Steps = [new RegionInvert()] } }).Describe());
        Assert.IsTrue(picked.AsksPictureSelection);
        Assert.IsFalse(new RecipeCutOut { Region = new RecipeRegion() }.AsksPictureSelection);
    }

    [TestMethod]
    [CoversNode("images-selection")]
    public void ACutOutThroughCoverage_KeepsAsMuchOfEachPixelAsIsSelected()
    {
        var rgba = new byte[4 * 4 * 4];
        for (int i = 0; i < 16; i++) (rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]) = (10, 20, 30, 255);
        var coverage = new byte[16];
        coverage[5] = 255;
        coverage[6] = 128;

        var (cut, width, height) = PictureCutOut.Through(rgba, 4, 4, coverage)!.Value;
        Assert.AreEqual((2, 1), (width, height), "cropped to what is selected at all");
        Assert.AreEqual(255, cut[3]);
        Assert.AreEqual(128, cut[7], "half selected is half kept");
        CollectionAssert.AreEqual(new[] { 10, 20, 30 }, new int[] { cut[4], cut[5], cut[6] }, "the colour is kept as it was");
        Assert.IsNull(PictureCutOut.Through(rgba, 4, 4, new byte[16]));
    }

    // ── In the photo editor ─────────────────────────────────────────────────

    private IShellServices Shell(string? saveAs = null)
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => _ui.Run(call.Arg<Action>()));
        shell.PickSaveFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<long>()).Returns(saveAs);
        return shell;
    }

    private ArtistViewModel Editing(EditTool tool, IPictureSelection? selection = null, IShellServices? shell = null)
    {
        var vm = new ArtistViewModel(_photo, shell ?? Shell(), _services, selection: selection);
        Until(() => vm.Canvas is not null);
        Assert.IsNotNull(vm.Canvas, "the photo was never decoded and presented");
        OnUi(() => vm.Editor.Tool = tool);
        return vm;
    }

    private void OnUi(Action action) => _ui.Run(action).GetAwaiter().GetResult();

    private void Until(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10) && !_ui.Invoke(done)) Thread.Sleep(10);
    }

    private static SKColor Shown(ImageEditSession session, int x, int y)
    {
        using var picture = session.Snapshot(session.Recipe, new RenderRequest(RenderFrame.Output, 1))!;
        return PixelAt(picture, x, y);
    }

    [TestMethod]
    [CoversNode("images-selection-marquee")]
    public void AMarquee_RemovesTheBackground_WithoutPictureSelection()
    {
        var vm = Editing(EditTool.CutOut);
        Assert.AreEqual(CutOutMarkWay.Marquee, _ui.Invoke(() => vm.Editor.CutOutWay), "without picture selection, Cut out marks with the marquee");
        Assert.IsTrue(_ui.Invoke(() => vm.Editor.DrawsMarquee));

        OnUi(() => vm.Editor.RegionMarks.Add(new RegionRectangle(4, 4, 12, 12) { Mode = RegionMode.Replace }));
        Assert.IsTrue(_ui.Invoke(() => vm.Editor.RegionMarks.HasSelection));
        Until(() => vm.Editor.RemoveBackgroundCommand.CanExecute(null));
        OnUi(() => vm.Editor.RemoveBackgroundCommand.Execute(null));
        // The mask is attached once decoded, after its path is known: what a snapshot draws is the mask, so that is what is waited on.
        Until(() => vm.CurrentSession!.CutOutMask is not null);

        var session = vm.CurrentSession!;
        Assert.IsNotNull(session.CutOutPath, "the mask is made with no models at all");
        Assert.IsFalse(session.Recipe.CutOut!.AsksPictureSelection);
        Assert.AreEqual(255, Shown(session, 8, 8).Alpha, "inside the marquee is kept");
        Assert.AreEqual(0, Shown(session, 20, 20).Alpha, "outside it is cleared");
        StringAssert.Contains(ImageRecipeJson.Serialize(session.Recipe), "\"version\": 5");
    }

    [TestMethod]
    [CoversNode("images-selection")]
    public void AShape_MeetsWhatIsPicked_AndOneThatReplacesLetsThePicksGo()
    {
        var square = new Square();
        var vm = Editing(EditTool.CutOut, square);
        OnUi(() => vm.Cutter.Click(8, 8, include: true));
        Until(() => vm.Editor.RegionMarks.HasSelection);
        Assert.AreEqual(255, _ui.Invoke(() => vm.Editor.RegionMarks.Shown!.Coverage[8, 8]), "what is picked is selected");

        OnUi(() => vm.Editor.RegionMarks.Add(new RegionRectangle(0, 0, 10, 24) { Mode = RegionMode.Subtract }));
        var coverage = _ui.Invoke(() => vm.Editor.RegionMarks.Shown!.Coverage);
        CollectionAssert.AreEqual(new[] { 0, 255 }, new int[] { coverage[8, 8], coverage[12, 8] }, "the marquee is taken out of the pick");

        Until(() => vm.Editor.RemoveBackgroundCommand.CanExecute(null));
        OnUi(() => vm.Editor.RemoveBackgroundCommand.Execute(null));
        var cutOut = _ui.Invoke(() => vm.CurrentSession!.Recipe.CutOut)!;
        Assert.AreEqual(1, cutOut.Clicks.Length, "the pick is kept as asked");
        Assert.AreEqual(1, cutOut.Region!.Steps.Length, "and the region drawn over it");
        // The mask is attached once decoded, after its path is known: what a snapshot draws is the mask, so that is what is waited on.
        Until(() => vm.CurrentSession!.CutOutMask is not null);
        var session = vm.CurrentSession!;
        CollectionAssert.AreEqual(new[] { 0, 255 }, new int[] { Shown(session, 8, 8).Alpha, Shown(session, 12, 8).Alpha }, "the photo is cut out by both together");

        OnUi(() => vm.Editor.RegionMarks.Add(new RegionEllipse(0, 0, 4, 4) { Mode = RegionMode.Replace }));
        Assert.AreEqual(0, _ui.Invoke(() => vm.Cutter.Points.Count), "a shape that replaces the selection lets the picks go");
    }

    [TestMethod]
    [CoversNode("images-selection-lasso")]
    public void ALassoedSpot_IsErased_ByContentAwareFill()
    {
        var vm = Editing(EditTool.Retouch);
        OnUi(() =>
        {
            vm.Editor.EraseMarks.Way = EraseMarkWay.Lasso;
            vm.Editor.EraseMarks.Fill = EraseFill.ContentAware;
            vm.Editor.RegionMarks.Lasso = LassoShape.Polygon;
            vm.Editor.RegionMarks.AddDraftPoint(new MarkupPoint(12, 8), RegionMode.Replace);
            vm.Editor.RegionMarks.AddDraftPoint(new MarkupPoint(20, 8), RegionMode.Add);
            vm.Editor.RegionMarks.AddDraftPoint(new MarkupPoint(20, 16), RegionMode.Add);
            vm.Editor.RegionMarks.AddDraftPoint(new MarkupPoint(12, 16), RegionMode.Add);
            Assert.IsTrue(vm.Editor.RegionMarks.CloseDraft());
        });
        Assert.AreEqual(0, _ui.Invoke(() => vm.Editor.RegionMarks.Draft.Count));
        Assert.AreEqual(RegionMode.Replace, _ui.Invoke(() => ((RegionShape)vm.Editor.RegionMarks.Region!.Steps[0]).Mode), "the outline is in the mode its first point began it in");

        Until(() => vm.Editor.EraseCommand.CanExecute(null));
        OnUi(() => vm.Editor.EraseCommand.Execute(null));
        Until(() => vm.CurrentSession!.ErasedProxy is not null);

        var session = vm.CurrentSession!;
        Assert.IsNull(session.Recipe.Erasures[0].Pick);
        Assert.IsNotNull(session.Recipe.Erasures[0].Region);
        AssertNear(Ground, Shown(session, 16, 12), because: "the spot inside the outline is erased");
        Assert.IsNull(_ui.Invoke(() => vm.Editor.RegionMarks.Region), "the marks go once they are erased");
    }

    [TestMethod]
    [CoversNode("images-selection-refine")]
    public async Task SaveCutOut_TakesTheWholeSelection_ItsFeatheredEdgeKept()
    {
        var saved = Path.Combine(_dir, "wall cut out.png");
        var vm = Editing(EditTool.CutOut, shell: Shell(saveAs: saved));
        OnUi(() =>
        {
            vm.Editor.RegionMarks.Add(new RegionRectangle(8, 4, 24, 20) { Mode = RegionMode.Replace });
            vm.Editor.RegionMarks.RefineBy = 4;
            vm.Editor.RegionMarks.FeatherCommand.Execute(null);
        });
        Until(() => vm.Editor.SaveCutOutCommand.CanExecute(null));

        await _ui.Invoke(() => vm.Editor.SaveCutOutCommand.ExecuteAsync(null));

        using var cut = SKBitmap.Decode(saved);
        Assert.IsNotNull(cut, "the cut-out was saved");
        Assert.IsTrue(cut.Width > 16 && cut.Width < 32, "cropped to what the feathered marquee reaches");
        Assert.AreEqual(255, cut.GetPixel(cut.Width / 2, cut.Height / 2).Alpha, "the middle is kept whole");
        Assert.IsTrue(cut.GetPixel(0, cut.Height / 2).Alpha is > 0 and < 128, "the feathered edge fades out");
    }

    [TestMethod]
    [CoversNode("images-selection-refine")]
    public void SelectAll_Invert_AndDeselect_ActOnTheWholeSelection()
    {
        var vm = Editing(EditTool.CutOut);
        OnUi(() => vm.Editor.RegionMarks.SelectAllCommand.Execute(null));
        Assert.AreEqual(255, _ui.Invoke(() => vm.Editor.RegionMarks.Shown!.Coverage[31, 23]), "all of the photo is selected");
        OnUi(() => vm.Editor.RegionMarks.InvertCommand.Execute(null));
        Assert.IsFalse(_ui.Invoke(() => vm.Editor.RegionMarks.HasSelection), "turned over, nothing is");
        Assert.IsFalse(_ui.Invoke(() => vm.Editor.RegionMarks.FeatherCommand.CanExecute(null)), "nothing to refine");
        OnUi(() => vm.Editor.RegionMarks.DeselectCommand.Execute(null));
        Assert.IsNull(_ui.Invoke(() => vm.Editor.RegionMarks.Region));
        Assert.IsNull(_ui.Invoke(() => vm.Editor.RegionMarks.Shown));
    }

    /// <summary>Picture selection that picks the square from (6, 6) to (16, 16) of whatever it is asked.</summary>
    private sealed class Square : IPictureSelection, IPictureSelectionSession
    {
        private const int From = 6, To = 16;

        public ToolModelsState State => ToolModelsState.Ready;
        public string ModelsVersion => "square-1";
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

        public Task<PictureMask> SelectAsync(IReadOnlyList<PicturePoint> points, PictureBox? box, int maskWidth, int maskHeight, CancellationToken ct)
        {
            double sx = (double)maskWidth / Width, sy = (double)maskHeight / Height;
            var pixels = new byte[maskWidth * maskHeight];
            for (int y = (int)(From * sy); y < (int)(To * sy); y++)
                for (int x = (int)(From * sx); x < (int)(To * sx); x++) pixels[y * maskWidth + x] = 255;
            return Task.FromResult(new PictureMask(pixels, maskWidth, maskHeight, new PictureBox(From, From, To, To)));
        }

        public Task<IReadOnlyList<PictureFind>> FindAsync(string phrase, IReadOnlyList<PictureExample> examples, int maskWidth, int maskHeight, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PictureFind>>([]);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
