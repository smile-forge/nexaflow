using Nexaflow.IO.Common;
using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Nexaflow.Features.Audio;
using Nexaflow.Features.Audio.ViewModels;
using Nexaflow.Features.Common;
using NSubstitute;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Audio;

/// <summary>listen_to_track: a stretch of the open track handed to the model as a recording it can hear.</summary>
[TestClass]
[CoversNode("audio-ai-act-listen")]
public class AudioListenTests
{
    private static AudioViewModel Player(string track)
        => new([track], 0, Substitute.For<IShellServices>(), VirtualFileSystem.Instance, new AudioConfig());

    private static string Track(string name) => TestSampleData.Files("audio").Single(f => f.EndsWith(name));

    [TestMethod]
    public async Task TheTrack_IsHandedOver_As16KilohertzMono_HoweverItWasRecorded()
    {
        var result = await Player(Track("tone_stereo.wav")).GetClientTools().Single(t => t.Name == "listen_to_track")
                                                         .InvokeAsync(new JsonObject { ["start"] = "0" }, default);

        var wav = result.Audio!.Single().Wav;
        Assert.IsFalse(result.IsError);
        Assert.AreEqual(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)), "mono");
        Assert.AreEqual(16_000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        Assert.AreEqual(0.3, (wav.Length - 44) / 2 / 16_000.0, 0.01, "the whole 0.3 s track");
    }

    [TestMethod]
    public async Task PastTheEnd_ThereIsNothingToHear()
    {
        var result = await Player(Track("tone_mono.wav")).GetClientTools().Single(t => t.Name == "listen_to_track")
                                                       .InvokeAsync(new JsonObject { ["start"] = "5" }, default);

        Assert.IsNull(result.Audio);
        StringAssert.Contains(result.ModelText, "nothing to hear");
    }

    [TestMethod]
    public async Task NoTrack_AndABadLength_AreErrorsTheModelCanRecoverFrom()
    {
        var empty = new AudioViewModel([], 0, Substitute.For<IShellServices>(), VirtualFileSystem.Instance, new AudioConfig());
        var listen = Player(Track("tone_mono.wav")).GetClientTools().Single(t => t.Name == "listen_to_track");

        Assert.IsTrue((await empty.GetClientTools().Single(t => t.Name == "listen_to_track").InvokeAsync([], default)).IsError);
        Assert.IsTrue((await listen.InvokeAsync(new JsonObject { ["seconds"] = "lots" }, default)).IsError);
    }
}
