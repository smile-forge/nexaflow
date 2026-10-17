using Nexaflow.IO.Common.Audio;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Audio;

/// <summary>
/// The audio endpoints this machine has. It only lists what Windows knows — nothing is opened —
/// so it is safe anywhere, and inconclusive where there are none.
/// </summary>
[TestClass]
[CoversNode("iocommon-audio-test")]
public class AudioEndpointCatalogTests
{
    [TestMethod]
    public void EveryActiveEndpoint_HasAFormat_AndMostKnowTheirHardware()
    {
        var endpoints = AudioEndpointCatalog.List();
        if (endpoints.Count == 0) Assert.Inconclusive("no active audio endpoints");

        foreach (var e in endpoints)
        {
            Assert.IsFalse(string.IsNullOrEmpty(e.Id));
            Assert.IsFalse(string.IsNullOrEmpty(e.Name));
            Assert.IsTrue(e.Channels >= 1 && e.SampleRate >= 8_000, $"{e.Name}: {e.Channels} ch at {e.SampleRate} Hz");
            Assert.AreEqual(e.Channels, e.ChannelNames.Count);
        }
        Assert.AreEqual(endpoints.Count, endpoints.Select(e => e.Id).Distinct().Count());
        Assert.IsTrue(endpoints.Any(e => e.AdapterInstanceId is not null), "the device topology names the hardware behind the endpoints");
    }
}
