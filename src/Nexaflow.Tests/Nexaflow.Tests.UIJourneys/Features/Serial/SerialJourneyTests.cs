using System;
using System.IO;
using System.Threading;
using FlaUI.Core.Capturing;
using Nexaflow.Tests.UIJourneys.Infrastructure;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Serial.UI;

/// <summary>
/// The serial terminal, looked at and never connected: connecting toggles DTR, which resets many boards, and a journey
/// that reset whatever is on a COM port would be a test that did something.
/// </summary>
/// <remarks>
/// Serial_Disconnect shows only while connected, so it is named here and not expected. Interactive desktop only — run
/// with --filter "TestCategory=UI".
/// </remarks>
[TestClass]
[CoversNode("serial")]
public class SerialJourneyTests : UiJourneyTestBase
{
    protected override string? LaunchTabKind => "Serial";

    static readonly string[] Settings = ["Serial_Port", "Serial_Baud", "Serial_DataBits", "Serial_Parity", "Serial_StopBits", "Serial_Handshake",
                                         "Serial_Connect", "Serial_Dtr", "Serial_Rts"];
    static readonly string[] Sending = ["Serial_Transcript", "Serial_SendText", "Serial_LineEnding", "Serial_SendHex", "Serial_Send", "Serial_Save"];
    const string ShownWhileConnected = "Serial_Disconnect";

    [TestMethod]
    [CoversNode("serial-ui")]
    public void Serial_ShowsThePortsAndSettings_WithoutConnecting()
    {
        MainWindow.SetForeground();
        Thread.Sleep(500);
        Assert.IsNotNull(WaitForId("SerialView", 30), "the Serial tab did not open");

        foreach (var id in Settings) CheckPresent(id, id, 5);
        foreach (var id in Sending) CheckPresent(id, id);
        CheckInvoke("list the ports again", "Serial_RefreshPorts");
        Check("not connected", () => !Exists(ShownWhileConnected));

        Check("show hex", () => SetToggle("Serial_ShowHex", true) && SetToggle("Serial_ShowHex", false));
        Check("times", () => SetToggle("Serial_Times", false) && SetToggle("Serial_Times", true));
        CheckInvoke("clear the transcript", "Serial_Clear");

        var dir = Path.Combine(Path.GetTempPath(), "nexaflow-serial-journey");
        Directory.CreateDirectory(dir);
        Capture.Element(MainWindow).ToFile(Path.Combine(dir, "serial.png"));
        global::System.Console.WriteLine($"[shot] {Path.Combine(dir, "serial")}.png");
        AssertJourney();
    }
}
