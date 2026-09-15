using System.IO;
using System.Linq;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;
using FlaUI.Core.AutomationElements;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// One-pass UI journey for the image viewer: opens a single sample image via the explicit <b>"As Image"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the floating rotate / fit tools and
/// confirms the full-screen button is present — soft-asserting each so a single gap doesn't hide the rest.
/// <para>
/// Opening one image means <c>HasMultiple</c> is false, so the sub-view selector, auto-advance toggle/slider,
/// carousel arrows and dot strip are collapsed; <see cref="Image_Slideshow_ControlsRespondInOnePass"/> opens the
/// whole sample folder to drive those. The full-screen button is checked for presence (not
/// invoked) because invoking it opens a separate top-level window that would obscure the rest of the pass.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("images")]
public class ImagesJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("images-ui")]
    public void Image_Controls_RespondInOnePass()
    {
        var file = Path.GetFileName(TestSampleData.Files("images").First());
        var view = OpenFileVia(TestSampleData.Path("images"), file, "As Image", "ImageView");
        Assert.IsNotNull(view, "ImageView did not open via the 'As Image' action.");

        // Floating image-tools toolbar (rotate / fit) — pure in-place state, safe to invoke.
        CheckInvoke("Rotate left", "Image_RotateLeft");
        CheckInvoke("Rotate right", "Image_RotateRight");
        CheckInvoke("Fit / actual-size toggle", "Image_FitMode");

        // Full-screen — present for every image; invoking it opens a separate window, so only check presence.
        CheckPresent("Full screen", "Image_FullScreen");

        AssertJourney();
    }

    /// <summary>
    /// The multi-image pass. The "Slideshow" folder action opens the whole sample folder in the carousel, and the
    /// sample set is larger than the 20-dot window, so the dot strip pages. Everything here is in-place view state.
    /// </summary>
    [TestMethod]
    [CoversNode("images-ui")]
    public void Image_Slideshow_ControlsRespondInOnePass()
    {
        NavigateFileBrowserTo(TestSampleData.Path("images"));
        var slideshow = WaitForId("Slideshow", 8);
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

        AssertJourney();
    }

    /// <summary>Moves a slider to whichever end it is not already at.</summary>
    private bool MoveSlider(string automationId)
    {
        if (MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationId))?.Patterns.RangeValue.PatternOrDefault
            is not { } range) return false;
        var target = range.Value.Value >= range.Maximum.Value ? range.Minimum.Value : range.Maximum.Value;
        range.SetValue(target);
        return WaitForFs(() => range.Value.Value == target, 2);
    }
}
