using System;
using System.Reflection;
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
    /// <summary>
    /// Builds the surface and runs the body on a fresh STA thread. <see cref="WebSurface"/> is a WPF element,
    /// so constructing one on MSTest's own (MTA) thread throws "The calling thread must be STA" before a
    /// single assertion runs — which is what every test here did. The sibling control tests go through
    /// <see cref="UiThread"/> for exactly this reason.
    /// </summary>
    private static void WithSurface(Action<WebSurface> test) => UiThread.Run(() => test(new WebSurface()));

    /// <summary>
    /// The same, for the bodies that await. Blocking on the task is safe here precisely because the thread is
    /// a bare one: it has no <see cref="SynchronizationContext"/>, so no continuation is waiting to be pumped
    /// back onto it. (The test methods stay <c>void</c> — an <c>async Task</c> body would hand itself back to
    /// MSTest's thread and land off the STA thread mid-test.)
    /// </summary>
    private static void WithSurfaceAsync(Func<WebSurface, Task> test) =>
        UiThread.Run(() => test(new WebSurface()).GetAwaiter().GetResult());

    [TestMethod]
    public void ANewSurface_IsAvailableButNotReady() => WithSurface(surface =>
    {
        // "Available" is about this machine (is a browser possible?), "ready" is about right now (has one
        // started?). A host that conflates them either shows its fallback panel too early or navigates too soon.
        Assert.IsTrue(surface.IsAvailable);
        Assert.IsFalse(surface.IsReady);
        Assert.IsFalse(surface.RuntimeMissing);
        Assert.AreEqual(string.Empty, surface.FailureMessage);
        Assert.AreEqual(string.Empty, surface.CurrentUrl);
    });

    [TestMethod]
    public void UserDataFolder_DefaultsUnderLocalAppData() => WithSurface(surface =>
    {
        // The WebView2 default is created next to the executable, which under an installed build is Program
        // Files — read-only for a standard user, and initialisation throws. This default is the fix.
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        StringAssert.StartsWith(surface.UserDataFolder, local);
        StringAssert.Contains(surface.UserDataFolder, "nexaflow");
    });

    [TestMethod]
    public void NavigationAndHistory_AreNoOps_BeforeTheBrowserStarts() => WithSurface(surface =>
    {
        Assert.IsFalse(surface.NavigateTo("https://example.invalid"), "false, not an exception");
        Assert.IsFalse(surface.CanGoBack);
        Assert.IsFalse(surface.CanGoForward);

        // These have no return value, so "doesn't throw" is the whole contract.
        surface.GoBack();
        surface.GoForward();
        surface.Reload();
    });

    [TestMethod]
    public void ReadingThePage_ReturnsNull_RatherThanThrowing_BeforeTheBrowserStarts() =>
        WithSurfaceAsync(async surface =>
    {
        Assert.IsNull(await surface.CapturePngAsync(1200, CancellationToken.None));
        Assert.IsNull(await surface.ExecuteScriptAsync("1+1", CancellationToken.None));
        Assert.IsNull(await surface.GetScrollInfoAsync(CancellationToken.None));

        // The scroll helper's settle delay must not run when there was nothing to scroll — an AI tool calling
        // it on a dead surface would otherwise pay 250ms for nothing, per call.
        await surface.ScrollByViewportFractionAsync(0.75, CancellationToken.None);
    });

    [TestMethod]
    public void SettingAFragment_ReportsFailure_RatherThanClaimingTheViewMoved() =>
        WithSurfaceAsync(async surface =>
    {
        // The caller uses the return value to decide whether it still needs a real navigation. Answering
        // "true" from a surface with no page in it would leave the reader looking at the wrong place with
        // nothing to correct it.
        Assert.IsFalse(await surface.TrySetFragmentAsync("#page=3", CancellationToken.None));
        Assert.IsFalse(await surface.TrySetFragmentAsync("page=3", CancellationToken.None),
            "with or without the leading hash");
    });

    [TestMethod]
    public void NavigateAndWait_ReportsFailure_RatherThanHanging() => WithSurfaceAsync(async surface =>
    {
        // The caller uses this to find out whether a page fragment moved an already-loaded document, and the
        // renderer may legitimately never raise an event. Returning false is what lets it escalate.
        Assert.IsFalse(await surface.NavigateAndWaitAsync(
            "https://example.invalid", TimeSpan.FromMilliseconds(50), CancellationToken.None));
    });

    [TestMethod]
    public void Dispose_IsIdempotent_AndLeavesTheSurfaceInert() => WithSurfaceAsync(async surface =>
    {
        surface.Dispose();
        surface.Dispose();   // a tab closed twice, or closed after its view was already torn down

        Assert.IsFalse(surface.IsReady);
        Assert.IsFalse(surface.NavigateTo("https://example.invalid"));
        Assert.IsNull(await surface.CapturePngAsync(1200, CancellationToken.None));
    });

    // ── Runtime-missing classification ────────────────────────────────────
    //
    // This is what decides whether the host offers "Install the Microsoft Edge WebView2 runtime" or the
    // generic "couldn't be started". Getting it wrong is silent: the fallback panel still appears, just
    // without the one link that fixes the machine. It went untested while the check was a flat type test.

    [TestMethod]
    public void RuntimeMissing_IsRecognised_WhenThrownDirectly()
    {
        Assert.IsTrue(WebSurface.IsRuntimeMissing(WebSurfaceTestHooks.NewRuntimeNotFound()));
    }

    [TestMethod]
    public void RuntimeMissing_IsRecognised_ThroughAWrapper()
    {
        // WPF and the async machinery both re-wrap what the WebView2 element throws, so the real exception
        // routinely arrives one or two levels down rather than on top.
        Assert.IsTrue(WebSurface.IsRuntimeMissing(
            new TargetInvocationException(WebSurfaceTestHooks.NewRuntimeNotFound())));

        Assert.IsTrue(WebSurface.IsRuntimeMissing(
            new AggregateException(WebSurfaceTestHooks.NewRuntimeNotFound())));

        Assert.IsTrue(WebSurface.IsRuntimeMissing(
            new InvalidOperationException("start failed",
                new TargetInvocationException(WebSurfaceTestHooks.NewRuntimeNotFound()))));
    }

    [TestMethod]
    public void RuntimeMissing_IsFalse_ForAnUnrelatedFailure()
    {
        // A read-only user-data folder, a blocked process — real failures that are NOT "go and install it",
        // and offering the download link for them would send the user somewhere that cannot help.
        Assert.IsFalse(WebSurface.IsRuntimeMissing(null));
        Assert.IsFalse(WebSurface.IsRuntimeMissing(new UnauthorizedAccessException()));
        Assert.IsFalse(WebSurface.IsRuntimeMissing(
            new AggregateException(new InvalidOperationException())));
    }
}
