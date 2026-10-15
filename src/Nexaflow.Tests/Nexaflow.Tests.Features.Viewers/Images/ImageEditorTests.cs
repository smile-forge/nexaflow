using System.IO;
using System.Text.Json.Nodes;
using Nexaflow.Features.Common;
using Nexaflow.Features.Images.Editing;
using Nexaflow.Features.Images.Editing.Codecs;
using Nexaflow.Features.Images.Editing.Recipe;
using Nexaflow.Features.Images.Editing.Rendering;
using Nexaflow.Features.Images.FileActions;
using Nexaflow.Features.Images.ViewModels;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Common.Theming;
using NSubstitute;
using SkiaSharp;
using static Nexaflow.Tests.Features.Images.EditingFixtures;

namespace Nexaflow.Tests.Features.Images;

/// <summary>
/// Edit mode's controls, at the view-model behind each: the crop frame and its presets, straightening and the level
/// line, the sliders and auto enhance, every markup tool, resize, export, compare, revert, save and save as copy.
/// <para>
/// Each test opens a copy of the generated photo stored on its side (<see cref="OrientedPhotoSample"/>) with a
/// shell that really runs what is marshalled to the UI and queued in the background, so the editor sees a decoded
/// picture and a presented frame exactly as it does in the app.
/// </para>
/// </summary>
[TestClass]
public class ImageEditorTests
{
    private string _dir = string.Empty;
    private string _photo = string.Empty;
    private ViewerUiThread _ui = null!;

    [TestInitialize]
    public void CopyThePhoto()
    {
        _dir = TempDirectory();
        _photo = CopyOfSample(_dir, OrientedPhotoSample.FileName);
        _ui = new ViewerUiThread();
    }

    [TestCleanup]
    public void RemoveTheCopy()
    {
        _ui.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>A shell whose UI hops and background tasks run, the confirmations say yes, and the save picker returns <paramref name="saveAs"/>.</summary>
    private IShellServices Shell(string? saveAs = null)
    {
        var shell = Substitute.For<IShellServices>();
        shell.RunOnUiAsync(Arg.Any<Action>()).Returns(call => _ui.Run(call.Arg<Action>()));
        shell.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        shell.PickSaveFileAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>?>(), Arg.Any<string?>(), Arg.Any<long>()).Returns(saveAs);
        shell.When(s => s.QueueBackgroundTask(Arg.Any<IBackgroundTask>(), Arg.Any<Action<bool>?>(), Arg.Any<CancellationToken>()))
             .Do(call =>
             {
                 call.Arg<IBackgroundTask>().RunAsync(CancellationToken.None).GetAwaiter().GetResult();
                 call.Arg<Action<bool>?>()?.Invoke(true);
             });
        shell.When(s => s.ShowConfirmation(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action>(), Arg.Any<Action>()))
             .Do(call => call.ArgAt<Action>(2)());
        return shell;
    }

    /// <summary>The viewer in edit mode over the copied photo, once a frame of it is on screen.</summary>
    private ImageViewModel Editing(IShellServices? shell = null)
    {
        var vm = new ImageViewModel([_photo], shell ?? Shell(), ImageViewMode.Edit, new ImageEditingServices(new ImageRecipeStore(TimeSpan.FromHours(1))));
        Settle(vm);
        Assert.IsNotNull(vm.Canvas, "the photo was never decoded and presented");
        return vm;
    }

    /// <summary>Waits for whatever render the last change asked for to land.</summary>
    private static void Settle(ImageViewModel vm, Func<bool>? until = null)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (vm.Canvas is { } canvas && canvas.Recipe.Equals(vm.IsEditing && vm.Editor.ShowOriginal ? ImageRecipe.Identity : vm.CurrentRecipe)
                && (until?.Invoke() ?? true)) return;
            Thread.Sleep(10);
        }
    }

    private static int Steps(ImageViewModel vm)
    {
        int steps = 0;
        while (vm.UndoEditCommand.CanExecute(null)) { vm.UndoEditCommand.Execute(null); steps++; }
        for (int i = 0; i < steps; i++) vm.RedoEditCommand.Execute(null);
        return steps;
    }

    // The sample is stored 64×32 on its side, so upright it is 32 wide and 64 tall.
    private static readonly SKSize Upright = new(OrientedPhotoSample.StoredHeight, OrientedPhotoSample.StoredWidth);

    // ── Crop ────────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-edit-crop")]
    public void DraggingACropHandle_IsOneStep_AndStopsAtThePicturesEdge()
    {
        var vm = Editing();
        var editor = vm.Editor;
        var shown = editor.ShownCrop!.Value;
        var corner = new SKPoint(shown.Right, shown.Bottom);

        Assert.AreEqual(CropHandle.BottomRight, editor.CropHandleAt(corner, 4));
        editor.BeginCropDrag(CropHandle.BottomRight, corner);
        editor.ContinueCropDrag(corner - new SKPoint(shown.Width / 4, shown.Height / 4));
        editor.ContinueCropDrag(corner - new SKPoint(shown.Width / 2, shown.Height / 2));
        editor.EndCropDrag();

        var crop = vm.CurrentRecipe.Geometry.Crop!.Value;
        Assert.AreEqual(Upright.Width / 2, crop.Width, 0.5, "half the picture across");
        Assert.AreEqual(1, Steps(vm), "a drag is one undo step, however far it went");

        editor.BeginCropDrag(CropHandle.Move, new SKPoint(shown.Left + 1, shown.Top + 1));
        editor.ContinueCropDrag(new SKPoint(shown.Left + 10_000, shown.Top + 1));
        editor.EndCropDrag();
        Assert.AreEqual(Upright.Width, vm.CurrentRecipe.Geometry.Crop!.Value.Right, 0.5, "moved as far as the edge, no further");
    }

    [TestMethod]
    [CoversNode("images-edit-aspect")]
    public void AnAspectPreset_ShapesTheCrop_AndPortraitTurnsIt()
    {
        var editor = Editing().Editor;

        editor.Aspect = AspectPreset.Square;
        var square = editor.ShownCrop!.Value;
        Assert.AreEqual(square.Width, square.Height, 0.5);

        editor.Aspect = AspectPreset.FourThree;
        var across = editor.ShownCrop!.Value;
        Assert.AreEqual(4.0 / 3, across.Width / across.Height, 0.02);

        editor.AspectPortrait = true;
        var upright = editor.ShownCrop!.Value;
        Assert.AreEqual(3.0 / 4, upright.Width / upright.Height, 0.02);
    }

    [TestMethod]
    [CoversNode("images-edit-straighten")]
    public void StraighteningThroughASteepAngleAndBack_LeavesTheCropAsItWas()
    {
        var vm = Editing();
        var editor = vm.Editor;
        editor.Aspect = AspectPreset.Square;
        var before = vm.CurrentRecipe.Geometry.Crop;

        editor.BeginGesture();
        editor.Straighten = 30;
        Assert.IsTrue(vm.CurrentRecipe.Geometry.Crop!.Value.Width < before!.Value.Width, "shrunk to stay on the picture");
        editor.Straighten = 0;
        editor.EndGesture();

        Assert.AreEqual(before, vm.CurrentRecipe.Geometry.Crop, "the drag is measured from where it began");
        Assert.IsTrue(CropGeometry.Contains(Upright, vm.CurrentRecipe.Geometry.StraightenDegrees, vm.CurrentRecipe.Geometry.Crop!.Value));
    }

    [TestMethod]
    [CoversNode("images-edit-level")]
    public void ALevelLine_StraightensAndDisarmsTheTool()
    {
        var editor = Editing().Editor;
        editor.IsLevelTool = true;

        editor.Level(new SKPoint(0, 0), new SKPoint(100, (float)(100 * Math.Tan(4 * Math.PI / 180))));

        Assert.AreEqual(-4, editor.Straighten, 0.11);
        Assert.IsFalse(editor.IsLevelTool, "one line, then the tool puts itself away");
    }

    [TestMethod]
    [CoversNode("images-edit-crop")]
    public void EscWhileCropping_GoesBackToTheCropTheToolOpenedWith()
    {
        var vm = Editing();
        vm.Editor.Aspect = AspectPreset.Square;
        Assert.IsTrue(vm.Editor.CancelCrop());
        Assert.IsNull(vm.CurrentRecipe.Geometry.Crop);
        Assert.IsFalse(vm.Editor.CancelCrop(), "nothing changed since: Esc means leave");
    }

    // ── Adjust ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-edit-adjust")]
    public void ASliderDrag_IsOneStep_AndDoubleClickingResetsIt()
    {
        var vm = Editing();
        var editor = vm.Editor;

        editor.BeginGesture();
        for (int i = 1; i <= 10; i++) editor.Contrast = i * 5;
        editor.EndGesture();
        editor.Shadows = 20;

        Assert.AreEqual(50, vm.CurrentRecipe.Adjustments.Contrast);
        Assert.AreEqual(2, Steps(vm));

        editor.ResetAdjustmentsCommand.Execute(null);
        Assert.IsTrue(vm.CurrentRecipe.Adjustments.IsIdentity);
    }

    [TestMethod]
    [CoversNode("images-edit-auto")]
    public void AutoEnhance_SetsTheSliders_AsOneUndoableStep()
    {
        var vm = Editing();
        vm.Editor.AutoEnhanceCommand.Execute(null);

        Assert.IsFalse(vm.CurrentRecipe.Adjustments.IsIdentity, "the quadrant photo is far from neutral");
        vm.UndoEditCommand.Execute(null);
        Assert.IsTrue(vm.CurrentRecipe.Adjustments.IsIdentity);
    }

    // ── Markup ──────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-edit-markup-tools")]
    public void EachDrawingToolLeavesItsAnnotation_AndAClickThatDrewNothingLeavesNone()
    {
        var vm = Editing();
        var editor = vm.Editor;
        editor.Tool = EditTool.Markup;

        foreach (var tool in new[] { MarkupTool.Arrow, MarkupTool.Box, MarkupTool.Pen, MarkupTool.Highlighter })
        {
            editor.MarkupTool = tool;
            editor.MarkupPress(new SKPoint(4, 4), 1);
            editor.MarkupDrag(new SKPoint(14, 20), 0.5f);
            editor.MarkupDrag(new SKPoint(24, 40), 0.5f);
            editor.MarkupRelease();
        }
        CollectionAssert.AreEqual(new[] { typeof(MarkupArrow), typeof(MarkupBox), typeof(MarkupStroke), typeof(MarkupStroke) },
                                  vm.CurrentRecipe.Markup.Select(m => m.GetType()).ToArray());
        Assert.AreEqual(StrokeStyle.Highlighter, ((MarkupStroke)vm.CurrentRecipe.Markup[3]).Style);
        Assert.AreEqual(3, ((MarkupStroke)vm.CurrentRecipe.Markup[2]).Points.Length);
        Assert.AreEqual(4, Steps(vm), "one step per annotation");

        editor.MarkupTool = MarkupTool.Arrow;
        editor.MarkupPress(new SKPoint(4, 4), 1);
        editor.MarkupRelease();
        Assert.AreEqual(4, vm.CurrentRecipe.Markup.Length, "a click is not an arrow");
    }

    [TestMethod]
    [CoversNode("images-edit-markup-tools")]
    public void TextIsTypedIntoItsAnnotation_AndOneLeftEmptyIsDropped()
    {
        var vm = Editing();
        var editor = vm.Editor;
        editor.Tool = EditTool.Markup;
        editor.MarkupTool = MarkupTool.Text;
        bool asked = false;
        editor.TextEditRequested += () => asked = true;

        editor.MarkupPress(new SKPoint(5, 5), 1);
        Assert.IsTrue(asked, "the view is asked to focus the text box");
        editor.SelectedText = "H";
        editor.SelectedText = "Hi";
        editor.EndTextEdit();

        Assert.AreEqual("Hi", ((MarkupText)vm.CurrentRecipe.Markup.Single()).Text);
        Assert.AreEqual(1, Steps(vm), "placing and typing are one step");

        editor.MarkupPress(new SKPoint(9, 9), 1);
        editor.EndTextEdit();
        Assert.AreEqual(1, vm.CurrentRecipe.Markup.Length, "nothing typed, nothing kept");
    }

    [TestMethod]
    [CoversNode("images-edit-markup-select")]
    public void AnAnnotationCanBeSelected_Moved_AndDeleted()
    {
        var vm = Editing();
        var editor = vm.Editor;
        editor.Tool = EditTool.Markup;
        vm.CurrentSession!.History.Commit(vm.CurrentRecipe with { Markup = [new MarkupBox { Corner = new(2, 2), OppositeCorner = new(20, 20), StrokeWidth = 2 }] });

        editor.MarkupTool = MarkupTool.Select;
        editor.MarkupPress(new SKPoint(2, 10), 2);
        Assert.IsTrue(editor.HasSelection);
        editor.MarkupDrag(new SKPoint(7, 10), 0.5f);
        editor.MarkupRelease();
        Assert.AreEqual(new MarkupPoint(7, 2), ((MarkupBox)vm.CurrentRecipe.Markup[0]).Corner);
        Assert.IsNotNull(editor.SelectedOutline, "the selection is drawn around it");

        editor.DeleteSelectedCommand.Execute(null);
        Assert.AreEqual(0, vm.CurrentRecipe.Markup.Length);
        Assert.IsFalse(editor.DeleteSelectedCommand.CanExecute(null));

        editor.MarkupPress(new SKPoint(30, 50), 2);
        Assert.IsFalse(editor.HasSelection, "a click on nothing selects nothing");
    }

    [TestMethod]
    [CoversNode("images-edit-markup-style")]
    public void TheStyleControls_ApplyToTheSelectedAnnotation()
    {
        var vm = Editing();
        var editor = vm.Editor;
        editor.Tool = EditTool.Markup;
        vm.CurrentSession!.History.Commit(vm.CurrentRecipe with { Markup = [new MarkupBox { Corner = new(2, 2), OppositeCorner = new(20, 20), StrokeWidth = 2 }] });
        editor.MarkupTool = MarkupTool.Select;
        editor.MarkupPress(new SKPoint(2, 10), 2);
        editor.MarkupRelease();

        editor.InkColour = ColorSpec.Custom(System.Windows.Media.Color.FromRgb(0x12, 0x34, 0x56));
        editor.Weight = 20;
        editor.FillBoxes = true;

        var box = (MarkupBox)vm.CurrentRecipe.Markup[0];
        Assert.AreEqual(0xFF123456u, box.Color);
        Assert.AreEqual(20 * Math.Max(Upright.Width, Upright.Height) / 800.0, box.StrokeWidth, 1e-6, "weight is relative to the picture");
        Assert.AreEqual(0x40123456u, box.Fill, "a wash of the ink");
    }

    // ── Resize, export ──────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-edit-resize")]
    public void ResizingByPercentOrPixels_SetsTheSavedSize()
    {
        var vm = Editing();
        var editor = vm.Editor;

        editor.ResizePercent = 50;
        StringAssert.Contains(editor.OutputSizeText, "16");

        editor.ResizeByPixels = true;
        Assert.IsTrue(editor.ResizeByPixels, "the choice holds");
        editor.ResizeWidth = 8;
        Assert.AreEqual(new SKSizeI(8, 16), vm.CurrentSession!.OutputSize(vm.CurrentRecipe), "the height follows, proportions kept");

        editor.ResetResizeCommand.Execute(null);
        Assert.IsNull(vm.CurrentRecipe.Resize);
        editor.ResizeByPixels = true;
        Assert.IsTrue(editor.ResizeByPixels, "choosing pixels without changing the size is no edit, but the panel remembers");
        Assert.IsNull(vm.CurrentRecipe.Resize);
    }

    [TestMethod]
    [CoversNode("images-edit-export")]
    public void ExportOptions_AreKeptWithTheEdits()
    {
        var vm = Editing();
        vm.Editor.StripMetadata = true;
        vm.Editor.Quality = 70;
        Assert.AreEqual(new RecipeExport { StripMetadata = true, JpegQuality = 70 }, vm.CurrentRecipe.Export);
        vm.Editor.Quality = 10;
        Assert.AreEqual(50, vm.CurrentRecipe.Export.JpegQuality, "never below what still looks like the photo");
    }

    // ── The toolbar ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-edit-compare")]
    public void BeforeShowsThePhotoAsItIsOnDisk()
    {
        var vm = Editing();
        vm.RotateRightCommand.Execute(null);
        Settle(vm);
        Assert.AreEqual(1, vm.Canvas!.Recipe.Geometry.QuarterTurns);

        vm.Editor.ShowOriginal = true;
        Settle(vm);
        Assert.IsTrue(vm.Canvas!.Recipe.IsIdentity, "the presented frame is the unedited photo");
        Assert.AreEqual(1, vm.CurrentRecipe.Geometry.QuarterTurns, "and the edits are untouched");
    }

    [TestMethod]
    [CoversNode("images-edit-revert")]
    [CoversNode("images-edit-done")]
    public void RevertIsUndoable_AndDoneLeavesTheEdits()
    {
        var vm = Editing();
        vm.RotateRightCommand.Execute(null);
        vm.RevertEditsCommand.Execute(null);
        Assert.IsFalse(vm.IsEdited);
        vm.UndoEditCommand.Execute(null);
        Assert.IsTrue(vm.IsEdited);

        vm.ExitEditModeCommand.Execute(null);
        Assert.IsFalse(vm.IsEditing);
        Assert.IsTrue(vm.IsCarousel);
        Assert.IsTrue(vm.IsEdited, "leaving the editor keeps the edits, shown on view");
    }

    [TestMethod]
    [CoversNode("images-edit-save")]
    [CoversNode("images-bake")]
    public async Task SaveWritesTheEditsIntoThePhoto_AndSpendsTheRecipe()
    {
        var vm = Editing();
        vm.RotateRightCommand.Execute(null);
        Assert.IsTrue(vm.Editor.SaveCommand.CanExecute(null));

        await vm.Editor.SaveCommand.ExecuteAsync(null);
        Settle(vm, () => !vm.IsEdited);

        using var saved = new ImageEditingServices().Decode(_photo, null);
        Assert.AreEqual(SKEncodedOrigin.TopLeft, saved.Origin);
        Assert.AreEqual(new SKSizeI(64, 32), saved.StoredFullSize, "upright, then turned a quarter: 64 wide");
        Assert.IsFalse(File.Exists(ImageRecipeStore.SidecarPath(_photo)), "the edits are in the file now");
        Assert.IsFalse(vm.IsEdited, "and the photo is shown as it now is, with nothing to undo into");
        Assert.IsFalse(vm.UndoEditCommand.CanExecute(null));
    }

    [TestMethod]
    [CoversNode("images-edit-save-copy")]
    public async Task SaveAsCopy_WritesTheFormatItsNameAsks_AndLeavesTheOriginal()
    {
        var copy = Path.Combine(_dir, "copy.png");
        var vm = Editing(Shell(saveAs: copy));
        vm.RotateRightCommand.Execute(null);
        var original = File.ReadAllBytes(_photo);

        await vm.Editor.SaveCopyCommand.ExecuteAsync(null);

        Assert.AreEqual(ImageContainer.Png, ImageContainers.Sniff(File.ReadAllBytes(copy).AsSpan(0, 16)));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(_photo), "the original is untouched");
        Assert.IsTrue(vm.IsEdited, "and keeps its edits");
    }

    [TestMethod]
    [CoversNode("images-edit-save")]
    public void AnAnimatedOrUnwritableImage_OffersOnlyACopy_AndSaysWhy()
    {
        var ico = Path.Combine(_dir, "icon.ico");
        using (var bmp = Quadrants(16, 16)) File.WriteAllBytes(ico, IcoOf(EncodePng(bmp)));
        _photo = ico;

        var vm = Editing();
        Assert.IsFalse(vm.Editor.CanSaveInPlace);
        Assert.IsFalse(vm.Editor.SaveCommand.CanExecute(null));
        Assert.IsTrue(vm.Editor.SaveCopyCommand.CanExecute(null));
        Assert.IsNotNull(vm.Editor.SaveNote);
    }

    /// <summary>A one-image ICO wrapping <paramref name="png"/> — the format nothing here writes.</summary>
    private static byte[] IcoOf(byte[] png)
    {
        var ico = new byte[22 + png.Length];
        ico[2] = 1; ico[4] = 1;                                  // type icon, one image
        ico[6] = 16; ico[7] = 16; ico[10] = 1; ico[12] = 32;     // 16×16, one plane, 32 bpp
        BitConverter.GetBytes(png.Length).CopyTo(ico, 14);
        BitConverter.GetBytes(22).CopyTo(ico, 18);
        png.CopyTo(ico, 22);
        return ico;
    }

    [TestMethod]
    [CoversNode("images-edit-banner")]
    public void APhotoChangedSinceItsEdits_AsksBeforeApplyingThem()
    {
        File.WriteAllText(ImageRecipeStore.SidecarPath(_photo),
            """{ "version": 1, "source": { "length": 1, "lastWriteUtc": "2000-01-01T00:00:00Z", "contentHash": "00" }, "geometry": { "quarterTurns": 1 } }""");
        var vm = Editing();

        Assert.IsTrue(vm.HasChangedSourceNotice);
        vm.KeepChangedEditsCommand.Execute(null);
        Assert.IsFalse(vm.HasChangedSourceNotice);
        Assert.AreEqual(1, vm.CurrentRecipe.Geometry.QuarterTurns, "kept");
    }

    // ── Preview and the assistant ───────────────────────────────────────────

    [TestMethod]
    [CoversNode("images-render-preview")]
    public void ThePreview_IsTheEditedPicture_SizedForTheScreen()
    {
        var vm = new ImageViewModel([_photo], Shell(), ImageViewMode.Carousel, new ImageEditingServices());
        vm.SetViewport(16, 16, 1);
        vm.RotateRightCommand.Execute(null);
        Settle(vm, () => vm.CurrentImage is not null);

        var (width, height) = _ui.Invoke(() => (vm.CurrentImage!.PixelWidth, vm.CurrentImage.PixelHeight));
        Assert.AreEqual(16, Math.Max(width, height), "fitted to the 16 px viewport");
        Assert.IsTrue(width > height, "and turned: the upright portrait is landscape now");
    }

    [TestMethod]
    [CoversNode("images-ai-act-capture-image")]
    public async Task CaptureImage_HandsTheAssistantTheEditedPicture()
    {
        var vm = Editing();
        vm.RotateRightCommand.Execute(null);
        var capture = vm.GetClientTools().Single(t => t.Name == "capture_image");

        var result = await capture.InvokeAsync(new JsonObject(), CancellationToken.None);

        Assert.IsTrue(result.Success);
        var png = result.Images!.Single().Bytes;
        using var decoded = SKBitmap.Decode(png);
        Assert.AreEqual(64, decoded.Width, "the picture as edited: upright, then turned");
        Assert.AreEqual(32, decoded.Height);
    }

    // ── Opening, from the file browser ──────────────────────────────────────

    [TestMethod]
    [CoversNode("images-open-edit")]
    public void TheEditAction_OpensTheImagesStraightIntoTheEditor()
    {
        var shell = Shell();
        Assert.IsTrue(new EditImageAction(shell).PerformAction([_photo, Path.Combine(_dir, "notes.txt")]));
        shell.Received().OpenTab("Images", Arg.Is<Dictionary<string, string>>(p => p["view"] == "edit" && p["paths"] == _photo));
    }

    [TestMethod]
    [CoversNode("images-revert-action")]
    public void TheRevertAction_ConfirmsThenRemovesTheEdits_AndAsksForTheCompanion()
    {
        new ImageRecipeStore().Save(_photo, new ImageRecipe { Geometry = new RecipeGeometry { QuarterTurns = 1 } });
        var shell = Shell();
        var action = new RevertImageEditsAction(shell);

        Assert.AreEqual(ImageRecipeStore.Extension, action.RequiredCompanionSuffix, "offered only on an edited image");
        action.PerformAction(_photo);

        shell.Received().ShowConfirmation(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action>(), Arg.Any<Action>());
        Assert.IsFalse(File.Exists(ImageRecipeStore.SidecarPath(_photo)));
        Assert.IsFalse(action.PerformAction(Path.Combine(_dir, "unedited.jpg")), "nothing to revert");
    }
}
