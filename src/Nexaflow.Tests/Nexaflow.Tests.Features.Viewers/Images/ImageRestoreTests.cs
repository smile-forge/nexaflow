using System.IO;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Restoring;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// AI enhance and AI upscale in the editor: the photo restored by the picture tools once, kept, and shown; laid over the
/// photo as it was at its strength; made larger, every other edit keeping its place; saved from the restoration; and,
/// where the tools are not here or fail, the banner saying so and offering what to do.
/// <para>
/// The tools here are a stand-in that "restores" a photo by turning each colour to its opposite as it enlarges it, so
/// what the restoration made is plain to see in every pixel.
/// </para>
/// </summary>
[TestClass]
[CoversNode("images-edit-restore")]
public class ImageRestoreTests
{
    /// <summary>The photo: one colour, 32 × 24, so the restoration's opposite colour is unmistakable.</summary>
    private static readonly SKColor Photo = new(200, 100, 50);

    private static readonly SKColor Opposite = new(55, 155, 205);

    private string _dir = "";
    private string _photo = "";
    private ViewerUiThread _ui = null!;
    private RestoredPictures _restored = null!;

    [TestInitialize]
    public void Make()
    {
        _dir = Directory.CreateTempSubdirectory("nexaflow-restore-edit-").FullName;
        _photo = Path.Combine(_dir, "harbour.png");
        using (var bitmap = Flat(32, 24, Photo)) File.WriteAllBytes(_photo, EncodePng(bitmap));
        _restored = new RestoredPictures(Path.Combine(_dir, "restored"), RestoredPictures.DefaultBudget);
        _ui = new ViewerUiThread();
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

    private ImageViewModel Editing(Opposites tools, IShellServices? shell = null)
    {
        var vm = new ImageViewModel([_photo], shell ?? Shell(), ImageViewMode.Edit,
                                    new ImageEditingServices(new ImageRecipeStore(TimeSpan.FromHours(1)), _restored), pictures: tools);
        Until(() => vm.Canvas is not null);
        Assert.IsNotNull(vm.Canvas, "the photo was never decoded and presented");
        return vm;
    }

    /// <summary>Waits — on the viewer's UI thread, where it is read — until <paramref name="done"/>.</summary>
    private void Until(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10) && !_ui.Invoke(done)) Thread.Sleep(10);
    }

    /// <summary>The middle pixel of a saved picture, and its size.</summary>
    private static (SKColor Middle, int Width, int Height) Saved(string path)
    {
        using var bitmap = SKBitmap.Decode(path);
        return (bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2), bitmap.Width, bitmap.Height);
    }

    [TestMethod]
    public void AiEnhance_RestoresThePhotoOnce_AndShowsIt_TheStrengthAloneNeverRestoringAgain()
    {
        var tools = new Opposites();
        var vm = Editing(tools);

        vm.Editor.AiEnhance = true;
        Until(() => vm.CurrentSession!.RestoredPath is not null);

        Assert.AreEqual(1, tools.Restorations.Count);
        Assert.AreEqual((32, 24, 1, true), (tools.Restorations[0].Width, tools.Restorations[0].Height, tools.Restorations[0].Scale, tools.Restorations[0].Faces),
                        "the whole photo, upright, kept at its size, faces repaired");
        Assert.IsFalse(vm.Restorer.HasNotice, "nothing left to say once it shows");

        vm.Editor.EnhanceStrength = 40;
        vm.Editor.EnhanceStrength = 70;
        Thread.Sleep(200);
        Assert.AreEqual(1, tools.Restorations.Count, "the strength lays the same restoration over the photo");
    }

    [TestMethod]
    public async Task AiUpscale_SavesThePictureTwiceTheSize_FromTheRestoration()
    {
        var copy = Path.Combine(_dir, "copy.png");
        var tools = new Opposites();
        var vm = Editing(tools, Shell(saveAs: copy));

        vm.Editor.UpscaleTwice = true;
        Until(() => vm.CurrentSession!.RestoredPath is not null && vm.Editor.SaveCopyCommand.CanExecute(null));

        Assert.IsTrue(vm.Editor.AiEnhance, "upscaling restores the photo");
        Assert.AreEqual(new SKSizeI(64, 48), vm.CurrentSession!.OutputSize(vm.CurrentRecipe));
        await vm.Editor.SaveCopyCommand.ExecuteAsync(null);

        var (middle, width, height) = Saved(copy);
        Assert.AreEqual((64, 48), (width, height));
        AssertNear(Opposite, middle, because: "the saved picture is the restoration's");
    }

    [TestMethod]
    public async Task TheStrength_LaysTheRestorationOverThePhotoAsItWas()
    {
        var copy = Path.Combine(_dir, "copy.png");
        var vm = Editing(new Opposites(), Shell(saveAs: copy));
        vm.Editor.AiEnhance = true;
        Until(() => vm.CurrentSession!.RestoredPath is not null);

        vm.Editor.EnhanceStrength = 50;
        await vm.Editor.SaveCopyCommand.ExecuteAsync(null);

        AssertNear(new SKColor(127, 127, 127), Saved(copy).Middle, tolerance: 8, because: "half the restoration, half the photo");
    }

    [TestMethod]
    public void ToolsNotDownloaded_AreOfferedInTheBanner_AndThePhotoWaitsToBeSaved()
    {
        var tools = new Opposites { State = ToolModelsState.NotDownloaded };
        var vm = Editing(tools);

        vm.Editor.AiEnhance = true;
        Until(() => vm.Restorer.HasNotice);

        Assert.IsTrue(vm.Restorer.CanDownload);
        Assert.IsFalse(vm.Editor.SaveCopyCommand.CanExecute(null), "not until its restoration is made");
        Assert.AreEqual(Str.Get("Images.Restore.Download"), vm.Restorer.ActionLabel);
        _ui.Run(() => vm.Restorer.ActCommand.Execute(null)).GetAwaiter().GetResult();
        Assert.AreEqual(1, tools.Downloads);

        tools.Arrive();
        Until(() => vm.CurrentSession!.RestoredPath is not null);
        Assert.AreEqual(1, tools.Restorations.Count, "once the tools are here, the photo is restored");
    }

    [TestMethod]
    public void ARestorationThatFails_SaysSo_AndIsTriedAgainWhenAsked()
    {
        var tools = new Opposites { Fails = true };
        var vm = Editing(tools);

        vm.Editor.AiEnhance = true;
        Until(() => vm.Restorer.Failed);
        Assert.IsTrue(vm.Restorer.HasNotice);

        tools.Fails = false;
        Assert.AreEqual(Str.Get("Images.Restore.Retry"), vm.Restorer.ActionLabel);
        _ui.Run(() => vm.Restorer.ActCommand.Execute(null)).GetAwaiter().GetResult();
        Until(() => vm.CurrentSession!.RestoredPath is not null);

        Assert.IsFalse(vm.Restorer.Failed);
        Assert.AreEqual(2, tools.Restorations.Count);
    }

    [TestMethod]
    public void TheMachineCannotRestore_AndTheAiControlsAreNotOffered()
    {
        var vm = Editing(new Opposites { State = ToolModelsState.Unavailable });

        Assert.IsFalse(vm.Editor.CanRestore);
    }

    [TestMethod]
    public async Task ARestoration_IsKept_AndMadeOnceForThePhoto()
    {
        var tools = new Opposites();
        var services = new ImageEditingServices(new ImageRecipeStore(TimeSpan.FromHours(1)), _restored);
        var restore = new RecipeRestore { Scale = 2 };

        var first = await _restored.MakeAsync(services, _photo, restore, tools, null, CancellationToken.None);
        var again = await _restored.MakeAsync(services, _photo, restore with { Strength = 0.3 }, tools, null, CancellationToken.None);

        Assert.AreEqual(first, again, "a strength over it is the same restoration");
        Assert.AreEqual(1, tools.Restorations.Count);
        Assert.AreEqual(first, _restored.Find(_photo, restore, tools.ModelsVersion));
        Assert.IsNull(_restored.Find(_photo, restore with { Faces = false }, tools.ModelsVersion), "a different restoration is made afresh");
    }

    [TestMethod]
    public void ARecipe_IsWrittenAsVersion1_UntilItRestoresThePhoto()
    {
        var plain = ImageRecipe.Identity with { Adjustments = new RecipeAdjustments { Exposure = 1 } };
        var restored = plain with { Restore = new RecipeRestore { Scale = 4, Strength = 0.6, Faces = false } };

        StringAssert.Contains(ImageRecipeJson.Serialize(plain), "\"version\": 1", "a build from before reads it");
        StringAssert.Contains(ImageRecipeJson.Serialize(restored), "\"version\": 2", "a build from before leaves it alone rather than drop the restoration");
        var read = ImageRecipeJson.Deserialize(ImageRecipeJson.Serialize(restored));
        Assert.AreEqual(restored, read.Recipe, "read back as it was written");
        Assert.AreEqual(plain, ImageRecipeJson.Deserialize(ImageRecipeJson.Serialize(plain)).Recipe, "a version 1 file is the same recipe once read");
    }

    /// <summary>Picture tools that restore a photo by turning each colour to its opposite and enlarging it, pixel for pixel.</summary>
    private sealed class Opposites : IPictureTools
    {
        public ToolModelsState State { get; set; } = ToolModelsState.Ready;
        public bool Fails { get; set; }
        public int Downloads { get; private set; }
        public List<PictureRestoreRequest> Restorations { get; } = [];
        public long DownloadBytes => State == ToolModelsState.Ready ? 0 : 643L << 20;
        public int DownloadPercent => 0;
        public string ModelsVersion => "opposites";
        public event EventHandler? Changed;

        public void Download() => Downloads++;

        /// <summary>The download is done.</summary>
        public void Arrive()
        {
            State = ToolModelsState.Ready;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public bool Restores(int width, int height, int scale) => true;

        public Task<PictureRestoration> RestoreAsync(PictureRestoreRequest request, IProgress<double>? progress, CancellationToken ct)
        {
            lock (Restorations) Restorations.Add(request);
            if (Fails) throw new InvalidOperationException("the GPU had no room");
            var source = File.ReadAllBytes(request.Pixels);
            int s = request.Scale, w = request.Width * s, h = request.Height * s;
            var made = new byte[w * h * 4];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int from = ((y / s) * request.Width + x / s) * 4, to = (y * w + x) * 4;
                    for (int c = 0; c < 3; c++) made[to + c] = (byte)(255 - source[from + c]);
                    made[to + 3] = source[from + 3];
                }
            File.WriteAllBytes(request.Into, made);
            progress?.Report(1);
            return Task.FromResult(new PictureRestoration(w, h, 0));
        }
    }
}
