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

    [TestInitialize]
    public void Make()
    {
        _dir = Directory.CreateTempSubdirectory("nexaflow-cut-out-").FullName;
        _photo = Path.Combine(_dir, "mug.png");
        using (var bitmap = Flat(32, 24, Photo)) File.WriteAllBytes(_photo, EncodePng(bitmap));
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
        return shell;
    }

    private ImageViewModel Editing(Patch selection, IShellServices? shell = null)
    {
        var vm = new ImageViewModel([_photo], shell ?? Shell(), ImageViewMode.Edit,
                                    new ImageEditingServices(new ImageRecipeStore(TimeSpan.FromHours(1))), selection: selection);
        Until(() => vm.Canvas is not null);
        Assert.IsNotNull(vm.Canvas, "the photo was never decoded and presented");
        OnUi(() => vm.Editor.Tool = EditTool.CutOut);
        return vm;
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
        var vm = new ImageViewModel([_photo], Shell(), ImageViewMode.Carousel,
                                    new ImageEditingServices(new ImageRecipeStore(TimeSpan.FromHours(1))), selection: selection);
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
        public long DownloadBytes => 1 << 30;
        public int DownloadPercent => 0;
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
