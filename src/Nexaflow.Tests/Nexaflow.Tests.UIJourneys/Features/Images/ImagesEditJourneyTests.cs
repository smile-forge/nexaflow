using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using System.Collections.Generic;
using FlaUI.Core.WindowsAPI;
using Point = System.Drawing.Point;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// The Images journey, in an app whose workspace draws no pictures and whose model folder is empty (so every machine meets the
/// same banners and offers): a draft picture, the image viewer and its slideshow, edit mode with every control end to end, the
/// histogram, every tool's panels, and Artist's start screen — a canvas created, the clipboard's picture pasted, a canvas opened
/// before — then pictures dragged onto Artist. Layers are <see cref="ImagesLayersJourneyTests"/>'s, which would take this past two
/// minutes, and the pictures a model draws are the model journeys'.
/// <para>
/// Edit mode is driven on copies of the generated samples in a folder of the journey's own — edits write a sidecar beside the photo
/// and Save rewrites it, neither of which may happen to the shared dataset.
/// <para>
/// Both photos start with a recipe made against a different file, so the changed-photo notice shows: the first
/// photo's edits are kept, the second's discarded. The canvas is driven with the mouse — a text annotation placed and
/// typed, an arrow drawn, selected and deleted — and the pass ends with a real Save, confirmed, after which the
/// file on disk is the edited picture and its sidecar is gone. Save as copy opens the file picker, so it is checked
/// for presence only.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("images")]
public class ImagesEditJourneyTests : ImageEditJourneyBase
{
    /// <summary>Every scenario of the feature that needs no model and no more than two minutes between them, one launch.</summary>
    [TestMethod]
    [CoversNode("images-ui")]
    [CoversNode("images-edit-histogram")]
    [CoversNode("images-edit-panels")]
    [CoversNode("images-start")]
    [CoversNode("made-file-drafts")]
    [CoversNode("images-layers-panel")]
    public void Images_Journey()
    {
        RunStep("a draft picture says it is not saved", DraftStrip);

        RunStep("the image viewer's tools", ViewerControls);
        RunStep("the slideshow's controls", SlideshowControls);
        RunStep("edit mode: every control, ending in a real Save", EditControls);
        RunStep("the histogram says when highlights clip", Histogram);
        RunStep("every tool's groups fold and open, and a typed value is taken", ToolPanels);
        // Before the others, which each add a canvas to the list the seeded one is among.
        RunStep("a canvas opened before is opened again from the list", () => { OpenArtistStartScreen(); RecentCanvasOpenedAgain(); });
        RunStep("a blank canvas, set up and created from the start screen", () => { OpenArtistStartScreen(); BlankCanvasFromTheStartScreen(); });
        RunStep("the clipboard's picture, pasted as a canvas", () => { PutPictureOnTheClipboard(); OpenArtistStartScreen(); ClipboardPicturePasted(); });
        RunStep("pictures dropped anywhere on Artist become layers", () => { OpenArtistStartScreen(); PicturesDroppedOnArtist(); });
        AssertJourney();
    }
    /// <summary>A recipe (a quarter turn) made against some other file, so the photo reads as changed since.</summary>
    private const string StaleRecipe =
        """{ "version": 1, "source": { "length": 1, "lastWriteUtc": "2000-01-01T00:00:00Z", "contentHash": "00" }, "geometry": { "quarterTurns": 1 } }""";

    /// <summary>The same, made on a machine with the AI models: the photo's background removed around whatever is called a
    /// mug, and the photo AI-enhanced. Kept, it asks for picture selection and the picture tools, which are never here.</summary>
    private const string StaleCutOutRecipe =
        """{ "version": 3, "source": { "length": 1, "lastWriteUtc": "2000-01-01T00:00:00Z", "contentHash": "00" }, "geometry": { "quarterTurns": 1 }, "cutOut": { "phrase": "mug" }, "restore": { "scale": 1 } }""";

    /// <summary>A hundred controls, each one look and its effect: more than the usual budget.</summary>
    protected override TimeSpan JourneyBudget => TimeSpan.FromSeconds(200);

    private void EditControls()
    {
        var folder = Path.Combine(TestTemp.Root, "nexaflow_edit_journey_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var photo = Path.Combine(folder, "a_" + OrientedPhotoSample.FileName);
            File.Copy(TestSampleData.Path("images", OrientedPhotoSample.FileName), photo);
            var plain = Path.Combine(folder, "b_" + Path.GetFileName(TestSampleData.Files("images").First(f => f.EndsWith(".png"))));
            File.Copy(TestSampleData.Files("images").First(f => f.EndsWith(".png")), plain);
            File.WriteAllText(photo + ".nfedit", StaleCutOutRecipe);
            File.WriteAllText(plain + ".nfedit", StaleRecipe);

            NavigateFileBrowserTo(folder);
            var slideshow = WaitForId("SlideshowFolderAction", 8);
            Assert.IsNotNull(slideshow, "The 'Slideshow' folder action did not appear for the journey's folder.");
            slideshow!.AsButton().Invoke();
            Assert.IsNotNull(WaitForId("ImageView", 15), "Slideshow did not open the image viewer.");

            // ── The photos changed since their recipes: keep one, discard the other ──
            CheckDoes("Keep the edits made against an earlier file", "Image_EditKeepChanged", () => WaitForFs(() => !Exists("Image_EditKeepChanged"), 3));
            Check("The kept edits remove its background and enhance it: the banners offer picture selection and the picture tools",
                  () => WaitForFs(() => WaitForId("Image_RemoveBackgroundAction", 1)?.IsEnabled == true && WaitForId("Image_RestoreAction", 1)?.IsEnabled == true, 5));
            CheckInvoke("Next photo", "Image_Next");
            CheckDoes("Discard the edits made against an earlier file", "Image_EditDiscardChanged", () => WaitForFs(() => !Exists("Image_EditDiscardChanged"), 3));
            CheckInvoke("Back to the first photo", "Image_Previous");



            // ── Floating tools: flips are remembered edits like turns ──
            CheckInvoke("Flip horizontally", "Image_FlipHorizontal");
            CheckInvoke("Flip vertically", "Image_FlipVertical");

            // ── Into Artist ──
            CheckDoes("Edit opens the photo in an Artist tab", "Image_Edit", () => WaitForFs(() => Exists("ArtistView") && Exists("ImageEditView"), 8));

            // Crop & rotate (the default panel)
            CheckInvoke("Edit: rotate left", "Image_EditRotateLeft");
            CheckInvoke("Edit: rotate right", "Image_EditRotateRight");
            CheckInvoke("Edit: flip horizontally", "Image_EditFlipHorizontal");
            CheckInvoke("Edit: flip vertically", "Image_EditFlipVertical");
            Check("Aspect: original", () => SelectRadio("Image_EditAspectOriginal"));
            Check("Aspect: 1:1", () => SelectRadio("Image_EditAspectSquare"));
            Check("Aspect: 4:3", () => SelectRadio("Image_EditAspectFourThree"));
            Check("Aspect: 16:9", () => SelectRadio("Image_EditAspectSixteenNine"));
            Check("Aspect held in portrait", () => SetToggle("Image_EditAspectPortrait", true) && SetToggle("Image_EditAspectPortrait", false));
            Check("Aspect: free", () => SelectRadio("Image_EditAspectFree"));
            Check("Straighten slider moves", () => MoveSlider("Image_EditStraighten"));
            Check("Level tool arms and disarms", () => SetToggle("Image_EditLevel", true) && SetToggle("Image_EditLevel", false));
            CheckInvoke("Reset crop and straighten", "Image_EditResetCrop");

            // Adjust
            Check("Adjust panel, its Light and Colour opened", () => SelectRadio("Image_EditToolAdjust")
                  && OpenGroup("Image_GroupAdjustLight") && OpenGroup("Image_GroupAdjustColour"));
            CheckInvoke("Auto enhance", "Image_EditAuto");
            foreach (var slider in new[] { "Image_EditExposure", "Image_EditContrast", "Image_EditHighlights", "Image_EditShadows",
                                           "Image_EditSaturation", "Image_EditTemperature", "Image_EditTint" })
                Check($"{slider} moves", () => MoveSlider(slider));
            CheckInvoke("Reset adjustments", "Image_EditResetAdjust");

            // AI enhance, kept from the edits, can still be turned off; then it waits for the picture tools. The removed background
            // kept with it waits on the same models and its banner already offers them, so they are offered there, once — the panel's
            // own offer takes over only when that banner goes, below. The AI tools journey turns it on where they are here.
            Check("AI enhance from the kept edits turns off", () => SetToggle("Image_EditAiEnhance", false));
            Check("Its banner goes", () => WaitForGone("Image_RestoreAction", 3));
            Check("The AI picture models are offered once, by the removed background's banner",
                  () => WaitForFs(() => WaitForId("Image_RemoveBackgroundAction", 1)?.IsEnabled == true, 3) && !Exists("Image_PictureModelsDownload"));
            Check("AI enhance now waits for the AI picture models", () => !Enabled("Image_EditAiEnhance"));

            // Markup: its tools and how they draw
            Check("Markup panel", () => SelectRadio("Image_EditToolMarkup"));
            Check("The shared colour shows beside the picture, its picker opening over it",
                  () => WaitForFs(() => Exists("Image_ColourDock"), 3) && SetToggle("Image_ColourCurrent", true)
                        && WaitFor(() => FindInAppWindows("Image_Colour_Hex"), 3) is not null && SetToggle("Image_ColourCurrent", false));
            Check("A colour on the palette is chosen, and the current colour added to it", () => PressNth("Image_ColourSwatch", 1) && PressNth("Image_ColourAdd", 0));
            Check("Line weight slider moves", () => MoveSlider("Image_EditWeight"));
            Check("Fill boxes toggles", () => SetToggle("Image_EditFillBoxes", true) && SetToggle("Image_EditFillBoxes", false));
            Check("Box tool", () => SelectRadio("Image_EditMarkupBox"));
            Check("Pen tool", () => SelectRadio("Image_EditMarkupPen"));
            Check("Highlighter tool", () => SelectRadio("Image_EditMarkupHighlighter"));
            Check("Text tool", () => SelectRadio("Image_EditMarkupText"));
            CheckPresent("Font list", "Image_EditFont");
            Check("Text size slider moves", () => MoveSlider("Image_EditTextSize"));
            Check("Text backing toggles", () => SetToggle("Image_EditTextBacking", true) && SetToggle("Image_EditTextBacking", false));
            // Drawing goes on a markup layer, which makes the photo a layered picture: the layers journey draws, so this one can
            // go on to save the photo as a photo.

            // Resize
            Check("Resize panel", () => SelectRadio("Image_EditToolResize"));
            Check("Percent slider moves", () => MoveSlider("Image_EditResizePercentValue"));
            Check("Resize by pixels", () => SelectRadio("Image_EditResizePixels"));
            Check("Width typed", () => TypeInto("Image_EditResizeWidth", "20"));
            CheckPresent("Height box", "Image_EditResizeHeight");
            Check("Keep proportions toggles", () => SetToggle("Image_EditResizeLock", false) && SetToggle("Image_EditResizeLock", true));
            CheckInvoke("Original size", "Image_EditResetResize");
            Check("Resize by percent", () => SelectRadio("Image_EditResizePercent"));
            Check("AI upscale waits for the AI picture models", () => !Enabled("Image_EditUpscaleTwice") && !Enabled("Image_EditUpscaleFourTimes"));
            Check("They are offered over the picture, once", () => Exists("Image_RemoveBackgroundAction") && !Exists("Image_PictureModelsDownload"));
            Check("AI upscale stays off", () => SelectRadio("Image_EditUpscaleOff"));

            // Cut out: picture selection is never here (the AI folder is the journey's own), so picking waits for it, saying why, and
            // the tool opens on the marquee, which needs nothing; the AI tools journey picks where it is here.
            Check("Cut out opens on the marquee: picking waits for picture selection, and nothing is selected to take",
                  () => SelectRadio("Image_EditToolSelect") && WaitForFs(() => Exists("Image_SelectRectangle"), 3)
                        && !Enabled("Image_CutOutMarkPick") && !Enabled("Image_RegionSelectSubject") && !Enabled("Image_RegionFeather")
                        && !Enabled("Image_CutOutCopy") && !Enabled("Image_CutOutSave"));
            Check("An ellipse or a rectangle, each a way of selecting", () => SelectRadio("Image_SelectEllipse") && SelectRadio("Image_SelectRectangle"));
            Check("Each shape adds, subtracts, intersects or starts the selection again",
                  () => SelectRadio("Image_RegionModeAdd") && SelectRadio("Image_RegionModeSubtract") && SelectRadio("Image_RegionModeIntersect")
                        && SelectRadio("Image_RegionModeReplace"));
            // A marquee has to start on the photo: fit it to the canvas first, so the canvas's centre is on the photo.
            Check("A marquee dragged on the photo selects, and what takes the selection is offered",
                  () => FitCanvas() && DragOnCanvasAcross(0.08)
                        && WaitForFs(() => Enabled("Image_CutOutCopy"), 3) && Enabled("Image_CutOutSave") && Enabled("Image_CutOutRemoveBackground"));
            // Moved to its far end and back to its least, so the marquee outlasts Shrink.
            Check("Refine by moves", () => MoveSlider("Image_RegionRefineBy") && MoveSlider("Image_RegionRefineBy"));
            CheckInvoke("Feather the selection", "Image_RegionFeather");
            CheckInvoke("Grow it", "Image_RegionGrow");
            CheckInvoke("Shrink it", "Image_RegionShrink");

            CheckInvoke("Smooth it", "Image_RegionSmooth");
            CheckInvoke("Invert it", "Image_RegionInvert");
            CheckInvoke("Select all of the photo", "Image_RegionSelectAll");
            Check("A lasso drawn freehand, or a polygon click to click", () => SelectRadio("Image_SelectPolygon") && SelectRadio("Image_SelectLasso"));
            Check("By colour, with its tolerance and joined-only, a click selecting", () => SelectRadio("Image_SelectColour")
                  && WaitForFs(() => Exists("Image_RegionTolerance"), 2) && MoveSlider("Image_RegionTolerance") && ToggleAndBack("Image_RegionJoined")
                  && ClickCanvas() && WaitForFs(() => Enabled("Image_CutOutCopy"), 3));
            CheckDoes("Select nothing, and nothing is offered to take it", "Image_RegionDeselect", () => WaitForFs(() => !Enabled("Image_CutOutCopy"), 3));
            Check("Back to the rectangle", () => SelectRadio("Image_SelectRectangle"));
            CheckInvoke("Clear what is selected", "Image_CutOutClear");
            CheckDoes("Keep background puts it back; the kept edits' banner goes, and the panel's offer of the AI picture models takes over",
                      "Image_CutOutKeepBackground",
                      () => WaitForGone("Image_RemoveBackgroundAction", 3) && WaitForFs(() => WaitForId("Image_PictureModelsDownload", 1)?.IsEnabled == true, 3));

            // Retouch: picking waits for picture selection and AI fill for picture erasing, neither of which the journey's AI folder
            // has, so it opens on the brush and content-aware fill, which need nothing and erase at once; the AI tools journey picks
            // and fills by AI where they are here.
            Check("Retouch brushes what to erase, and erases what Select selects", () => SelectRadio("Image_EditToolRetouch")
                  && WaitForFs(() => Exists("Image_EraseBrushSize"), 3) && !Exists("Image_SelectRectangle"));
            Check("AI fill waits for the AI picture models, offered over the picture", () => !Enabled("Image_EraseFillAi")
                  && WaitForFs(() => WaitForId("Image_PictureModelsDownload", 1)?.IsEnabled == true, 3));
            Check("Brush size slider moves", () => MoveSlider("Image_EraseBrushSize"));
            Check("Heal, which asks where to copy from", () => SelectRadio("Image_EraseFillHeal"));
            // A brush stroke has to start on the photo, and the edits above leave the small sample wherever they left the view: fit
            // it to the canvas first, so the canvas's centre is on the photo.
            Check("Content-aware fill, the photo fitted, a stroke brushed and erased at once",
                  () => SelectRadio("Image_EraseFillContentAware") && FitCanvas() && DragOnCanvas(-10, 4, 10, 4)
                        && WaitForFs(() => WaitForId("Image_EraseApply", 1)?.IsEnabled == true, 3) && PressNth("Image_EraseApply", 0)
                        && WaitForGone("Image_EraseAction", 5));
            CheckInvoke("Clear what is marked", "Image_EraseClear");

            // Save options: beside Save rather than among the tools, in a popup
            Check("Back to the crop panel", () => SelectRadio("Image_EditToolCrop"));
            CheckDoes("Save options open beside Save", "Image_EditSaveOptions", () => WaitFor(() => FindInAppWindows("Image_EditStripMetadata"), 3) is not null);
            Check("Strip metadata toggles", () => ToggleAndBack("Image_EditStripMetadata"));
            Check("Quality slider moves", () => MoveSlider("Image_EditQuality"));
            Check("A click on the picture puts them away", () => ClickCanvas() && WaitForFs(() => FindInAppWindows("Image_EditStripMetadata") is null, 3));

            // History and compare
            CheckInvoke("Undo", "Image_EditUndo");
            CheckInvoke("Redo", "Image_EditRedo");
            Check("Before/after toggles", () => SetToggle("Image_EditCompare", true) && SetToggle("Image_EditCompare", false));
            CheckInvoke("Revert to original", "Image_EditRevert");
            CheckInvoke("Turn again, so there is something to save", "Image_EditRotateRight");

            // Save: confirmed, the photo is rewritten and its recipe is spent
            var before = File.ReadAllBytes(photo);
            CheckPresent("Save as copy", "Image_EditSaveCopy");
            Check("Save writes the edits into the photo", () =>
                Exists("Image_EditSave") && PressSave()
                && WaitForFs(() => !File.Exists(photo + ".nfedit") && !File.ReadAllBytes(photo).SequenceEqual(before), 10));


            
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* still open in the app; the temp folder is cleaned by the OS */ }
        }
    }

    private bool PressSave()
    {
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Image_EditSave"))?.AsButton().Invoke();
        return PressInDialog("Chrome_ConfirmOk");
    }

    /// <summary>None of Nexaflow's own models, whatever this machine's store holds, so the start screen offers the image model's
    /// download here as it does on a machine that has never fetched one.</summary>
    private readonly EmptyModelStore _store = new();

    protected override IReadOnlyDictionary<string, string> LaunchEnvironment => _store.LaunchEnvironment;

    private string _folder = "";

    private string _recent = "";

    protected override string? LaunchTabKind => "Artist";

    /// <summary>A canvas opened here before, remembered by the Images settings, a workspace that draws no pictures, and a
    /// picture on the clipboard.</summary>
    protected override void SeedConfig(string configDir)
    {
        _folder = Path.Combine(TestTemp.Root, "nexaflow-start-journey-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _recent = Path.Combine(_folder, "poster.ora");
        WriteCanvas(_recent);

        var version = FileVersionInfo.GetVersionInfo(BuiltOutput.App).FileVersion ?? "1.0.0.0";
        var images = Path.Combine(configDir, "images");
        Directory.CreateDirectory(images);
        File.WriteAllText(Path.Combine(images, $"config_{version}.json"), $$"""
            {
              "ConfigName": "images",
              "HistoryMegabytes": 1024,
              "RecentCanvases": [ {{JsonSerializer.Serialize(_recent)}} ]
            }
            """);

        // A workspace with no image model chosen. It settles onto Nexaflow's own as it loads, where this machine can run one, so the
        // start screen offers that model's download — or, where there is none to download, the workspace's AI settings.
        var abilities = Path.Combine(configDir, "Contexts", "Default", "ai-abilities");
        Directory.CreateDirectory(abilities);
        File.WriteAllText(Path.Combine(abilities, $"config_{version}.json"), """
            {
              "ConfigName": "ai-abilities",
              "FriendlyName": "AI",
              "Columns": [],
              "Assignments": {}
            }
            """);

    }

    [TestCleanup]
    public void RemoveFolder()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); } catch { }
    }

    private void BlankCanvasFromTheStartScreen()
    {
        CheckPresent("An Artist tab with no picture shows its start screen", "Images_Start", 15);
        CheckPresent("Its canvas is set up as New's is", "Images_CanvasSetup");
        Check("No model draws yet: its download is offered, or else the workspace's AI settings",
              () => WaitForFs(() => Exists("Images_GenerateDownload") || Exists("Images_StartAiSettings"), 5));
        CheckPresent("Pictures can be opened from disk", "Images_StartOpen");
        Check("No picture yet, so no toolbar: nothing to print or save", () => !Exists("Image_Print") && !Exists("Image_EditSave"));
        Check("A size typed by hand", () => TypeInto("Images_CanvasWidth", "320") && TypeInto("Images_CanvasHeight", "200"));
        CheckDoes("Create opens the canvas in the editor", "Images_StartCreate",
                  () => WaitForFs(() => Exists("Image_LayersPanel"), 15) && WaitForGone("Images_Start", 3));


    }

    private void ClipboardPicturePasted()
    {
        CheckPresent("The start screen offers what the clipboard holds", "Images_StartPaste", 15);
        CheckDoes("Paste opens it as a canvas of its own", "Images_StartPaste", () => WaitForFs(() => Exists("Image_LayersPanel"), 15));


    }

    private void RecentCanvasOpenedAgain()
    {
        CheckPresent("Canvases opened before are listed", "Images_StartRecentList", 15);
        // Each row is a button, Images_StartRecent, which the accessibility tree exposes only as the list item once other pages
        // have been about in the app, so the row is found by what it lists and pressed where it is.
        Check("The canvas opened before is among them", () => WaitForFs(() => RecentRow() is not null, 5));
        Check("Clicked, it opens in the editor", () =>
        {
            RecentRow()?.Click();
            return WaitForFs(() => Exists("Image_LayersPanel"), 15);
        });


    }

    /// <summary>A small OpenRaster canvas: one layer and its merged image.</summary>
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

    /// <summary>A small picture on the clipboard, as another application puts one there.</summary>
    private static void PutPictureOnTheClipboard()
    {
        var thread = new Thread(() =>
        {
            var pixels = Enumerable.Repeat((byte)180, 8 * 6 * 4).ToArray();
            Clipboard.SetImage(BitmapSource.Create(8, 6, 96, 96, PixelFormats.Bgra32, null, pixels, 8 * 4));
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    private static readonly string Sample = TestSampleData.Files("images").First();

    private readonly string _draft = Drafted(Sample);

    protected override IReadOnlyDictionary<string, string> LaunchTabParams => new Dictionary<string, string>
    {
        ["path"]  = _draft,
        ["draft"] = Path.GetDirectoryName(Sample)!,
    };

    /// <summary>Artist's Save asks where a draft goes, so the strip over it only says it is not saved.</summary>
    private void DraftStrip()
    {
        Check("The draft opens in Artist", () => WaitForId("ArtistView", 15) is not null);
        CheckPresent("The draft says it is not saved", "Draft_Banner");
        Check("The strip has no Save as of its own: Save is it", () => WaitForId("Image_EditSave", 5) is not null && FindInAppWindows("Draft_SaveAs") is null);


    }

    /// <summary>A copy of <paramref name="sample"/> in a temp folder of its own, as a draft is kept.</summary>
    private static string Drafted(string sample)
    {
        var folder = TestTemp.CreateSubdirectory("nexaflow-draft-journey-").FullName;
        var draft = Path.Combine(folder, Path.GetFileName(sample));
        File.Copy(sample, draft);
        return draft;
    }

    private void ViewerControls()
    {
        var file = Path.GetFileName(TestSampleData.Files("images").First());
        var view = OpenFileVia(TestSampleData.Path("images"), file, "ShowImageAction", "ImageView");
        Assert.IsNotNull(view, "ImageView did not open via the 'As Image' action.");

        // Floating image-tools toolbar (rotate / fit) — pure in-place state, safe to invoke.
        CheckInvoke("Rotate left", "Image_RotateLeft");
        CheckInvoke("Rotate right", "Image_RotateRight");
        CheckInvoke("Fit / actual-size toggle", "Image_FitMode");

        // Details: what the picture's file says about it, in a panel beside it.
        Check("Details opens beside the picture", () => SetToggle("Image_Details", true) && WaitForFs(() => Exists("Image_DetailsPanel"), 3));
        Check("It names the picture's file", () => WaitForFs(() => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Image_DetailsPanel"))
                                                                        ?.FindFirstDescendant(cf => cf.ByName(file)) is not null, 5));
        CheckDoes("Its close button puts it away", "Image_DetailsClose", () => WaitForGone("Image_DetailsPanel", 3));

        // Full-screen — present for every image; invoking it opens a separate window, so only check presence.
        CheckPresent("Full screen", "Image_FullScreen");

        // Convert to Markdown — offered, where this machine can read pages, for every picture until page reading is here to tell
        // a page. Page reading is not in this run's model folder, so converting waits on its download, which the conversion's
        // banner offers; a reading under way, and its Stop, are left to PageConversionTests, which can hold one open.
        if (WaitForId("Image_ConvertToMarkdown", 3) is not null)
            CheckDoes("Convert to Markdown", "Image_ConvertToMarkdown",
                      () => WaitForId("PageConversion_Status", 5) is not null && WaitForId("PageConversion_Download", 5) is not null);


    }

    /// <summary>
    /// The multi-image pass. The "Slideshow" folder action opens the whole sample folder in the carousel, and the
    /// sample set is larger than the 20-dot window, so the dot strip pages. Everything here is in-place view state.
    /// </summary>
    private void SlideshowControls()
    {
        NavigateFileBrowserTo(TestSampleData.Path("images"));
        var slideshow = WaitForId("SlideshowFolderAction", 8);
        Assert.IsNotNull(slideshow, "The 'Slideshow' folder action did not appear for the image samples folder.");
        slideshow!.AsButton().Invoke();
        Assert.IsNotNull(WaitForId("ImageView", 15), "Slideshow did not open the image viewer.");

        // ── Carousel: arrows, then the dot strip ──────────────────────────────
        CheckInvoke("Next image", "Image_Next");
        CheckInvoke("Previous image", "Image_Previous");
        Check("A dot jumps to its image", () =>
            WaitForFs(() => CountOf("Image_DotJump") > 1, 5) && PressNth("Image_DotJump", 1));
        CheckDoes("› pages the dots on by 20", "Image_DotsPageRight", () => WaitForFs(() => Exists("Image_DotsPageLeft"), 3));
        CheckDoes("‹ pages back to the first 20", "Image_DotsPageLeft", () => WaitForFs(() => !Exists("Image_DotsPageLeft"), 3));

        // ── Auto-advance: on, a different speed, and off again ────────────────
        CheckInvoke("Auto-advance on", "Image_AutoAdvance");
        Check("The speed slider moves", () => MoveSlider("Image_AutoSpeed"));
        CheckInvoke("Auto-advance off", "Image_AutoAdvance");

        // ── Sub-views, ending back on the carousel ────────────────────────────
        Check("Album view", () => SelectRadio("Image_ViewAlbum"));
        Check("Explore view", () => SelectRadio("Image_ViewExplore"));
        Check("Collage view", () => SelectRadio("Image_ViewCollage"));
        Check("Carousel view", () => SelectRadio("Image_ViewCarousel") && WaitForFs(() => Exists("Image_Next"), 3));


    }

    private string? Status(string markId) => FindInAppWindows(markId)?.Properties.ItemStatus.ValueOrDefault;

    private bool SetsExposure(double stops)
    {
        if (!OpenGroup("Image_GroupAdjustLight") || FindInAppWindows("Image_EditExposure")?.Patterns.RangeValue.PatternOrDefault is not { } range) return false;
        range.SetValue(stops);
        return WaitForFs(() => Math.Abs(range.Value.Value - stops) < 1e-6, 2);
    }

    private bool Toggles(string markId, ToggleState to)
    {
        if (FindInAppWindows(markId)?.Patterns.Toggle.PatternOrDefault is not { } toggle) return false;
        if (toggle.ToggleState.Value != to) toggle.Toggle();
        return WaitForFs(() => toggle.ToggleState.Value == to, 2);
    }

    private void Histogram()
    {
        var folder = Path.Combine(TestTemp.Root, "nexaflow_histogram_journey_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.Copy(TestSampleData.Path("images", OrientedPhotoSample.FileName), Path.Combine(folder, OrientedPhotoSample.FileName));
            Assert.IsNotNull(OpenFileVia(folder, OrientedPhotoSample.FileName, "ShowImageAction", "ImageView"), "ImageView did not open via the 'As Image' action.");
            CheckDoes("Edit opens the photo in an Artist tab", "Image_Edit", () => WaitForFs(() => Exists("ArtistView") && Exists("ImageEditView"), 8));

            Check("The histogram heads Adjust, with a clip mark at either end", () => SelectRadio("Image_EditToolAdjust")
                  && Exists("Image_GroupAdjustHistogram") && Exists("Image_HistogramShadows") && Exists("Image_HistogramHighlights"));
            Check("It folds and opens like any group", () => FoldsAndOpens("Image_GroupAdjustHistogram"));

            string? clear = null;
            Check("Three stops down, the highlights are clear", () => SetsExposure(-3) && WaitForFs(() => (clear = Status("Image_HistogramHighlights")) is { Length: > 0 }, 4));
            Check("Three stops up, the highlights mark says they clip", () => SetsExposure(3)
                  && WaitForFs(() => Status("Image_HistogramHighlights") is { Length: > 0 } now && now != clear, 6));

            Check("Pressed, the clipping is kept shown", () => Toggles("Image_HistogramHighlights", ToggleState.On));
            Check("Pressed again, it is let go", () => Toggles("Image_HistogramHighlights", ToggleState.Off));
            Check("The shadows mark keeps theirs the same way", () => Toggles("Image_HistogramShadows", ToggleState.On)
                  && Toggles("Image_HistogramShadows", ToggleState.Off));
            Check("Exposure back to none", () => SetsExposure(0));


        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* still open in the app; the temp folder is cleaned by the OS */ }
        }
    }

    /// <summary>The group <paramref name="automationId"/> folded and opened again, through its expand and collapse.</summary>
    private bool FoldsAndOpens(string automationId)
    {
        var group = FindInAppWindows(automationId);
        try { group?.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView(); } catch { /* not in a scroller */ }
        if (group?.Patterns.ExpandCollapse.PatternOrDefault is not { } fold) return false;
        fold.Collapse();
        if (!WaitForFs(() => fold.ExpandCollapseState.Value == ExpandCollapseState.Collapsed, 2)) return false;
        fold.Expand();
        return WaitForFs(() => fold.ExpandCollapseState.Value == ExpandCollapseState.Expanded, 2);
    }

    /// <summary>A group shown only where what it needs is here: folded and opened where it shows.</summary>
    private bool FoldsWhereShown(string automationId) => !Exists(automationId) || FoldsAndOpens(automationId);

    private bool Folded(string automationId, bool folded)
    {
        if (FindInAppWindows(automationId)?.Patterns.ExpandCollapse.PatternOrDefault is not { } fold) return false;
        var state = folded ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded;
        if (fold.ExpandCollapseState.Value != state) { if (folded) fold.Collapse(); else fold.Expand(); }
        return WaitForFs(() => fold.ExpandCollapseState.Value == state, 2);
    }

    private bool IsFolded(string automationId)
        => FindInAppWindows(automationId)?.Patterns.ExpandCollapse.PatternOrDefault?.ExpandCollapseState.Value == ExpandCollapseState.Collapsed;

    /// <summary><paramref name="typed"/> typed into the value at the end of <paramref name="sliderId"/>'s row and taken with
    /// Enter: the slider then stands at <paramref name="meant"/>.</summary>
    private bool TypesValue(string sliderId, string typed, double meant)
    {
        var slider = FindInAppWindows(sliderId);
        try { slider?.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView(); } catch { /* not in a scroller */ }
        if (slider?.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit)) is not { } box) return false;
        box.Click();
        Wait.UntilInputIsProcessed();
        Keyboard.Type(typed);
        Keyboard.Type(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => slider.Patterns.RangeValue.PatternOrDefault is { } range && Math.Abs(range.Value.Value - meant) < 1e-6, 2);
    }

    private void ToolPanels()
    {
        var folder = Path.Combine(TestTemp.Root, "nexaflow_panels_journey_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.Copy(TestSampleData.Path("images", OrientedPhotoSample.FileName), Path.Combine(folder, OrientedPhotoSample.FileName));
            Assert.IsNotNull(OpenFileVia(folder, OrientedPhotoSample.FileName, "ShowImageAction", "ImageView"), "ImageView did not open via the 'As Image' action.");
            CheckDoes("Edit opens the photo in an Artist tab", "Image_Edit", () => WaitForFs(() => Exists("ArtistView") && Exists("ImageEditView"), 8));

            // Crop & rotate.
            Check("Crop's groups fold and open", () => SelectRadio("Image_EditToolCrop") && FoldsAndOpens("Image_GroupCropRotate")
                  && FoldsAndOpens("Image_GroupCropAspect") && FoldsAndOpens("Image_GroupCropStraighten"));
            Check("An angle typed is taken", () => TypesValue("Image_EditStraighten", "2.5", 2.5));

            // Adjust, the group folded staying folded as the tools change.
            Check("Adjust's groups fold and open", () => SelectRadio("Image_EditToolAdjust") && FoldsAndOpens("Image_GroupAdjustHistogram")
                  && FoldsAndOpens("Image_GroupAdjustQuickFix")
                  && FoldsAndOpens("Image_GroupAdjustLight") && FoldsAndOpens("Image_GroupAdjustColour") && FoldsWhereShown("Image_GroupAdjustDepth"));
            Check("An exposure typed is taken, with its sign", () => TypesValue("Image_EditExposure", "+0.5", 0.5));
            Check("Light folded stays folded through another tool", () => Folded("Image_GroupAdjustLight", true)
                  && SelectRadio("Image_EditToolCrop") && SelectRadio("Image_EditToolAdjust") && IsFolded("Image_GroupAdjustLight")
                  && Folded("Image_GroupAdjustLight", false));

            // Markup, its text group with the text tool.
            Check("Markup's groups fold and open", () => SelectRadio("Image_EditToolMarkup") && FoldsAndOpens("Image_GroupMarkupTool")
                  && FoldsAndOpens("Image_GroupMarkupStroke"));
            Check("With the text tool, its text group folds and opens", () => SelectRadio("Image_EditMarkupText") && FoldsAndOpens("Image_GroupMarkupText"));

            // Resize.
            Check("Resize's groups fold and open", () => SelectRadio("Image_EditToolResize") && FoldsAndOpens("Image_GroupResizeSize")
                  && FoldsWhereShown("Image_GroupResizeUpscale"));

            // Select, how a shape meets the selection with a shape to draw.
            Check("Select's groups fold and open", () => SelectRadio("Image_EditToolSelect") && SelectRadio("Image_SelectRectangle")
                  && FoldsAndOpens("Image_GroupSelectBy") && FoldsAndOpens("Image_GroupSelectCombine")
                  && FoldsAndOpens("Image_GroupSelectSelection") && FoldsAndOpens("Image_GroupSelectWith"));

            // Retouch.
            Check("Retouch's groups fold and open", () => SelectRadio("Image_EditToolRetouch") && FoldsAndOpens("Image_GroupRetouchMark")
                  && FoldsAndOpens("Image_GroupRetouchFill"));

            // Paint, its fill group with the fill.
            Check("Paint's groups fold and open", () => SelectRadio("Image_EditToolPaint") && FoldsAndOpens("Image_GroupPaintTool")
                  && FoldsAndOpens("Image_GroupPaintBrush"));
            Check("With the fill, its group folds and opens", () => SelectRadio("Image_PaintFill") && FoldsAndOpens("Image_GroupPaintFill"));


        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* still open in the app; the temp folder is cleaned by the OS */ }
        }
    }

    private void PicturesDroppedOnArtist()
    {


                var start = WaitForId("Images_Start", 15);
        Assert.IsNotNull(start, "Artist's start screen did not open.");
        var r = start.BoundingRectangle;
        var from = new Point(r.Left + 70, r.Bottom - 50);
        var middle = new Point(r.Left + r.Width * 2 / 5, r.Top + r.Height / 2);

        var folder = TestTemp.CreateSubdirectory("artist-drops-").FullName;
        var first = Picture(folder, "first.png", 0x40);
        var second = Picture(folder, "second.png", 0x90);
        var third = Picture(folder, "third.png", 0xD0);

        OutsideDrag.Files(from, middle, first);
        Check("A picture dropped on the start screen opens", () => WaitForId("Image_EditCanvas", 10) is { IsOffscreen: false });

        OutsideDrag.Files(from, middle, second);
        Check("A picture dropped on the canvas is a layer over the first", () => WaitForFs(() => Layers() == 2, 10));

        if (WaitForId("Image_LayersList", 5) is { } list)
        {
            var l = list.BoundingRectangle;
            OutsideDrag.Files(from, new Point(l.Left + l.Width / 2, l.Top + 15), third);
            Check("A picture dropped on the layers is one too", () => WaitForFs(() => Layers() == 3, 10));
        }
        else Check("The layers are shown", () => false);

        Check("The pictures stay where they were", () => File.Exists(first) && File.Exists(second) && File.Exists(third));


    }

    private int Layers() => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Image_LayersList"))
        ?.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem)).Length ?? 0;

    private static string Picture(string folder, string name, byte shade)
    {
        var pixels = new byte[320 * 240 * 4];
        Array.Fill(pixels, shade);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(320, 240, 96, 96, PixelFormats.Bgra32, null, pixels, 320 * 4)));

        var path = Path.Combine(folder, name);
        using var file = File.Create(path);
        encoder.Save(file);
        return path;
    }

    /// <summary>The start screen's row for the canvas opened before: its list item, named by what it lists.</summary>
    private AutomationElement? RecentRow() =>
        FindInAppWindows("Images_StartRecentList")?.FindAllChildren().FirstOrDefault(row => row.Name.Contains(Path.GetFileName(_recent), StringComparison.OrdinalIgnoreCase));
}
