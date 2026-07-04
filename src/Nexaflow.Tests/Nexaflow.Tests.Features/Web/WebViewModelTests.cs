using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Features.Web.ViewModels;

namespace Nexaflow.Tests.Features.Web;

/// <summary>
/// Headless command/state logic behind the Web (WebView2) tab. <see cref="HtmlViewModel"/> constructs
/// without a live WebView2 — it just resolves a path/URL to a navigable <see cref="Uri"/> and exposes the
/// chrome/visibility/AI-tool surface. The navigation buttons (back/forward/reload) live on the view and
/// drive the real browser, so they're covered by the UI journey; everything here runs with no browser.
/// (Tab-title / breadcrumb derivation lives in <see cref="Nexaflow.Features.Web.WebPageChrome"/> and is
/// already covered by <c>WebPageChromeTests</c> — not duplicated here.)
/// </summary>
[TestClass]
public class WebViewModelTests
{
    /// <summary>Per-test scratch folder for sample .html / .url files; deleted at cleanup.</summary>
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup() => _tempDir = Directory.CreateDirectory(
        Path.Combine(Path.GetTempPath(), "nexaflow-webvm-" + Guid.NewGuid().ToString("N"))).FullName;

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private string Write(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    // ── URI / chrome resolution ───────────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void HtmlFile_ResolvesToFileUri_AndExposesFileName()
    {
        var path = Write("page.html", "<html><body>hi</body></html>");
        var vm   = new HtmlViewModel(path);

        Assert.AreEqual(Uri.UriSchemeFile, vm.NavigationUri.Scheme);
        Assert.AreEqual("page.html", vm.FileName);
        Assert.AreEqual(vm.NavigationUri.ToString(), vm.CurrentUrl);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UrlShortcut_ParsesUrlValue()
    {
        var path = Write("link.url", "[InternetShortcut]\r\nURL=https://example.com/path\r\n");
        var vm   = new HtmlViewModel(path);

        Assert.AreEqual("https", vm.NavigationUri.Scheme);
        Assert.AreEqual("example.com", vm.NavigationUri.Host);
        Assert.AreEqual("/path", vm.NavigationUri.AbsolutePath);
        Assert.AreEqual("https://example.com/path", vm.CurrentUrl);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void UrlShortcut_WithNoUrlLine_FallsBackToAboutBlank()
    {
        var path = Write("broken.url", "[InternetShortcut]\r\nIconIndex=0\r\n");
        var vm   = new HtmlViewModel(path);

        Assert.AreEqual("about:blank", vm.NavigationUri.ToString());
    }

    // ── State: visibility & context ───────────────────────────────────────

    [TestMethod]
    [TestCategory("Unit")]
    public void WebViewVisible_TracksAvailabilityAndOverlayCover()
    {
        var vm = new HtmlViewModel(Write("v.html", "<html></html>"));

        Assert.IsTrue(vm.WebViewAvailable);       // default
        Assert.IsFalse(vm.IsCovered);             // default
        Assert.IsTrue(vm.WebViewVisible);         // available && !covered

        vm.IsCovered = true;                      // a shell overlay covers the page
        Assert.IsFalse(vm.WebViewVisible);

        vm.IsCovered = false;
        vm.WebViewAvailable = false;              // WebView2 couldn't start
        Assert.IsFalse(vm.WebViewVisible);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void GetContext_ReflectsLoadingState()
    {
        var vm = new HtmlViewModel(Write("c.html", "<html></html>"));

        Assert.IsTrue(vm.IsLoading);                              // default before navigation completes
        StringAssert.Contains(vm.GetContext(), "(loading)");
        StringAssert.Contains(vm.GetContext(), vm.CurrentUrl);

        vm.IsLoading = false;
        StringAssert.Contains(vm.GetContext(), vm.CurrentUrl);
        Assert.IsFalse(vm.GetContext().Contains("(loading)"));
    }

    // ── AI client tools: guards when the view hasn't wired its delegates ───

    [TestMethod]
    [TestCategory("Unit")]
    public void ClientTools_ExposesCaptureAndScroll()
    {
        var vm    = new HtmlViewModel(Write("t.html", "<html></html>"));
        var names = vm.GetClientTools().Select(t => t.Name).ToArray();

        CollectionAssert.AreEquivalent(new[] { "capture_web_page", "scroll_web_page" }, names);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async System.Threading.Tasks.Task CaptureTool_WithoutLiveView_ReturnsError()
    {
        var vm   = new HtmlViewModel(Write("t.html", "<html></html>"));
        var tool = vm.GetClientTools().Single(t => t.Name == "capture_web_page");

        // The view normally assigns CaptureScreenshotAsync; with no browser it stays null, so the tool errors.
        var result = await tool.InvokeAsync(new JsonObject(), CancellationToken.None);

        Assert.IsTrue(result.IsError);
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async System.Threading.Tasks.Task ScrollTool_WithoutLiveView_ReturnsError()
    {
        var vm   = new HtmlViewModel(Write("t.html", "<html></html>"));
        var tool = vm.GetClientTools().Single(t => t.Name == "scroll_web_page");

        var result = await tool.InvokeAsync(
            new JsonObject { ["direction"] = "down" }, CancellationToken.None);

        Assert.IsTrue(result.IsError);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ScrollTool_IsReadOnly_AndExemptFromRepeatGuard()
    {
        var vm   = new HtmlViewModel(Write("t.html", "<html></html>"));
        var tool = vm.GetClientTools().Single(t => t.Name == "scroll_web_page");

        Assert.AreEqual(ToolSafety.ReadOnly, tool.Safety);   // scrolling never needs approval
        Assert.IsTrue(tool.ExemptFromRepeatGuard);           // repeated "scroll down" is real progress
    }
}
