using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Images;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Erasing;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Restoring;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Drafts;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// New picture and the pictures that come into a canvas: an Images tab with no picture making a blank canvas as a draft and
/// becoming it, a picture drawn on the start screen arriving as the canvas's first layer in the proportions nearest it,
/// Generate layer shown while it draws and adding nothing once stopped, pictures dropped on a photo made layers fitted to it,
/// and pictures opened from the start screen shown in place.
/// <para>
/// The image model here is a stand-in whose jobs end when the test says, with the file it names.
/// </para>
/// </summary>
[TestClass]
public class ImageStartTests
{
    private static readonly SKColor Ground = new(100, 150, 200);

    private string _dir = "";
    private string _photo = "";
    private ViewerUiThread _ui = null!;
    private ImageEditingServices _services = null!;
    private ImagesConfig _config = null!;

    // The image model's jobs, in the order they were asked for: what was asked, how each ends, and whether it was stopped.
    private readonly List<(PictureAsked Asked, TaskCompletionSource<string?> Made)> _jobs = [];
    private int _stops;

    [TestInitialize]
    public void Make()
    {
        _dir = TestTemp.CreateSubdirectory("nexaflow-start-").FullName;
        _photo = Path.Combine(_dir, "wall.png");
        using (var bitmap = Flat(32, 24, Ground))
            File.WriteAllBytes(_photo, EncodePng(bitmap));
        _ui = new ViewerUiThread();
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new ZipArchiveHandler());
        _services = new ImageEditingServices(vfs, new ImageRecipeStore(TimeSpan.FromHours(1)),
                                             new RestoredPictures(Path.Combine(_dir, "restored"), RestoredPictures.DefaultBudget),
                                             new CutOutMasks(Path.Combine(_dir, "cutouts"), CutOutMasks.DefaultBudget),
                                             new ErasedPictures(Path.Combine(_dir, "erased"), ErasedPictures.DefaultBudget));
        _config = new ImagesConfig();
    }

    [TestCleanup]
    public void Clean()
    {
        _ui.Dispose();
        _services.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private IShellServices Shell(IReadOnlyList<string>? opened = null)
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => _ui.Run(call.Arg<Action>()));
        shell.PickOpenFileAsync(Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>()).Returns(opened?.FirstOrDefault());
        return shell;
    }

    /// <summary>A workspace drawing pictures in three proportions, its jobs held until the test ends them.</summary>
    private IAIService Ai()
    {
        var ai = Substitute.For<IAIService>();
        ai.MakesPictures.Returns(new PictureMaking(new PictureChoices(["1:1", "16:9", "9:16"]), null));
        ai.StatusOf(Arg.Any<AiAbility>()).Returns(new AiAbilityStatus(AiAbilityState.Ready));
        ai.MakePicture(Arg.Any<PictureAsked>()).Returns(call =>
        {
            var made = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _jobs.Add((call.Arg<PictureAsked>(), made));
            return new PictureJob(made.Task, () => { _stops++; made.TrySetResult(null); });
        });
        return ai;
    }

    private ArtistViewModel Artist(string? path, IShellServices? shell = null, IAIService? ai = null)
        => _ui.Invoke(() => new ArtistViewModel(path, shell ?? Shell(), _services, ai: ai, config: _config)
        {
            MakeDraft = (path, folder) => new DraftSaving(path, folder, (_, _, _) => Task.FromResult<string?>(null), _ => { }, (_, _) => Task.CompletedTask),
        });

    private string Png(string name, int width, int height)
    {
        var path = Path.Combine(_dir, name);
        using var bitmap = Flat(width, height, Ground);
        File.WriteAllBytes(path, EncodePng(bitmap));
        return path;
    }

    private LayeredPicture Picture(ArtistViewModel vm) => _ui.Invoke(() => vm.CurrentDocument!.Picture);

    private void OnUi(Action action) => _ui.Run(action).GetAwaiter().GetResult();

    private void Until(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10) && !_ui.Invoke(done)) Thread.Sleep(10);
    }

    private void SetSize(ArtistViewModel vm, int width, int height) => OnUi(() =>
    {
        vm.Start.Setup.Preset = vm.Start.Setup.Custom;
        vm.Start.Setup.WidthText = width.ToString();
        vm.Start.Setup.HeightText = height.ToString();
    });

    [TestMethod]
    [CoversNode("images-start")]
    public async Task ATabWithNoPicture_MakesABlankCanvasAsADraft_AndBecomesIt()
    {
        var vm = Artist(null);
        ArtistPicture? shown = null;
        vm.PictureReplaced += s => shown = s;
        Assert.IsTrue(_ui.Invoke(() => vm.IsStarting), "no picture: the start screen shows");

        SetSize(vm, 64, 48);
        await _ui.Invoke(() => vm.Start.CreateCommand.ExecuteAsync(null));
        Until(() => vm.CurrentDocument is not null);

        Assert.IsFalse(_ui.Invoke(() => vm.IsStarting));
        Assert.IsTrue(_ui.Invoke(() => vm.IsEditing), "opened for editing");
        var picture = Picture(vm);
        Assert.AreEqual((64, 48), (picture.Width, picture.Height));
        Assert.IsInstanceOfType<PixelLayer>(picture.Layers.Single(), "one empty pixel layer");
        Assert.IsNotNull(shown);
        Assert.IsTrue(shown!.IsDraft, "the page follows: a draft");
        var draft = shown.Path;
        StringAssert.StartsWith(draft, DraftLocation.Root, "kept as a draft in the temp folder");
        Assert.AreEqual(draft, _ui.Invoke(() => vm.Draft?.Path), "with its Save as");
        Assert.AreEqual(draft, _config.RecentCanvases.First(), "remembered for New picture");

        await _ui.Invoke(() => vm.Start.RefreshAsync());
        var recent = _ui.Invoke(() => vm.Start.Recent.First());
        Assert.IsTrue(recent.IsDraft, "listed as the draft it is");
    }

    [TestMethod]
    [CoversNode("images-start")]
    public async Task APictureDrawnOnTheStartScreen_ArrivesAsTheCanvasesFirstLayer_InTheProportionsNearestIt()
    {
        var vm = Artist(null, ai: Ai());
        SetSize(vm, 90, 160);
        OnUi(() => vm.Start.Generation!.Prompt = "a lighthouse at dusk over a stormy sea");
        Until(() => vm.Start.GenerateCommand.CanExecute(null));

        await _ui.Invoke(() => vm.Start.GenerateCommand.ExecuteAsync(null));
        Until(() => vm.CurrentDocument is not null);

        var (asked, made) = _jobs.Single();
        Assert.AreEqual("9:16", asked.Aspect, "the proportions nearest a 90 × 160 canvas");
        Assert.IsFalse(asked.OpensWhenMade, "the canvas takes it in, nothing else opens it");
        Assert.AreEqual(1, _ui.Invoke(() => vm.Pictures.Pending.Count), "shown while it is drawn");

        var drawn = Png("drawn.png", 18, 32);
        made.SetResult(drawn);
        Until(() => vm.Pictures.Pending.Count == 0 && vm.CurrentDocument!.Picture.Layers.Length == 2);

        var picture = Picture(vm);
        var layer = (PictureLayer)picture.Layers[0];
        Assert.AreEqual(drawn, layer.Source.Path, "beneath the empty layer, the canvas's first");
        Assert.AreEqual("a lighthouse at dusk over…", layer.Name, "named by what was asked for");
        Assert.AreEqual((36.0, 64.0, 1.0), (layer.Placement.OffsetX, layer.Placement.OffsetY, layer.Placement.Scale), "centred at its own size");
        Assert.IsInstanceOfType<PixelLayer>(picture.Layers[1]);
    }

    [TestMethod]
    [CoversNode("images-start")]
    public void GenerateLayer_IsShownWhileItDraws_AStoppedOneAddsNothing_AndOneDrawnGoesAboveTheActiveLayer()
    {
        var path = Path.Combine(_dir, "canvas.ora");
        _services.WriteLayeredAsync(LayeredPicture.Blank(40, 36, LayerBackground.Transparent, "Layer 1"), path).GetAwaiter().GetResult();
        var vm = Artist(path, ai: Ai());
        Until(() => vm.CurrentDocument is not null);
        var bottom = Picture(vm).ActiveId;

        OnUi(() =>
        {
            vm.Pictures.AskForLayerCommand.Execute(null);
            vm.Pictures.Options!.Prompt = "clouds";
        });
        Assert.AreEqual("1:1", _ui.Invoke(() => vm.Pictures.Options!.Aspect?.Value), "a 40 × 36 canvas is nearest square");
        OnUi(() => vm.Pictures.GenerateLayerCommand.Execute(null));
        Assert.AreEqual("clouds", _ui.Invoke(() => vm.Pictures.Pending.Single().Name));
        Assert.IsFalse(_ui.Invoke(() => vm.Pictures.IsAsking), "set going, it closes");

        OnUi(() => vm.Pictures.Pending.Single().StopCommand.Execute(null));
        Until(() => vm.Pictures.Pending.Count == 0);
        Assert.AreEqual(1, _stops, "Stop stops the job");
        Assert.AreEqual(1, Picture(vm).Layers.Length, "stopped, nothing is added");

        OnUi(() =>
        {
            vm.Pictures.AskForLayerCommand.Execute(null);
            vm.Pictures.GenerateLayerCommand.Execute(null);
        });
        _jobs[1].Made.SetResult(Png("clouds.png", 80, 30));
        Until(() => vm.CurrentDocument!.Picture.Layers.Length == 2);
        var picture = Picture(vm);
        Assert.AreEqual(bottom, picture.Layers[0].Id, "above the layer active when it was asked for");
        var clouds = (PictureLayer)picture.Layers[1];
        Assert.AreEqual(clouds.Id, picture.ActiveId);
        Assert.AreEqual(0.5, clouds.Placement.Scale, 1e-9, "fitted to the canvas, scaled down");
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public async Task PicturesDroppedOnAPhoto_BecomeLayersFittedToTheCanvas()
    {
        var vm = Artist(_photo);
        Until(() => vm.Canvas is not null);
        var big = Png("big.png", 64, 48);

        await _ui.Invoke(() => vm.Pictures.AddFilesAsync([big, Path.Combine(_dir, "notes.txt")]));

        var picture = Picture(vm);
        Assert.AreEqual(2, picture.Layers.Length, "the photo, then what was dropped; what is no picture passed over");
        Assert.AreEqual(_photo, ((PictureLayer)picture.Layers[0]).Source.Path);
        var dropped = (PictureLayer)picture.Layers[1];
        Assert.AreEqual((big, "big"), (dropped.Source.Path, dropped.Name));
        Assert.AreEqual((0.0, 0.0, 0.5), (dropped.Placement.OffsetX, dropped.Placement.OffsetY, dropped.Placement.Scale), "twice the canvas, halved");
        Assert.AreEqual(dropped.Id, picture.ActiveId);
    }

    [TestMethod]
    [CoversNode("images-start")]
    public async Task APictureOpenedFromTheStartScreen_IsEditedInPlace()
    {
        var vm = Artist(null, Shell(opened: [_photo]));
        ArtistPicture? shown = null;
        vm.PictureReplaced += s => shown = s;

        await _ui.Invoke(() => vm.Start.OpenPictureCommand.ExecuteAsync(null));
        Until(() => vm.CurrentSession is not null);

        Assert.IsFalse(_ui.Invoke(() => vm.IsStarting));
        Assert.AreEqual(_photo, _ui.Invoke(() => vm.CurrentPath));
        Assert.AreEqual(_photo, shown!.Path);
        Assert.IsFalse(shown.IsDraft);
        Assert.IsNull(_ui.Invoke(() => vm.Draft), "pictures opened where they live are no draft");
    }
}
