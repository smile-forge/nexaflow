using System;
using System.Linq;
using System.Threading;
using Nexaflow.IO.Usb.Inventory;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UsbProbe.UI;

/// <summary>
/// A USB audio device: a tool for each output and input it drives, with every control there. Nothing is played or
/// recorded.
/// </summary>
/// <remarks>
/// Inconclusive at once, with no app launched, when no USB audio device is plugged in. Interactive desktop only — run
/// with --filter "TestCategory=UI".
/// </remarks>
[TestClass]
[CoversNode("usbprobe-audio")]
public class UsbAudioJourneyTests : UsbJourneyBase
{
    /// <summary>How long the input meters run before what they read is written down.</summary>
    static readonly TimeSpan MeterSettle = TimeSpan.FromSeconds(1);

    UsbDeviceNode? _audio;

    protected override string ShotFolder => "nexaflow-usbdevice-journey";

    protected override string? MissingPrerequisite()
    {
        _audio = Devices.FirstOrDefault(d => Runs(d, "usbaudio", "usbaudio2"));
        return _audio is null ? "no USB audio device plugged in" : null;
    }

    [TestMethod]
    public void AUsbAudioDevice_ShowsItsOutputsAndInputs()
    {
        Assert.IsTrue(OpenDevicePage(_audio!), "the audio device's page did not open");
        Check("Live tools", () => Select("UsbDevice_Tab_Tools"));
        Check("the tools are offered", ToolsOffered);
        bool output = Exists("UsbAudio_Play"), input = Exists("UsbAudio_Meters");
        Check("an output or an input of the device has a tool", () => output || input);
        if (output) foreach (var id in AudioOutControls) CheckExists(id, id);
        if (input) foreach (var id in AudioInControls) CheckExists(id, id);
        Shoot("6-audio");
        if (input)
        {
            // What the meters read, for the record: a silent or muted microphone reads the floor, so this is shown, not asserted.
            Thread.Sleep(MeterSettle);
            var meters = MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("UsbAudio_Meters"));
            var readings = meters?.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text)).Select(t => t.Name) ?? [];
            global::System.Console.WriteLine($"[meters] {string.Join(" | ", readings)}");
            Shoot("7-audio-later");
        }
        AssertJourney();
    }
}
