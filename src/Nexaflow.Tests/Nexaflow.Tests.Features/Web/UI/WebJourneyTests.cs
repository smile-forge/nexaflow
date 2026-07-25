using System;
using System.IO;
using FlaUI.Core.AutomationElements;
using Nexaflow.Tests.Features.UI.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Web.UI;

/// <summary>
/// The one UI journey for the Web (WebView2) tab, driven the way a user drives it: open a local page,
/// <b>follow a link on it</b>, then walk back and forward through the history that creates.
/// <para>
/// Every control is asserted by its <em>effect</em>, never by "it was clickable": following the link must
/// change the rendered document and the address bar; Back must return the first page; Forward must return
/// the second; Reload must leave the document still rendered. Back and Forward are also asserted to be
/// <b>disabled</b> at the ends of the history — a navigation button that does nothing when pressed is a
/// defect, not a no-op worth testing.
/// </para>
/// <para>
/// Requires WebView2: without it there is no browser to navigate and the page shows its fallback panel
/// instead, so the test fails rather than quietly downgrading — a machine that cannot run this needs to
/// know. "Open in your default browser" is checked for presence and enablement only; invoking it would
/// launch a real browser on the machine running the test.
/// </para>
/// Interactive desktop only — run with <c>--filter "TestCategory=UI"</c>.
/// </summary>
[TestClass]
[CoversNode("web-viewer-ui")]
public class WebJourneyTests : UiJourneyTestBase
{
    private string _folder = string.Empty;

    private const string FirstPage = "first.html";
    private const string SecondPage = "second.html";

    // Distinctive words so UIA lookups can't collide with app chrome or the file list.
    private const string FirstHeading = "Zeroth Kestrel";
    private const string SecondHeading = "Marbled Quokka";
    private const string LinkText = "Onward to page two";

    /// <summary>Two self-contained pages, the first linking to the second — the history the journey needs.</summary>
    private string PageFolder()
    {
        if (_folder.Length > 0) return _folder;

        _folder = Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), "nexaflow-webjourney-" + Guid.NewGuid().ToString("N"))).FullName;

        File.WriteAllText(Path.Combine(_folder, FirstPage),
            $"<html><head><title>First</title></head><body><h1>{FirstHeading}</h1>" +
            $"<p><a href=\"{SecondPage}\">{LinkText}</a></p></body></html>");

        File.WriteAllText(Path.Combine(_folder, SecondPage),
            $"<html><head><title>Second</title></head><body><h1>{SecondHeading}</h1></body></html>");

        return _folder;
    }

    [TestCleanup]
    public void RemoveFolder()
    {
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true); } catch { }
    }

    /// <summary>The address bar's current text (a TextBlock, so UIA exposes it as the element's Name).</summary>
    private string Address() => WaitForId("Web_Address", 8)?.Name ?? string.Empty;

    /// <summary>Waits for the rendered document to show <paramref name="heading"/>. WebView2 exposes the
    /// live document to UIA, so this is the real "what is on screen right now" probe.</summary>
    private bool DocumentShows(string heading, int seconds = 15) => WaitForName(heading, seconds) is not null;

    [TestMethod]
    [TestCategory("UI")]
    [CoversNode("web-browse-action")]
    [CoversNode("web-toolbar")]
    public void Web_UserNavigatesForwardAndBack_ThroughRealHistory()
    {
        // ── Open: "Browse" is ShowHtmlAction.DisplayName on the ActionStrip.
        var view = OpenFileVia(PageFolder(), FirstPage, "Browse", "Web_View");
        Assert.IsNotNull(view, "The Web view did not open via the 'Browse' action.");

        Assert.IsNull(MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Web_FallbackPanel")),
            "WebView2 could not start on this machine, so the navigation journey cannot run. "
            + "Install the Edge WebView2 runtime.");

        Assert.IsTrue(DocumentShows(FirstHeading), $"The first page never rendered ('{FirstHeading}' not found).");
        StringAssert.Contains(Address(), FirstPage, "The address bar does not show the opened page.");

        // ── At the start of history, there is nowhere to go: both must be disabled.
        Check("Back is disabled with no history", () => WaitForId("Web_Back", 5)?.IsEnabled == false);
        Check("Forward is disabled with no history", () => WaitForId("Web_Forward", 5)?.IsEnabled == false);

        // ── Follow the link in the page, as a user would. WebView2 surfaces the anchor to UIA.
        var link = WaitForName(LinkText, 10);
        Assert.IsNotNull(link, "The link on the first page was not reachable in the document.");
        link!.Click();

        Check("Following the link rendered the second page", () => DocumentShows(SecondHeading));
        Check("Following the link updated the address bar", () => Address().Contains(SecondPage, StringComparison.OrdinalIgnoreCase));
        Check("Back became available once there was history", () => WaitForId("Web_Back", 8)?.IsEnabled == true);

        // ── Back: must actually return to the first page.
        CheckDoes("Back", "Web_Back", () => DocumentShows(FirstHeading), seconds: 8);
        Check("Back restored the first page's address", () => Address().Contains(FirstPage, StringComparison.OrdinalIgnoreCase));
        Check("Forward became available after going back", () => WaitForId("Web_Forward", 8)?.IsEnabled == true);

        // ── Forward: must actually return to the second page.
        CheckDoes("Forward", "Web_Forward", () => DocumentShows(SecondHeading), seconds: 8);
        Check("Forward restored the second page's address", () => Address().Contains(SecondPage, StringComparison.OrdinalIgnoreCase));

        // ── Reload: the same document must come back, and the toolbar must survive it.
        CheckDoes("Reload", "Web_Reload", () => DocumentShows(SecondHeading), seconds: 8);
        Check("View survived reload", () => WaitForId("Web_View", 8) is not null);

        // ── Present and enabled only: invoking this launches the machine's real browser.
        var external = CheckPresent("Open in default browser", "Web_OpenExternal");
        Check("Open in default browser is enabled for a real page", () => external?.IsEnabled == true);

        AssertJourney();
    }
}
