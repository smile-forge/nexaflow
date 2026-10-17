using Nexaflow.Tests.Features.UI;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UsbProbe.UI;

/// <summary>
/// Options → USB: the monthly check and Check now, looked at and left alone.
/// </summary>
/// <remarks>
/// Cancel, never Save, and Check now is never pressed: a journey that downloaded from linux-usb.org would be a
/// test that did something. Interactive desktop only — run with --filter "TestCategory=UI".
/// </remarks>
[TestClass]
[CoversNode("usbprobe-names")]
public class UsbProbeOptionsJourneyTests : OptionsOverlayJourney
{
    [TestMethod]
    public void Options_Usb_ShowsTheNameSettings()
    {
        var list = OpenOptions();

        try
        {
            SelectSection(list, "USB", "UsbOpt_CheckMonthly");

            CheckPresent("the monthly check", "UsbOpt_CheckMonthly", 10);
            CheckPresent("Check now", "UsbOpt_CheckNow");
        }
        finally
        {
            CancelOptions();
        }

        AssertJourney();
    }
}
