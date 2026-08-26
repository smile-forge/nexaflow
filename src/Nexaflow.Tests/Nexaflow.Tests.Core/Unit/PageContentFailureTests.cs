using System;
using System.Windows.Controls;
using Nexaflow.Features.Common;
using Nexaflow.Tests.Fixtures;
using Page = Nexaflow.Features.Common.Page;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// What happens when a feature's view constructor throws.
/// <para>
/// This was the last unguarded step of opening a tab. A feature view is a rich place to fail — XAML parsing,
/// a missing native dependency behind a hosted control, a bad path reaching a <see cref="Uri"/> — and the
/// throw used to unwind through tab activation to the app-level dispatcher handler, which reports only
/// "Something went wrong": no tab, no cause, nothing to act on. These pin the containment.
/// </para>
/// <para>Needs an STA thread to construct WPF elements; opens no window.</para>
/// </summary>
[TestClass]
[TestCategory("UI")]
[CoversNode("chrome-tab-activate")]
public class PageContentFailureTests
{
    private static Page PageWithFactory(Func<UserControl> factory) => new()
    {
        Title           = "Broken",
        PageKind        = "Test",
        ContentFactory  = factory,
    };

    [TestMethod]
    public void AThrowingFactory_IsCaptured_RatherThanPropagated() => UiThread.Run(() =>
    {
        var page = PageWithFactory(() => throw new InvalidOperationException("view ctor blew up"));

        var content = page.GetOrCreateContent();   // must not throw

        Assert.IsNotNull(content, "a placeholder stands in so the caller always has something to host");
        Assert.IsNotNull(page.LoadException);
        StringAssert.Contains(page.LoadException!.Message, "view ctor blew up");
    });

    [TestMethod]
    public void AFailedFactory_IsNotRetried() => UiThread.Run(() =>
    {
        var attempts = 0;
        var page = PageWithFactory(() =>
        {
            attempts++;
            throw new InvalidOperationException("still broken");
        });

        var first  = page.GetOrCreateContent();
        var second = page.GetOrCreateContent();

        // Tab activation calls this on every switch. Re-running a factory that has already proven it throws
        // would re-do whatever side effects it got through before failing, once per tab switch.
        Assert.AreEqual(1, attempts);
        Assert.AreSame(first, second);
    });

    [TestMethod]
    public void AHealthyFactory_LeavesNoLoadException() => UiThread.Run(() =>
    {
        var page = PageWithFactory(() => new UserControl());

        var content = page.GetOrCreateContent();

        Assert.IsNull(page.LoadException);
        Assert.AreSame(content, page.GetOrCreateContent(), "content is still cached");
    });

    [TestMethod]
    public void ReplaceContent_SwapsInTheShellsOwnSurface_AndCachesIt() => UiThread.Run(() =>
    {
        var page = PageWithFactory(() => throw new InvalidOperationException("boom"));
        page.GetOrCreateContent();

        var replacement = new UserControl();
        var returned    = page.ReplaceContent(replacement);

        // This is how Core substitutes PageLoadErrorView: Features.Common cannot build one (it must not
        // reference Core), so the shell hands it in and the page keeps owning the caching.
        Assert.AreSame(replacement, returned);
        Assert.AreSame(replacement, page.GetOrCreateContent());
    });
}
