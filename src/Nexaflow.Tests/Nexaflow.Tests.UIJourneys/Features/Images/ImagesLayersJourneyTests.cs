using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// Layered pictures, the user driving: a blank canvas set up and made from the file view's New picker, opened in the editor
/// and taken through every control of the layers panel; a photo turned into layers from the editor's toolbar; and an
/// OpenRaster file holding a layer no reader can draw, opened on its saved look and taken on either way.
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
public class ImagesLayersJourneyTests : ImageEditJourneyBase
{
    private string _folder = "";

    /// <summary>Every layers control and its effect: more than the usual budget.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromSeconds(200);

    [TestInitialize]
    public void MakeFolder()
    {
        _folder = Path.Combine(TestTemp.Root, "nexaflow_layers_journey_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TestCleanup]
    public void RemoveFolder()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); } catch { }
    }

    [TestMethod]
    [CoversNode("images-layers-panel")]
    public void ACanvasFromTheNewPicker_IsMadeOpenedAndTakenThroughItsLayers()
    {
        NavigateFileBrowserTo(_folder);

        // ── The New picker's layered picture: a size by name or by hand, a unit, a resolution, a background ──
        CheckDoes("New opens the picker", "NewMenuAction", () => WaitForId("CreateFileNameBox", 6) is not null, 10);
        CheckDoes("Layered picture asks the canvas's size and background", "LayeredPictureCreateAction", () => WaitForId("Images_CanvasPreset", 6) is not null);
        CheckPresent("They sit in the picker", "Images_CanvasSetup");
        Check("A size picked by name fills in its width and height", () => PickPreset("1024") && SizeReads("1024", "1024"));
        Check("A size typed by hand", () => TypeInto("Images_CanvasWidth", "640") && TypeInto("Images_CanvasHeight", "480"));
        CheckDoes("Swap turns it a quarter", "Images_CanvasSwap", () => SizeReads("480", "640"));
        Check("In inches the size reads in inches, and in pixels again as it was",
              () => PickIndex("Images_CanvasUnit", 2) && WaitForFs(() => TextOf("Images_CanvasWidth") != "480", 2)
                    && PickIndex("Images_CanvasUnit", 0) && SizeReads("480", "640"));
        Check("A resolution typed", () => TypeInto("Images_CanvasDpi", "150"));
        Check("Background: white, a colour, or transparent", () => SelectRadio("Images_CanvasBackgroundWhite")
              && SelectRadio("Images_CanvasBackgroundColour") && SelectRadio("Images_CanvasBackgroundTransparent"));
        Check("The name is given", () => TypeInto("CreateFileNameBox", "poster"));
        CheckDoes("Create makes the canvas", "FileSystem_CreateConfirm", () => WaitForGone("CreateFileNameBox", 4));
        var poster = Path.Combine(_folder, "poster.ora");
        Check("It lands in the folder as OpenRaster", () => WaitForFs(() => File.Exists(poster), 10));
        Check("It opens in the editor on its one empty layer", () => WaitForFs(() => Exists("Image_LayersPanel"), 15) && LayersBecome(1));
        var made = File.GetLastWriteTimeUtc(poster);

        // ── A layer's own settings ──
        CheckDoes("Add a layer", "Image_LayersAddPixel", () => LayersBecome(2));
        Check("Name it", () => TypeInto("Image_LayersName", "Paint"));
        Check("Its opacity moves", () => MoveSlider("Image_LayersOpacity"));
        Check("A blend is picked", () => PickIndex("Image_LayersBlend", 1));
        Check("Clip to the layer beneath, and back", () => ToggleAndBack("Image_LayersClip"));
        Check("Keep its transparency, and back", () => ToggleAndBack("Image_LayersAlphaLock"));
        Check("Lock it, and back", () => ToggleAndBack("Image_LayersLock"));
        Check("Hide it, and show it", () => ToggleAndBack("Image_LayersVisible"));
        CheckInvoke("Make it active by its thumbnail", "Image_LayersThumbnail");
        CheckDoes("Duplicate it", "Image_LayersDuplicate", () => LayersBecome(3));
        CheckInvoke("Move it down", "Image_LayersMoveDown");
        CheckInvoke("Move it up", "Image_LayersMoveUp");
        CheckDoes("Delete it", "Image_LayersDelete", () => LayersBecome(2));

        // ── What a selection does to the active layer ──
        Check("The pixels section opens", () => Expand("Image_LayersPixelsSection"));
        Check("Select all of the canvas", SelectAllOnCanvas);
        CheckInvoke("Fill the selection", "Image_LayersFill");
        CheckInvoke("Copy it", "Image_LayersCopy");
        CheckInvoke("Copy what shows of every layer", "Image_LayersCopyMerged");
        CheckDoes("Paste it as a layer", "Image_LayersPaste", () => LayersBecome(3));
        CheckInvoke("Cut it", "Image_LayersCut");
        CheckInvoke("Clear what is selected", "Image_LayersClear");
        CheckDoes("Merge it down", "Image_LayersMergeDown", () => LayersBecome(2));

        // ── Masks ──
        Check("The mask section opens", () => Expand("Image_LayersMaskSection"));
        // A layer has one mask: each way of making one is offered while it has none.
        CheckDoes("A mask that reveals the whole layer", "Image_LayersMaskRevealAll", () => WaitForFs(() => Exists("Image_LayersMaskThumbnail"), 3));
        Check("With a mask, no other is made", () => !Enabled("Image_LayersMaskHideAll") && !Enabled("Image_LayersMaskFromSelection"));
        CheckInvoke("Edit the mask by its thumbnail", "Image_LayersMaskThumbnail");
        CheckInvoke("Invert the mask", "Image_LayersMaskInvert");
        CheckDoes("Apply the mask to the pixels", "Image_LayersMaskApply", () => WaitForGone("Image_LayersMaskThumbnail", 3));
        CheckDoes("A mask hiding the whole layer", "Image_LayersMaskHideAll", () => WaitForFs(() => Exists("Image_LayersMaskThumbnail"), 3));
        CheckDoes("Delete it", "Image_LayersMaskDelete", () => WaitForGone("Image_LayersMaskThumbnail", 3));
        CheckDoes("A mask from the selection", "Image_LayersMaskFromSelection", () => WaitForFs(() => Exists("Image_LayersMaskThumbnail"), 3));
        CheckDoes("Delete it", "Image_LayersMaskDelete", () => WaitForGone("Image_LayersMaskThumbnail", 3));
        CheckDoes("A mask hiding the selection", "Image_LayersMaskHideSelection", () => WaitForFs(() => Exists("Image_LayersMaskThumbnail"), 3));
        CheckDoes("Delete it", "Image_LayersMaskDelete", () => WaitForGone("Image_LayersMaskThumbnail", 3));
        CheckDoes("A mask from the layer's transparency", "Image_LayersMaskFromTransparency", () => WaitForFs(() => Exists("Image_LayersMaskThumbnail"), 3));
        CheckDoes("Delete it", "Image_LayersMaskDelete", () => WaitForGone("Image_LayersMaskThumbnail", 3));
        Check("A mask from a cut-out waits for one picked", () => !Enabled("Image_LayersMaskFromCutOut"));

        // ── The canvas ──
        Check("The canvas section opens", () => Expand("Image_LayersCanvasSection"));
        Check("A new canvas size", () => TypeInto("Image_LayersCanvasWidth", "800") && TypeInto("Image_LayersCanvasHeight", "600"));
        foreach (var anchor in new[] { "Image_LayersAnchorTopLeft", "Image_LayersAnchorTop", "Image_LayersAnchorTopRight", "Image_LayersAnchorLeft",
                                       "Image_LayersAnchorRight", "Image_LayersAnchorBottomLeft", "Image_LayersAnchorBottom",
                                       "Image_LayersAnchorBottomRight", "Image_LayersAnchorCentre" })
            Check($"Anchored at {anchor}", () => SelectRadio(anchor));
        CheckInvoke("Resize the canvas", "Image_LayersCanvasApply");
        Check("Select all of the canvas again", SelectAllOnCanvas);
        CheckInvoke("Crop the canvas to the selection", "Image_LayersCropToSelection");

        // ── Notes, groups and flattening ──
        CheckDoes("Notes go on a layer of their own", "Image_LayersAddMarkup", () => LayersBecome(3));
        CheckDoes("Rasterize the notes into pixels", "Image_LayersRasterize", () => WaitForFs(() => !Enabled("Image_LayersRasterize"), 3));
        CheckDoes("Group the layer", "Image_LayersGroup", () => WaitForFs(() => Exists("Image_LayersIsolated"), 3));
        Check("The group blends on its own, or straight onto what is beneath", () => ToggleAndBack("Image_LayersIsolated"));
        CheckDoes("Flatten every layer into one", "Image_LayersFlatten", () => LayersBecome(1));
        Check("The canvas is saved back to its file", () => WaitForFs(() => File.GetLastWriteTimeUtc(poster) > made, 10));

        AssertJourney();
    }

    [TestMethod]
    [CoversNode("images-layers")]
    public void APhoto_BecomesLayersFromTheEditorsToolbar()
    {
        var sample = TestSampleData.Files("images").First(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        File.Copy(sample, Path.Combine(_folder, "photo.png"));
        OpenEditorOnTheFolder();

        // A photo has no layer to paint on or move: the Paint and Move tools offer one.
        Check("Paint offers a layer to paint on", () => SelectRadio("Image_EditToolPaint") && WaitForFs(() => Exists("Image_PaintPanel"), 3)
              && WaitForFs(() => Exists("Image_PaintAddLayer"), 3));
        Check("Move offers a layer to move", () => SelectRadio("Image_EditToolMove") && WaitForFs(() => Exists("Image_ArrangePanel"), 3)
              && WaitForFs(() => Exists("Image_ArrangeAddLayer"), 3));
        Check("Back to the crop", () => SelectRadio("Image_EditToolCrop"));

        CheckDoes("Add layer makes the photo a layered picture, with a layer above it", "Image_EditAddLayer",
                  () => WaitForFs(() => Exists("Image_LayersPanel"), 5) && LayersBecome(2));
        Check("The photo is untouched beside it: nothing is written until it is saved", () => !File.Exists(Path.Combine(_folder, "photo.ora")));
        Check("The pixels section opens", () => Expand("Image_LayersPixelsSection"));
        CheckInvoke("Copy what shows", "Image_LayersCopyMerged");
        CheckDoes("Paste as layer adds what was copied", "Image_EditPasteLayer", () => LayersBecome(3));

        AssertJourney();
    }

    [TestMethod]
    [CoversNode("images-ora")]
    public void AFileHoldingALayerNoReaderDraws_IsEditedOnItsSavedLook()
    {
        WriteOpenRasterWithAFilterLayer(Path.Combine(_folder, "look.ora"));
        OpenEditorOnTheFolder();

        CheckPresent("The bar says a layer cannot be drawn, and offers the saved look", "Image_SavedLookUse", 10);
        CheckDoes("Use the saved look: one layer made from it, and the bar goes", "Image_SavedLookUse",
                  () => WaitForGone("Image_SavedLookUse", 5) && LayersBecome(1));

        AssertJourney();
    }

    [TestMethod]
    [CoversNode("images-ora")]
    public void AFileHoldingALayerNoReaderDraws_KeepsItsLayersWhenTheyAreEdited()
    {
        WriteOpenRasterWithAFilterLayer(Path.Combine(_folder, "look.ora"));
        OpenEditorOnTheFolder();

        CheckPresent("The bar offers to edit the layers", "Image_SavedLookEditLayers", 10);
        CheckDoes("Edit the layers: the bar goes, and the layer it cannot draw stays in the list", "Image_SavedLookEditLayers",
                  () => WaitForGone("Image_SavedLookEditLayers", 5) && LayersBecome(2));

        AssertJourney();
    }

    [TestMethod]
    [CoversNode("images-paint")]
    public void ACanvas_IsPaintedOn_ItsLayerMovedAndTransformed_AndPrintIsSetUp()
    {
        WriteCanvas(Path.Combine(_folder, "canvas.ora"));
        OpenEditorOnTheFolder();
        Check("It opens on its layers", () => WaitForFs(() => Exists("Image_LayersPanel"), 10) && LayersBecome(1));

        // ── Paint ──
        Check("Paint is chosen, its panel showing", () => SelectRadio("Image_EditToolPaint") && WaitForFs(() => Exists("Image_PaintPanel"), 3));
        Check("The brush", () => SelectRadio("Image_PaintBrush"));
        foreach (var slider in new[] { "Image_PaintSize", "Image_PaintHardness", "Image_PaintOpacity", "Image_PaintFlow", "Image_PaintSpacing" })
            Check($"{slider} moves", () => MoveSlider(slider));
        Check("Pen pressure sets the size, and back", () => ToggleAndBack("Image_PaintPressureSize"));
        Check("Pen pressure sets the opacity, and back", () => ToggleAndBack("Image_PaintPressureOpacity"));
        Check("A stroke is painted, one step of the history", () => FitCanvas() && DragOnCanvas(-30, -10, 30, 10) && WaitForFs(() => Enabled("Image_EditUndo"), 3));
        Check("The eraser takes some away", () => SelectRadio("Image_PaintEraser") && DragOnCanvas(-10, -10, 10, 10));
        Check("The fill", () => SelectRadio("Image_PaintFill") && WaitForFs(() => Exists("Image_PaintTolerance"), 2));
        Check("Its tolerance moves", () => MoveSlider("Image_PaintTolerance"));
        Check("Joined only, and back", () => ToggleAndBack("Image_PaintContiguous"));
        Check("A click fills", ClickCanvas);
        Check("The eyedropper", () => SelectRadio("Image_PaintEyedropper") && WaitForFs(() => Exists("Image_PaintSampleMerged"), 2));
        Check("It picks from every layer, or the active one", () => ToggleAndBack("Image_PaintSampleMerged"));
        Check("A click picks a colour", ClickCanvas);

        // ── Move and transform ──
        Check("Move is chosen, its panel showing", () => SelectRadio("Image_EditToolMove") && WaitForFs(() => Exists("Image_ArrangePanel"), 3));
        Check("A drag moves the layer", () => DragOnCanvas(0, 0, 20, 10));
        Check("What is selected floats once it is moved", () => SelectAllOnCanvas() && DragOnCanvas(0, 0, -10, -5)
              && WaitForFs(() => Exists("Image_ArrangeDrop"), 3) && LayersBecome(2));
        CheckDoes("Drop lays it back into its layer", "Image_ArrangeDrop", () => LayersBecome(1));
        CheckDoes("Transform shows its frame and the panel's turns and flips", "Image_ArrangeTransform", () => WaitForFs(() => Exists("Image_ArrangeApply"), 3));
        CheckInvoke("Flip across", "Image_ArrangeFlipHorizontal");
        CheckInvoke("Flip upside down", "Image_ArrangeFlipVertical");
        CheckInvoke("Turn left", "Image_ArrangeRotateLeft");
        CheckInvoke("Turn right", "Image_ArrangeRotateRight");
        CheckDoes("Cancel puts it back", "Image_ArrangeCancel", () => WaitForGone("Image_ArrangeApply", 3));
        CheckDoes("Transform again", "Image_ArrangeTransform", () => WaitForFs(() => Exists("Image_ArrangeApply"), 3));
        CheckInvoke("Turn right", "Image_ArrangeRotateRight");
        CheckDoes("Apply keeps it", "Image_ArrangeApply", () => WaitForGone("Image_ArrangeApply", 3));

        // ── Print ──
        Check("Print opens its options", () => SetToggle("Image_Print", true) && WaitForFs(() => FindInAppWindows("Image_PrintOptions") is not null, 3));
        Check("Fill the page", () => SelectRadio("Image_PrintFill"));
        Check("Actual size", () => SelectRadio("Image_PrintActual"));
        Check("A width", () => SelectRadio("Image_PrintWidth") && TypeInto("Image_PrintWidthMillimetres", "120"));
        Check("Fit to page", () => SelectRadio("Image_PrintFit"));
        Check("Landscape, portrait, or as the picture suggests", () => SelectRadio("Image_PrintLandscape") && SelectRadio("Image_PrintPortrait") && SelectRadio("Image_PrintAuto"));
        Check("Margins typed", () => TypeInto("Image_PrintMargins", "12"));
        Check("Centred, and back", () => ToggleAndBack("Image_PrintCentre"));
        Check("Print… chooses the printer", () => Enabled("Image_PrintGo"));
        CheckDoes("Close puts the options away", "Image_PrintClose", () => WaitForFs(() => FindInAppWindows("Image_PrintOptions") is null, 3));

        AssertJourney();
    }

    /// <summary>A small OpenRaster canvas: one layer of the sample picture's pixels, and its merged image.</summary>
    private static void WriteCanvas(string path)
    {
        var png = File.ReadAllBytes(TestSampleData.Files("images").First(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));
        int width  = png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19];
        int height = png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23];
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var mime = new StreamWriter(zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open(), new UTF8Encoding(false)))
            mime.Write("image/openraster");
        using (var xml = new StreamWriter(zip.CreateEntry("stack.xml").Open(), new UTF8Encoding(false)))
            xml.Write($"""
                <?xml version="1.0" encoding="UTF-8"?>
                <image version="0.0.6" w="{width}" h="{height}">
                 <stack>
                  <layer src="data/base.png" x="0" y="0" name="Base"/>
                 </stack>
                </image>
                """);
        using (var layer = zip.CreateEntry("data/base.png").Open()) layer.Write(png);
        using (var merged = zip.CreateEntry("mergedimage.png").Open()) merged.Write(png);
    }

    /// <summary>The folder's pictures shown, and the first opened in Artist.</summary>
    private void OpenEditorOnTheFolder()
    {
        NavigateFileBrowserTo(_folder);
        var slideshow = WaitForId("SlideshowFolderAction", 8);
        Assert.IsNotNull(slideshow, "The 'Slideshow' folder action did not appear for the journey's folder.");
        slideshow!.AsButton().Invoke();
        Assert.IsNotNull(WaitForId("ImageView", 15), "Slideshow did not open the image viewer.");
        CheckDoes("Edit opens the picture in an Artist tab", "Image_Edit", () => WaitForFs(() => Exists("ArtistView") && Exists("ImageEditView"), 8));
    }

    /// <summary>
    /// An OpenRaster file as Krita writes one with a filter layer: a picture layer, the filter no reader draws, and the merged
    /// image Krita drew with it.
    /// </summary>
    private static void WriteOpenRasterWithAFilterLayer(string path)
    {
        var png = File.ReadAllBytes(TestSampleData.Files("images").First(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)));
        int width  = png[16] << 24 | png[17] << 16 | png[18] << 8 | png[19];
        int height = png[20] << 24 | png[21] << 16 | png[22] << 8 | png[23];
        var stack = $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <image version="0.0.1" w="{width}" h="{height}">
             <stack>
              <filter type="applications:krita:levels" name="Levels" opacity="1" visibility="visible"/>
              <layer src="data/base.png" x="0" y="0" name="Base"/>
             </stack>
            </image>
            """;
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (var mime = new StreamWriter(zip.CreateEntry("mimetype", CompressionLevel.NoCompression).Open(), new UTF8Encoding(false)))
            mime.Write("image/openraster");
        using (var xml = new StreamWriter(zip.CreateEntry("stack.xml").Open(), new UTF8Encoding(false)))
            xml.Write(stack);
        using (var layer = zip.CreateEntry("data/base.png").Open())
            layer.Write(png);
        using (var merged = zip.CreateEntry("mergedimage.png").Open())
            merged.Write(png);
    }

    private int LayerCount() => FindInAppWindows("Image_LayersList")?.AsListBox().Items.Length ?? -1;

    private bool LayersBecome(int count) => WaitForFs(() => LayerCount() == count, 3);

    private string? TextOf(string automationId) => FindInAppWindows(automationId)?.AsTextBox().Text;

    private bool SizeReads(string width, string height) => WaitForFs(() => TextOf("Images_CanvasWidth") == width && TextOf("Images_CanvasHeight") == height, 2);

    /// <summary>Picks the size offered by name whose label holds <paramref name="dimension"/>.</summary>
    private bool PickPreset(string dimension)
    {
        if (FindInAppWindows("Images_CanvasPreset")?.AsComboBox() is not { } presets) return false;
        var index = Array.FindIndex(presets.Items, i => i.Name.Contains(dimension, StringComparison.Ordinal));
        return index >= 0 && PickIndex("Images_CanvasPreset", index);
    }

    private bool PickIndex(string automationId, int index)
    {
        if (FindInAppWindows(automationId)?.AsComboBox() is not { } box || box.Items.Length <= index) return false;
        var picked = box.Select(index);
        return WaitForFs(() => box.SelectedItem?.Name == picked.Name, 2);
    }

    /// <summary>Opens a section of the layers panel.</summary>
    private bool Expand(string automationId)
    {
        if (FindInAppWindows(automationId)?.Patterns.ExpandCollapse.PatternOrDefault is not { } section) return false;
        section.Expand();
        return WaitForFs(() => section.ExpandCollapseState.Value == ExpandCollapseState.Expanded, 2);
    }

    /// <summary>Ctrl+A over the canvas: everything selected, whichever tool is out — seen in the pixels section's Fill being offered.</summary>
    private bool SelectAllOnCanvas()
    {
        if (!Expand("Image_LayersPixelsSection") || !ClickCanvas()) return false;
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => Enabled("Image_LayersFill"), 2);
    }
}
