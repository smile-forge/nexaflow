using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows.Media;
using Nexaflow.Features.Common;
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
using Nexaflow.Visuals.Common.Theming;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;
using Nexaflow.Features.Compressed.Handlers;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// A layered picture in the editor: a photo made into layers and kept apart from it until saved, a blank canvas made from the
/// New picker, a selection filled, cleared, erased and kept on a pixel layer, the canvas resized and cropped — each one step of
/// the picture's history — a file holding a layer no reader draws opened on its saved look and taken on either way, and the
/// history kept within its budget.
/// </summary>
[TestClass]
public class ImageLayeredDocumentTests
{
    private static readonly SKColor Ground = new(100, 150, 200);
    private static readonly SKColor Spot = new(230, 30, 30);
    private static readonly SKColor Ink = new(40, 220, 60);

    private string _dir = "";
    private string _photo = "";
    private ViewerUiThread _ui = null!;
    private ImageEditingServices _services = null!;

    [TestInitialize]
    public void Make()
    {
        _dir = TestTemp.CreateSubdirectory("nexaflow-layered-").FullName;
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
    }

    [TestCleanup]
    public void Clean()
    {
        _ui.Dispose();
        _services.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private IShellServices Shell(string? saveAs = null)
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => _ui.Run(call.Arg<Action>()));
        shell.PickSaveFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<long>()).Returns(saveAs);
        return shell;
    }

    /// <summary>The editor open on <paramref name="path"/>: a photo shown, or a layered picture read.</summary>
    private ArtistViewModel Editing(string path, IShellServices? shell = null)
    {
        var vm = new ArtistViewModel(path, shell ?? Shell(), _services, config: new ImagesConfig());
        Until(() => vm.CurrentDocument is not null || vm.Canvas is not null && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        return vm;
    }

    /// <summary>A blank canvas — one empty pixel layer — written at <paramref name="name"/> and opened in the editor, the ink chosen.</summary>
    private ArtistViewModel BlankCanvas(string name, int width, int height)
    {
        var path = Path.Combine(_dir, name);
        _services.WriteLayeredAsync(LayeredPicture.Blank(width, height, LayerBackground.Transparent, "Layer 1"), path).GetAwaiter().GetResult();
        var vm = Editing(path);
        Assert.IsNotNull(_ui.Invoke(() => vm.CurrentDocument), "the canvas was never read as a layered picture");
        OnUi(() => vm.Editor.InkColour = ColorSpec.Custom(Color.FromRgb(Ink.Red, Ink.Green, Ink.Blue)));
        return vm;
    }

    private LayeredPicture Picture(ArtistViewModel vm) => _ui.Invoke(() => vm.CurrentDocument!.Picture);

    private TiledPixels ActivePixels(ArtistViewModel vm) => ((PixelLayer)Picture(vm).Active!).Pixels;

    private static SKColor PixelOf(TiledPixels pixels, int x, int y)
    {
        var rgba = pixels.Read(new SKRectI(x, y, x + 1, y + 1));
        return new SKColor(rgba[0], rgba[1], rgba[2], rgba[3]);
    }

    private void Select(ArtistViewModel vm, float left, float top, float right, float bottom)
        => OnUi(() => vm.Editor.RegionMarks.Add(new RegionRectangle(left, top, right, bottom) { Mode = RegionMode.Replace }));

    private void Run(System.Windows.Input.ICommand command)
    {
        Until(() => command.CanExecute(null));
        Assert.IsTrue(_ui.Invoke(() => command.CanExecute(null)), "the command was never offered");
        OnUi(() => command.Execute(null));
    }

    private void OnUi(Action action) => _ui.Run(action).GetAwaiter().GetResult();

    private void Until(Func<bool> done)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10) && !_ui.Invoke(done)) Thread.Sleep(10);
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public async Task APhoto_MadeLayered_IsKeptApartUntilSaved_ThenReopensAsTheLayersItWasSavedAs()
    {
        var photo = File.ReadAllBytes(_photo);
        var saved = Path.Combine(_dir, "wall.ora");
        var vm = Editing(_photo, Shell(saveAs: saved));

        Run(vm.AddLayerCommand);

        var picture = Picture(vm);
        Assert.AreEqual((32, 24), (picture.Width, picture.Height), "the canvas is the photo's size");
        Assert.AreEqual(2, picture.Layers.Length);
        Assert.IsInstanceOfType<PictureLayer>(picture.Layers[0], "the photo, at the bottom, with its recipe");
        Assert.IsInstanceOfType<PixelLayer>(picture.Layers[1], "the layer added above it");
        Assert.AreEqual(picture.Layers[1].Id, picture.ActiveId);
        Assert.IsTrue(_ui.Invoke(() => vm.CurrentDocument!.IsDraft), "kept in a draft until the first Save asks where");

        await _ui.Invoke(() => vm.Editor.SaveCommand.ExecuteAsync(null));
        Assert.IsTrue(File.Exists(saved), "the first Save asks where, and writes there");
        CollectionAssert.AreEqual(photo, File.ReadAllBytes(_photo), "the photo is never written over");
        Assert.IsFalse(_ui.Invoke(() => vm.CurrentDocument!.IsDraft));
        Assert.AreEqual(saved, _ui.Invoke(() => vm.CurrentDocument!.Path), "kept where it was saved from then on");

        var reopened = Editing(saved);
        var read = Picture(reopened);
        Assert.AreEqual(2, read.Layers.Length);
        Assert.IsInstanceOfType<PictureLayer>(read.Layers[0]);
        Assert.AreEqual(picture.Layers[0].Id, read.Layers[0].Id, "the same layers, by id");
        Assert.IsInstanceOfType<PixelLayer>(read.Layers[1]);
    }

    [TestMethod]
    [CoversNode("images-ora")]
    public async Task ACanvasFromTheNewPicker_IsWrittenAtItsSize_AndOpenedForEditing()
    {
        var shell = Shell();
        using var options = new NewLayeredPictureOptions(shell, _services, _dir);
        options.Setup.Preset = options.Setup.Custom;
        options.Setup.WidthText = "120";
        options.Setup.HeightText = "80";
        options.Setup.DpiText = "150";
        options.Setup.Background = CanvasBackgroundKind.White;
        Assert.IsTrue(options.CanCreate);

        var path = options.Create("poster.ora");
        Assert.AreEqual(Path.Combine(_dir, "poster.ora"), path);
        var opened = System.Diagnostics.Stopwatch.StartNew();
        while (opened.Elapsed < TimeSpan.FromSeconds(10) && !shell.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IShellServices.OpenTab)))
            await Task.Delay(10);

        var read = await (await _services.LayeredFormatOfAsync(path!))!.ReadAsync(path!);
        Assert.AreEqual((120, 80, 150.0), (read.Picture.Width, read.Picture.Height, read.Picture.Dpi));
        Assert.AreEqual(LayerBackground.White, read.Picture.Background);
        Assert.IsInstanceOfType<PixelLayer>(read.Picture.Layers.Single(), "one empty pixel layer");
        shell.Received(1).OpenTab(ArtistTabRegistration.StaticPageKind, Arg.Is<Dictionary<string, string>>(p => p["path"] == path));
    }

    [TestMethod]
    [CoversNode("images-ora")]
    public void TheCanvasSetup_ShowsASizeInAnyUnit_TurnsIt_AndSaysWhatIsNoSize()
    {
        var setup = new CanvasSetup();
        var a4 = setup.Presets.Single(p => p.Dpi == 300 && p.Width == 2480);
        setup.Preset = a4;
        Assert.AreEqual((2480, 3508, 300.0), (setup.PixelWidth, setup.PixelHeight, setup.Dpi), "A4 at 300 dpi");

        setup.Unit = setup.Units.Single(u => u.Unit == CanvasUnit.Millimetres);
        Assert.AreEqual(210, double.Parse(setup.WidthText), 0.1, "the same size, in millimetres");
        Assert.AreSame(a4, setup.Preset, "a unit changed is the same size");

        setup.SwapOrientationCommand.Execute(null);
        Assert.AreEqual((3508, 2480), (setup.PixelWidth, setup.PixelHeight), "turned a quarter");
        Assert.AreSame(setup.Custom, setup.Preset, "a size no longer the one named is the custom one");

        setup.Unit = setup.Units.Single(u => u.Unit == CanvasUnit.Pixels);
        setup.WidthText = "0";
        Assert.IsFalse(setup.IsValid);
        Assert.IsNull(setup.Make("Layer"));
        setup.WidthText = (LayeredPicture.MaxSide + 1).ToString();
        Assert.IsFalse(setup.IsValid, "past the largest side a canvas has");
        StringAssert.Contains(setup.SizeNote, LayeredPicture.MaxSide.ToString());
        setup.WidthText = "640";
        setup.DpiText = "none";
        Assert.IsFalse(setup.IsValid, "no resolution, no canvas");

        setup.DpiText = "72";
        setup.UseScreen(new SKSizeI(2560, 1440));
        Assert.AreEqual((2560, 1440), (setup.Presets[0].Width, setup.Presets[0].Height), "the screen's size is offered first");
        setup.Preset = setup.Presets[0];
        setup.UseScreen(new SKSizeI(1920, 1200));
        Assert.AreEqual((1920, 1200), (setup.PixelWidth, setup.PixelHeight), "chosen, the screen's size follows the screen");
        Assert.AreEqual(1, setup.Presets.Count(p => p.Width == 1920 && p.Height == 1200), "offered once");
    }

    [TestMethod]
    [CoversNode("images-start")]
    public void TheCanvasSetup_StartsOnTheScreensSize_UnlessASizeWasChosenFirst()
    {
        var untouched = new CanvasSetup();
        untouched.UseScreen(new SKSizeI(2560, 1440));
        Assert.AreSame(untouched.Presets[0], untouched.Preset, "nothing chosen yet: the screen's size is");
        Assert.AreEqual((2560, 1440), (untouched.PixelWidth, untouched.PixelHeight));

        var chosen = new CanvasSetup();
        var square = chosen.Presets.Single(p => p.Width == 1024 && p.Height == 1024);
        chosen.Preset = square;
        chosen.UseScreen(new SKSizeI(2560, 1440));
        Assert.AreSame(square, chosen.Preset, "a size chosen first is kept; the screen's is offered beside it");
    }

    [TestMethod]
    [CoversNode("images-layers-panel")]
    public void FillClearAndTheCanvasSize_AreEachOneStep_UndoneInTurn()
    {
        var vm = BlankCanvas("canvas.ora", 40, 30);
        Select(vm, 10, 10, 20, 20);
        Assert.IsFalse(Picture(vm).Selection.IsEmpty, "a marquee selects on the canvas");

        Run(vm.Layers.FillCommand);
        AssertNear(Ink, PixelOf(ActivePixels(vm), 15, 15), because: "what is selected is filled with the ink");
        Assert.AreEqual(0, PixelOf(ActivePixels(vm), 5, 5).Alpha, "what is not stays clear");

        Run(vm.Layers.DeleteSelectedCommand);
        Assert.AreEqual(0, PixelOf(ActivePixels(vm), 15, 15).Alpha, "Delete clears what is selected");
        Run(vm.UndoEditCommand);
        AssertNear(Ink, PixelOf(ActivePixels(vm), 15, 15), because: "one undo puts the fill back");

        OnUi(() =>
        {
            vm.Layers.BeginCanvasSizeCommand.Execute(null);
            vm.Layers.CanvasWidth = 60;
            vm.Layers.CanvasAnchor = CanvasAnchor.TopLeft;
        });
        Run(vm.Layers.ResizeCanvasCommand);
        Assert.AreEqual((60, 30), (Picture(vm).Width, Picture(vm).Height));
        AssertNear(Ink, PixelOf(ActivePixels(vm), 15, 15), because: "anchored top left, the pixels stay where they were");
        Assert.IsTrue(Picture(vm).Selection.IsEmpty, "a new canvas size lets the selection go");

        Select(vm, 10, 10, 20, 20);
        Run(vm.Layers.CropToSelectionCommand);
        Assert.AreEqual((10, 10), (Picture(vm).Width, Picture(vm).Height), "cropped to the selection's bounds");
        AssertNear(Ink, PixelOf(ActivePixels(vm), 5, 5), because: "the filled square is all that is left");

        Run(vm.UndoEditCommand);
        Assert.AreEqual((60, 30), (Picture(vm).Width, Picture(vm).Height), "undo uncrops");
        Run(vm.UndoEditCommand);
        Assert.IsTrue(Picture(vm).Selection.IsEmpty, "the selection was a step of its own");
        Run(vm.UndoEditCommand);
        Assert.AreEqual((40, 30), (Picture(vm).Width, Picture(vm).Height), "and the canvas size another");
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public void ASelectionOnAPixelLayer_IsErasedOrKeptInItsPixels()
    {
        var vm = BlankCanvas("pixels.ora", 32, 24);
        OnUi(() => vm.Editor.InkColour = ColorSpec.Custom(Color.FromRgb(Ground.Red, Ground.Green, Ground.Blue)));
        Run(vm.Layers.FillCommand);   // nothing selected: the whole layer
        Select(vm, 14, 10, 18, 14);
        OnUi(() => vm.Editor.InkColour = ColorSpec.Custom(Color.FromRgb(Spot.Red, Spot.Green, Spot.Blue)));
        Run(vm.Layers.FillCommand);
        AssertNear(Spot, PixelOf(ActivePixels(vm), 16, 12));

        Select(vm, 12, 8, 20, 16);
        OnUi(() =>
        {
            vm.Editor.Tool = EditTool.Retouch;
            vm.Editor.EraseMarks.Fill = EraseFill.ContentAware;
        });
        Run(vm.Editor.EraseCommand);
        Until(() => !vm.Editor.IsErasingPixels && PixelOf(ActivePixels(vm), 16, 12) != Spot);
        AssertNear(Ground, PixelOf(ActivePixels(vm), 16, 12), because: "the spot is filled from round it, in the layer's own pixels");
        Assert.AreEqual(0, Picture(vm).All.OfType<PictureLayer>().Count(), "no recipe step: the pixels themselves");

        Select(vm, 4, 4, 12, 12);
        OnUi(() => vm.Editor.Tool = EditTool.CutOut);
        Run(vm.Editor.RemoveBackgroundCommand);
        Assert.AreEqual(255, PixelOf(ActivePixels(vm), 8, 8).Alpha, "what is selected is kept");
        Assert.AreEqual(0, PixelOf(ActivePixels(vm), 20, 20).Alpha, "what is not is deleted");
    }

    [TestMethod]
    [CoversNode("images-ora")]
    public async Task AFileHoldingALayerNoReaderDraws_KeepsItWhenItsLayersAreEdited()
    {
        var path = WriteWithAFilterLayer("levels.ora");
        var vm = Editing(path);
        Assert.IsTrue(_ui.Invoke(() => vm.ShowsSavedLookBar), "it opens on the look the application that wrote it saved");
        StringAssert.Contains(_ui.Invoke(() => vm.SavedLookNotice), "Levels");

        Run(vm.EditTheLayersCommand);
        Assert.IsFalse(_ui.Invoke(() => vm.ShowsSavedLookBar));
        Run(vm.Layers.AddPixelLayerCommand);
        var before = File.GetLastWriteTimeUtc(path);
        Until(() => vm.Editor.SaveCommand.CanExecute(null));
        await _ui.Invoke(() => vm.Editor.SaveCommand.ExecuteAsync(null));
        Assert.IsTrue(File.GetLastWriteTimeUtc(path) > before, "saved where it is, asking nothing");

        var read = await (await _services.LayeredFormatOfAsync(path))!.ReadAsync(path);
        Assert.IsTrue(read.HoldsUndrawable, "the filter layer is kept");
        Assert.AreEqual(3, read.Picture.Layers.Length, "the base, the filter and the layer added");
        Assert.AreEqual("applications:krita:levels", read.Picture.Layers.OfType<UndrawableLayer>().Single().Kind);
    }

    [TestMethod]
    [CoversNode("images-ora")]
    public void AFileHoldingALayerNoReaderDraws_UsedAsItsSavedLook_IsOnePictureLayer()
    {
        var vm = Editing(WriteWithAFilterLayer("levels.ora"));
        Run(vm.UseSavedLookCommand);
        Until(() => !vm.ShowsSavedLookBar);

        var picture = Picture(vm);
        Assert.IsFalse(_ui.Invoke(() => vm.ShowsSavedLookBar));
        Assert.IsInstanceOfType<PictureLayer>(picture.Layers.Single(), "the saved look, baked in, as one picture layer");
        Assert.IsFalse(picture.All.OfType<UndrawableLayer>().Any());
        Run(vm.UndoEditCommand);
        Assert.AreEqual(2, Picture(vm).Layers.Length, "one step of the history, undone like any");
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public void TheHistory_KeepsWithinItsBudget_DroppingTheOldestSteps()
    {
        const int side = 512;
        long layerBytes = 4L * side * side;
        using var document = new LayeredSession(Path.Combine(_dir, "budget.ora"), LayeredPicture.Blank(side, side, LayerBackground.Transparent, "Layer 1"),
                                                _services, historyBudget: 3 * layerBytes);
        var everything = TiledCoverage.Covered(side, side);
        for (int step = 0; step < 8; step++)
        {
            var colour = new SKColor((byte)(30 * step), 80, 120);
            document.Commit(p => p.WithLayer(p.ActiveId!.Value, l => ((PixelLayer)l) with { Pixels = ((PixelLayer)l).Pixels.Filled(everything, colour, keepAlpha: false) }));
        }

        Assert.IsTrue(document.History.UndoCount is >= 1 and <= 3, $"{document.History.UndoCount} step(s) kept of 8, each a whole layer's pixels");
        while (document.History.CanUndo) document.History.Undo();
        Assert.AreNotEqual(0, PixelOf(((PixelLayer)document.Picture.Active!).Pixels, 0, 0).Alpha, "the oldest steps, past the budget, are gone");
    }

    /// <summary>An OpenRaster file as Krita writes one with a filter layer: a picture layer, the filter, and the merged image.</summary>
    private string WriteWithAFilterLayer(string name)
    {
        var path = Path.Combine(_dir, name);
        byte[] png;
        using (var bitmap = Flat(16, 12, Ground)) png = EncodePng(bitmap);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var mime = new StreamWriter(zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open(), new UTF8Encoding(false)))
            mime.Write("image/openraster");
        using (var xml = new StreamWriter(zip.CreateEntry("stack.xml").Open(), new UTF8Encoding(false)))
            xml.Write("""
                <?xml version="1.0" encoding="UTF-8"?>
                <image version="0.0.1" w="16" h="12">
                 <stack>
                  <filter type="applications:krita:levels" name="Levels" opacity="1" visibility="visible"/>
                  <layer src="data/base.png" x="0" y="0" name="Base"/>
                 </stack>
                </image>
                """);
        using (var layer = zip.CreateEntry("data/base.png").Open()) layer.Write(png);
        using (var merged = zip.CreateEntry("mergedimage.png").Open()) merged.Write(png);
        return path;
    }
}
