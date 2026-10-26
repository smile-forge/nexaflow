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
/// Controls that need models wait for them: disabled, saying why, while the panel that needs them offers the download
/// over the picture — once, standing back where a picture tool's own banner already offers it.
/// </summary>
[TestClass]
[CoversNode("images-edit-models")]
public class ModelsGateTests
{
    private static readonly Func<Action, Task> Here = action => { action(); return Task.CompletedTask; };

    [TestMethod]
    public void ModelsNotHere_AreOfferedOnlyWhileWanted_AndDisableWithTheirSize()
    {
        var models = new Models { State = ToolModelsState.NotDownloaded, DownloadBytes = 1800L << 20 };
        var gate = new ModelsGate(models, "Images.Models.Picking.", Here);

        Assert.IsTrue(gate.IsOffered, "this machine runs them, so their controls show");
        Assert.IsFalse(gate.IsReady);
        Assert.IsFalse(gate.ShowsOffer, "no panel wants them yet");
        Assert.AreEqual(Str.Format("Images.Models.NeedsTipFormat", Str.Get("Images.Models.Picking.Name"), SizeFormatter.FormatBytes(1800L << 20)),
                        gate.DisabledReason);

        gate.IsWanted = true;
        Assert.IsTrue(gate.ShowsOffer, "a panel that needs them is open");
        StringAssert.Contains(gate.Notice!, SizeFormatter.FormatBytes(1800L << 20));

        gate.DownloadCommand.Execute(null);
        Assert.AreEqual(1, models.Downloads);
    }

    [TestMethod]
    public void ModelsComing_SayHowFar_AndAStoppedDownloadSaysWhy()
    {
        var models = new Models { State = ToolModelsState.Downloading, DownloadPercent = 42 };
        var gate = new ModelsGate(models, "Images.Models.Restoring.", Here) { IsWanted = true };

        Assert.IsFalse(gate.CanDownload, "already coming");
        StringAssert.Contains(gate.Notice!, "42");
        StringAssert.Contains(gate.DisabledReason!, "42");

        models.State = ToolModelsState.NotDownloaded;
        models.DownloadFailure = "the disk is full";
        models.Raise();
        StringAssert.Contains(gate.Notice!, "the disk is full");
        Assert.IsTrue(gate.CanDownload, "offered again");
    }

    [TestMethod]
    public void ModelsHere_OrNeverRunnableHere_SayNothing()
    {
        var ready = new ModelsGate(new Models { State = ToolModelsState.Ready }, "Images.Models.Erasing.", Here) { IsWanted = true };
        Assert.IsTrue(ready.IsReady);
        Assert.IsNull(ready.DisabledReason);
        Assert.IsFalse(ready.ShowsOffer);

        var never = new ModelsGate(new Models { State = ToolModelsState.Unavailable }, "Images.Models.Erasing.", Here) { IsWanted = true };
        Assert.IsFalse(never.IsOffered, "a machine that cannot run them does not show their controls");
        Assert.IsFalse(never.ShowsOffer);

        var absent = new ModelsGate(null, "Images.Models.Erasing.", Here) { IsWanted = true };
        Assert.IsFalse(absent.IsOffered);
        Assert.IsFalse(absent.ShowsOffer);
    }

    [TestMethod]
    public void ModelsArriving_FreeTheirControls_AndTheOfferGoes()
    {
        var models = new Models { State = ToolModelsState.NotDownloaded, DownloadBytes = 1 };
        var gate = new ModelsGate(models, "Images.Models.Picking.", Here) { IsWanted = true };
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
        var gate = new ModelsGate(new Models { State = ToolModelsState.NotDownloaded, DownloadBytes = 1 }, "Images.Models.Picking.", Here) { IsWanted = true };
        var banner = new Offer();
        gate.Defer(banner);
        Assert.IsTrue(gate.ShowsOffer);

        banner.CanDownload = true;
        Assert.IsFalse(gate.ShowsOffer, "one download is offered once");

        banner.CanDownload = false;
        Assert.IsTrue(gate.ShowsOffer);
    }

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
            var vm = new ImageViewModel([photo], shell, ImageViewMode.Edit, new ImageEditingServices(VirtualFileSystem.Instance, new ImageRecipeStore(TimeSpan.FromHours(1))),
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

            OnUi(() => editor.Tool = EditTool.Adjust);
            Assert.IsTrue(editor.RestoringModels.ShowsOffer);
            Assert.IsFalse(editor.PickingModels.ShowsOffer);
            Assert.IsFalse(editor.CanToggleAiEnhance, "AI enhance waits for the picture tools");
            Assert.IsFalse(editor.UpscaleTwiceOffered);

            OnUi(() => editor.Tool = EditTool.CutOut);
            Assert.IsTrue(editor.PickingModels.ShowsOffer);

            OnUi(() => vm.ViewMode = ImageViewMode.Carousel);
            Assert.IsFalse(editor.PickingModels.ShowsOffer, "out of the editor, nothing is offered");

            selection.State.Returns(ToolModelsState.Ready);
            selection.Changed += Raise.Event();
            Until(() => ui.Invoke(() => editor.PickingModels.IsReady));
            OnUi(() => vm.ViewMode = ImageViewMode.Edit);
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
