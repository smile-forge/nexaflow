using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Email.UI;

/// <summary>
/// One-pass UI journey for the Email viewer: opens the sample message via the explicit <b>"As Email"</b>
/// ActionStrip button, then walks the body-view toolbar and the raw-header expander, soft-asserting each so
/// one gap doesn't hide the rest.
/// <para>
/// "Open in browser" is deliberately not invoked: it writes a temp file and hands it to the shell's WebView2
/// tab, which is a second live surface this journey has no business spawning. That it is offered only for a
/// message with an HTML body is asserted against the view-model instead.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("email")]
public class EmailJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    [CoversNode("email-ui")]
    public void Email_Controls_RespondInOnePass()
    {
        var view = OpenFileVia(TestSampleData.Path("email"), "simple.eml", "As Email", "EmailView");
        Assert.IsNotNull(view, "EmailView did not open via the 'As Email' action.");

        // The envelope is up before anything is clicked.
        CheckPresent("All headers expander", "Email_AllHeaders");

        // Body views — simple.eml is multipart/alternative, so all three buttons are earned.
        CheckInvoke("Plain text view", "Email_PlainText");
        CheckInvoke("HTML source view", "Email_HtmlSource");
        CheckInvoke("Rendered view", "Email_Rendered");

        CheckInvoke("Expand all headers", "Email_AllHeaders");

        AssertJourney();
    }
}
