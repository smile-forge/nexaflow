using Nexaflow.Features.UsbProbe.SerialLink;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UsbProbe;

/// <summary>The serial link: offered where Windows made a COM port, and opening the Serial page on it.</summary>
[TestClass]
[CoversNode("usbprobe-serial-link")]
public class UsbSerialLinkToolTests
{
    [TestMethod]
    public async Task ACdcFunctionsComPort_OpensTheSerialPage_WithoutConnecting()
    {
        var host = new RecordingHost();
        var tool = (SerialLinkTool)new UsbSerialToolProvider().Offer(UsbProbeSamples.Lg(), host).Single();

        Assert.AreEqual("COM5", tool.Port);
        Assert.AreEqual("COM5", tool.Status);
        StringAssert.Contains(tool.FunctionInstanceId, "MI_01");

        var said = await tool.Operations.Single(o => o.Name == "open_terminal").RunAsync(new Dictionary<string, string>(), CancellationToken.None);
        var (kind, parameters) = host.Opened.Single();
        Assert.AreEqual("Serial", kind);
        Assert.AreEqual("COM5", parameters["port"]);
        StringAssert.Contains(said, "connects when asked");
    }

    [TestMethod]
    public void ADeviceWithNoComPort_IsOfferedNothing()
    {
        var stick = UsbProbeSamples.Stick();
        Assert.AreEqual(0, new UsbSerialToolProvider().Offer(stick, new RecordingHost()).Count);
    }
}
