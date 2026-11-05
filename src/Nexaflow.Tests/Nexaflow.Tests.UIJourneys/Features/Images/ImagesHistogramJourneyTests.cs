using System.IO;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// The histogram at the head of Adjust, in the running app: it is there and folds like any group, its highlights mark says
/// when exposure pushes the picture past white, and a press keeps the clipping shown — and another lets it go.
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("images-edit-histogram")]
public class ImagesHistogramJourneyTests : ImageEditJourneyBase
{
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

    private bool FoldsAndOpens(string groupId)
    {
        if (FindInAppWindows(groupId)?.Patterns.ExpandCollapse.PatternOrDefault is not { } fold) return false;
        fold.Collapse();
        if (!WaitForFs(() => fold.ExpandCollapseState.Value == ExpandCollapseState.Collapsed, 2)) return false;
        fold.Expand();
        return WaitForFs(() => fold.ExpandCollapseState.Value == ExpandCollapseState.Expanded, 2);
    }

    [TestMethod]
    public void Image_TheHistogram_SaysWhenHighlightsClip_AndKeepsThemShownWhenPressed()
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

            AssertJourney();
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* still open in the app; the temp folder is cleaned by the OS */ }
        }
    }
}
