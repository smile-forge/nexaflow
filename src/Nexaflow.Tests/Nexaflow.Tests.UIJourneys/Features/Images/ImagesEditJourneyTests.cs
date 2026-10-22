using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// One-pass UI journey for editing: every control of edit mode, end to end, on copies of the generated samples in a
/// folder of the journey's own — edits write a sidecar beside the photo and Save rewrites it, neither of which may
/// happen to the shared dataset.
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
    /// <summary>A recipe (a quarter turn) made against some other file, so the photo reads as changed since.</summary>
    private const string StaleRecipe =
        """{ "version": 1, "source": { "length": 1, "lastWriteUtc": "2000-01-01T00:00:00Z", "contentHash": "00" }, "geometry": { "quarterTurns": 1 } }""";

    /// <summary>The same, made on a machine with the AI models: the photo's background removed around whatever is called a
    /// mug, and the photo AI-enhanced. Kept, it asks for picture selection and the picture tools, which are never here.</summary>
    private const string StaleCutOutRecipe =
        """{ "version": 3, "source": { "length": 1, "lastWriteUtc": "2000-01-01T00:00:00Z", "contentHash": "00" }, "geometry": { "quarterTurns": 1 }, "cutOut": { "phrase": "mug" }, "restore": { "scale": 1 } }""";

    /// <summary>
    /// An AI folder of its own, empty: the picture tools are never here, so AI enhance always offers them for download —
    /// the same banner on every machine, whatever this one has downloaded. A journey downloads nothing.
    /// </summary>
    private readonly string _aiDir = TestTemp.CreateSubdirectory("nexaflow-edit-ai-").FullName;

    protected override IReadOnlyDictionary<string, string> LaunchEnvironment =>
        new Dictionary<string, string> { ["NEXAFLOW_AI_DIR"] = _aiDir };

    [TestCleanup]
    public void RemoveAiDir()
    {
        try { Directory.Delete(_aiDir, recursive: true); } catch { }
    }

    [TestMethod]
    [CoversNode("images-ui")]
    public void Image_Edit_ControlsRespondInOnePass()
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

            // ── Into edit mode ──
            CheckDoes("Edit opens the editor", "Image_EditMode", () => WaitForFs(() => Exists("ImageEditView"), 5));

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
            Check("Adjust panel", () => SelectRadio("Image_EditToolAdjust"));
            CheckInvoke("Auto enhance", "Image_EditAuto");
            foreach (var slider in new[] { "Image_EditExposure", "Image_EditContrast", "Image_EditHighlights", "Image_EditShadows",
                                           "Image_EditSaturation", "Image_EditTemperature", "Image_EditTint" })
                Check($"{slider} moves", () => MoveSlider(slider));
            CheckInvoke("Reset adjustments", "Image_EditResetAdjust");

            // AI enhance, kept from the edits, can still be turned off; then it waits for the picture tools, which the panel offers
            // over the picture in place of the kept edits' banner — the AI tools journey turns it on where they are here.
            Check("AI enhance from the kept edits turns off; its banner goes, and the panel's offer of the picture tools takes over",
                  () => SetToggle("Image_EditAiEnhance", false) && WaitForGone("Image_RestoreAction", 3)
                        && WaitForFs(() => WaitForId("Image_RestoringModelsDownload", 1)?.IsEnabled == true, 3));
            Check("AI enhance now waits for the picture tools", () => !Enabled("Image_EditAiEnhance"));

            // Markup: a text annotation typed, an arrow drawn, selected and deleted
            Check("Markup panel", () => SelectRadio("Image_EditToolMarkup"));
            CheckPresent("Ink colour picker", "Image_EditInk_Hex");
            Check("Line weight slider moves", () => MoveSlider("Image_EditWeight"));
            Check("Fill boxes toggles", () => SetToggle("Image_EditFillBoxes", true) && SetToggle("Image_EditFillBoxes", false));
            Check("Box tool", () => SelectRadio("Image_EditMarkupBox"));
            Check("Pen tool", () => SelectRadio("Image_EditMarkupPen"));
            Check("Highlighter tool", () => SelectRadio("Image_EditMarkupHighlighter"));
            Check("Text tool", () => SelectRadio("Image_EditMarkupText"));
            CheckPresent("Font list", "Image_EditFont");
            Check("Text size slider moves", () => MoveSlider("Image_EditTextSize"));
            Check("Text backing toggles", () => SetToggle("Image_EditTextBacking", true) && SetToggle("Image_EditTextBacking", false));
            // The banners over the picture come and go with the panels, and the small sample stays where it was laid out: fit it
            // to the canvas, so the canvas's centre is on the photo.
            Check("The photo fitted, a click places text, typed in the panel", () => FitCanvas() && ClickCanvas() && TypeInto("Image_EditMarkupTextBox", "Hello"));
            Check("Arrow tool draws an arrow", () => SelectRadio("Image_EditMarkupArrow") && DragOnCanvas(-8, -8, 8, 8));
            Check("Select tool picks the arrow", () => SelectRadio("Image_EditMarkupSelect") && ClickCanvas()
                                                       && WaitForFs(() => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Image_EditDeleteMarkup"))?.IsEnabled == true, 3));
            CheckInvoke("Delete the selected annotation", "Image_EditDeleteMarkup");
            CheckInvoke("Clear every annotation", "Image_EditClearMarkup");

            // Resize
            Check("Resize panel", () => SelectRadio("Image_EditToolResize"));
            Check("Percent slider moves", () => MoveSlider("Image_EditResizePercentValue"));
            Check("Resize by pixels", () => SelectRadio("Image_EditResizePixels"));
            Check("Width typed", () => TypeInto("Image_EditResizeWidth", "20"));
            CheckPresent("Height box", "Image_EditResizeHeight");
            Check("Keep proportions toggles", () => SetToggle("Image_EditResizeLock", false) && SetToggle("Image_EditResizeLock", true));
            CheckInvoke("Original size", "Image_EditResetResize");
            Check("Resize by percent", () => SelectRadio("Image_EditResizePercent"));
            Check("AI upscale waits for the picture tools, offered over the picture", () => !Enabled("Image_EditUpscaleTwice") && !Enabled("Image_EditUpscaleFourTimes")
                                                                                          && Exists("Image_RestoringModelsDownload"));
            Check("AI upscale stays off", () => SelectRadio("Image_EditUpscaleOff"));

            // Cut out: picture selection is never here (the AI folder is the journey's own), so everything that picks waits for it,
            // saying why; the AI tools journey picks where it is here.
            Check("Cut out waits for picture selection: what picks is disabled", () => SelectRadio("Image_EditToolCutOut")
                  && WaitForFs(() => Exists("Image_CutOutPhrase"), 3) && !Enabled("Image_CutOutPhrase") && !Enabled("Image_CutOutFind")
                  && !Enabled("Image_CutOutRemoveBackground") && !Enabled("Image_CutOutCopy") && !Enabled("Image_CutOutSave"));
            CheckInvoke("Clear what is picked", "Image_CutOutClear");
            CheckDoes("Keep background puts it back; the kept edits' banner goes, and the panel's offer of picture selection takes over",
                      "Image_CutOutKeepBackground",
                      () => WaitForGone("Image_RemoveBackgroundAction", 3) && WaitForFs(() => WaitForId("Image_PickingModelsDownload", 1)?.IsEnabled == true, 3));

            // Retouch: picking waits for picture selection and AI fill for picture erasing, neither of which the journey's AI folder
            // has, so it opens on the brush and content-aware fill, which need nothing and erase at once; the AI tools journey picks
            // and fills by AI where they are here.
            Check("Retouch opens on the brush: picking waits for picture selection", () => SelectRadio("Image_EditToolRetouch")
                  && WaitForFs(() => Exists("Image_EraseBrushSize"), 3) && SelectRadio("Image_EraseMarkBrush") && !Enabled("Image_EraseMarkPick"));
            Check("AI fill waits for picture erasing, offered over the picture", () => !Enabled("Image_EraseFillAi")
                  && WaitForFs(() => WaitForId("Image_ErasingModelsDownload", 1)?.IsEnabled == true, 3));
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

            CheckDoes("Done leaves the editor", "Image_EditDone", () => WaitForFs(() => !Exists("ImageEditView") || Exists("Image_Next"), 5));

            AssertJourney();
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
}
