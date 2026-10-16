using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Localization;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Kept;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.Services;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The editor's cut-out tool: the photo read for picking once, the first time something is picked, and each click, box
/// and phrase answered from that reading; what is picked saved as a PNG cut out of its background; the reading let go
/// with the editor; and, where picture selection is not here or fails, the tool saying so and offering what to do.
/// <para>
/// Picture selection here is a stand-in that picks a fixed patch of the photo, so what a cut-out kept is plain to see.
/// </para>
/// </summary>
[TestClass]
[CoversNode("images-edit-cutout")]
public class ImageCutOutTests
{
    private static readonly SKColor Photo = new(200, 100, 50);

    private string _dir = "";
    private string _photo = "";
    private ViewerUiThread _ui = null!;
    private ImageEditingServices _services = null!;

    [TestInitialize]
    public void Make()
    {
        _dir = Directory.CreateTempSubdirectory("nexaflow-cut-out-").FullName;
        _photo = Path.Combine(_dir, "mug.png");
        using (var bitmap = Flat(32, 24, Photo)) File.WriteAllBytes(_photo, EncodePng(bitmap));
        _ui = new ViewerUiThread();
        _services = new ImageEditingServices(new ImageRecipeStore(TimeSpan.FromHours(1)),
                                             cutOuts: new CutOutMasks(Path.Combine(_dir, "cutouts"), CutOutMasks.DefaultBudget));
    }

    [TestCleanup]
    public void Clean()
    {
        _ui.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private IShellServices Shell(string? saveAs = null)
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => _ui.Run(call.Arg<Action>()));
        shell.PickSaveFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<long>()).Returns(saveAs);
        shell.When(s => s.QueueBackgroundTask(Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>?>(), Arg.Any<CancellationToken>()))
             .Do(call =>
             {
                 call.Arg<IBackgroundTask>().RunAsync(CancellationToken.None).GetAwaiter().GetResult();
                 call.Arg<Action<bool>?>()?.Invoke(true);
             });
        return shell;
    }

    private ImageViewModel Editing(Patch selection, IShellServices? shell = null, string? photo = null)
    {
        var vm = new ImageViewModel([photo ?? _photo], shell ?? Shell(), ImageViewMode.Edit, _services, selection: selection);
        Until(() => vm.Canvas is not null);
        Assert.IsNotNull(vm.Canvas, "the photo was never decoded and presented");
        OnUi(() => vm.Editor.Tool = EditTool.CutOut);
        return vm;
    }

    /// <summary>Picks the patch with a click and removes the photo's background around it, then waits for its mask to be
    /// made and drawn through.</summary>
    private void RemoveBackgroundAroundAClick(ImageViewModel vm)
    {
        OnUi(() => vm.Cutter.Click(10, 8, include: true));
        Until(() => vm.Editor.RemoveBackgroundCommand.CanExecute(null));
        OnUi(() => vm.Editor.RemoveBackgroundCommand.Execute(null));
        Until(() => vm.CurrentSession!.CutOutMask is not null);
    }

    /// <summary>Does <paramref name="action"/> on the viewer's UI thread, where it is driven.</summary>
    private void OnUi(Action action) => _ui.Run(action).GetAwaiter().GetResult();

    private void Until(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10) && !_ui.Invoke(done)) Thread.Sleep(10);
    }

    [TestMethod]
    public void Clicks_AreAnsweredFromOneReadingOfThePhoto_AsItStands()
    {
        var selection = new Patch();
        var vm = Editing(selection);

        OnUi(() => vm.Cutter.Click(10, 8, include: true));
        Until(() => vm.Cutter.HasSelection);
        OnUi(() => vm.Cutter.Click(20, 4, include: false));
        Until(() => selection.Asks.Count == 2 && !vm.Cutter.IsWorking);

        Assert.AreEqual(1, selection.Opened, "the photo is read once");
        Assert.AreEqual((32, 24), (selection.Width, selection.Height));
        CollectionAssert.AreEqual(new byte[] { 200, 100, 50, 255 }, selection.Pixels![..4], "the photo's own pixels, upright RGBA");
        CollectionAssert.AreEqual(new[] { new PicturePoint(10, 8, true), new PicturePoint(20, 4, false) }, selection.Asks[1].Points.ToArray());
        Assert.AreEqual((32, 24), (selection.Asks[1].Width, selection.Asks[1].Height), "a small photo is shown picked at its own size");
    }

    [TestMethod]
    public void ABox_IsAskedWithTheClicks_AndTakesThePlaceOfTheLast()
    {
        var selection = new Patch();
        var vm = Editing(selection);

        OnUi(() => vm.Cutter.Boxed(new PictureBox(1, 1, 10, 10)));
        OnUi(() => vm.Cutter.Boxed(new PictureBox(2, 2, 20, 20)));
        Until(() => vm.Cutter.HasSelection && !vm.Cutter.IsWorking);

        Assert.AreEqual(new PictureBox(2, 2, 20, 20), selection.Asks[^1].Box);
        Assert.AreEqual(new PictureBox(2, 2, 20, 20), vm.Cutter.Box);
    }

    [TestMethod]
    public void APhrase_FindsEverythingItNames_AndAClickAfterwardsStartsAgain()
    {
        var selection = new Patch();
        var vm = Editing(selection);

        OnUi(() => vm.Cutter.Phrase = "mug");
        OnUi(() => vm.Cutter.FindCommand.Execute(null));
        Until(() => vm.Cutter.Found is not null);

        Assert.AreEqual("mug", selection.Phrases.Single());
        Assert.AreEqual(Str.Format("Images.CutOut.FoundFormat", 2, "mug"), vm.Cutter.Notice);
        Assert.IsTrue(vm.Cutter.HasSelection);

        OnUi(() => vm.Cutter.Click(3, 3, include: true));
        Until(() => selection.Asks.Count == 1 && !vm.Cutter.IsWorking);
        Assert.IsNull(vm.Cutter.Found, "a click picks anew");
        Assert.AreEqual(1, vm.Cutter.Points.Count);
    }

    [TestMethod]
    public async Task SaveCutOut_WritesAPng_TransparentAroundWhatIsPicked_CroppedToIt()
    {
        var saved = Path.Combine(_dir, "mug cut out.png");
        var selection = new Patch();
        var vm = Editing(selection, Shell(saveAs: saved));
        OnUi(() => vm.Cutter.Click(10, 8, include: true));
        Until(() => vm.Cutter.SaveCutOutCommand.CanExecute(null));

        await _ui.Invoke(() => vm.Cutter.SaveCutOutCommand.ExecuteAsync(null));

        using var cut = SKBitmap.Decode(saved);
        Assert.AreEqual((Patch.Right - Patch.Left, Patch.Bottom - Patch.Top), (cut.Width, cut.Height), "cropped to the patch");
        Assert.AreEqual(Photo, cut.GetPixel(1, 1), "the photo, as it was, inside");
        Assert.AreEqual(0, cut.GetPixel(0, 0).Alpha, "the patch's notched corner is clear");
        Assert.AreEqual((32, 24), (selection.Asks[^1].Width, selection.Asks[^1].Height), "cut out at the photo's full size");
    }

    [TestMethod]
    public void LeavingTheEditor_LetsTheReadingGo()
    {
        var selection = new Patch();
        var vm = Editing(selection);
        OnUi(() => vm.Cutter.Click(10, 8, include: true));
        Until(() => vm.Cutter.HasSelection);

        OnUi(() => vm.ExitEditModeCommand.Execute(null));
        Until(() => selection.Ended);

        Assert.IsTrue(selection.Ended, "the selection on the photo ends with the editor");
        Assert.IsFalse(vm.Cutter.HasSelection);
        Assert.AreEqual(0, vm.Cutter.Points.Count);
    }

    [TestMethod]
    public void PictureSelectionNotHere_IsOfferedForDownload_AndNothingIsRead()
    {
        var selection = new Patch { State = ToolModelsState.NotDownloaded };
        var vm = Editing(selection);

        OnUi(() => vm.Cutter.Click(10, 8, include: true));
        Thread.Sleep(100);

        Assert.AreEqual(0, selection.Opened);
        Assert.IsTrue(vm.Cutter.CanDownload);
        Assert.AreEqual(Str.Get("Images.CutOut.Download"), vm.Cutter.ActionLabel);
        OnUi(() => vm.Cutter.ActCommand.Execute(null));
        Assert.IsTrue(selection.Downloading);
    }

    [TestMethod]
    public void APickThatFails_SaysWhy_AndIsTriedAgainWhenAsked()
    {
        var selection = new Patch { FailNext = true };
        var vm = Editing(selection);

        OnUi(() => vm.Cutter.Click(10, 8, include: true));
        Until(() => vm.Cutter.Failed);
        StringAssert.Contains(vm.Cutter.Notice, "the host fell over");
        Assert.AreEqual(Str.Get("Images.CutOut.Retry"), vm.Cutter.ActionLabel);

        OnUi(() => vm.Cutter.ActCommand.Execute(null));
        Until(() => vm.Cutter.HasSelection);
        Assert.IsFalse(vm.Cutter.Failed);
        Assert.AreEqual(2, selection.Opened, "a selection that failed is opened afresh");
    }

    [TestMethod]
    public async Task TheAssistant_PicksOutWhatItNames_InTheEditorsCutOut_FromBrowsing()
    {
        var selection = new Patch();
        var vm = new ImageViewModel([_photo], Shell(), ImageViewMode.Carousel, _services, selection: selection);
        Until(() => vm.CurrentSession is not null);
        var tool = vm.GetClientTools().Single(t => t.Name == "select_in_picture");

        var result = await tool.InvokeAsync(new System.Text.Json.Nodes.JsonObject { ["what"] = "mug" }, CancellationToken.None);

        Assert.IsTrue(result.Success, result.ModelText);
        StringAssert.Contains(result.ModelText, "Picked out 2 \"mug\"");
        StringAssert.Contains(result.ModelText, "4,2,12,10");
        Assert.IsTrue(_ui.Invoke(() => vm.IsEditing && vm.Editor.IsCutOutTool), "the editor opens on Cut out");
        Assert.AreEqual("mug", _ui.Invoke(() => vm.Cutter.Found));
    }

    [TestMethod]
    public async Task TheAssistant_IsToldWhereToGetPictureSelection_WhereItIsNotHere()
    {
        var vm = Editing(new Patch { State = ToolModelsState.NotDownloaded });

        var result = await vm.GetClientTools().Single(t => t.Name == "select_in_picture")
                             .InvokeAsync(new System.Text.Json.Nodes.JsonObject { ["what"] = "mug" }, CancellationToken.None);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ModelText, "Settings > Local > Tools");
    }

    [TestMethod]
    [DataRow("cutout", EditTool.CutOut)]
    [DataRow("CutOut", EditTool.CutOut)]
    [DataRow("crop", EditTool.Crop)]
    public void ATabAskedToOpenOnATool_OpensTheEditorThere(string asked, EditTool tool)
        => Assert.AreEqual(tool, Nexaflow.Features.Images.ImageTabRegistration.ParseTool(asked));

    [TestMethod]
    public void ATabAskedForNoTool_OrOneThatIsNot_OpensTheEditorAsItWould()
    {
        Assert.IsNull(Nexaflow.Features.Images.ImageTabRegistration.ParseTool(null));
        Assert.IsNull(Nexaflow.Features.Images.ImageTabRegistration.ParseTool("lasso"));
    }

    // ── Remove background: what is picked kept in the photo itself ──────────

    [TestMethod]
    [CoversNode("images-edit-remove-background")]
    public void RemoveBackground_KeepsWhatIsPicked_InThePhotoItself_TheRestClear()
    {
        var selection = new Patch();
        var vm = Editing(selection);

        RemoveBackgroundAroundAClick(vm);

        var session = _ui.Invoke(() => vm.CurrentSession!);
        Assert.AreEqual(new RecipeCutOut { Clicks = [new RecipeCutClick(10, 8, true)] }, session.Recipe.CutOut);
        Assert.AreEqual((32, 24), (selection.Asks[^1].Width, selection.Asks[^1].Height), "the mask is picked at the photo's full size");
        using var picture = session.Snapshot(session.Recipe, RenderRequest.Saved)!;
        using var bitmap = SKBitmap.FromImage(picture);
        Assert.AreEqual((32, 24), (bitmap.Width, bitmap.Height), "the photo keeps its size");
        Assert.AreEqual(Photo, bitmap.GetPixel(8, 6), "what is picked, as it was");
        Assert.AreEqual(0, bitmap.GetPixel(0, 0).Alpha, "the background is clear");
        Assert.IsTrue(bitmap.GetPixel(Patch.Left, Patch.Top).Alpha < 64, "the patch's notched corner is all but clear, its edge resampled");
        Assert.IsFalse(_ui.Invoke(() => vm.Editor.RemoveBackgroundCommand.CanExecute(null)), "what is picked is what is kept already");
        Assert.IsTrue(_ui.Invoke(() => vm.Editor.KeepBackgroundCommand.CanExecute(null)));
    }

    [TestMethod]
    [CoversNode("images-edit-remove-background")]
    public void TheMask_IsMadeOnce_AndDrawnThroughAgainAfterAnUndoAndRedo()
    {
        var selection = new Patch();
        var vm = Editing(selection);
        RemoveBackgroundAroundAClick(vm);
        int opened = selection.Opened;

        OnUi(() => vm.UndoEditCommand.Execute(null));
        Until(() => vm.CurrentSession!.CutOutPath is null);
        Assert.IsNull(_ui.Invoke(() => vm.CurrentSession!.CutOutMask), "the background is back");

        OnUi(() => vm.RedoEditCommand.Execute(null));
        Until(() => vm.CurrentSession!.CutOutMask is not null);
        Assert.IsNotNull(_ui.Invoke(() => vm.CurrentSession!.CutOutMask), "the kept mask is drawn through again");
        Assert.AreEqual(opened, selection.Opened, "and not picked again");
    }

    [TestMethod]
    [CoversNode("images-edit-remove-background")]
    public void KeepBackground_PutsItBack()
    {
        var vm = Editing(new Patch());
        RemoveBackgroundAroundAClick(vm);

        OnUi(() => vm.Editor.KeepBackgroundCommand.Execute(null));
        Until(() => vm.CurrentSession!.CutOutPath is null);

        Assert.IsNull(_ui.Invoke(() => vm.CurrentSession!.Recipe.CutOut));
        Assert.IsFalse(_ui.Invoke(() => vm.Editor.RemovesBackground));
    }

    [TestMethod]
    [CoversNode("images-edit-remove-background")]
    public void AFoundPhrase_IsWhatIsKept_PickedAgainAtFullSize()
    {
        var selection = new Patch();
        var vm = Editing(selection);
        OnUi(() => vm.Cutter.Phrase = "mug");
        OnUi(() => vm.Cutter.FindCommand.Execute(null));
        Until(() => vm.Editor.RemoveBackgroundCommand.CanExecute(null));

        OnUi(() => vm.Editor.RemoveBackgroundCommand.Execute(null));
        Until(() => vm.CurrentSession!.CutOutPath is not null);

        Assert.AreEqual("mug", _ui.Invoke(() => vm.CurrentSession!.Recipe.CutOut!.Phrase));
        CollectionAssert.AreEqual(new[] { "mug", "mug" }, selection.Phrases, "found once to show, once for the mask");
    }

    [TestMethod]
    [CoversNode("images-edit-remove-background")]
    public async Task ACopy_IsSavedTransparentAroundWhatIsKept()
    {
        var copy = Path.Combine(_dir, "mug copy.png");
        var vm = Editing(new Patch(), Shell(saveAs: copy));
        RemoveBackgroundAroundAClick(vm);
        Until(() => vm.Editor.SaveCopyCommand.CanExecute(null));

        await _ui.Invoke(() => vm.Editor.SaveCopyCommand.ExecuteAsync(null));

        using var saved = SKBitmap.Decode(copy);
        Assert.AreEqual((32, 24), (saved.Width, saved.Height));
        Assert.AreEqual(Photo, saved.GetPixel(8, 6));
        Assert.AreEqual(0, saved.GetPixel(0, 0).Alpha, "transparent where the background was");
    }

    [TestMethod]
    [CoversNode("images-edit-remove-background")]
    public async Task AJpegWithItsBackgroundRemoved_IsSavedOnlyAsACopy_OfferedAsAPng()
    {
        var jpeg = Path.Combine(_dir, "mug.jpg");
        using (var bitmap = Flat(32, 24, Photo))
        using (var image = SKImage.FromBitmap(bitmap))
        using (var data = image.Encode(SKEncodedImageFormat.Jpeg, 95))
            File.WriteAllBytes(jpeg, data.ToArray());
        var copy = Path.Combine(_dir, "mug copy.png");
        var shell = Shell(saveAs: copy);
        var vm = Editing(new Patch(), shell, jpeg);
        Assert.IsTrue(_ui.Invoke(() => vm.Editor.CanSaveInPlace), "a JPEG is saved in place while it keeps its background");

        RemoveBackgroundAroundAClick(vm);
        Until(() => vm.Editor.SaveCopyCommand.CanExecute(null));

        Assert.IsFalse(_ui.Invoke(() => vm.Editor.CanSaveInPlace));
        Assert.IsFalse(_ui.Invoke(() => vm.Editor.SaveCommand.CanExecute(null)));
        Assert.AreEqual(Str.Format("Images.Edit.Save.NoteNoTransparencyFormat", "JPEG"), _ui.Invoke(() => vm.Editor.SaveNote));
        await _ui.Invoke(() => vm.Editor.SaveCopyCommand.ExecuteAsync(null));
        await shell.Received().PickSaveFileAsync(Arg.Is<string>(name => name.EndsWith(".png")), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<long>());
        using var saved = SKBitmap.Decode(copy);
        Assert.AreEqual(0, saved.GetPixel(0, 0).Alpha, "the copy is transparent where the background was");
    }

    [TestMethod]
    [CoversNode("images-edit-remove-background")]
    public void APhotoWhoseBackgroundIsRemoved_WaitsForPictureSelection_AndOffersIt()
    {
        File.WriteAllText(ImageRecipeStore.SidecarPath(_photo),
                          ImageRecipeJson.Serialize(ImageRecipe.Identity with { CutOut = new RecipeCutOut { Phrase = "mug" } }));
        var selection = new Patch { State = ToolModelsState.NotDownloaded };
        var vm = Editing(selection);
        Until(() => vm.Remover.HasNotice);

        Assert.IsTrue(_ui.Invoke(() => vm.Remover.CanDownload));
        Assert.AreEqual(Str.Get("Images.RemoveBackground.Download"), _ui.Invoke(() => vm.Remover.ActionLabel));
        Assert.IsFalse(_ui.Invoke(() => vm.Editor.SaveCopyCommand.CanExecute(null)), "not until its background is removed");
        OnUi(() => vm.Remover.ActCommand.Execute(null));
        Assert.IsTrue(selection.Downloading);
    }

    [TestMethod]
    [CoversNode("images-edit-remove-background")]
    public async Task AThumbnail_IsDrawnThroughItsKeptMask()
    {
        var selection = new Patch();
        var cutOut = new RecipeCutOut { Clicks = [new RecipeCutClick(10, 8, true)] };
        _services.Recipes.SaveDebounced(_photo, ImageRecipe.Identity with { CutOut = cutOut });
        _services.Recipes.Flush(_photo);
        var mask = await _services.CutOuts.MakeAsync(_services, _photo, cutOut, selection, null, CancellationToken.None);
        var item = new ImageThumbItem { FilePath = _photo, FileName = Path.GetFileName(_photo) };

        await new ThumbnailLoadTask([item], 32, Shell(), _services, (_, _) => new MadeByTools(CutOut: mask)).RunAsync(CancellationToken.None);
        Until(() => item.Thumbnail is not null);

        var thumb = new System.Windows.Media.Imaging.FormatConvertedBitmap(item.Thumbnail!, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var corner = new byte[4];
        thumb.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), corner, 4, 0);
        Assert.AreEqual(0, corner[3], "the thumbnail shows the photo without its background");
    }

    /// <summary>Picture selection that picks the patch from (4, 2) to (12, 10) of the photo, but for its top-left
    /// pixel, wherever it is asked; a phrase finds it twice.</summary>
    private sealed class Patch : IPictureSelection, IPictureSelectionSession
    {
        public const int Left = 4, Top = 2, Right = 12, Bottom = 10;

        public sealed record Ask(IReadOnlyList<PicturePoint> Points, PictureBox? Box, int Width, int Height);

        public List<Ask> Asks { get; } = [];
        public List<string> Phrases { get; } = [];
        public int Opened { get; private set; }
        public byte[]? Pixels { get; private set; }
        public bool Ended { get; private set; }
        public bool Downloading { get; private set; }
        public bool FailNext { get; set; }

        public ToolModelsState State { get; init; } = ToolModelsState.Ready;

        public string ModelsVersion => "patch-1";
        public long DownloadBytes => 1 << 30;
        public int DownloadPercent => 0;

        public string? DownloadFailure { get; init; }
        public event EventHandler? Changed { add { } remove { } }
        public void Download() => Downloading = true;
        public int Width { get; private set; }
        public int Height { get; private set; }

        public Task<IPictureSelectionSession> OpenAsync(ReadOnlyMemory<byte> rgba, int width, int height, CancellationToken ct)
        {
            Opened++;
            (Pixels, Width, Height, Ended) = (rgba.ToArray(), width, height, false);
            return Task.FromResult<IPictureSelectionSession>(this);
        }

        private PictureMask Mask(int width, int height)
        {
            double sx = (double)width / Width, sy = (double)height / Height;
            var pixels = new byte[width * height];
            for (int y = (int)(Top * sy); y < (int)(Bottom * sy); y++)
                for (int x = (int)(Left * sx); x < (int)(Right * sx); x++)
                    pixels[y * width + x] = 255;
            pixels[(int)(Top * sy) * width + (int)(Left * sx)] = 0;
            return new PictureMask(pixels, width, height, new PictureBox(Left, Top, Right, Bottom));
        }

        public Task<PictureMask> SelectAsync(IReadOnlyList<PicturePoint> points, PictureBox? box, int maskWidth, int maskHeight, CancellationToken ct)
        {
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("the host fell over");
            }
            Asks.Add(new Ask(points, box, maskWidth, maskHeight));
            return Task.FromResult(Mask(maskWidth, maskHeight));
        }

        public Task<IReadOnlyList<PictureFind>> FindAsync(string phrase, IReadOnlyList<PictureExample> examples, int maskWidth, int maskHeight, CancellationToken ct)
        {
            Phrases.Add(phrase);
            var mask = Mask(maskWidth, maskHeight);
            return Task.FromResult<IReadOnlyList<PictureFind>>([new PictureFind(0.9, mask.Bounds!, mask), new PictureFind(0.7, mask.Bounds!, mask)]);
        }

        public ValueTask DisposeAsync()
        {
            Ended = true;
            return ValueTask.CompletedTask;
        }
    }
}
