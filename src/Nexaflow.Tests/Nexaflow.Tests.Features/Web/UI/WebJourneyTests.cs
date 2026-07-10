using System;
using System.IO;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Web.UI;

/// <summary>
/// One-pass UI journey for the Web (WebView2) tab: writes a tiny local .html file, opens it via the
/// explicit <b>"Browse"</b> ActionStrip button, waits for the view root, then exercises every tagged
/// nav-bar control — soft-asserting each so a single gap doesn't hide the rest.
/// <para>
/// The nav toolbar (back / forward / address / reload) is always present even when the embedded WebView2
/// can't start on this machine (only the WebView surface itself collapses to the fallback panel), so the
/// journey verifies the chrome regardless of WebView2 availability. It opens a <c>file://</c> page and
/// never drives an external navigation — back/forward/reload are safe no-ops here, and the
/// "open in default browser" button is checked for presence only (invoking it would launch a real browser).
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("web-toolbar")]
public class WebJourneyTests : UiJourneyTestBase
{
    [TestMethod]
    public void Web_NavControls_RespondInOnePass()
    {
        // A self-contained local page — no network, no sample-file dependency (the Web feature ships none).
        var folder = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "nexaflow-webjourney-" + Guid.NewGuid().ToString("N"))).FullName;
        var fileName = "page.html";
        File.WriteAllText(Path.Combine(folder, fileName),
            "<html><head><title>Nexaflow Web Test</title></head><body><h1>Hello</h1></body></html>");

        try
        {
            // "Browse" is ShowHtmlAction.DisplayName; the ActionStrip binds each button's AutomationId to it.
            var view = OpenFileVia(folder, fileName, "Browse", "Web_View");
            Assert.IsNotNull(view, "Web view (Web_View) did not open via the 'Browse' action.");

            // Address display (read-only URL bar) — always present.
            CheckPresent("Address bar", "Web_Address");

            // Nav buttons — present regardless of WebView2 availability; invoking is a safe no-op on a
            // local page (back/forward have no history; reload re-renders the same file://).
            CheckInvoke("Back", "Web_Back");
            CheckInvoke("Forward", "Web_Forward");
            CheckInvoke("Reload", "Web_Reload");

            // Open-in-default-browser — present only; do NOT invoke (it would launch the system browser).
            CheckPresent("Open external", "Web_OpenExternal");

            AssertJourney();
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch { }
        }
    }
}
