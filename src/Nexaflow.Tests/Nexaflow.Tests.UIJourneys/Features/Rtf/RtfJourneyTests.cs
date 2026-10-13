using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Rtf.UI;

/// <summary>
/// One-pass UI journey for the rich-text page: opens the sample through the explicit <b>"ShowRtfAction"</b> ActionStrip
/// button and checks the drawn document is there.
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("rtf-viewer")]
public class RtfJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("rtf-viewer-ui")]
    
    public void Rtf_Document_IsDrawnInOnePass()
    {
        var view = OpenFileVia(TestSampleData.Path("rtf"), "sample.rtf", "ShowRtfAction", "RtfView");
        Assert.IsNotNull(view, "RtfView did not open via the 'As rich text' action.");

        CheckPresent("The drawn document", "Rtf_Document");

        AssertJourney();
    }
}
