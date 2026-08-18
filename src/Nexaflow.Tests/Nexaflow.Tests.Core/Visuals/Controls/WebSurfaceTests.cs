using System;
using System.Threading;
using System.Threading.Tasks;
using Nexaflow.Tests.Fixtures;
using Nexaflow.Visuals.Web;
using Nexaflow.Visuals.Web.Controls;

namespace Nexaflow.Tests.Core.Visuals.Controls;

/// <summary>
/// The shared chrome-free browser host, extracted from the Web tab so the PDF reader could render a document
/// without a browser's framing around it.
/// <para>
/// These assert the states a host relies on <em>before</em> a browser exists — which is the whole point of
/// the extraction: every operation has to answer honestly rather than throw, because the Web tab and the PDF
/// reader both start calling into it while WebView2 is still starting, or on a machine where it never will.
/// </para>
/// <para>Interactive desktop only (WPF elements need an STA thread). Run with
/// <c>--filter "TestCategory=UI"</c>.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[DoNotParallelize]
[NoCoverage("Nexaflow.Visuals.Web is shared infrastructure; the feature nodes cover its use.")]
public class WebSurfaceTests
{
    private static WebSurface New() => new();

    [TestMethod]
    public void ANewSurface_IsAvailableButNotReady()
    {
        var surface = New();

        // "Available" is about this machine (is a browser possible?), "ready" is about right now (has one
        // started?). A host that conflates them either shows its fallback panel too early or navigates too soon.
        Assert.IsTrue(surface.IsAvailable);
        Assert.IsFalse(surface.IsReady);
        Assert.IsFalse(surface.RuntimeMissing);
        Assert.AreEqual(string.Empty, surface.FailureMessage);
        Assert.AreEqual(string.Empty, surface.CurrentUrl);
    }

    [TestMethod]
    public void UserDataFolder_DefaultsUnderLocalAppData()
    {
        // The WebView2 default is created next to the executable, which under an installed build is Program
        // Files — read-only for a standard user, and initialisation throws. This default is the fix.
        var surface = New();

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        StringAssert.StartsWith(surface.UserDataFolder, local);
        StringAssert.Contains(surface.UserDataFolder, "nexaflow");
    }

    [TestMethod]
    public void NavigationAndHistory_AreNoOps_BeforeTheBrowserStarts()
    {
        var surface = New();

        Assert.IsFalse(surface.NavigateTo("https://example.invalid"), "false, not an exception");
        Assert.IsFalse(surface.CanGoBack);
        Assert.IsFalse(surface.CanGoForward);

        // These have no return value, so "doesn't throw" is the whole contract.
        surface.GoBack();
        surface.GoForward();
        surface.Reload();
    }

    [TestMethod]
    public async Task ReadingThePage_ReturnsNull_RatherThanThrowing_BeforeTheBrowserStarts()
    {
        var surface = New();

        Assert.IsNull(await surface.CapturePngAsync(1200, CancellationToken.None));
        Assert.IsNull(await surface.ExecuteScriptAsync("1+1", CancellationToken.None));
        Assert.IsNull(await surface.GetScrollInfoAsync(CancellationToken.None));

        // The scroll helper's settle delay must not run when there was nothing to scroll — an AI tool calling
        // it on a dead surface would otherwise pay 250ms for nothing, per call.
        await surface.ScrollByViewportFractionAsync(0.75, CancellationToken.None);
    }

    [TestMethod]
    public async Task SettingAFragment_ReportsFailure_RatherThanClaimingTheViewMoved()
    {
        // The caller uses the return value to decide whether it still needs a real navigation. Answering
        // "true" from a surface with no page in it would leave the reader looking at the wrong place with
        // nothing to correct it.
        var surface = New();

        Assert.IsFalse(await surface.TrySetFragmentAsync("#page=3", CancellationToken.None));
        Assert.IsFalse(await surface.TrySetFragmentAsync("page=3", CancellationToken.None),
            "with or without the leading hash");
    }

    [TestMethod]
    public async Task NavigateAndWait_ReportsFailure_RatherThanHanging()
    {
        // The caller uses this to find out whether a page fragment moved an already-loaded document, and the
        // renderer may legitimately never raise an event. Returning false is what lets it escalate.
        var surface = New();

        Assert.IsFalse(await surface.NavigateAndWaitAsync(
            "https://example.invalid", TimeSpan.FromMilliseconds(50), CancellationToken.None));
    }

    [TestMethod]
    public async Task Dispose_IsIdempotent_AndLeavesTheSurfaceInert()
    {
        var surface = New();

        surface.Dispose();
        surface.Dispose();   // a tab closed twice, or closed after its view was already torn down

        Assert.IsFalse(surface.IsReady);
        Assert.IsFalse(surface.NavigateTo("https://example.invalid"));
        Assert.IsNull(await surface.CapturePngAsync(1200, CancellationToken.None));
    }
}
