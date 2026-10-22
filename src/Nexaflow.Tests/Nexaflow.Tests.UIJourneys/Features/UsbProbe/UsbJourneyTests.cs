using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Windows;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UsbProbe.UI;

/// <summary>
/// The USB pages in one launch of the app, on whatever this machine has plugged in: the USB page walked from the top
/// down to a descriptor and back, then each kind of device on its own page — a hub, a device with its four tabs, a
/// disk browsed like the file browser, a phone, an audio device, a function on WinUSB — and Options → USB last.
/// </summary>
/// <remarks>
/// <para>
/// Each device's section is walked when such a device is plugged in and said to be skipped when it is not, so one run
/// covers what the machine has and is never inconclusive for what it lacks. Every section closes the pages it opened,
/// so the next starts on the USB page as a person would.
/// </para>
/// <para>
/// Nothing is written to a device and no driver is changed: no copy is started (the picker is cancelled), no tone
/// played, no report sent, the serial terminal is opened without connecting, and the one raw transfer is
/// GET_DESCRIPTOR for the device — the request the USB page itself sends. Options is cancelled, and Check now is never
/// pressed. Interactive desktop only — run with --filter "TestCategory=UI".
/// </para>
/// </remarks>
[TestClass]
[CoversNode("usbprobe")]
public class UsbJourneyTests : UsbJourneyBase
{
    /// <summary>How long the input meters run before what they read is written down.</summary>
    static readonly TimeSpan MeterSettle = TimeSpan.FromSeconds(1);

    protected override string ShotFolder => "nexaflow-usb-journey";

    [TestMethod]
    [CoversNode("usbprobe-ui")]
    [CoversNode("usbprobe-device")]
    [CoversNode("usbprobe-device-tools")]
    [CoversNode("usbprobe-storage")]
    [CoversNode("usbprobe-portable")]
    [CoversNode("usbprobe-audio")]
    [CoversNode("usbprobe-takeover")]
    [CoversNode("usbprobe-raw")]
    [CoversNode("usbprobe-names")]
    public void Usb_WalksThePage_AndEveryKindOfDevicePluggedIn()
    {
        Assert.IsNotNull(WaitForId("UsbProbeView", 30), "the USB tab did not open");
        UsbPage();

        var raw = new WinUsbRawAccess();
        Section("a hub", Devices.FirstOrDefault(d => d.IsHub), HubPage);
        Section("a device", Devices.Where(d => !d.IsHub).OrderByDescending(d => d.Functions.Any(f => f.ComPort is not null)).FirstOrDefault(), DevicePage);
        Section("a USB disk", Devices.FirstOrDefault(d => Runs(d, "USBSTOR", "UASPStor")), DiskPage);
        Section("a phone or camera over MTP", Devices.FirstOrDefault(d => Runs(d, "WUDFWpdMtp", "WpdUsb", "WUDFWpdFs")
            || d.Functions.Any(f => f.Children.Any(c => string.Equals(c.ClassName, "WPD", StringComparison.OrdinalIgnoreCase)))), PhonePage);
        Section("a USB audio device", Devices.FirstOrDefault(d => Runs(d, "usbaudio", "usbaudio2")), AudioPage);
        Section("a function on WinUSB", Devices.FirstOrDefault(d => d.Functions.Any(f => raw.UnreachableReason(f) is null)), RawPage);

        OptionsPage();
        AssertJourney();
    }

    /// <summary>One device's section: walked when one is plugged in, said to be skipped when not, and its pages closed
    /// after it either way.</summary>
    void Section(string what, UsbDeviceNode? device, Action<UsbDeviceNode> walk)
    {
        if (device is null)
        {
            global::System.Console.WriteLine($"[skip] {what}: none plugged in");
            return;
        }
        walk(device);
        Check($"{what}: its pages close, back to the USB page", BackToUsbPage);
    }

    // ── The USB page ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>What each place's right-click offers, a click on blank space, '&gt;' to bus 1, a device's descriptor
    /// beside its bytes, and Up back to the top.</summary>
    void UsbPage()
    {
        Shoot("01-opened");
        Check("Up is greyed at the top", () => WaitForId("Usb_Up", 10) is { IsEnabled: false });
        Check("Open device is greyed with no device in view", () => WaitForId("Usb_OpenDevice", 5) is { IsEnabled: false });
        var tree = CheckPresent("the tree of places", "Usb_Tree", 10)?.AsTree();
        CheckPresent("the listing", "Usb_Listing");
        CheckPresent("the detail pane", "Usb_Detail");
        CheckPresent("the open place's path", "Usb_DetailPath");
        CheckInvoke("Refresh", "Usb_Refresh");

        if (tree is not null && tree.Items.Length > 0)
        {
            tree.Items[0].Expand();
            if (WaitForFs(() => tree.Items[0].Items.Length > 0, 3))
            {
                var bus = tree.Items[0].Items[0];
                Check("right-click on a tree node offers Copy USB path", () =>
                    RightClick(CentreOf(bus)) && MenuItemNamed("Copy USB path") is { IsEnabled: true });
                ShootWindowArea(ShotFolder, "02-tree-menu");
                Keyboard.Press(VirtualKeyShort.ESCAPE);
                bus.Click();
            }
        }

        // A device row: its own menu, with the IDs to copy — one that is not a hub, so inside it the listing is short.
        var list = WaitForId("Usb_Listing", 5)!.AsListBox();
        WaitForFs(() => list.Items.Any(r => IdsOf(r) is not null), 3);
        if (DeviceRow(list) is { } device)
        {
            string ids = IdsOf(device)!;
            Check("right-click on a device offers its instance ID", () =>
                RightClick(CentreOf(device)) && MenuItemNamed("Copy device instance ID") is { IsEnabled: true });
            Check("…and Open device", () => MenuItemNamed("Open device") is { IsEnabled: true });
            Check("…and its debug details", () =>
                MenuItemNamed("Copy debug details") is { IsEnabled: true } && MenuItemNamed("Save debug details…") is { IsEnabled: true });
            ShootWindowArea(ShotFolder, "03-device-menu");
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Check("the right-clicked row is the one selected", () => device.AsListBoxItem().IsSelected);

            device.DoubleClick();
            WaitForFs(() => !list.Items.Any(r => IdsOf(r) == ids), 3);
            list.Items[0].Click();
            WaitForFs(() => list.Items[0].AsListBoxItem().IsSelected, 2);
        }

        // The blank space just below the last row, clear of the scroll bar: the selection clears, and the menu is the
        // open place's.
        var last = list.Items[^1].BoundingRectangle;
        var blank = new System.Drawing.Point(last.X + last.Width / 2, last.Bottom + 24);
        Check("a click on blank space clears the selection", () =>
        {
            Mouse.Click(blank);
            return WaitForFs(() => list.Items.All(i => !i.AsListBoxItem().IsSelected), 2);
        });
        Check("right-click on blank space offers Refresh", () => RightClick(blank) && MenuItemNamed("Refresh") is not null);
        ShootWindowArea(ShotFolder, "04-blank-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);

        // '>' on this page goes to a place, as it does in the file browser.
        Check("'> 1' opens bus 1", () => GoTo("1"));
        Shoot("05-bus");

        AutomationElement? descriptorTab = null;
        if (DeviceRow(list) is { } row)
        {
            row.Click();
            descriptorTab = WaitFor(() => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Usb_Tab_Descriptor")), 3);
        }
        Check("a device on bus 1 shows a Descriptor tab", () => descriptorTab is not null);
        if (descriptorTab is not null)
        {
            Check("the Descriptor tab opens the fields beside the bytes", () =>
                Select("Usb_Tab_Descriptor") && WaitForFs(() => Exists("Usb_Fields") && Exists("Usb_Hex"), 2));
            if (MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Usb_Hex")) is { } hex)
            {
                // Pointing at the bytes names the field under them, below the hex.
                Mouse.MoveTo(new System.Drawing.Point(hex.BoundingRectangle.Left + 80, hex.BoundingRectangle.Top + 12));
                Wait.UntilInputIsProcessed();
                Shoot("06-hovered");
            }
            Check("the Overview tab shows the device's facts", () => Select("Usb_Tab_Overview"));
            CheckInvoke("Read device again", "Usb_ReRead");
        }

        CheckInvoke("Up", "Usb_Up");
        Shoot("07-up");
    }

    /// <summary>The VID:PID of every device here that is not a hub.</summary>
    static readonly HashSet<string> NotHubs =
        [.. Devices.Where(d => !d.IsHub).Select(d => $"{d.VendorId:X4}:{d.ProductId:X4}")];

    /// <summary>A device's row in the listing — one that is not a hub first.</summary>
    static AutomationElement? DeviceRow(ListBox list) =>
        list.Items.Where(r => IdsOf(r) is not null).OrderBy(r => NotHubs.Contains(IdsOf(r)!) ? 0 : 1).FirstOrDefault();

    /// <summary>A row's VID:PID, which only a device's row has; null for any other.</summary>
    static string? IdsOf(AutomationElement row) =>
        row.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))
            .Select(t => t.Name ?? "")
            .FirstOrDefault(n => System.Text.RegularExpressions.Regex.IsMatch(n, "^[0-9A-F]{4}:[0-9A-F]{4}$"));

    // ── A hub ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A hub's own page: no tools, and the tab says why.</summary>
    void HubPage(UsbDeviceNode hub)
    {
        Check("a hub opens on its own page", () => OpenDevicePage(hub));
        Check("Live tools", () => Select("UsbDevice_Tab_Tools"));
        Check("no tiles on a hub", () => ToolsOffered() && CountOf("UsbDevice_TileToggle") == 0);
        CheckPresent("the tab says why it is empty", "UsbDevice_NoTools");
        Shoot("08-hub");
    }

    // ── A device and its four tabs ─────────────────────────────────────────────────────────────────────────

    /// <summary>The first device that is not a hub, one with a COM port first so the serial terminal is reached too:
    /// its four tabs, the live tools it offers with each tool's controls, its debug details, and its terminal or the
    /// way back to the tree.</summary>
    void DevicePage(UsbDeviceNode device)
    {
        string path = device.Location.PortPath;
        Check($"'> {path}' goes to the device", () => GoTo(path));
        CheckInvoke("Open device", "Usb_OpenDevice");
        Check("the device page opens", () => WaitForId("UsbDeviceView", 15) is not null);
        CheckPresent("the device's port path", "UsbDevice_Path");
        Shoot("10-overview");

        Check("Overview", () => Select("UsbDevice_Tab_Overview"));
        Check("Live tools", () => Select("UsbDevice_Tab_Tools"));
        CheckPresent("the tiles", "UsbDevice_Tiles");
        Check("the tools are offered", ToolsOffered);
        Shoot("11-tools");

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
        Shoot("12-interfaces");
        Check("Diagnostics", () => Select("UsbDevice_Tab_Diagnostics"));
        CheckPresent("the problems", "UsbDevice_Problems");
        CheckPresent("what the tools did", "UsbDevice_Log");
        CheckInvoke("Read device again", "UsbDevice_ReRead");
        // The debug details are read from the device and its tools, then copied; the status line says so once they are.
        CheckDoes("Copy debug details", "UsbDevice_CopyDebug",
                  () => WaitForFs(() => WaitForId("UsbDevice_Status", 1)?.AsTextBox().Text.Contains("copied") == true, Affordable(30)));
        CheckPresent("Save debug details", "UsbDevice_SaveDebug");
        Shoot("13-diagnostics");

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

    // ── A USB disk ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A disk's volumes in the store browser, used as the file browser is: the folder tree and the list,
    /// each right-click menu, a click on blank space, a folder opened and left with Backspace, and the picker Add
    /// files opens — cancelled.</summary>
    void DiskPage(UsbDeviceNode disk)
    {
        if (!Opened("the disk's page opens", disk)) return;
        Check("its volume is the folder tree's root", () => WaitForId("Store_Tree", 15)?.AsTree().Items.Length > 0);
        Check("the volume's root is listed", () => WaitForFs(() => WaitForId("Store_Entries", 1)?.AsListBox().Items.Length > 0, 15));
        foreach (var id in StorageControls) CheckExists(id, id);
        Check("nothing above the volume's root", () => WaitForId("Store_Up", 5) is { IsEnabled: false });
        Shoot("20-storage");

        if (WaitForId("Store_Entries", 5)?.AsListBox() is not { Items.Length: > 0 } list) return;
        var first = list.Items[0];

        // The menus a right-click opens, each drawn in the app's theme — photographed off the window, where they open.
        Check("right-click on a row offers Copy path", () => RightClick(CentreOf(first)) && MenuItemNamed("Copy path") is not null);
        ShootWindowArea(ShotFolder, "21-row-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Check("the right-clicked row is the one selected", () => first.AsListBoxItem().IsSelected);

        var area = list.BoundingRectangle;
        var blank = new System.Drawing.Point(area.X + area.Width / 2, area.Bottom - 12);
        Check("a click on blank space clears the selection", () =>
        {
            Mouse.Click(blank);
            return WaitForFs(() => !first.AsListBoxItem().IsSelected, 2);
        });
        Check("right-click on blank space offers New folder", () => RightClick(blank) && MenuItemNamed("New folder…") is not null);
        ShootWindowArea(ShotFolder, "22-blank-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);

        Check("right-click on the path bar offers Copy path", () =>
            RightClick(CentreOf(WaitForId("Store_Path", 5)!)) && MenuItemNamed("Copy path") is not null);
        ShootWindowArea(ShotFolder, "23-path-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);

        Check("right-click on selectable text offers Select all", () =>
            RightClick(CentreOf(WaitForId("Store_Root", 5)!)) && MenuItemNamed("Select all") is not null);
        ShootWindowArea(ShotFolder, "24-text-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);

        // A folder opens on a double-click and the path bar walks into it; Backspace comes back out.
        Check("a row is named for what it holds", () => list.Items.All(i => !string.IsNullOrEmpty(i.Properties.Name.ValueOrDefault)));
        if (list.Items.FirstOrDefault(IsFolderRow) is { } folder)
        {
            int steps = CountOf("Store_Crumb");
            Check("double-clicking a folder opens it", () =>
            {
                folder.DoubleClick();
                return WaitForFs(() => CountOf("Store_Crumb") == steps + 1, 3);
            });
            Shoot("25-storage-folder");
            Check("Backspace goes back up", () =>
            {
                Keyboard.Press(VirtualKeyShort.BACK);
                return WaitForFs(() => CountOf("Store_Crumb") == steps, 3);
            });
        }

        // Add files opens the app's picker choosing several — a tick beside each file. Clicked, not invoked: the picker is
        // modal, and an Invoke would wait on it.
        Mouse.Click(CentreOf(WaitForId("Store_AddFiles", 5)!));
        Check("Add files opens the picker", () => WaitFor(() => FindInAppWindows("FileBrowser_Ok"), 10) is not null);
        if (TickAFileInThePicker() is { } tick)
            Check("ticking a file lets OK answer", () =>
            {
                tick.AsCheckBox().IsChecked = true;
                return WaitForFs(() => FindInAppWindows("FileBrowser_Ok")?.IsEnabled == true, 2);
            });
        ShootWindowArea(ShotFolder, "26-picker");
        Check("cancelling the picker copies nothing", () => PressInDialog("FileBrowser_Cancel"));
    }

    /// <summary>Opens the picker's places one by one until a file shows its tick; that tick, or null when none does.</summary>
    AutomationElement? TickAFileInThePicker()
    {
        var window = FindInAppWindows("FileBrowser_Ok");
        while (window is not null && window.ControlType != FlaUI.Core.Definitions.ControlType.Window) window = window.Parent;
        if (window?.FindFirstDescendant(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Tree))?.AsTree() is not { } tree) return null;
        foreach (var place in tree.Items.Take(Affordable(4)))
        {
            place.Expand();
            if (WaitFor(() => window.FindFirstDescendant(cf => cf.ByAutomationId("FileBrowser_Tick")), 2) is { } tick) return tick;
        }
        return null;
    }

    /// <summary>A storage row that is a folder: its Size column is blank, where a file's says how big it is.</summary>
    static bool IsFolderRow(AutomationElement row) =>
        !row.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))
            .Any(t => System.Text.RegularExpressions.Regex.IsMatch(t.Name ?? "", @"^\d+(\.\d+)? (B|KB|MB|GB|TB)$"));

    // ── A phone ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A phone or camera over MTP: its storage browsed, or — locked, or charging only — the tile saying how
    /// to share it.</summary>
    void PhonePage(UsbDeviceNode phone)
    {
        if (!Opened("the phone's page opens", phone)) return;
        Check("its storage is browsed, or it says how to share it", () =>
            WaitFor(() => FindInAppWindows("Store_Tree") ?? FindInAppWindows("UsbPortable_Note"), 15) is not null);
        if (WaitForId("Store_Tree", 2)?.AsTree().Items.Length > 0)
            Check("what it shares is listed", () => WaitForFs(() => WaitForId("Store_Entries", 1)?.AsListBox().Items.Length > 0, 10));
        else
            Check("it says how to share its files", () => Exists("Store_Note") || Exists("UsbPortable_Note"));
        Shoot("30-phone");
    }

    // ── An audio device ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A tool for each output and input the device drives, with every control there. Nothing is played or
    /// recorded.</summary>
    void AudioPage(UsbDeviceNode audio)
    {
        if (!Opened("the audio device's page opens", audio)) return;
        bool output = Exists("UsbAudio_Play"), input = Exists("UsbAudio_Meters");
        Check("an output or an input of the device has a tool", () => output || input);
        if (output) foreach (var id in AudioOutControls) CheckExists(id, id);
        if (input) foreach (var id in AudioInControls) CheckExists(id, id);
        Shoot("40-audio");
        if (input)
        {
            // What the meters read, for the record: a silent or muted microphone reads the floor, so this is shown, not asserted.
            Thread.Sleep(MeterSettle);
            var meters = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UsbAudio_Meters"));
            var readings = meters?.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text)).Select(t => t.Name) ?? [];
            global::System.Console.WriteLine($"[meters] {string.Join(" | ", readings)}");
            Shoot("41-audio-later");
        }
    }

    // ── Raw access ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A function on WinUSB: its Raw access tile with every control there, GET_DESCRIPTOR for the device
    /// into its session, and the function let go again.</summary>
    void RawPage(UsbDeviceNode device)
    {
        if (!Opened("the WinUSB device's page opens", device)) return;
        foreach (var id in ToolControls.Single(t => t.Tool == "raw").Always) CheckExists($"raw: {id}", id);
        Shoot("50-raw");

        // The console starts on GET_DESCRIPTOR (device); sending it opens the function and logs the answer.
        CheckDoes("send GET_DESCRIPTOR (device)", "UsbRaw_SendControl", () => WaitForFs(() => SessionRows() > 0, Affordable(10)));
        CheckPresent("the function is held open, with the way to let it go", "UsbRaw_LetGo");
        Shoot("51-raw-read");
        CheckDoes("let it go", "UsbRaw_LetGo", () => WaitForFs(() => !Exists("UsbRaw_LetGo"), Affordable(5)));
    }

    /// <summary>The session's rows: a list with columns shows each as a data item.</summary>
    int SessionRows() =>
        MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UsbRaw_Session"))?
            .FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.DataItem)).Length ?? 0;

    // ── Options ────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Options → USB: the monthly check and Check now, looked at and left alone — Cancel, never Save, and Check
    /// now never pressed, since a journey that downloaded from linux-usb.org would be one that did something.</summary>
    void OptionsPage()
    {
        var list = OpenOptions("UsbProbeView");
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
    }

    /// <summary>Opens the device's page on its Live tools tab, with its tools offered — as one check, so a section whose
    /// page does not open says so and stops there.</summary>
    bool Opened(string label, UsbDeviceNode device)
    {
        bool opened = false;
        Check(label, () => opened = OpenDevicePage(device) && Select("UsbDevice_Tab_Tools") && ToolsOffered());
        return opened;
    }
}
