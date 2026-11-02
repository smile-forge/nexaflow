using Nexaflow.IO.Common;
using System.IO;
using System.ComponentModel;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// Controls that need models wait for them: disabled, saying why, while the panel that needs them offers AI picture editing's
/// models over the picture — all of them as one, once, standing back where a picture tool's own banner already offers them.
/// </summary>
[TestClass]
[CoversNode("images-edit-models")]
public class ModelsGateTests
{
    private static readonly Func<Action, Task> Here = action => { action(); return Task.CompletedTask; };

    [TestMethod]
    public void AiPictureEditingsModels_AreOfferedAsOne_SizedTogether_AndOnlyWhatIsMissingIsFetched()
    {
        var selection = new Models { State = ToolModelsState.NotDownloaded, DownloadBytes = 1800L << 20 };
        var erasing = new Models { State = ToolModelsState.NotDownloaded, DownloadBytes = 200L << 20 };
        var tools = new Models { State = ToolModelsState.Ready };
        var set = Set(selection, erasing, tools, null);

        Assert.IsTrue(set.CanDownload);
        Assert.AreEqual(2000L << 20, set.MissingBytes, "what is here, and what the app lacks, cost nothing");
        Assert.AreEqual(Str.Format("Images.Models.OfferFormat", SizeFormatter.FormatBytes(2000L << 20)), set.Notice, "one offer, one size");

        set.DownloadCommand.Execute(null);
        Assert.AreEqual((1, 1, 0), (selection.Downloads, erasing.Downloads, tools.Downloads), "one press fetches every set not here");

        selection.State = ToolModelsState.Downloading;
        selection.DownloadPercent = 50;
        erasing.State = ToolModelsState.Downloading;
        erasing.DownloadPercent = 100;
        selection.Raise();
        Assert.AreEqual(Str.Format("Images.Models.DownloadingFormat", 55), set.Notice, "how far, each set by its share of what was left");

        erasing.State = ToolModelsState.Ready;
        selection.State = ToolModelsState.NotDownloaded;
        selection.DownloadFailure = "the disk is full";
        selection.Raise();
        Assert.AreEqual(Str.Format("Images.Models.StoppedFormat", "the disk is full"), set.Notice);
        Assert.IsTrue(set.CanDownload, "offered again");

        selection.State = ToolModelsState.Ready;
        selection.Raise();
        Assert.IsNull(set.Notice, "all here: nothing to say");
        Assert.IsFalse(set.CanDownload);
    }

    [TestMethod]
    public void ModelsNotHere_AreOfferedOnlyWhileWanted_AndDisableTheirControls()
    {
        var models = new Models { State = ToolModelsState.NotDownloaded, DownloadBytes = 1800L << 20 };
        var gate = new ModelsGate(models, Set(models), Here);

        Assert.IsTrue(gate.IsOffered, "this machine runs them, so their controls show");
        Assert.IsFalse(gate.IsReady);
        Assert.IsFalse(gate.ShowsOffer, "no panel wants them yet");
        Assert.AreEqual(Str.Get("Images.Models.NeedsTip"), gate.DisabledReason, "the models, not which of them");

        gate.IsWanted = true;
        Assert.IsTrue(gate.ShowsOffer, "a panel that needs them is open");
    }

    [TestMethod]
    public void ModelsComing_SayHowFar()
    {
        var models = new Models { State = ToolModelsState.Downloading, DownloadPercent = 42 };
        var gate = new ModelsGate(models, Set(models), Here) { IsWanted = true };

        StringAssert.Contains(gate.DisabledReason!, "42");
        Assert.IsTrue(gate.ShowsOffer, "the banner says how far they have come");
    }

    [TestMethod]
    public void ModelsHere_OrNeverRunnableHere_SayNothing()
    {
        var here = new Models { State = ToolModelsState.Ready };
        var ready = new ModelsGate(here, Set(here), Here) { IsWanted = true };
        Assert.IsTrue(ready.IsReady);
        Assert.IsNull(ready.DisabledReason);
        Assert.IsFalse(ready.ShowsOffer);

        var cannot = new Models { State = ToolModelsState.Unavailable };
        var never = new ModelsGate(cannot, Set(cannot), Here) { IsWanted = true };
        Assert.IsFalse(never.IsOffered, "a machine that cannot run them does not show their controls");
        Assert.IsFalse(never.ShowsOffer);

        var absent = new ModelsGate(null, Set(), Here) { IsWanted = true };
        Assert.IsFalse(absent.IsOffered);
        Assert.IsFalse(absent.ShowsOffer);
    }

    [TestMethod]
    public void ModelsArriving_FreeTheirControls_AndTheOfferGoes()
    {
        var models = new Models { State = ToolModelsState.NotDownloaded, DownloadBytes = 1 };
        var gate = new ModelsGate(models, Set(models), Here) { IsWanted = true };
        var changed = new List<string?>();
        gate.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        models.State = ToolModelsState.Ready;
        models.Raise();

        Assert.IsTrue(gate.IsReady);
        Assert.IsFalse(gate.ShowsOffer);
        CollectionAssert.Contains(changed, nameof(ModelsGate.IsReady), "the controls bound to it are told");
        CollectionAssert.Contains(changed, nameof(ModelsGate.ShowsOffer));
    }

    [TestMethod]
    public void ABannerAlreadyOfferingTheModels_HasTheGateStandBack()
    {
        var models = new Models { State = ToolModelsState.NotDownloaded, DownloadBytes = 1 };
        var gate = new ModelsGate(models, Set(models), Here) { IsWanted = true };
        var banner = new Offer();
        gate.Defer(banner);
        Assert.IsTrue(gate.ShowsOffer);

        banner.CanDownload = true;
        Assert.IsFalse(gate.ShowsOffer, "one download is offered once");

        banner.CanDownload = false;
        Assert.IsTrue(gate.ShowsOffer);
    }

    private static PictureModels Set(params IToolModels?[] sets) => new(sets, Here);

    // ── In the editor ───────────────────────────────────────────────────────

    [TestMethod]
    public void TheEditor_WithoutModels_RetouchesByBrushAndContentAware_AndEachPanelWantsWhatItNeeds()
    {
        var dir = TestTemp.CreateSubdirectory("nexaflow-models-gate-").FullName;
        using var ui = new ViewerUiThread();
        try
        {
            var photo = Path.Combine(dir, "plain.png");
            using (var bitmap = Flat(24, 16, new SKColor(90, 120, 160))) File.WriteAllBytes(photo, EncodePng(bitmap));
            var shell = Substitute.For<IShellServices>();
            shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => ui.Run(call.Arg<Action>()));

            var selection = Missing<IPictureSelection>(1800L << 20);
            var erasing = Missing<IPictureErasing>(210L << 20);
            var pictures = Missing<IPictureTools>(640L << 20);
            pictures.Restores(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int>()).Returns(true);
            var vm = new ArtistViewModel(photo, shell, new ImageEditingServices(VirtualFileSystem.Instance, new ImageRecipeStore(TimeSpan.FromHours(1))),
                                        pictures: pictures, selection: selection, erasing: erasing);
            Until(() => vm.Canvas is not null);
            var editor = vm.Editor;
            void OnUi(Action action) => ui.Run(action).Wait();

            OnUi(() => editor.Tool = EditTool.Retouch);
            Assert.IsTrue(editor.EraseMarks.IsBrushing, "picking waits for picture selection, so retouching starts with the brush");
            Assert.AreEqual(EraseFill.ContentAware, editor.EraseMarks.Fill, "AI fill waits for picture erasing");
            Assert.IsTrue(editor.PickingModels.ShowsOffer);
            Assert.IsTrue(editor.ErasingModels.ShowsOffer);
            Assert.IsFalse(editor.RestoringModels.ShowsOffer, "Retouch does not need the picture tools");
            Assert.IsTrue(editor.ShowsModelsOffer, "the banner offers them");
            Assert.AreEqual(Str.Format("Images.Models.OfferFormat", SizeFormatter.FormatBytes((1800L + 210 + 640) << 20)), editor.Models.Notice,
                            "all of AI picture editing's models, as one");

            OnUi(() => editor.Tool = EditTool.Adjust);
            Assert.IsTrue(editor.RestoringModels.ShowsOffer);
            Assert.IsFalse(editor.PickingModels.ShowsOffer);
            Assert.IsFalse(editor.CanToggleAiEnhance, "AI enhance waits for the picture tools");
            Assert.IsFalse(editor.UpscaleTwiceOffered);

            OnUi(() => editor.Tool = EditTool.Select);
            Assert.IsTrue(editor.PickingModels.ShowsOffer);

            OnUi(() => editor.Tool = EditTool.Crop);
            Assert.IsFalse(editor.PickingModels.ShowsOffer, "a panel needing no model offers none");
            Assert.IsFalse(editor.ShowsModelsOffer);

            selection.State.Returns(ToolModelsState.Ready);
            selection.Changed += Raise.Event();
            Until(() => ui.Invoke(() => editor.PickingModels.IsReady));
            OnUi(() => editor.Tool = EditTool.Select);
            Assert.IsFalse(editor.PickingModels.ShowsOffer, "here now, so nothing to offer");
            vm.Dispose();
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    private static T Missing<T>(long bytes) where T : class, IToolModels
    {
        var models = Substitute.For<T>();
        models.State.Returns(ToolModelsState.NotDownloaded);
        models.DownloadBytes.Returns(bytes);
        models.ModelsVersion.Returns("test-1");
        return models;
    }

    private static void Until(Func<bool> condition)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(10)) Thread.Sleep(20);
        Assert.IsTrue(condition(), "timed out");
    }

    private sealed class Models : IToolModels
    {
        public ToolModelsState State { get; set; }
        public long DownloadBytes { get; set; }
        public int DownloadPercent { get; set; }
        public string? DownloadFailure { get; set; }
        public string ModelsVersion => "test-1";
        public int Downloads { get; private set; }
        public event EventHandler? Changed;
        public void Download() => Downloads++;
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
    }

    private sealed class Offer : IDownloadOffer
    {
        private bool _canDownload;

        public bool CanDownload
        {
            get => _canDownload;
            set { _canDownload = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanDownload))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
