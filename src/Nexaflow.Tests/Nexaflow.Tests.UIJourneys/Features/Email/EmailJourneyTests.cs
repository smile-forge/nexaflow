using FlaUI.Core.Input;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Email.UI;

/// <summary>
/// One-pass UI journey for the Email viewer: opens the sample message via the explicit <b>"OpenAsEmailAction"</b>
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
    /// <summary>A message opened by double-click, then the same message's controls through the explicit As Email action.</summary>
    [TestMethod]
    [CoversNode("email-ui")]
    [CoversNode("email-open")]
    public void Email_Journey()
    {
        RunStep("a .eml opens by double-click with its envelope rendered", EmlOpensWithEnvelope);
        RunStep("the viewer's body views and headers", ViewerControls);
        AssertJourney();
    }

    /// <summary>
    /// The default-open route: double-clicking a <c>.eml</c> opens it in <c>EmailView</c> (→ <c>/email</c> →
    /// <c>OpenAsEmailAction</c>) with its envelope rendered.
    /// </summary>
    private void EmlOpensWithEnvelope()
    {
        NavigateFileBrowserTo(TestSampleData.Path("email"));

        var row = WaitForName("simple.eml", 10);
        Assert.IsNotNull(row, "simple.eml not found in the file list.");
        row!.DoubleClick();
        Wait.UntilInputIsProcessed();

        Assert.IsNotNull(WaitForId("EmailView", 15), "Double-clicking the .eml did not open the Email viewer.");
        Assert.IsNotNull(WaitForName("Quarterly report", 8), "The message subject was not rendered in the Email viewer.");
        Assert.IsFalse(App.HasExited, "App crashed while opening the email.");
    }

    private void ViewerControls()
    {
        var view = OpenFileVia(TestSampleData.Path("email"), "simple.eml", "OpenAsEmailAction", "EmailView");
        Assert.IsNotNull(view, "EmailView did not open via the 'As Email' action.");

        // The envelope is up before anything is clicked.
        CheckPresent("All headers expander", "Email_AllHeaders");

        // Body views — simple.eml is multipart/alternative, so all three buttons are earned.
        CheckInvoke("Plain text view", "Email_PlainText");
        CheckInvoke("HTML source view", "Email_HtmlSource");
        CheckInvoke("Rendered view", "Email_Rendered");

        CheckInvoke("Expand all headers", "Email_AllHeaders");
    }
}
