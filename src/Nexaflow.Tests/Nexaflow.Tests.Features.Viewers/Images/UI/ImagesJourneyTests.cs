using System.IO;
using System.Linq;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Images.UI;

/// <summary>
/// One-pass UI journey for the image viewer: opens a single sample image via the explicit <b>"As Image"</b>
/// ActionStrip button (not a default-mapping double-click), then exercises the floating rotate / fit tools and
/// confirms the full-screen button is present — soft-asserting each so a single gap doesn't hide the rest.
/// <para>
/// Opening one image means <c>HasMultiple</c> is false, so the sub-view selector, auto-advance toggle/slider,
/// carousel arrows and dot strip are collapsed; those are tagged in the XAML for completeness but only the
/// always-present single-image controls are checked here. The full-screen button is checked for presence (not
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
}
