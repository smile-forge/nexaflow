using System.Linq;
using FlaUI.Core.AutomationElements;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UsbProbe.UI;

/// <summary>
/// A device on its own page: the four tabs, the live tools it offers, its debug details, and back to the tree — on the
/// first device plugged in here that is not a hub, one with a COM port first so the serial terminal is reached too.
/// </summary>
/// <remarks>
/// Which tools appear depends on the device; each tool's controls are checked where that tool is offered. Nothing here
/// writes: no copy is started, no tone played, no report sent — the buttons that would are only checked for. Opening
/// the serial terminal does not connect. Interactive desktop only — run with --filter "TestCategory=UI".
/// </remarks>
[TestClass]
[CoversNode("usbprobe-device")]
public class UsbDeviceJourneyTests : UsbJourneyBase
{
    UsbDeviceNode? _device;

    protected override string ShotFolder => "nexaflow-usbdevice-journey";

    protected override string? MissingPrerequisite()
    {
        _device = Devices.Where(d => !d.IsHub).OrderByDescending(d => d.Functions.Any(f => f.ComPort is not null)).FirstOrDefault();
        return _device is null ? "no USB device plugged in apart from hubs" : null;
    }

    [TestMethod]
    [CoversNode("usbprobe-device-tools")]
    public void Device_OpensOnItsOwnPage_AndShowsWhatItCanDo()
    {
        string path = _device!.Location.PortPath;
        Assert.IsNotNull(WaitForId("UsbProbeView", 30), "the USB tab did not open");

        Check($"'> {path}' goes to the device", () => GoTo(path));
        CheckInvoke("Open device", "Usb_OpenDevice");
        Check("the device page opens", () => WaitForId("UsbDeviceView", 15) is not null);
        CheckPresent("the device's port path", "UsbDevice_Path");
        Shoot("1-overview");

        Check("Overview", () => Select("UsbDevice_Tab_Overview"));
        Check("Live tools", () => Select("UsbDevice_Tab_Tools"));
        CheckPresent("the tiles", "UsbDevice_Tiles");
        Check("the tools are offered", ToolsOffered);
        Shoot("2-tools");

        if (CountOf("UsbDevice_TileToggle") > 0)
        {
            Check("a tile shuts and opens again", () => ToggledTwice("UsbDevice_TileToggle"));
            foreach (var (tool, always, _) in ToolControls)
                if (Exists(always[0]))
                    foreach (var id in always) CheckExists($"{tool}: {id}", id);
        }

        if (Exists("Store_Refresh")) CheckInvoke("storage: read the volumes again", "Store_Refresh");

        Check("Interfaces", () => Select("UsbDevice_Tab_Interfaces"));
        CheckPresent("the device's structure", "UsbDevice_Structure");
        Shoot("3-interfaces");
        Check("Diagnostics", () => Select("UsbDevice_Tab_Diagnostics"));
        CheckPresent("the problems", "UsbDevice_Problems");
        CheckPresent("what the tools did", "UsbDevice_Log");
        CheckInvoke("Read device again", "UsbDevice_ReRead");
        // The debug details are read from the device and its tools, then copied; the status line says so once they are.
        CheckDoes("Copy debug details", "UsbDevice_CopyDebug",
                  () => WaitForFs(() => WaitForId("UsbDevice_Status", 1)?.AsTextBox().Text.Contains("copied") == true, Affordable(30)));
        CheckPresent("Save debug details", "UsbDevice_SaveDebug");
        Shoot("4-diagnostics");

        // A serial device's terminal opens on its port, and does not connect.
        Check("Live tools again", () => Select("UsbDevice_Tab_Tools"));
        if (Exists("UsbSerial_OpenTerminal"))
        {
            CheckInvoke("open the serial terminal", "UsbSerial_OpenTerminal");
            Check("the Serial page opens on the device's port", () => WaitForId("SerialView", 15) is not null);
            Check("…without connecting", () => Exists("Serial_Connect") && !Exists("Serial_Disconnect"));
        }
        else
        {
            CheckInvoke("Show in USB tree", "UsbDevice_ShowInTree");
            Check("back on the USB page", () => WaitForId("UsbProbeView", 10) is not null);
        }

        AssertJourney();
    }

    /// <summary>A tile's header is a toggle: shut it, see it shut, open it again.</summary>
    bool ToggledTwice(string id)
    {
        var toggle = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(id))?.Patterns.Toggle.PatternOrDefault;
        if (toggle is null) return false;
        var open = toggle.ToggleState.Value;
        toggle.Toggle();
        bool shut = WaitForFs(() => toggle.ToggleState.Value != open, 2);
        toggle.Toggle();
        return shut && WaitForFs(() => toggle.ToggleState.Value == open, 2);
    }
}
