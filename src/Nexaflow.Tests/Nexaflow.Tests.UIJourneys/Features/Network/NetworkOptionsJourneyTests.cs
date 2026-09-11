using Nexaflow.Tests.Features.UI;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Network.UI;

/// <summary>
/// Options → Network: the guard's switches, looked at and left alone.
/// </summary>
/// <remarks>
/// Cancel, never Save, and the sweep permission is never ticked. A journey runs on somebody's real network,
/// and one that left a machine allowed to ping every address on it would be a test that did something.
/// Interactive desktop only — run with --filter "TestCategory=UI".
/// </remarks>
[TestClass]
[CoversNode("network-guard-options")]
public class NetworkOptionsJourneyTests : OptionsOverlayJourney
{
    [TestMethod]
    public void Options_Network_ShowsTheKillSwitchAndTheLimits()
    {
        var list = OpenOptions();

        try
        {
            SelectSection(list, "Network", "NetOpt_Enabled");

            CheckPresent("the kill switch",    "NetOpt_Enabled", 10);
            CheckPresent("the packet ceiling", "NetOpt_MaxPackets");
            CheckPresent("the time ceiling",   "NetOpt_MaxSeconds");
        }
        finally
        {
            CancelOptions();
        }

        AssertJourney();
    }
}
