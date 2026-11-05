using System.IO;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// The Artist panels as one design: every tool's groups fold away under their titles and open again, a group folded stays
/// folded as the tools change, and a value typed at the end of a slider's row is taken. Groups shown only where their models
/// are here — AI upscale, depth — are checked where they show.
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("images-edit-panels")]
public class ImagesPanelsJourneyTests : ImageEditJourneyBase
{
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
        Keyboard.Press(VirtualKeyShort.ENTER);
        Wait.UntilInputIsProcessed();
        return WaitForFs(() => slider.Patterns.RangeValue.PatternOrDefault is { } range && Math.Abs(range.Value.Value - meant) < 1e-6, 2);
    }

    [TestMethod]
    public void Image_EveryToolsGroups_FoldAndOpen_AndATypedValueIsTaken()
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

            AssertJourney();
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* still open in the app; the temp folder is cleaned by the OS */ }
        }
    }
}
