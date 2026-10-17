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
/// One pass through the USB page on whatever this machine has plugged in: the tree, the listing and the detail
/// pane are there; <c>&gt;</c> goes to bus 1; a device's descriptor opens beside its bytes; Up climbs back.
/// </summary>
/// <remarks>
/// It reads the real USB tree, so nothing here asserts on a particular device — only on the page's chrome and
/// on there being, somewhere on bus 1, a device to look inside. "Read device again" sends that device standard
/// GET_DESCRIPTOR requests and nothing else. Interactive desktop only — run with --filter "TestCategory=UI".
/// </remarks>
[TestClass]
[CoversNode("usbprobe")]
public class UsbProbeJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "UsbProbe";

    [TestMethod]
    [CoversNode("usbprobe-ui")]
    public void Usb_WalksDownToADescriptor_AndBackUp()
    {
        MainWindow.SetForeground();
        Thread.Sleep(500);

        var view = WaitForId("UsbProbeView", 30);
        Shoot("1-opened");
        Assert.IsNotNull(view, "the USB tab did not open");

        CheckPresent("the tree of places", "Usb_Tree", 10);
        var listing = CheckPresent("the listing", "Usb_Listing");
        CheckPresent("the detail pane", "Usb_Detail");
        CheckPresent("the open place's path", "Usb_DetailPath");
        CheckInvoke("Refresh", "Usb_Refresh");

        // '>' on this page goes to a place, as it does in the file browser.
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
        Shoot("2-bus");

        // The rows are clicked until one is a device — the one kind of row with a descriptor to show.
        AutomationElement? descriptorTab = null;
        if (listing is { } list)
        {
            foreach (var row in list.AsListBox().Items.Take(Affordable(12)))
            {
                row.Click();
                Thread.Sleep(400);
                descriptorTab = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Usb_Tab_Descriptor"));
                if (descriptorTab is not null) break;
            }
        }
        Shoot("3-selected");
        Check("a device on bus 1 shows a Descriptor tab", () => descriptorTab is not null);

        if (descriptorTab is not null)
        {
            Check("the Descriptor tab opens the fields beside the bytes", () =>
            {
                descriptorTab.Patterns.SelectionItem.Pattern.Select();
                Thread.Sleep(400);
                return Exists("Usb_Fields") && Exists("Usb_Hex");
            });
            if (MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Usb_Hex")) is { } hex)
            {
                // Pointing at the bytes names the field under them, below the hex.
                Mouse.MoveTo(new System.Drawing.Point(hex.BoundingRectangle.Left + 80, hex.BoundingRectangle.Top + 12));
                Thread.Sleep(300);
                Shoot("4-hovered");
            }

            if (MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Usb_Tab_Overview")) is { } overview)
                Check("the Overview tab shows the device's facts", () =>
                {
                    overview.Patterns.SelectionItem.Pattern.Select();
                    Thread.Sleep(300);
                    return overview.Patterns.SelectionItem.Pattern.IsSelected.Value;
                });

            CheckInvoke("Read device again", "Usb_ReRead");
        }

        CheckInvoke("Up", "Usb_Up");
        Shoot("5-up");
        AssertJourney();
    }

    [TestMethod]
    [CoversNode("usbprobe-ui")]
    public void Usb_RightClick_OffersWhatEachPlaceAllows()
    {
        MainWindow.SetForeground();
        Thread.Sleep(500);
        Assert.IsNotNull(WaitForId("UsbProbeView", 30), "the USB tab did not open");
        Check("Up is greyed at the top", () => WaitForId("Usb_Up", 10) is { IsEnabled: false });
        Check("Open device is greyed with no device in view", () => WaitForId("Usb_OpenDevice", 5) is { IsEnabled: false });

        // The tree: a bus's menu, without opening it.
        var tree = WaitForId("Usb_Tree", 10)!.AsTree();
        tree.Items[0].Expand();
        Thread.Sleep(300);
        var bus = tree.Items[0].Items[0];
        Check("right-click on a tree node offers Copy USB path", () =>
        {
            RightClickAt(CentreOf(bus));
            return MenuItemNamed("Copy USB path") is { IsEnabled: true };
        });
        ShootWindowArea(ShotFolder, "6-tree-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);

        // A device row: its own menu, with the IDs to copy.
        bus.Click();
        Thread.Sleep(600);
        var list = WaitForId("Usb_Listing", 5)!.AsListBox();
        var device = list.Items.FirstOrDefault(IsDeviceRow);
        if (device is not null)
        {
            Check("right-click on a device offers its instance ID", () =>
            {
                RightClickAt(CentreOf(device));
                return MenuItemNamed("Copy device instance ID") is { IsEnabled: true } && MenuItemNamed("Open device") is { IsEnabled: true };
            });
            ShootWindowArea(ShotFolder, "7-device-menu");
            Keyboard.Press(VirtualKeyShort.ESCAPE);
            Check("the right-clicked row is the one selected", () => device.AsListBoxItem().IsSelected);

            // Inside the device the listing is short, so there is blank space beneath its rows.
            device.DoubleClick();
            Thread.Sleep(800);
            list = WaitForId("Usb_Listing", 5)!.AsListBox();
            list.Items[0].Click();
            Thread.Sleep(300);
        }

        // The blank space: the selection clears, and the menu is the place open's.    // Just below the last row: the list's own blank space, clear of its scroll bar.
    var last = list.Items[^1].BoundingRectangle;
    var blank = new System.Drawing.Point(last.X + last.Width / 2, last.Bottom + 24);
        Check("a click on blank space clears the selection", () =>
        {
            Mouse.Click(blank);
            Thread.Sleep(300);
            return list.Items.All(i => !i.AsListBoxItem().IsSelected);
        });
        Check("right-click on blank space offers Refresh", () =>
        {
            RightClickAt(blank);
            return MenuItemNamed("Refresh") is not null;
        });
        ShootWindowArea(ShotFolder, "8-blank-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        AssertJourney();
    }

    const string ShotFolder = "nexaflow-usb-journey";

    /// <summary>A device's row: the only kind with a VID:PID in its IDs column.</summary>
    static bool IsDeviceRow(AutomationElement row) =>
        row.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text))
            .Any(t => System.Text.RegularExpressions.Regex.IsMatch(t.Name ?? "", "^[0-9A-F]{4}:[0-9A-F]{4}$"));

    void Shoot(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexaflow-usb-journey");
        Directory.CreateDirectory(dir);
        Capture.Element(MainWindow).ToFile(Path.Combine(dir, $"{name}.png"));
        global::System.Console.WriteLine($"[shot] {Path.Combine(dir, name)}.png");
    }
}
