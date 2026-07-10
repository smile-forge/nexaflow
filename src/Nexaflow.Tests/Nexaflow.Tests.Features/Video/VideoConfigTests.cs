using System.Text.Json;
using Nexaflow.Features.Video;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Video;

/// <summary>Identity, default, and round-trip for the Video feature config.</summary>
[TestClass]
[CoversNode("video-hwdecode-config")]
public class VideoConfigTests
{
    [TestMethod]
    public void Defaults_HardwareDecodingOff()
    {
        var c = new VideoConfig();
        Assert.AreEqual("video", c.ConfigName);
        Assert.AreEqual("Video", c.FriendlyName);
        Assert.IsFalse(c.EnableHardwareDecoding);
    }

    [TestMethod]
    public void RoundTrips_EnableFlag()
    {
        var json = JsonSerializer.Serialize(new VideoConfig { EnableHardwareDecoding = true });
        var c = JsonSerializer.Deserialize<VideoConfig>(json)!;
        Assert.IsTrue(c.EnableHardwareDecoding);
    }
}
