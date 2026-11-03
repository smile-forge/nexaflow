using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Compressed.Handlers;
using Nexaflow.Features.Images;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.CuttingOut;
using Nexaflow.Features.Images.Editing.Erasing;
using Nexaflow.Features.Images.Editing.Layers;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Restoring;
using Nexaflow.Features.Images.Editing.Selection;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.IO.Common;
using Nexaflow.Tests.Fixtures;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The AI edits of a layered picture: Fill sending the picture about the selection with the selection as its mask and laying
/// what comes back exactly over that area under the selection; Expand growing the canvas and drawing its border; Change
/// sending the whole picture, or the active layer alone, and laying the result over it; each one of the workspace's jobs
/// through <see cref="IAIService.MakePicture"/>, and none offered where the workspace has no model that edits.
/// <para>
/// The editing model here is a stand-in whose jobs end when the test says, with a picture of the size it names — half
/// the size sent, so a result laid over its area is seen to be stretched back onto it.
/// </para>
/// </summary>
[TestClass]
[CoversNode("images-edit-ai")]
public class ImageAiEditsTests
{
    private string _dir = "";
    private ViewerUiThread _ui = null!;
    private ImageEditingServices _services = null!;
    private readonly List<(PictureEditAsked Asked, TaskCompletionSource<string?> Made)> _jobs = [];

    [TestInitialize]
    public void Make()
    {
        _dir = TestTemp.CreateSubdirectory("nexaflow-ai-edits-").FullName;
        _ui = new ViewerUiThread();
        var vfs = new VirtualFileSystem();
        vfs.RegisterHandler(new ZipArchiveHandler());   // a layered picture is a package
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

    private IShellServices Shell()
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => _ui.Run(call.Arg<Action>()));
        return shell;
    }

    /// <summary>A workspace with an editing model that takes a picture and a mask — or, <paramref name="edits"/> false, one
    /// that only draws — its jobs held until the test ends them.</summary>
    private IAIService Ai(bool edits = true)
    {
        var ai = Substitute.For<IAIService>();
        var drawing = new PictureChoices(["1:1"]);
        ai.MakesPictures.Returns(new PictureMaking(drawing, null));
        ai.EditsPictures.Returns(edits ? new PictureChoices(["1:1"]) { Sources = 2, Masks = true, KeepsSourceProportions = true } : null);
        ai.StatusOf(Arg.Any<AiAbility>()).Returns(new AiAbilityStatus(AiAbilityState.Ready));
        ai.EditPicture(Arg.Any<PictureEditAsked>()).Returns(call =>
        {
            var made = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _jobs.Add((call.Arg<PictureEditAsked>(), made));
            return new PictureJob(made.Task, () => made.TrySetResult(null));
        });
        return ai;
    }

    private ArtistViewModel Canvas(IAIService ai, int width = 64, int height = 48)
    {
        var path = Path.Combine(_dir, "canvas.ora");
        _services.WriteLayeredAsync(LayeredPicture.Blank(width, height, LayerBackground.White, "Layer 1"), path).GetAwaiter().GetResult();
        var vm = _ui.Invoke(() => new ArtistViewModel(path, Shell(), _services, ai: ai, config: new ImagesConfig()));
        Until(() => vm.CurrentDocument is not null);
        Assert.IsNotNull(_ui.Invoke(() => vm.CurrentDocument), "the canvas was never read as a layered picture");
        return vm;
    }

    private static TiledCoverage Rectangle(int width, int height, SKRectI rect) =>
        TiledCoverage.Clear(width, height).Written(rect, Enumerable.Repeat((byte)255, rect.Width * rect.Height).ToArray());

    /// <summary>What the stand-in model makes: a picture half the size of the one it was sent.</summary>
    private string Made((PictureEditAsked Asked, TaskCompletionSource<string?> Made) job)
    {
        using var sent = SKBitmap.Decode(job.Asked.Sources[0]);
        var path = Path.Combine(_dir, $"made-{Guid.NewGuid():N}.png");
        using var bitmap = Flat(sent.Width / 2, sent.Height / 2, new SKColor(200, 40, 40));
        File.WriteAllBytes(path, EncodePng(bitmap));
        return path;
    }

    private LayeredPicture Picture(ArtistViewModel vm) => _ui.Invoke(() => vm.CurrentDocument!.Picture);

    private async Task<string?> OnUi(Func<Task<string?>> begin) => await _ui.Invoke(begin);

    private void Until(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10) && !_ui.Invoke(done)) Thread.Sleep(10);
    }

    [TestMethod]
    [CoversNode("images-edit-ai-fill")]
    public async Task AFill_SendsThePictureAboutTheSelection_AndLaysWhatComesBackOverIt_UnderTheSelection()
    {
        var vm = Canvas(Ai());
        var selected = new SKRectI(24, 16, 40, 32);
        _ui.Run(() => vm.CurrentDocument!.Commit(p => p with { Selection = Rectangle(p.Width, p.Height, selected) })).GetAwaiter().GetResult();
        var before = Picture(vm);

        Assert.IsNull(await OnUi(() => vm.Edits.FillAsync("a red ball")));

        var job = _jobs.Single();
        Assert.AreEqual("a red ball", job.Asked.Instruction);
        Assert.IsFalse(job.Asked.OpensWhenMade, "the canvas takes it in");
        using (var source = SKBitmap.Decode(job.Asked.Sources.Single()))
            Assert.AreEqual((64, 48), (source.Width, source.Height), "the selection's 16 px and 64 px of context either way, held to the canvas");
        using (var mask = SKBitmap.Decode(job.Asked.Mask!))
        {
            Assert.AreEqual(0, mask.GetPixel(30, 20).Alpha, "what is selected is transparent: drawn again");
            Assert.AreEqual(255, mask.GetPixel(5, 5).Alpha, "the rest is kept");
        }

        job.Made.SetResult(Made(job));
        Until(() => Picture(vm).Layers.Length == 2);
        var picture = Picture(vm);
        var filled = (PictureLayer)picture.Layers[1];
        Assert.AreEqual(filled.Id, picture.ActiveId, "above the active layer, and active");
        Assert.AreEqual(before.Layers[0].Id, picture.Layers[0].Id, "the original stays beneath");
        Assert.AreEqual(2.0, filled.Placement.ScaleX, 1e-9, "stretched back over the area it was made from");
        Assert.AreEqual((0.0, 0.0), (filled.Placement.OffsetX, filled.Placement.OffsetY));
        CollectionAssert.AreEqual(Rectangle(64, 48, selected).Read(new SKRectI(0, 0, 64, 48)), filled.Mask!.Coverage.Read(new SKRectI(0, 0, 64, 48)),
                                  "masked by the selection");
    }

    [TestMethod]
    [CoversNode("images-edit-ai-fill")]
    public async Task AFill_WithNothingSelected_IsNotBegun()
    {
        var vm = Canvas(Ai());

        var why = await OnUi(() => vm.Edits.FillAsync("a red ball"));

        Assert.IsNotNull(why);
        Assert.AreEqual(0, _jobs.Count);
        Assert.IsFalse(_ui.Invoke(() => vm.Edits.AskFillCommand.CanExecute(null)), "Fill selection waits for a selection");
    }

    [TestMethod]
    [CoversNode("images-edit-ai-expand")]
    public async Task AnExpansion_GrowsTheCanvas_AndDrawsItsBorder_OverTheWholeCanvas()
    {
        var vm = Canvas(Ai(), 200, 150);

        Assert.IsNull(await OnUi(() => vm.Edits.ExpandAsync(16, 0, 16, 8, null)));

        Assert.AreEqual((232, 158), (Picture(vm).Width, Picture(vm).Height), "grown 16 each side and 8 below");
        var job = _jobs.Single();
        Assert.AreEqual(PictureEdits.ContinuePrompt, job.Asked.Instruction, "told nothing, it carries the picture on");
        using (var mask = SKBitmap.Decode(job.Asked.Mask!))
        {
            Assert.AreEqual((232, 158), (mask.Width, mask.Height));
            Assert.AreEqual(0, mask.GetPixel(2, 75).Alpha, "the new border is drawn");
            Assert.AreEqual(255, mask.GetPixel(116, 75).Alpha, "the picture is kept, away from its edges");
            Assert.IsTrue(mask.GetPixel(18, 75).Alpha is > 0 and < 255, "and blended across them");
        }

        job.Made.SetResult(Made(job));
        Until(() => Picture(vm).Layers.Length == 2);
        var border = (PictureLayer)Picture(vm).Layers[1];
        Assert.AreEqual((2.0, 2.0), (border.Placement.ScaleX, border.Placement.ScaleY));
        var shown = border.Mask!.Coverage.Read(new SKRectI(0, 0, 232, 158));
        Assert.AreEqual(255, shown[75 * 232 + 2], "shown in the border");
        Assert.AreEqual(0, shown[75 * 232 + 116], "not over the picture");
    }

    [TestMethod]
    [CoversNode("images-edit-ai-change")]
    public async Task AChange_OfTheWholePicture_SendsItWithNoMask_AndLaysTheResultOverTheCanvas()
    {
        var vm = Canvas(Ai(), 40, 30);

        Assert.IsNull(await OnUi(() => vm.Edits.ChangeAsync("make it night", wholePicture: true)));

        var job = _jobs.Single();
        Assert.IsNull(job.Asked.Mask, "the whole picture is changed");
        job.Made.SetResult(Made(job));
        Until(() => Picture(vm).Layers.Length == 2);
        var changed = (PictureLayer)Picture(vm).Layers[1];
        Assert.IsNull(changed.Mask);
        Assert.AreEqual((2.0, 2.0), (changed.Placement.ScaleX, changed.Placement.ScaleY), "laid over the whole canvas");
    }

    [TestMethod]
    [CoversNode("images-edit-ai-change-picture")]
    public async Task WithNoModelThatEdits_NoEditIsOffered_AndTheAssistantIsToldWhy()
    {
        var vm = Canvas(Ai(edits: false));

        Assert.IsFalse(_ui.Invoke(() => vm.Edits.OffersEditing));
        var result = await vm.GetClientTools().Single(t => t.Name == "change_picture")
                             .InvokeAsync(new JsonObject { ["instruction"] = "make it night" }, CancellationToken.None);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(0, _jobs.Count);
        Assert.IsTrue(vm.GetClientTools().Single(t => t.Name == "fill_selection").Needs.Contains(AiAbility.ImageEditing),
                      "the harness offers it only where the workspace's Image Editing row has a model");
    }

    [TestMethod]
    [CoversNode("images-edit-ai-fill-selection")]
    public async Task TheAssistant_FillsTheSelection_AsAJobOfTheWorkspaces()
    {
        var vm = Canvas(Ai());
        _ui.Run(() => vm.CurrentDocument!.Commit(p => p with { Selection = Rectangle(p.Width, p.Height, new SKRectI(8, 8, 24, 24)) })).GetAwaiter().GetResult();

        var result = await vm.GetClientTools().Single(t => t.Name == "fill_selection")
                             .InvokeAsync(new JsonObject { ["description"] = "a blue door" }, CancellationToken.None);

        Assert.IsTrue(result.Success, result.ModelText);
        Assert.AreEqual("a blue door", _jobs.Single().Asked.Instruction);
        Assert.IsNotNull(_jobs.Single().Asked.Mask);
    }

    [TestMethod]
    [CoversNode("images-edit-ai-expand-canvas")]
    public async Task TheAssistant_ExpandsTheCanvas_AsAJobOfTheWorkspaces()
    {
        var vm = Canvas(Ai(), 64, 48);

        var result = await vm.GetClientTools().Single(t => t.Name == "expand_canvas")
                             .InvokeAsync(new JsonObject { ["right"] = 32, ["description"] = "more of the beach" }, CancellationToken.None);

        Assert.IsTrue(result.Success, result.ModelText);
        Assert.AreEqual((96, 48), (Picture(vm).Width, Picture(vm).Height));
        Assert.AreEqual("more of the beach", _jobs.Single().Asked.Instruction);

        var nothing = await vm.GetClientTools().Single(t => t.Name == "expand_canvas").InvokeAsync([], CancellationToken.None);
        Assert.IsFalse(nothing.Success, "grown by nothing, it is not begun");
    }
}
