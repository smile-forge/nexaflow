using System.Collections.Generic;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UsbProbe.UI;

/// <summary>
/// One pass through the USB page on whatever this machine has plugged in: what each place's right-click offers, a
/// click on blank space, <c>&gt;</c> to bus 1, a device's descriptor beside its bytes, Up back to the top, and a hub's
/// own page saying why it has no live tools.
/// </summary>
/// <remarks>
/// It reads the real USB tree, so nothing here asserts on a particular device — only on the page's chrome and on there
/// being, on bus 1, a device to look inside. "Read device again" sends that device standard GET_DESCRIPTOR requests and
/// nothing else. Interactive desktop only — run with --filter "TestCategory=UI".
/// </remarks>
[TestClass]
[CoversNode("usbprobe")]
public class UsbProbeJourneyTests : UsbJourneyBase
{
    protected override string ShotFolder => "nexaflow-usb-journey";

    [TestMethod]
    [CoversNode("usbprobe-ui")]
    [CoversNode("usbprobe-device-tools")]
    public void Usb_RightClicksWalksDownToADescriptor_AndOpensAHub()
    {
        Assert.IsNotNull(WaitForId("UsbProbeView", 30), "the USB tab did not open");
        Shoot("1-opened");

        // ── At the top, before going anywhere ─────────────────────────────────────────────────────────────────────
        Check("Up is greyed at the top", () => WaitForId("Usb_Up", 10) is { IsEnabled: false });
        Check("Open device is greyed with no device in view", () => WaitForId("Usb_OpenDevice", 5) is { IsEnabled: false });
        var tree = CheckPresent("the tree of places", "Usb_Tree", 10)?.AsTree();
        CheckPresent("the listing", "Usb_Listing");
        CheckPresent("the detail pane", "Usb_Detail");
        CheckPresent("the open place's path", "Usb_DetailPath");
        CheckInvoke("Refresh", "Usb_Refresh");

        // ── What each place's right-click offers ──────────────────────────────────────────────────────────────────
        if (tree is not null && tree.Items.Length > 0)
        {
            tree.Items[0].Expand();
            if (WaitForFs(() => tree.Items[0].Items.Length > 0, 3))
            {
                var bus = tree.Items[0].Items[0];
                Check("right-click on a tree node offers Copy USB path", () =>
                    RightClick(CentreOf(bus)) && MenuItemNamed("Copy USB path") is { IsEnabled: true });
                ShootWindowArea(ShotFolder, "6-tree-menu");
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
            ShootWindowArea(ShotFolder, "7-device-menu");
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
        ShootWindowArea(ShotFolder, "8-blank-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);

        // ── Down to a descriptor and back up ──────────────────────────────────────────────────────────────────────
        // '>' on this page goes to a place, as it does in the file browser.
        Check("'> 1' opens bus 1", () => GoTo("1"));
        Shoot("2-bus");

        AutomationElement? descriptorTab = null;
        if (DeviceRow(list) is { } row)
        {
            row.Click();
            descriptorTab = WaitFor(() => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Usb_Tab_Descriptor")), 3);
        }
        Shoot("3-selected");
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
                Shoot("4-hovered");
            }
            Check("the Overview tab shows the device's facts", () => Select("Usb_Tab_Overview"));
            CheckInvoke("Read device again", "Usb_ReRead");
        }

        CheckInvoke("Up", "Usb_Up");
        Shoot("5-up");

        // ── A hub's own page: no tools, and the tab says why ─────────────────────────────────────────────────────
        if (Devices.FirstOrDefault(d => d.IsHub) is { } hub)
        {
            Check("a hub opens on its own page", () => OpenDevicePage(hub));
            Check("Live tools", () => Select("UsbDevice_Tab_Tools"));
            Check("no tiles on a hub", () => ToolsOffered() && CountOf("UsbDevice_TileToggle") == 0);
            CheckPresent("the tab says why it is empty", "UsbDevice_NoTools");
            Shoot("9-hub");
        }
        AssertJourney();
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
}
