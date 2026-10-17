using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UsbProbe.UI;

/// <summary>
/// A USB disk's volumes in the store browser, as the file browser is used: the folder tree and the list, each
/// right-click menu, a click on blank space, a folder opened and left with Backspace, and the picker Add files opens.
/// </summary>
/// <remarks>
/// Nothing is written: the picker is cancelled. Inconclusive at once, with no app launched, when no USB disk is
/// plugged in. Interactive desktop only — run with --filter "TestCategory=UI".
/// </remarks>
[TestClass]
[CoversNode("usbprobe-storage")]
public class UsbDiskJourneyTests : UsbJourneyBase
{
    UsbDeviceNode? _disk;

    protected override string ShotFolder => "nexaflow-usbdevice-journey";

    protected override string? MissingPrerequisite()
    {
        _disk = Devices.FirstOrDefault(d => Runs(d, "USBSTOR", "UASPStor"));
        return _disk is null ? "no USB disk plugged in" : null;
    }

    [TestMethod]
    public void AUsbDisk_BrowsesLikeTheFileBrowser()
    {
        Assert.IsTrue(OpenDevicePage(_disk!), "the disk's page did not open");
        Check("Live tools", () => Select("UsbDevice_Tab_Tools"));
        Check("its volume is the folder tree's root", () => WaitForId("Store_Tree", 15)?.AsTree().Items.Length > 0);
        Check("the volume's root is listed", () => WaitForFs(() => WaitForId("Store_Entries", 1)?.AsListBox().Items.Length > 0, 15));
        foreach (var id in StorageControls) CheckExists(id, id);
        Check("nothing above the volume's root", () => WaitForId("Store_Up", 5) is { IsEnabled: false });
        Shoot("5-storage");

        var list = WaitForId("Store_Entries", 5)!.AsListBox();
        var first = list.Items[0];

        // The menus a right-click opens, each drawn in the app's theme — photographed off the window, where they open.
        Check("right-click on a row offers Copy path", () => RightClick(CentreOf(first)) && MenuItemNamed("Copy path") is not null);
        ShootWindowArea(ShotFolder, "5a-row-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);
        Check("the right-clicked row is the one selected", () => first.AsListBoxItem().IsSelected);

        var blank = list.BoundingRectangle;
        var blankPoint = new System.Drawing.Point(blank.X + blank.Width / 2, blank.Bottom - 12);
        Check("a click on blank space clears the selection", () =>
        {
            Mouse.Click(blankPoint);
            return WaitForFs(() => !first.AsListBoxItem().IsSelected, 2);
        });
        Check("right-click on blank space offers New folder", () => RightClick(blankPoint) && MenuItemNamed("New folder…") is not null);
        ShootWindowArea(ShotFolder, "5b-blank-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);

        Check("right-click on the path bar offers Copy path", () =>
            RightClick(CentreOf(WaitForId("Store_Path", 5)!)) && MenuItemNamed("Copy path") is not null);
        ShootWindowArea(ShotFolder, "5c-path-menu");
        Keyboard.Press(VirtualKeyShort.ESCAPE);

        Check("right-click on selectable text offers Select all", () =>
            RightClick(CentreOf(WaitForId("Store_Root", 5)!)) && MenuItemNamed("Select all") is not null);
        ShootWindowArea(ShotFolder, "5d-text-menu");
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
            Shoot("5e-storage-folder");
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
        ShootWindowArea(ShotFolder, "5f-picker");
        Check("cancelling the picker copies nothing", () => PressInDialog("FileBrowser_Cancel"));
        AssertJourney();
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
}
