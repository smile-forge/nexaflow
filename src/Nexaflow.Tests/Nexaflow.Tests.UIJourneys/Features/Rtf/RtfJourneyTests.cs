using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Rtf.UI;

/// <summary>
/// One-pass UI journey for the rich-text page: opens the sample through the explicit <b>"ShowRtfAction"</b> ActionStrip
/// button and checks the drawn document and the way out to an application that writes rich text are there.
/// <para>
/// Open externally is found, not pressed: pressing it hands the file to whatever application this machine has for rich
/// text, which a test has no business starting.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("rtf-viewer")]
public class RtfJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("rtf-viewer-ui")]
    [CoversNode("rtf-open-externally")]
    public void Rtf_Document_IsDrawnInOnePass()
    {
        var view = OpenFileVia(TestSampleData.Path("rtf"), "sample.rtf", "ShowRtfAction", "RtfView");
        Assert.IsNotNull(view, "RtfView did not open via the 'As rich text' action.");

        CheckPresent("The drawn document", "Rtf_Document");
        CheckPresent("Open externally", "Rtf_OpenExternal");

        AssertJourney();
    }
}
