using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.SystemInfo.UI;

/// <summary>
/// One-pass UI journey for the Services page (PageKind "SystemServices", root <c>ServicesView</c>): refresh,
/// then the row actions.
/// <para>
/// <b>No row action is pressed.</b> Start, Stop, Restart, Pause and Resume act on a real Windows service
/// through the elevation bridge. They are stamped on every row, so each check finds the first row's, and they
/// are present-checked only; which of them is enabled depends on that service's state, which is the grid's
/// concern (covered headlessly by ServicesSurfaceTests), not this journey's.
/// </para>
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </summary>
[TestClass]
[CoversNode("sysinfo-services")]
public class ServicesJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "SystemServices";

    [TestMethod]
    public void Services_Controls_RespondInOnePass()
    {
        var view = WaitForId("ServicesView", 15);
        Assert.IsNotNull(view, "ServicesView did not open via --openTab SystemServices.");

        CheckInvoke("Refresh", "Services_Refresh");

        // The service list is gathered off-thread, so the first row gets a longer wait than the rest.
        CheckPresent("Start",   "Services_Start", 20);
        CheckPresent("Stop",    "Services_Stop");
        CheckPresent("Restart", "Services_Restart");
        CheckPresent("Pause",   "Services_Pause");
        CheckPresent("Resume",  "Services_Resume");

        AssertJourney();
    }
}
