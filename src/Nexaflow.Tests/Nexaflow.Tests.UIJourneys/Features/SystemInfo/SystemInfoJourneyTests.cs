using Nexaflow.Tests.UIJourneys.Infrastructure;

using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.SystemInfo.UI;

/// <summary>
/// One-pass UI journey for the System Information dashboard (PageKind "SystemInfo", root
/// <c>SystemInfoView</c>). Like the Scratchpad it opens from the ribbon rather than a file, so the
/// journey uses <see cref="UITestBase.TryOpenTabWithElement"/> to click ribbon buttons until the view
/// root appears, then exercises the dashboard's single interactive control — the "Refresh" button.
/// <para>
/// The dashboard has no in-view page navigation (the Services / Environment-Variables pages are their
/// own tabs, not sub-pages of this view) and no service-mutating or privileged controls — those live in
/// the separate Services view. So the only safe control to invoke here is Refresh, which merely re-queues
/// the off-thread WMI gather; it's soft-asserted so a gap doesn't hide the rest of the pass.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("sysinfo")]
public class SystemInfoJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "SystemInfo";

    [TestMethod]
    [CoversNode("sysinfo-ui")]
    public void SystemInfo_Controls_RespondInOnePass()
    {
        var view = WaitForId("SystemInfoView", 15);
        Assert.IsNotNull(view, "SystemInfoView did not open via --openTab SystemInfo.");

        // Refresh is safe — it only re-queues the background system-info gather.
        CheckInvoke("Refresh", "SysInfo_Refresh");

        AssertJourney();
    }
}
