using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Erasing;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Editing.Restoring;
using Nexaflow.Features.Images.Services;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Common.ClientTools;


namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The editor's Erase tool: what is brushed or picked erased as a step of the recipe — filled by content-aware fill or the
/// healing brush at once, or by picture erasing once it is here — shown in the photo's place, saved and drawn in
/// thumbnails as erased, each run of steps kept so a step added makes only itself, and a restoration made of the photo
/// as erased.
/// <para>
/// The photo is flat but for a red square, so an erased square shows as the flat colour. Picture erasing here is a
/// stand-in that paints what its mask covers <see cref="Rubber.Paint"/>; picture selection one that picks a fixed patch.
/// </para>
/// </summary>
[TestClass]
[CoversNode("images-edit-erase")]
public class ImageEraseTests
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
        _dir = Directory.CreateTempSubdirectory("nexaflow-erase-").FullName;
        _photo = Path.Combine(_dir, "wall.png");
        using (var bitmap = Flat(32, 24, Ground))
        {
            for (int y = 10; y < 14; y++)
                for (int x = 14; x < 18; x++) bitmap.SetPixel(x, y, Spot);
            File.WriteAllBytes(_photo, EncodePng(bitmap));
        }
        _ui = new ViewerUiThread();
        _services = new ImageEditingServices(new ImageRecipeStore(TimeSpan.FromHours(1)),
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

    private IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => _ui.Run(call.Arg<Action>()));
        return shell;
    }

    private ImageViewModel Editing(IPictureErasing? erasing = null, IPictureSelection? selection = null, IPictureTools? pictures = null)
    {
        var vm = new ImageViewModel([_photo], Shell(), ImageViewMode.Edit, _services, pictures: pictures, selection: selection, erasing: erasing);
        Until(() => vm.Canvas is not null);
        Assert.IsNotNull(vm.Canvas, "the photo was never decoded and presented");
        OnUi(() => vm.Editor.Tool = EditTool.Erase);
        return vm;
    }

    /// <summary>Brushes a dab of <paramref name="width"/> over the red square's centre, filled as <paramref name="fill"/>.</summary>
    private void BrushOverTheSpot(ImageViewModel vm, EraseFill fill, double width = 8)
    {
        OnUi(() =>
        {
            vm.Editor.EraseMarks.Way = EraseMarkWay.Brush;
            vm.Editor.EraseMarks.Fill = fill;
            vm.Editor.EraseMarks.BrushSize = width;
            vm.Editor.EraseMarks.BeginStroke(new MarkupPoint(16, 12), marks: true);
            vm.Editor.EraseMarks.EndStroke();
        });
    }

    private void Erase(ImageViewModel vm)
    {
        Until(() => vm.Editor.EraseCommand.CanExecute(null));
        OnUi(() => vm.Editor.EraseCommand.Execute(null));
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
    public void ABrushedStep_FilledByContentAwareFill_ErasesAtOnce_InThePhotoShownAndSaved()
    {
        var vm = Editing();
        BrushOverTheSpot(vm, EraseFill.ContentAware);
        Erase(vm);
        Until(() => vm.CurrentSession!.ErasedProxy is not null);

        var session = vm.CurrentSession!;
        Assert.AreEqual(1, session.Recipe.Erasures.Length);
        Assert.AreEqual(EraseFill.ContentAware, session.Recipe.Erasures[0].Fill);
        AssertNear(Ground, Shown(session, 16, 12), because: "the square is erased on screen");
        AssertNear(Ground, Shown(session, 3, 3), because: "what was not brushed is as it was");

        var saved = _ui.Invoke(() => _services.Exporter.Export(_photo, session.Recipe, ImageContainer.Png, made: session.Made));
        using var bitmap = SKBitmap.Decode(saved);
        AssertNear(Ground, bitmap.GetPixel(16, 12), because: "the square is erased in the saved photo");
        Assert.IsTrue(_ui.Invoke(() => vm.Editor.EraseMarks.Strokes.Count == 0), "the marks go once they are erased");
    }

    [TestMethod]
    public void TheHealingBrush_CopiesFromWhereItIsTold_AndNeedsTellingFirst()
    {
        var vm = Editing();
        BrushOverTheSpot(vm, EraseFill.Heal);
        Assert.IsFalse(_ui.Invoke(() => vm.Editor.EraseCommand.CanExecute(null)), "nowhere to copy from yet");

        OnUi(() =>
        {
            vm.Editor.EraseMarks.Clear();
            vm.Editor.EraseMarks.SetHealSource(new MarkupPoint(6, 6));
        });
        BrushOverTheSpot(vm, EraseFill.Heal);
        Erase(vm);
        Until(() => vm.CurrentSession!.ErasedProxy is not null);

        Assert.AreEqual(new MarkupPoint(-10, -6), _ui.Invoke(() => vm.CurrentSession!.Recipe.Erasures[0].HealFrom),
                        "copied from the source as the first stroke stands to it");
        AssertNear(Ground, Shown(vm.CurrentSession!, 16, 12));
    }

    [TestMethod]
    public void AStepFilledByAi_IsErasedByPictureErasing_HandedThePhotoAndWhatIsBrushed()
    {
        var rubber = new Rubber();
        var vm = Editing(rubber);
        BrushOverTheSpot(vm, EraseFill.Ai);
        Erase(vm);
        Until(() => vm.CurrentSession!.ErasedProxy is not null);

        var ask = rubber.Asks.Single();
        Assert.AreEqual((32, 24), (ask.Width, ask.Height));
        CollectionAssert.AreEqual(new byte[] { 100, 150, 200, 255 }, ask.Pixels[..4], "the photo upright, RGBA");
        Assert.AreEqual(255, ask.Mask[12 * 32 + 16], "the brushed dab is erased");
        Assert.AreEqual(0, ask.Mask[2 * 32 + 2], "nothing far from it is");
        AssertNear(Rubber.Paint, Shown(vm.CurrentSession!, 16, 12));
    }

    [TestMethod]
    public void APickedThing_IsErasedGrownPastItsEdge()
    {
        var rubber = new Rubber();
        var vm = Editing(rubber, new Patch());
        OnUi(() => vm.Editor.EraseMarks.Fill = EraseFill.Ai);
        OnUi(() => vm.Cutter.Click(8, 6, include: true));
        Erase(vm);
        Until(() => vm.CurrentSession!.ErasedProxy is not null);

        var mask = rubber.Asks.Single().Mask;
        int grown = EraseMasks.LeastPickGrowth;
        Assert.AreEqual(255, mask[6 * 32 + 8], "inside what is picked");
        Assert.AreEqual(255, mask[6 * 32 + Patch.Right + grown - 1], "just past its edge, within the growth");
        Assert.AreEqual(0, mask[6 * 32 + Patch.Right + grown + 1], "past the growth");
        Assert.IsTrue(_ui.Invoke(() => vm.Editor.EraseMarks.Strokes.Count == 0 && !vm.Cutter.HasSelection), "what is picked goes once erased");
    }

    [TestMethod]
    public void EachRunOfSteps_IsKept_SoAStepAddedMakesOnlyItself()
    {
        var rubber = new Rubber();
        var vm = Editing(rubber);
        BrushOverTheSpot(vm, EraseFill.Ai);
        Erase(vm);
        Until(() => vm.CurrentSession!.ErasedProxy is not null);
        var first = _ui.Invoke(() => vm.CurrentSession!.ErasedPath);

        OnUi(() =>
        {
            vm.Editor.EraseMarks.BeginStroke(new MarkupPoint(4, 4), marks: true);
            vm.Editor.EraseMarks.EndStroke();
        });
        Erase(vm);
        Until(() => vm.CurrentSession!.ErasedPath is { } path && path != first && vm.CurrentSession.ErasedProxy is not null);

        Assert.AreEqual(2, rubber.Asks.Count, "the second step was made from the first, kept; the first was not made again");
        Assert.AreEqual(0, rubber.Asks[1].Mask[12 * 32 + 16], "the second step erases only its own stroke");
        int spot = (12 * 32 + 16) * 4;
        CollectionAssert.AreEqual(new byte[] { Rubber.Paint.Red, Rubber.Paint.Green, Rubber.Paint.Blue },
                                  rubber.Asks[1].Pixels[spot..(spot + 3)], "from what the first step left");

        OnUi(() => vm.UndoEditCommand.Execute(null));
        Until(() => vm.CurrentSession!.ErasedPath == first);
        Assert.AreEqual(2, rubber.Asks.Count, "undoing the step shows the first run as kept");
    }

    [TestMethod]
    public void AStepFilledByAi_WaitsForPictureErasing_AndOffersIt()
    {
        var rubber = new Rubber { State = ToolModelsState.NotDownloaded };
        var vm = Editing(rubber);
        BrushOverTheSpot(vm, EraseFill.Ai);
        Erase(vm);
        Until(() => vm.Eraser.HasNotice);

        Assert.IsTrue(_ui.Invoke(() => vm.Eraser.CanDownload));
        Assert.IsTrue(_ui.Invoke(() => vm.Editor.SaveCopyCommand.CanExecute(null)) is false, "the photo cannot be saved until it is erased");
        OnUi(() => vm.Eraser.ActCommand.Execute(null));
        Assert.IsTrue(rubber.Downloaded);
        Assert.AreEqual(0, rubber.Asks.Count);
    }

    [TestMethod]
    public void ARestoration_IsMadeOfThePhotoAsErased()
    {
        var tools = new Enhancer();
        var vm = Editing(pictures: tools);
        BrushOverTheSpot(vm, EraseFill.ContentAware);
        Erase(vm);
        OnUi(() => vm.CurrentSession!.History.Commit(vm.CurrentSession.Recipe with { Restore = new RecipeRestore { Scale = 1 } }));
        Until(() => vm.CurrentSession!.RestoredPath is not null);

        var restored = tools.Pixels.Single();
        int at = (12 * 32 + 16) * 4;
        CollectionAssert.AreEqual(new byte[] { Ground.Red, Ground.Green, Ground.Blue }, restored[at..(at + 3)],
                                  "the restoration was handed the photo with the square erased");
    }

    [TestMethod]
    public async Task AThumbnail_IsDrawnFromThePhotoAsErased()
    {
        var steps = new EraseSteps([new RecipeErase { Fill = EraseFill.ContentAware, Strokes = [new RecipeEraseStroke { Points = [new MarkupPoint(16, 12)], Width = 8 }] }]);
        _services.Recipes.SaveDebounced(_photo, ImageRecipe.Identity with { Erasures = steps.Steps });
        _services.Recipes.Flush(_photo);
        var erased = await _services.Erased.MakeAsync(_services, _photo, steps, "", null, null, null, CancellationToken.None);
        var item = new ImageThumbItem { FilePath = _photo, FileName = Path.GetFileName(_photo) };

        await new ThumbnailLoadTask([item], 32, Shell(), _services, (_, _) => new MadeByTools(Erased: erased)).RunAsync(CancellationToken.None);
        Until(() => item.Thumbnail is not null);

        var thumb = new System.Windows.Media.Imaging.FormatConvertedBitmap(item.Thumbnail!, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        thumb.CopyPixels(new System.Windows.Int32Rect(16, 12, 1, 1), pixel, 4, 0);
        AssertNear(Ground, new SKColor(pixel[2], pixel[1], pixel[0]), because: "the square is erased in the thumbnail");
    }

    private static Task<ToolResult> EraseInPicture(ImageViewModel vm, string? what = null, string? box = null, string? fill = null)
    {
        var args = new System.Text.Json.Nodes.JsonObject();
        if (what is not null) args["what"] = what;
        if (box is not null) args["box"] = box;
        if (fill is not null) args["fill"] = fill;
        return vm.GetClientTools().Single(t => t.Name == "erase_in_picture").InvokeAsync(args, CancellationToken.None);
    }

    [TestMethod]
    public async Task TheAssistant_ErasesWhatItNames_AsAStepOfTheEdits_FilledByAiWhereItIsHere()
    {
        var rubber = new Rubber();
        var vm = Editing(rubber, new Patch());

        var result = await EraseInPicture(vm, what: "sign");

        Assert.IsFalse(result.IsError, result.ModelText);
        var step = _ui.Invoke(() => vm.CurrentSession!.Recipe.Erasures.Single());
        Assert.AreEqual(EraseFill.Ai, step.Fill);
        Assert.AreEqual("sign", step.Pick!.Phrase);
        Assert.AreEqual(255, rubber.Asks.Single().Mask[6 * 32 + 8], "what it names was erased");
        Assert.IsTrue(_ui.Invoke(() => vm.Editor.IsEraseTool), "the editor shows the Erase tool");
    }

    [TestMethod]
    public async Task TheAssistant_ErasesNothing_WhereNothingIsWhatItNames()
    {
        var rubber = new Rubber();
        var vm = Editing(rubber, new Patch());

        var result = await EraseInPicture(vm, what: "giraffe");

        Assert.IsFalse(result.IsError);
        StringAssert.Contains(result.ModelText, "nothing was erased");
        Assert.AreEqual(0, _ui.Invoke(() => vm.CurrentSession!.Recipe.Erasures.Length));
    }

    [TestMethod]
    public async Task TheAssistant_ErasesABox_ContentAware_WherePictureErasingIsNotHere()
    {
        var vm = Editing(new Rubber { State = ToolModelsState.NotDownloaded }, new Patch());

        var refused = await EraseInPicture(vm, box: "4,2,12,10", fill: "ai");
        Assert.IsTrue(refused.IsError, "AI asked for, and LaMa not downloaded");

        var result = await EraseInPicture(vm, box: "4,2,12,10");

        Assert.IsFalse(result.IsError, result.ModelText);
        var step = _ui.Invoke(() => vm.CurrentSession!.Recipe.Erasures.Single());
        Assert.AreEqual(EraseFill.ContentAware, step.Fill, "content-aware where picture erasing is not here");
        Assert.AreEqual(new RecipeCutBox(4, 2, 12, 10), step.Pick!.Box);
        Assert.IsTrue(_ui.Invoke(() => vm.CurrentSession!.ErasedPath is not null), "it answered once the step was made");
    }

    [TestMethod]
    [CoversNode("images-edit-restore")]
    public async Task TheAssistant_RestoresThePictureOnScreen_AsOneOfItsEdits()
    {
        var tools = new Enhancer();
        var vm = Editing(pictures: tools);
        var args = new System.Text.Json.Nodes.JsonObject { ["scale"] = "1", ["faces"] = "no", ["strength"] = "60" };

        var result = await vm.GetClientTools().Single(t => t.Name == "restore_picture").InvokeAsync(args, CancellationToken.None);

        Assert.IsFalse(result.IsError, result.ModelText);
        Assert.AreEqual(new RecipeRestore { Scale = 1, Faces = false, Strength = 0.6 }, _ui.Invoke(() => vm.CurrentSession!.Recipe.Restore));
        Assert.IsTrue(_ui.Invoke(() => vm.CurrentSession!.RestoredPath is not null), "it answered once the restoration was made");
        Assert.IsTrue(_ui.Invoke(() => vm.Editor.IsAdjustTool), "the editor shows AI enhance");
        Assert.AreEqual(1, tools.Pixels.Count);

        var again = await vm.GetClientTools().Single(t => t.Name == "restore_picture").InvokeAsync(args, CancellationToken.None);
        Assert.IsFalse(again.IsError, "asked again for what is already made, it answers at once");
        Assert.AreEqual(1, tools.Pixels.Count, "and makes nothing again");
    }

    [TestMethod]
    public void ARecipeThatErases_IsWrittenAsVersion4_AndReadsBackTheSame()
    {
        var recipe = ImageRecipe.Identity with
        {
            Erasures =
            [
                new RecipeErase { Fill = EraseFill.Heal, HealFrom = new MarkupPoint(-10, 4),
                                  Strokes = [new RecipeEraseStroke { Points = [new MarkupPoint(1, 2), new MarkupPoint(3.5, 4)], Width = 12 },
                                             new RecipeEraseStroke { Points = [new MarkupPoint(2, 2)], Width = 4, Marks = false }] },
                new RecipeErase { Fill = EraseFill.Ai, Pick = new RecipeCutOut { Phrase = "sign" } },
            ],
        };

        var json = ImageRecipeJson.Serialize(recipe);
        var read = ImageRecipeJson.Deserialize(json);

        StringAssert.Contains(json, "\"version\": 4");
        Assert.AreEqual(RecipeReadStatus.Read, read.Status);
        Assert.AreEqual(recipe, read.Recipe);
        Assert.AreEqual(2, ImageRecipeJson.Deserialize(ImageRecipeJson.Serialize(recipe with { Erasures = [] , Restore = new RecipeRestore() })).Recipe!.WrittenVersion);
    }

    [TestMethod]
    public void AStep_MarksNothing_WhereOnlyMarkingIsTakenBack()
    {
        var marks = new EraseMarking(aiOffered: false);
        Assert.AreEqual(EraseFill.ContentAware, marks.Fill, "without picture erasing, content-aware fill is the start");
        Assert.IsNull(marks.Step(null), "nothing marked");

        marks.BeginStroke(new MarkupPoint(5, 5), marks: false);
        marks.EndStroke();
        Assert.IsNull(marks.Step(null), "a stroke that only takes marking back erases nothing");

        marks.BeginStroke(new MarkupPoint(6, 6), marks: true);
        marks.ContinueStroke(new MarkupPoint(6.2, 6.1), step: 1);
        marks.ContinueStroke(new MarkupPoint(9, 6), step: 1);
        marks.EndStroke();
        var step = marks.Step(null)!;
        Assert.AreEqual(2, step.Strokes.Length);
        Assert.AreEqual(2, step.Strokes[1].Points.Length, "a point nearer than a step to the last is not kept");
    }

    [TestMethod]
    public void AMask_IsGrownSquare_ByTheGrowthForThePhotosSize()
    {
        var mask = new byte[20 * 10];
        mask[5 * 20 + 10] = 255;
        EraseMasks.Grow(mask, 20, 10, 2);
        Assert.AreEqual(25, mask.Count(v => v == 255), "a 5x5 square about the pixel");
        Assert.AreEqual(EraseMasks.LeastPickGrowth, EraseMasks.PickGrowthFor(32, 24), "a small photo is grown by the least");
        Assert.AreEqual(24, EraseMasks.PickGrowthFor(4000, 3000), "a large one by its share of the longer side");
    }

    [TestMethod]
    public void Strokes_ArePaintedInOrder_ALaterOneTakingMarkingBack()
    {
        var painted = new byte[20 * 10];
        EraseMasks.Paint(painted, 20, 10,
        [
            new RecipeEraseStroke { Points = [new MarkupPoint(2, 5), new MarkupPoint(17, 5)], Width = 4 },
            new RecipeEraseStroke { Points = [new MarkupPoint(10, 5)], Width = 4, Marks = false },
        ]);
        Assert.AreEqual(255, painted[5 * 20 + 4], "along the stroke");
        Assert.AreEqual(0, painted[5 * 20 + 10], "taken back by the later stroke");
        Assert.AreEqual(0, painted[0], "nowhere near");
    }

    /// <summary>Picture erasing: paints what its mask covers <see cref="Paint"/>, and keeps what it was handed.</summary>
    private sealed class Rubber : IPictureErasing
    {
        public static readonly SKColor Paint = new(10, 220, 90);

        public sealed record Ask(byte[] Pixels, byte[] Mask, int Width, int Height);

        public List<Ask> Asks { get; } = [];
        public bool Downloaded { get; private set; }
        public ToolModelsState State { get; init; } = ToolModelsState.Ready;
        public string ModelsVersion => "rubber-1";
        public long DownloadBytes => 200 << 20;
        public int DownloadPercent => 0;
        public string? DownloadFailure => null;
        public event EventHandler? Changed { add { } remove { } }
        public void Download() => Downloaded = true;

        public Task<PictureErasure> EraseAsync(PictureEraseRequest request, IProgress<double>? progress, CancellationToken ct)
        {
            var pixels = File.ReadAllBytes(request.Pixels);
            var mask = File.ReadAllBytes(request.Mask);
            lock (Asks) Asks.Add(new Ask(pixels.ToArray(), mask, request.Width, request.Height));
            for (int p = 0; p < mask.Length; p++)
                if (mask[p] != 0) (pixels[p * 4], pixels[p * 4 + 1], pixels[p * 4 + 2]) = (Paint.Red, Paint.Green, Paint.Blue);
            File.WriteAllBytes(request.Into, pixels);
            return Task.FromResult(new PictureErasure(1));
        }
    }

    /// <summary>Picture selection: picks a fixed patch, whatever is clicked.</summary>
    private sealed class Patch : IPictureSelection, IPictureSelectionSession
    {
        public const int Left = 4, Top = 2, Right = 12, Bottom = 10;

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
                if (phrase != "sign") return Task.FromResult<IReadOnlyList<PictureFind>>([]);
                var mask = Mask(maskWidth, maskHeight);
                return Task.FromResult<IReadOnlyList<PictureFind>>([new PictureFind(0.9, mask.Bounds!, mask)]);
            }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>The picture tools: hand back what they were handed, and keep it.</summary>
    private sealed class Enhancer : IPictureTools
    {
        public List<byte[]> Pixels { get; } = [];
        public ToolModelsState State => ToolModelsState.Ready;
        public string ModelsVersion => "enhancer-1";
        public long DownloadBytes => 0;
        public int DownloadPercent => 0;
        public string? DownloadFailure => null;
        public event EventHandler? Changed { add { } remove { } }
        public void Download() { }
        public bool Restores(int width, int height, int scale) => true;

        public Task<PictureRestoration> RestoreAsync(PictureRestoreRequest request, IProgress<double>? progress, CancellationToken ct)
        {
            var pixels = File.ReadAllBytes(request.Pixels);
            lock (Pixels) Pixels.Add(pixels);
            File.WriteAllBytes(request.Into, pixels);
            return Task.FromResult(new PictureRestoration(request.Width, request.Height, 0));
        }
    }
}
