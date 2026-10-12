using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
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
public class ImagesEditJourneyTests : UiJourneyTestBase
{
    /// <summary>A recipe (a quarter turn) made against some other file, so the photo reads as changed since.</summary>
    private const string StaleRecipe =
        """{ "version": 1, "source": { "length": 1, "lastWriteUtc": "2000-01-01T00:00:00Z", "contentHash": "00" }, "geometry": { "quarterTurns": 1 } }""";

    [TestMethod]
    [CoversNode("images-ui")]
    public void Image_Edit_ControlsRespondInOnePass()
    {
        var folder = Path.Combine(Path.GetTempPath(), "nexaflow_edit_journey_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var photo = Path.Combine(folder, "a_" + OrientedPhotoSample.FileName);
            File.Copy(TestSampleData.Path("images", OrientedPhotoSample.FileName), photo);
            var plain = Path.Combine(folder, "b_" + Path.GetFileName(TestSampleData.Files("images").First(f => f.EndsWith(".png"))));
            File.Copy(TestSampleData.Files("images").First(f => f.EndsWith(".png")), plain);
            foreach (var p in new[] { photo, plain }) File.WriteAllText(p + ".nfedit", StaleRecipe);

            NavigateFileBrowserTo(folder);
            var slideshow = WaitForId("SlideshowFolderAction", 8);
            Assert.IsNotNull(slideshow, "The 'Slideshow' folder action did not appear for the journey's folder.");
            slideshow!.AsButton().Invoke();
            Assert.IsNotNull(WaitForId("ImageView", 15), "Slideshow did not open the image viewer.");

            // ── The photos changed since their recipes: keep one, discard the other ──
            CheckDoes("Keep the edits made against an earlier file", "Image_EditKeepChanged", () => WaitForFs(() => !Exists("Image_EditKeepChanged"), 3));
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
            Check("A click places text, typed in the panel", () => ClickCanvas() && TypeInto("Image_EditMarkupTextBox", "Hello"));
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

            // Export options
            Check("Export panel", () => SelectRadio("Image_EditToolExport"));
            Check("Strip metadata toggles", () => SetToggle("Image_EditStripMetadata", true) && SetToggle("Image_EditStripMetadata", false));
            Check("Quality slider moves", () => MoveSlider("Image_EditQuality"));
            Check("Back to the crop panel", () => SelectRadio("Image_EditToolCrop"));

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

    /// <summary>The middle of the edit canvas — where the picture is fitted.</summary>
    private Point? CanvasCentre()
    {
        var canvas = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Image_EditCanvas"));
        if (canvas is null) return null;
        var r = canvas.BoundingRectangle;
        return new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
    }

    private bool ClickCanvas()
    {
        if (CanvasCentre() is not { } centre) return false;
        Mouse.MoveTo(centre);
        Mouse.Click(MouseButton.Left);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(200);
        return true;
    }

    private bool DragOnCanvas(int fromX, int fromY, int toX, int toY)
    {
        if (CanvasCentre() is not { } centre) return false;
        Mouse.Drag(new Point(centre.X + fromX, centre.Y + fromY), new Point(centre.X + toX, centre.Y + toY), MouseButton.Left);
        Wait.UntilInputIsProcessed();
        Thread.Sleep(200);
        return true;
    }

    /// <summary>Moves a slider to whichever end it is not already at.</summary>
    private bool MoveSlider(string automationId)
    {
        var element = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
        try { element?.Patterns.ScrollItem.PatternOrDefault?.ScrollIntoView(); } catch { /* not in a scroller */ }
        if (element?.Patterns.RangeValue.PatternOrDefault is not { } range) return false;
        var target = range.Value.Value >= range.Maximum.Value ? range.Minimum.Value : range.Maximum.Value;
        range.SetValue(target);
        return WaitForFs(() => Math.Abs(range.Value.Value - target) < 0.01, 2);
    }
}
