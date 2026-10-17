using System;
using System.Linq;
using FlaUI.Core.AutomationElements;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UsbProbe.UI;

/// <summary>
/// A phone or camera over MTP: its storage browsed, or — locked, or charging only — the tile saying how to share it.
/// </summary>
/// <remarks>
/// Inconclusive at once, with no app launched, when nothing is plugged in over MTP. Interactive desktop only — run with
/// --filter "TestCategory=UI".
/// </remarks>
[TestClass]
[CoversNode("usbprobe-portable")]
public class UsbPhoneJourneyTests : UsbJourneyBase
{
    UsbDeviceNode? _phone;

    protected override string ShotFolder => "nexaflow-usbdevice-journey";

    protected override string? MissingPrerequisite()
    {
        _phone = Devices.FirstOrDefault(d => Runs(d, "WUDFWpdMtp", "WpdUsb", "WUDFWpdFs")
            || d.Functions.Any(f => f.Children.Any(c => string.Equals(c.ClassName, "WPD", StringComparison.OrdinalIgnoreCase))));
        return _phone is null ? "no phone or camera plugged in over MTP" : null;
    }

    [TestMethod]
    public void APhone_ShowsWhatItShares()
    {
        Assert.IsTrue(OpenDevicePage(_phone!), "the phone's page did not open");
        Check("Live tools", () => Select("UsbDevice_Tab_Tools"));
        Check("its storage is browsed, or it says how to share it", () =>
            WaitFor(() => FindInAppWindows("Store_Tree") ?? FindInAppWindows("UsbPortable_Note"), 15) is not null);
        if (WaitForId("Store_Tree", 2)?.AsTree().Items.Length > 0)
            Check("what it shares is listed", () => WaitForFs(() => WaitForId("Store_Entries", 1)?.AsListBox().Items.Length > 0, 10));
        else
            Check("it says how to share its files", () => Exists("Store_Note") || Exists("UsbPortable_Note"));
        Shoot("9-phone");
        AssertJourney();
    }
}
