using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.IO.Usb.Windows;
using Nexaflow.Tests.UIJourneys.Infrastructure;

namespace Nexaflow.Tests.Features.UsbProbe.UI;

/// <summary>
/// What the USB journeys share. The machine's USB devices are read once per run, in this process, before any app is
/// launched — so a journey knows which device it drives and goes straight to it by its port path, and a journey whose
/// hardware is not plugged in says so without launching anything.
/// </summary>
public abstract class UsbJourneyBase : UiJourneyTestBase
{
    static readonly Lazy<IReadOnlyList<UsbDeviceNode>> Machine = new(() =>
    {
        using var inventory = new WindowsUsbInventory(null);
        return inventory.RefreshAsync(CancellationToken.None).GetAwaiter().GetResult().Devices;
    });

    protected override string? LaunchTabKind => "UsbProbe";

    protected static readonly string[] StorageControls =
        ["Store_Browser", "Store_Tree", "Store_Entries", "Store_Path", "Store_Up", "Store_Refresh",
         "Store_CopyToComputer", "Store_AddFiles", "Store_AddFolder", "Store_NewFolder", "Store_Root"];
    protected static readonly string[] AudioOutControls =
        ["UsbAudio_Signal", "UsbAudio_Frequency", "UsbAudio_Level", "UsbAudio_Channel", "UsbAudio_Play", "UsbAudio_Stop", "UsbAudio_Identify"];
    protected static readonly string[] AudioInControls = ["UsbAudio_Meters", "UsbAudio_Duration", "UsbAudio_Record"];

    /// <summary>Each tool's controls, checked when its tile is on the page. The ones shown only mid-action — a copy's
    /// Cancel, Stop recording, a WAV's chooser — or only where the device allows — watching a HID collection's input,
    /// which Windows refuses for keyboards and mice — are listed for what they are and not expected.</summary>
    protected static readonly (string Tool, string[] Always, string[] Sometimes)[] ToolControls =
    [
        ("storage", StorageControls, ["Store_Crumb", "Store_Cancel", "Store_LastResult"]),
        ("audio out", AudioOutControls, ["UsbAudio_PickFile"]),
        ("audio in", AudioInControls, ["UsbAudio_StopRecording"]),
        ("hid", ["UsbHid_Collection", "UsbHid_Layout"],
                ["UsbHid_Reports", "UsbHid_StartMonitor", "UsbHid_Clear", "UsbHid_StopMonitor", "UsbHid_FeatureId", "UsbHid_ReadFeature",
                 "UsbHid_FeatureHex", "UsbHid_SendFeature", "UsbHid_OutputHex", "UsbHid_SendOutput"]),
        ("serial", ["UsbSerial_OpenTerminal"], []),
    ];

    /// <summary>Where this journey's screenshots go, under the temp folder.</summary>
    protected abstract string ShotFolder { get; }

    /// <summary>This machine's USB devices, as the USB page lists them.</summary>
    protected static IReadOnlyList<UsbDeviceNode> Devices => Machine.Value;

    /// <summary>A device one of whose functions runs on one of <paramref name="services"/>.</summary>
    protected static bool Runs(UsbDeviceNode device, params string[] services) =>
        device.Functions.Any(f => services.Contains(f.Driver.Service ?? "", StringComparer.OrdinalIgnoreCase));

    /// <summary>Goes to a place on the USB page by typing '&gt; path' in the AI bar, as a person would; true once the
    /// detail pane shows it.</summary>
    protected bool GoTo(string path)
    {
        if (WaitForId("AiInputBox", Affordable(10)) is not { } ai) return false;
        ai.Click();
        if (!WaitForFs(() => ai.Properties.HasKeyboardFocus.ValueOrDefault, 2)) return false;
        Keyboard.Type($"> {path}");
        Keyboard.Press(VirtualKeyShort.RETURN);
        return WaitForFs(() => WaitForId("Usb_DetailPath", 1)?.AsTextBox().Text == path, Affordable(5));
    }

    /// <summary>Goes to the device on the USB page and opens it on its own page with Open device.</summary>
    protected bool OpenDevicePage(UsbDeviceNode device)
    {
        if (!GoTo(device.Location.PortPath)) return false;
        if (WaitFor(() => MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("Usb_OpenDevice")) is { IsEnabled: true } open ? open : null,
                Affordable(5)) is not { } button) return false;
        button.AsButton().Invoke();
        return WaitForId("UsbDeviceView", Affordable(15)) is not null;
    }

    /// <summary>Selects a tab by its id; true once it is the one selected.</summary>
    protected bool Select(string tabId)
    {
        if (MainWindow.FindFirstDescendant(cf => cf.ByAutomationId(tabId)) is not { } tab) return false;
        tab.Patterns.SelectionItem.Pattern.Select();
        return WaitForFs(() => tab.Patterns.SelectionItem.Pattern.IsSelected.Value, 2);
    }

    /// <summary>The device page's tools are offered off the UI thread: true once its tiles, or the note saying why there
    /// are none, are on the page.</summary>
    protected bool ToolsOffered() =>
        WaitForFs(() => CountOf("UsbDevice_TileToggle") > 0 || Exists("UsbDevice_NoTools"), Affordable(10));

    protected void Shoot(string name)
    {
        var dir = Path.Combine(Path.GetTempPath(), ShotFolder);
        Directory.CreateDirectory(dir);
        Capture.Element(MainWindow).ToFile(Path.Combine(dir, $"{name}.png"));
        global::System.Console.WriteLine($"[shot] {Path.Combine(dir, name)}.png");
    }

    /// <summary>Right-clicks, as a step of a check that then looks for the menu it opened.</summary>
    protected static bool RightClick(System.Drawing.Point at)
    {
        RightClickAt(at);
        return true;
    }
}
