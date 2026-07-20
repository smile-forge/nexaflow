using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Web.ViewModels;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Web;

/// <summary>
/// Covers the Web (WebView2) tab's AI-integration surface: the page-scoped security context (aspect 4 —
/// two web tabs on different pages stay distinguishable when pinned into one conversation), honest
/// get_context (URL + title, not a bare "web view"), and the two read-only visual tools driven exactly
/// as the conversation hub drives them.
/// <para>
/// <see cref="HtmlViewModel"/> constructs with no live WebView2, so the tools' delegates
/// (<c>CaptureScreenshotAsync</c> / <c>ScrollByViewportFractionAsync</c>) stay null — the tools' graceful
/// not-wired error path is exercised here. The real pixel capture / scroll path needs a live browser
/// control and is left to the UI journey.
/// </para>
/// </summary>
[TestClass]
public class WebAiTests
{
    /// <summary>Per-test scratch folder for sample .html / .url files; deleted at cleanup.</summary>
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup() => _tempDir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "nexaflow-web-ai-" + Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private HtmlViewModel MakeShortcut(string fileName, string url)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, $"[InternetShortcut]\r\nURL={url}\r\n");
        return new HtmlViewModel(path);   // ctor takes only a file path — no IShellServices seam to stub.
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("web-viewer-ai-context")]
    public void SecurityContext_TracksTheCurrentPage_AndIsDistinctPerPage_WithHonestContext()
    {
        var a = MakeShortcut("a.url", "https://example.com/alpha");

        // Scope is the page the tools act within: the current URL, not null (aspect 4).
        Assert.AreEqual(a.CurrentUrl, a.GetSecurityContext());
        Assert.AreEqual("https://example.com/alpha", a.GetSecurityContext());

        // …and it follows navigation — a new page is a new boundary.
        a.CurrentUrl = "https://example.com/beta";
        Assert.AreEqual("https://example.com/beta", a.GetSecurityContext());

        // Two web tabs on different pages produce distinct scopes, so the hub keeps their identically-named
        // tools apart instead of collapsing them first-wins.
        var b = MakeShortcut("b.url", "https://other.example/page");
        Assert.AreNotEqual(a.GetSecurityContext(), b.GetSecurityContext());

        // get_context names the URL honestly…
        StringAssert.Contains(b.GetContext(), b.CurrentUrl);

        // …and folds in the page title once WebView2 reports it (empty before then, so it's omitted).
        Assert.IsFalse(b.GetContext().Contains("titled"), "no title should appear before the page reports one");
        b.PageTitle = "Other Example";
        StringAssert.Contains(b.GetContext(), "Other Example");
        StringAssert.Contains(b.GetContext(), b.CurrentUrl);
    }

    [TestMethod]
    [TestCategory("Unit")]
    [CoversNode("web-viewer-ai-act")]
    public async Task VisualTools_ExposedAsReadOnly_AndFailGracefullyWhenNoLiveView()
    {
        var vm    = MakeShortcut("t.url", "https://example.com/read-me");
        var tools = vm.GetClientTools();

        CollectionAssert.AreEquivalent(
            new[] { "capture_web_page", "scroll_web_page" },
            tools.Select(t => t.Name).ToArray(),
            "the Web AI act tool surface changed — update the tree's web-viewer-ai-act leaves to match");

        // Both are read-only vision tools: they auto-run (no approval prompt).
        var capture = tools.Single(t => t.Name == "capture_web_page");
        var scroll  = tools.Single(t => t.Name == "scroll_web_page");
        Assert.AreEqual(ToolSafety.SafeOperation, capture.Safety);
        Assert.AreEqual(ToolSafety.SafeOperation, scroll.Safety);
        Assert.IsTrue(scroll.ExemptFromRepeatGuard, "repeated \"scroll down\" is real progress, not spinning");

        // No live WebView2 in a headless test → the capture/scroll delegates stay null. The tools must return
        // a graceful ToolResult.Error the model can recover from, NOT throw.
        var cap = await capture.InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsTrue(cap.IsError);
        Assert.IsFalse(cap.Success);

        var scr = await scroll.InvokeAsync(new JsonObject { ["direction"] = "down" }, CancellationToken.None);
        Assert.IsTrue(scr.IsError);
        Assert.IsFalse(scr.Success);

        // A missing/blank direction is still the not-wired error, not an argument throw.
        var scrDefault = await scroll.InvokeAsync(new JsonObject(), CancellationToken.None);
        Assert.IsTrue(scrDefault.IsError);
    }
}
