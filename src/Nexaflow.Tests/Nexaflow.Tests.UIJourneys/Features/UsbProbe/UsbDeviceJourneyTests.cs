using System;
using System.IO;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;
using System.Linq;

namespace Nexaflow.Tests.Features.UsbProbe.UI;

/// <summary>
/// A device on its own page, on whatever this machine has plugged into bus 1: the four tabs, the live tools its device
/// offers, and back to the tree.
/// </summary>
/// <remarks>
/// It reads the real USB tree, so which tools appear depends on the device it lands on; each tool's controls are
/// checked where that tool is offered. Nothing here writes: no copy is started, no tone played, no report sent — the
/// buttons that would are only checked for. Opening the serial terminal does not connect. Interactive desktop only —
/// run with --filter "TestCategory=UI".
/// </remarks>
[TestClass]
[CoversNode("usbprobe-device")]
public class UsbDeviceJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "UsbProbe";

    /// <summary>Each tool's controls, checked when its tile is on the page. The ones shown only mid-action — a copy's
    /// Cancel, Stop recording, a WAV's chooser — or only where the device allows — watching a HID collection's input,
    /// which Windows refuses for keyboards and mice — are listed for what they are and not expected.</summary>
    static readonly (string Tool, string[] Always, string[] Sometimes)[] ToolControls =
    [
        ("storage", ["UsbStorage_Browser", "UsbStorage_Tree", "UsbStorage_Entries", "UsbStorage_Path", "UsbStorage_Up", "UsbStorage_Refresh",
                     "UsbStorage_CopyToComputer", "UsbStorage_AddFile", "UsbStorage_AddFolder", "UsbStorage_NewFolder", "UsbStorage_Volume"],
                    ["UsbStorage_Crumb", "UsbStorage_Cancel", "UsbStorage_LastResult"]),
        ("audio out", ["UsbAudio_Signal", "UsbAudio_Frequency", "UsbAudio_Level", "UsbAudio_Channel", "UsbAudio_Play", "UsbAudio_Stop",
                       "UsbAudio_Identify"], ["UsbAudio_PickFile"]),
        ("audio in", ["UsbAudio_Meters", "UsbAudio_Duration", "UsbAudio_Record"], ["UsbAudio_StopRecording"]),
        ("hid", ["UsbHid_Collection", "UsbHid_Layout"],
                ["UsbHid_Reports", "UsbHid_StartMonitor", "UsbHid_Clear", "UsbHid_StopMonitor", "UsbHid_FeatureId", "UsbHid_ReadFeature",
                 "UsbHid_FeatureHex", "UsbHid_SendFeature", "UsbHid_OutputHex", "UsbHid_SendOutput"]),
        ("serial", ["UsbSerial_OpenTerminal"], []),
    ];

    [TestMethod]
    [CoversNode("usbprobe-device-tools")]
    public void Device_OpensOnItsOwnPage_AndShowsWhatItCanDo()
    {
        MainWindow.SetForeground();
        Thread.Sleep(500);
        Assert.IsNotNull(WaitForId("UsbProbeView", 30), "the USB tab did not open");

        Check("'> 1' opens bus 1", () =>
        {
            var ai = WaitForId("AiInputBox", 10);
            if (ai is null) return false;
            ai.Click();
            Thread.Sleep(150);
            Keyboard.Type("> 1");
            Keyboard.Press(VirtualKeyShort.RETURN);
            Thread.Sleep(800);
            return Exists("Usb_Listing");
        });

        // Rows have no UI Automation peer, so they are clicked by position until one is a device — the kind of row
        // Open device opens.
        bool device = false;
        if (CheckPresent("the listing", "Usb_Listing") is { } list)
        {
            var box = list.BoundingRectangle;
            for (int row = 0; row < Affordable(12) && !device; row++)
            {
                Mouse.Click(new System.Drawing.Point(box.Left + 120, box.Top + 36 + row * 24));
                Thread.Sleep(400);
                device = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Usb_Tab_Descriptor")) is not null;
            }
        }
        Check("a device on bus 1 is selected", () => device);
        if (!device) { AssertJourney(); return; }

        CheckInvoke("Open device", "Usb_OpenDevice");
        Check("the device page opens", () => WaitForId("UsbDeviceView", 15) is not null);
        CheckPresent("the device's port path", "UsbDevice_Path");
        Shoot("1-overview");

        Check("Overview", () => Select("UsbDevice_Tab_Overview"));
        Check("Live tools", () => Select("UsbDevice_Tab_Tools"));
        CheckPresent("the tiles", "UsbDevice_Tiles");
        Thread.Sleep(1500);   // the tools are offered off the UI thread, then read their state as their tiles open
        Shoot("2-tools");

        int tiles = CountOf("UsbDevice_TileToggle");
        if (tiles > 0)
        {
            Check("a tile shuts and opens again", () => ToggledTwice("UsbDevice_TileToggle"));
            foreach (var (tool, always, _) in ToolControls)
                if (Exists(always[0]))
                    foreach (var id in always) CheckPresent($"{tool}: {id}", id);
        }

        if (Exists("UsbStorage_Refresh")) CheckInvoke("storage: read the volumes again", "UsbStorage_Refresh");

        Check("Interfaces", () => Select("UsbDevice_Tab_Interfaces"));
        CheckPresent("the device's structure", "UsbDevice_Structure");
        Shoot("3-interfaces");
        Check("Diagnostics", () => Select("UsbDevice_Tab_Diagnostics"));
        CheckPresent("the problems", "UsbDevice_Problems");
        CheckPresent("what the tools did", "UsbDevice_Log");
        CheckInvoke("Read device again", "UsbDevice_ReRead");
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

    [TestMethod]
    [CoversNode("usbprobe-storage")]
    public void AUsbDisk_BrowsesLikeTheFileBrowser()
    {
        if (!OpenDeviceFoundBy("USBSTOR")) Assert.Inconclusive("no USB disk plugged in");
        Check("Live tools", () => Select("UsbDevice_Tab_Tools"));
        Check("its volume is the folder tree's root", () => WaitForId("UsbStorage_Tree", 15)?.AsTree().Items.Length > 0);
        Check("the volume's root is listed", () => WaitForId("UsbStorage_Entries", 15)?.AsListBox().Items.Length > 0);
        foreach (var id in ToolControls[0].Always) CheckPresent(id, id);
        Check("nothing above the volume's root", () => WaitForId("UsbStorage_Up", 5) is { IsEnabled: false });
        Thread.Sleep(800);
        Shoot("5-storage");

        var list = WaitForId("UsbStorage_Entries", 5)!.AsListBox();
        var first = list.Items[0];

        // The menus a right-click opens, each drawn in the app's theme — photographed off the window, where they open.
        Check("right-click on a row offers Copy path", () => RightClick(CentreOf(first)) && MenuItemNamed("Copy path") is not null);
        ShootWindowArea(ShotFolder, "5a-row-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Check("the right-clicked row is the one selected", () => first.AsListBoxItem().IsSelected);

        var blank = list.BoundingRectangle;
        Check("a click on blank space clears the selection", () =>
        {
            Mouse.Click(new System.Drawing.Point(blank.X + blank.Width / 2, blank.Bottom - 12));
            Thread.Sleep(300);
            return !first.AsListBoxItem().IsSelected;
        });
        Check("right-click on blank space offers New folder", () =>
            RightClick(new System.Drawing.Point(blank.X + blank.Width / 2, blank.Bottom - 12)) && MenuItemNamed("New folder…") is not null);
        ShootWindowArea(ShotFolder, "5b-blank-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);

        Check("right-click on the path bar offers Copy path", () =>
            RightClick(CentreOf(WaitForId("UsbStorage_Path", 5)!)) && MenuItemNamed("Copy path") is not null);
        ShootWindowArea(ShotFolder, "5c-path-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);

        Check("right-click on selectable text offers Select all", () =>
            RightClick(CentreOf(WaitForId("UsbStorage_Volume", 5)!)) && MenuItemNamed("Select all") is not null);
        ShootWindowArea(ShotFolder, "5d-text-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);

        // A folder opens on a double-click and the path bar walks into it; Backspace comes back out.
    Check("a row is named for what it holds", () => list.Items.All(i => !string.IsNullOrEmpty(i.Properties.Name.ValueOrDefault)));
    var folder = list.Items.FirstOrDefault(IsFolderRow);
        if (folder is not null)
        {
            int steps = CountOf("UsbStorage_Crumb");
            Check("double-clicking a folder opens it", () =>
            {
                folder.DoubleClick();
                Thread.Sleep(800);
                return CountOf("UsbStorage_Crumb") == steps + 1;
            });
            Shoot("5e-storage-folder");
            Check("Backspace goes back up", () =>
            {
                Keyboard.Press(VirtualKeyShort.BACK);
                Thread.Sleep(800);
                return CountOf("UsbStorage_Crumb") == steps;
            });
        }
        AssertJourney();
    }

    [TestMethod]
    [CoversNode("usbprobe-audio")]
    public void AUsbAudioDevice_ShowsItsOutputsAndInputs()
    {
        if (!OpenDeviceFoundBy("usbaudio")) Assert.Inconclusive("no USB audio device plugged in");
        Check("Live tools", () => Select("UsbDevice_Tab_Tools"));
        Thread.Sleep(1500);
        bool output = Exists("UsbAudio_Play"), input = Exists("UsbAudio_Meters");
        Check("an output or an input of the device has a tool", () => output || input);
        if (output) foreach (var id in ToolControls[1].Always) CheckPresent(id, id);
        if (input) foreach (var id in ToolControls[2].Always) CheckPresent(id, id);
        Shoot("6-audio");
        if (input)
        {
            // What the meters read, for the record: a silent or muted microphone reads the floor, so this is shown, not asserted.
            Thread.Sleep(2000);
            var meters = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UsbAudio_Meters"));
            var readings = meters?.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text)).Select(t => t.Name) ?? [];
            global::System.Console.WriteLine($"[meters] {string.Join(" | ", readings)}");
            Shoot("7-audio-later");
        }
        AssertJourney();
    }

    [TestMethod]
    [CoversNode("usbprobe-device-tools")]
    public void AHub_SaysWhyItsLiveToolsTabIsEmpty()
    {
        if (!OpenDeviceFoundBy("hub")) Assert.Inconclusive("no USB hub found");
        Check("Live tools", () => Select("UsbDevice_Tab_Tools"));
        Thread.Sleep(1500);
        Check("no tiles on a hub", () => CountOf("UsbDevice_TileToggle") == 0);
        CheckPresent("the tab says why it is empty", "UsbDevice_NoTools");
        Shoot("8-hub");
        AssertJourney();
    }

    /// <summary>'&gt; words' on the USB page, then Open device: false when nothing matched.</summary>
    bool OpenDeviceFoundBy(string words)
    {
        MainWindow.SetForeground();
        Thread.Sleep(500);
        if (WaitForId("UsbProbeView", 30) is null) return false;
        var ai = WaitForId("AiInputBox", 10);
        if (ai is null) return false;
        ai.Click();
        Thread.Sleep(150);
        Keyboard.Type($"> {words}");
        Keyboard.Press(VirtualKeyShort.RETURN);
        Thread.Sleep(1000);
        var open = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Usb_OpenDevice"));
        if (open is null || !open.IsEnabled) return false;
        open.AsButton().Invoke();
        return WaitForId("UsbDeviceView", 15) is not null;
    }

    bool Select(string tabId)
    {
        if (MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(tabId)) is not { } tab) return false;
        tab.Patterns.SelectionItem.Pattern.Select();
        Thread.Sleep(400);
        return tab.Patterns.SelectionItem.Pattern.IsSelected.Value;
    }

    /// <summary>A tile's header is a toggle: shut it, see it shut, open it again.</summary>
    bool ToggledTwice(string id)
    {
        var toggle = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(id))?.Patterns.Toggle.PatternOrDefault;
        if (toggle is null) return false;
        var open = toggle.ToggleState.Value;
        toggle.Toggle();
        Thread.Sleep(300);
        bool shut = toggle.ToggleState.Value != open;
        toggle.Toggle();
        Thread.Sleep(300);
        return shut && toggle.ToggleState.Value == open;
    }

    void Shoot(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexaflow-usbdevice-journey");
        Directory.CreateDirectory(dir);
        Capture.Element(MainWindow).ToFile(Path.Combine(dir, $"{name}.png"));
        global::System.Console.WriteLine($"[shot] {Path.Combine(dir, name)}.png");
    }

    const string ShotFolder = "nexaflow-usbdevice-journey";

    /// <summary>Right-clicks, as a step of a check that then looks for the menu it opened.</summary>
    static bool RightClick(System.Drawing.Point at)
    {
        RightClickAt(at);
        return true;
    }

    /// <summary>A storage row that is a folder: its Size column is blank, where a file's says how big it is.</summary>
    static bool IsFolderRow(AutomationElement row) =>
        !row.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))
            .Any(t => System.Text.RegularExpressions.Regex.IsMatch(t.Name ?? "", @"^\d+(\.\d+)? (B|KB|MB|GB|TB)$"));
}
