using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
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
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;
using Nexaflow.Features.Compressed.Handlers;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// The assistant's tools in Artist, shaped for a model: one description of the whole picture, and changes said as what to make of
/// it — the edits, the selection, a list of layer operations — each one step the user undoes at once, answered with the state left.
/// </summary>
[TestClass]
[CoversNode("images-edit-ai")]
public class ArtistToolsTests
{
    private static readonly SKColor Ground = new(100, 150, 200);

    private string _dir = "";
    private string _photo = "";
    private ViewerUiThread _ui = null!;
    private ImageEditingServices _services = null!;

    [TestInitialize]
    public void Make()
    {
        _dir = TestTemp.CreateSubdirectory("nexaflow-artist-tools-").FullName;
        _photo = Path.Combine(_dir, "wall.png");
        using (var bitmap = Quadrants(40, 30))
            File.WriteAllBytes(_photo, EncodePng(bitmap));
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

    private ArtistViewModel Photo()
    {
        var vm = _ui.Invoke(() => new ArtistViewModel(_photo, Shell(), _services, config: new ImagesConfig()));
        Until(() => vm.CurrentSession?.Proxy is not null && vm.Canvas is not null);
        return vm;
    }

    private ArtistViewModel Canvas(int width = 64, int height = 48)
    {
        var path = Path.Combine(_dir, "canvas.ora");
        _services.WriteLayeredAsync(LayeredPicture.Blank(width, height, LayerBackground.Transparent, "Layer 1"), path).GetAwaiter().GetResult();
        var vm = _ui.Invoke(() => new ArtistViewModel(path, Shell(), _services, config: new ImagesConfig()));
        Until(() => vm.CurrentDocument is not null);
        Assert.IsNotNull(_ui.Invoke(() => vm.CurrentDocument), "the canvas was never read as a layered picture");
        return vm;
    }

    private static async Task<ToolResult> Call(ArtistViewModel vm, string tool, JsonObject? args = null) =>
        await vm.GetClientTools().Single(t => t.Name == tool).InvokeAsync(args ?? [], CancellationToken.None);

    private static JsonObject Json(ToolResult result)
    {
        Assert.IsTrue(result.Success, result.ModelText);
        return JsonNode.Parse(result.ModelText)!.AsObject();
    }

    private int UndoSteps(ArtistViewModel vm) => _ui.Invoke(() => vm.CurrentDocument?.History.UndoCount ?? vm.CurrentSession!.History.UndoCount);

    private void Until(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10) && !_ui.Invoke(done)) Thread.Sleep(10);
    }

    [TestMethod]
    public void TheToolset_IsShapedForAModel_NotAfterTheButtons()
    {
        var names = Photo().GetClientTools().Select(t => t.Name).ToArray();
        CollectionAssert.AreEquivalent(
            new[] { "describe_picture", "capture_image", "edit_picture", "select_in_picture", "edit_layers", "generate_layer", "fill_selection",
                    "expand_canvas", "change_picture", "erase_in_picture", "restore_picture", "cut_out", "history", "describe_scene_depth",
                    "blur_background", "add_haze" },
            names, "the Artist tool surface changed — update the tree's images-edit-ai leaves to match");
    }

    [TestMethod]
    public async Task DescribePicture_GivesAPhotosWholeState_InTheToolsCoordinates()
    {
        var state = Json(await Call(Photo(), "describe_picture"));

        Assert.AreEqual("photo", (string)state["kind"]!);
        Assert.AreEqual(40, (int)state["photo"]!["width"]!);
        Assert.AreEqual(30, (int)state["photo"]!["height"]!);
        StringAssert.Contains((string)state["coordinates"]!, "own pixels");
        Assert.AreEqual(0, (int)state["edits"]!["quarter_turns"]!);
        Assert.IsNull(state["selection"]);
        Assert.IsFalse((bool)state["can_undo"]!);
        Assert.AreEqual("reshape", (string)state["tool"]!, "the tool by the name the user sees");
    }

    [TestMethod]
    public async Task EditPicture_AppliesEverythingAskedAsOneStep_AndAnswersWithTheEdits()
    {
        var vm = Photo();

        var edits = Json(await Call(vm, "edit_picture", new JsonObject
        {
            ["adjust"] = new JsonObject { ["exposure"] = 0.5, ["saturation"] = -20 },
            ["turn"] = 1,
            ["flip"] = "horizontal",
            ["crop"] = new JsonArray(10, 5, 30, 25),
        }));

        Assert.AreEqual(0.5, (double)edits["adjust"]!["exposure"]!);
        Assert.AreEqual(-20, (double)edits["adjust"]!["saturation"]!);
        Assert.AreEqual(1, (int)edits["quarter_turns"]!);
        Assert.IsTrue((bool)edits["flip_horizontal"]!);
        CollectionAssert.AreEqual(new[] { 10.0, 5, 30, 25 }, edits["crop"]!.AsArray().Select(n => (double)n!).ToArray());
        Assert.AreEqual(1, UndoSteps(vm), "one step, however much was asked");

        OnUi(() => vm.UndoEditCommand.Execute(null));
        Assert.IsTrue(_ui.Invoke(() => vm.CurrentRecipe.IsIdentity), "undone at once");
    }

    [TestMethod]
    public async Task EditPicture_RefusesWhatDoesNotRead_ChangingNothing()
    {
        var vm = Photo();

        var result = await Call(vm, "edit_picture", new JsonObject { ["adjust"] = new JsonObject { ["exposure"] = 9 }, ["turn"] = 1 });

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ModelText, "exposure is from -3 to 3");
        Assert.IsTrue(_ui.Invoke(() => vm.CurrentRecipe.IsIdentity), "nothing applied when part of the ask does not read");
    }

    [TestMethod]
    public async Task EditPicture_Annotates_InTheEditorsInkUnlessColoured()
    {
        var vm = Photo();

        await Call(vm, "edit_picture", new JsonObject
        {
            ["annotate"] = new JsonArray(
                new JsonObject { ["kind"] = "arrow", ["from"] = new JsonArray(2, 2), ["to"] = new JsonArray(20, 15) },
                new JsonObject { ["kind"] = "text", ["from"] = new JsonArray(5, 20), ["text"] = "Look", ["colour"] = "#102030" }),
        });

        var markup = _ui.Invoke(() => vm.CurrentRecipe.Markup);
        Assert.AreEqual(2, markup.Length);
        Assert.IsInstanceOfType<MarkupArrow>(markup[0]);
        Assert.AreEqual(0xFF102030u, ((MarkupText)markup[1]).Color);
        Assert.AreEqual(1, UndoSteps(vm));
    }

    [TestMethod]
    public async Task SelectInPicture_BuildsTheSelection_FromShapesAndRefinements()
    {
        var vm = Canvas();

        var answer = await Call(vm, "select_in_picture", new JsonObject
        {
            ["shapes"] = new JsonArray(
                new JsonObject { ["shape"] = "rectangle", ["box"] = new JsonArray(10, 10, 40, 30) },
                new JsonObject { ["shape"] = "rectangle", ["box"] = new JsonArray(10, 10, 20, 30), ["mode"] = "subtract" }),
        });

        Assert.IsTrue(answer.Success, answer.ModelText);
        StringAssert.Contains(answer.ModelText, "[20,10,40,30]");
        var area = LayerEdits.CoveredArea(_ui.Invoke(() => vm.CurrentDocument!.Picture.Selection))!.Value;
        Assert.AreEqual(new SKRectI(20, 10, 40, 30), area);

        await Call(vm, "select_in_picture", new JsonObject { ["whole"] = "none" });
        Assert.IsTrue(_ui.Invoke(() => vm.CurrentDocument!.Picture.Selection.IsEmpty));
    }

    [TestMethod]
    public async Task EditLayers_LiftsWhatIsSelected_IntoALayerOfItsOwn()
    {
        var vm = Canvas();
        await Call(vm, "select_in_picture", new JsonObject
        {
            ["shapes"] = new JsonArray(new JsonObject { ["shape"] = "rectangle", ["box"] = new JsonArray(10, 10, 40, 30) }),
        });

        var answer = await Call(vm, "edit_layers", new JsonObject { ["ops"] = new JsonArray(new JsonObject { ["op"] = "lift", ["copy"] = true }) });

        Assert.IsTrue(answer.Success, answer.ModelText);
        Until(() => _ui.Invoke(() => vm.CurrentDocument!.Picture.Layers.Length) == 2);
        Assert.AreEqual(2, _ui.Invoke(() => vm.CurrentDocument!.Picture.Layers.Length), "a layer of its own");
        Assert.IsFalse(_ui.Invoke(() => vm.Arranging.HasFloating), "the assistant's lift is a layer at once, nothing left floating");
    }

    [TestMethod]
    public async Task SelectInPicture_SelectsWhatIsNearAColour()
    {
        var vm = Photo();

        var answer = await Call(vm, "select_in_picture", new JsonObject { ["colour"] = new JsonObject { ["at"] = new JsonArray(5, 5), ["tolerance"] = 8 } });

        Assert.IsTrue(answer.Success, answer.ModelText);
        StringAssert.Contains(answer.ModelText, "[0,0,20,15]", "the quarter clicked, and no other");
        Assert.IsFalse((await Call(vm, "select_in_picture", new JsonObject { ["colour"] = new JsonObject { ["at"] = new JsonArray(5) } })).Success, "a point is two numbers");
    }

    [TestMethod]
    public async Task EditLayers_AppliesItsOperationsInOrder_AsOneStep_AndAnswersWithTheLayers()
    {
        var vm = Canvas();
        int before = UndoSteps(vm);
        await Call(vm, "select_in_picture", new JsonObject { ["whole"] = "all" });
        int selected = UndoSteps(vm);

        var answer = Json(await Call(vm, "edit_layers", new JsonObject
        {
            ["ops"] = new JsonArray(
                new JsonObject { ["op"] = "add", ["kind"] = "pixels", ["name"] = "Sky" },
                new JsonObject { ["op"] = "fill", ["colour"] = "#3366CC" },
                new JsonObject { ["op"] = "set", ["opacity"] = 50, ["blend"] = "multiply" },
                new JsonObject { ["op"] = "move", ["layer"] = "Sky", ["to"] = "bottom" }),
        }));

        var layers = answer["layers"]!.AsArray();
        Assert.AreEqual(2, layers.Count);
        Assert.AreEqual("Sky", (string)layers[1]!["name"]!, "moved to the bottom");
        Assert.AreEqual(50, (double)layers[1]!["opacity"]!);
        Assert.AreEqual("multiply", (string)layers[1]!["blend"]!);
        CollectionAssert.AreEqual(new[] { 0.0, 0, 64, 48 }, layers[1]!["bounds"]!.AsArray().Select(n => (double)n!).ToArray(), "filled where selected");
        Assert.AreEqual(selected + 1, UndoSteps(vm), "four operations, one step");
        Assert.IsTrue(selected > before, "the selection was a step of its own, before");

        OnUi(() => vm.UndoEditCommand.Execute(null));
        Assert.AreEqual(1, _ui.Invoke(() => vm.CurrentDocument!.Picture.All.Count()), "undone at once");
    }

    [TestMethod]
    public async Task EditLayers_StopsAtAnOperationItCannotDo_KeepingWhatCameBefore()
    {
        var vm = Canvas();

        var result = await Call(vm, "edit_layers", new JsonObject
        {
            ["ops"] = new JsonArray(
                new JsonObject { ["op"] = "add", ["kind"] = "notes", ["name"] = "Labels" },
                new JsonObject { ["op"] = "delete", ["layer"] = "Nowhere" },
                new JsonObject { ["op"] = "add", ["kind"] = "pixels" }),
        });

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.ModelText, "operation 2");
        StringAssert.Contains(result.ModelText, "no layer is 'Nowhere'");
        Assert.AreEqual(2, _ui.Invoke(() => vm.CurrentDocument!.Picture.All.Count()), "the first operation stays; the third is never tried");
    }

    [TestMethod]
    public async Task EditLayers_TransformsALayer_AboutItsMiddle_ByItsId()
    {
        var vm = Canvas();
        await Call(vm, "select_in_picture", new JsonObject { ["shapes"] = new JsonArray(new JsonObject { ["box"] = new JsonArray(0, 0, 20, 10) }) });
        await Call(vm, "edit_layers", new JsonObject { ["ops"] = new JsonArray(new JsonObject { ["op"] = "fill", ["colour"] = "#FF0000" }) });
        await Call(vm, "select_in_picture", new JsonObject { ["whole"] = "none" });
        var id = (string)Json(await Call(vm, "describe_picture"))["layers"]![0]!["id"]!;

        var answer = Json(await Call(vm, "edit_layers", new JsonObject
        {
            ["ops"] = new JsonArray(new JsonObject { ["op"] = "transform", ["layer"] = id, ["move"] = new JsonArray(10, 5) }),
        }));

        CollectionAssert.AreEqual(new[] { 10.0, 5, 30, 15 }, answer["layers"]![0]!["bounds"]!.AsArray().Select(n => (double)n!).ToArray());
    }

    [TestMethod]
    public async Task EditLayers_MakesAPhotoLayered_KeptApartFromIt()
    {
        var vm = Photo();
        var photo = File.ReadAllBytes(_photo);

        var answer = Json(await Call(vm, "edit_layers", new JsonObject { ["ops"] = new JsonArray(new JsonObject { ["op"] = "add", ["kind"] = "notes" }) }));

        Assert.AreEqual(2, answer["layers"]!.AsArray().Count, "the photo as a picture layer, the notes above it");
        Assert.AreEqual("picture", (string)answer["layers"]![1]!["kind"]!);
        CollectionAssert.AreEqual(photo, File.ReadAllBytes(_photo), "the photo itself is never written over");
    }

    [TestMethod]
    public async Task CaptureImage_ShowsAPartOfThePhoto_InItsOwnPixels()
    {
        var vm = Photo();
        await Call(vm, "edit_picture", new JsonObject { ["turn"] = 1 });

        var result = await Call(vm, "capture_image", new JsonObject { ["box"] = "0,0,20,15" });

        Assert.IsTrue(result.Success, result.ModelText);
        using var part = SKBitmap.Decode(result.Images!.Single().Bytes);
        Assert.AreEqual((20, 15), (part.Width, part.Height), "the box as asked, the turn left out of its coordinates");
        var corner = part.GetPixel(2, 2);
        Assert.IsTrue(corner.Red > 180 && corner.Green < 90, "the photo's own top-left quadrant");
    }

    [TestMethod]
    public async Task History_StepsBackAndForth_SayingWhatIsLeft()
    {
        var vm = Photo();
        await Call(vm, "edit_picture", new JsonObject { ["turn"] = 1 });
        await Call(vm, "edit_picture", new JsonObject { ["turn"] = 1 });

        var back = await Call(vm, "history", new JsonObject { ["undo"] = 5 });
        StringAssert.Contains(back.ModelText, "Took back 2 step(s)");
        Assert.AreEqual(0, _ui.Invoke(() => vm.CurrentRecipe.Geometry.QuarterTurns));

        await Call(vm, "history", new JsonObject { ["redo"] = 1 });
        Assert.AreEqual(1, _ui.Invoke(() => vm.CurrentRecipe.Geometry.QuarterTurns));
    }

    private void OnUi(Action action) => _ui.Run(action).GetAwaiter().GetResult();
}
