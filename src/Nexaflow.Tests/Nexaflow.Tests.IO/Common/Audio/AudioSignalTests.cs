using Nexaflow.IO.Common.Audio;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.IO.Common.Audio;

/// <summary>
/// The test signals and the meter's arithmetic, sample by sample: a tone is the frequency it says on the channels it
/// says, a sweep climbs, a clip lands on the right channels at the right rate, the channel walk sounds one channel at a
/// time, and a full-scale sine reads 0 dB peak and -3 dB RMS.
/// </summary>
[TestClass]
[CoversNode("iocommon-audio-test")]
public class AudioSignalTests
{
    const int Rate = 48_000;

    static float[] Render(IAudioSignal signal, int channels, int frames, out int written)
    {
        var buffer = new float[frames * channels];
        written = signal.Read(buffer, channels, Rate);
        return buffer;
    }

    static float[] Channel(float[] interleaved, int channels, int channel, int frames) =>
        [.. Enumerable.Range(0, frames).Select(f => interleaved[f * channels + channel])];

    static int RisingZeroCrossings(float[] samples) =>
        Enumerable.Range(1, samples.Length - 1).Count(i => samples[i - 1] < 0 && samples[i] >= 0);

    [TestMethod]
    public void ATone_IsItsFrequency_OnlyOnTheChannelsChosen()
    {
        var tone = new ToneSignal(1000, AudioLevels.Amplitude(-6), ChannelSelection.Only(1));
        var samples = Render(tone, channels: 2, frames: Rate, out int written);

        Assert.AreEqual(Rate, written, "a tone with no length never ends");
        var right = Channel(samples, 2, 1, Rate);
        Assert.IsTrue(Math.Abs(RisingZeroCrossings(right) - 1000) <= 1, $"{RisingZeroCrossings(right)} cycles in a second");
        Assert.AreEqual(AudioLevels.Amplitude(-6), right.Max(), 0.001, "its peak is the level asked for");
        Assert.IsTrue(Channel(samples, 2, 0, Rate).All(s => s == 0), "the other channel is silent");
    }

    [TestMethod]
    public void AToneWithALength_Ends()
    {
        var tone = new ToneSignal(440, 0.5, ChannelSelection.All, TimeSpan.FromMilliseconds(100));
        Render(tone, 2, Rate, out int first);
        Render(tone, 2, Rate, out int after);

        Assert.AreEqual(Rate / 10, first);
        Assert.AreEqual(0, after);
    }

    [TestMethod]
    public void ASweep_Climbs_FromItsStartToItsEnd()
    {
        var sweep = new SweepSignal(100, 10_000, TimeSpan.FromSeconds(1), 0.5, ChannelSelection.All);
        var samples = Render(sweep, 1, Rate, out int written);

        Assert.AreEqual(Rate, written);
        int early = RisingZeroCrossings(samples[..(Rate / 10)]), late = RisingZeroCrossings(samples[^(Rate / 10)..]);
        Assert.IsTrue(late > early * 20, $"the last tenth ({late} cycles) is far above the first ({early})");
        Assert.AreEqual(10_000, sweep.Frequency, 50, "and ends where it said");
        Assert.AreEqual(0, sweep.Read(new float[64], 1, Rate), "then stops");
    }

    [TestMethod]
    public void AMonoClip_PlaysOnEveryChannel_AtTheEndpointsRate()
    {
        var clip = new ClipSignal([.. Enumerable.Range(0, 24_000).Select(i => (float)Math.Sin(i * 2 * Math.PI * 100 / 24_000))], 1, 24_000, 1);
        var samples = Render(clip, 2, Rate * 2, out int written);

        Assert.AreEqual(Rate, written, 4, "one second at 24 kHz is one second at 48 kHz");
        CollectionAssert.AreEqual(Channel(samples, 2, 0, written), Channel(samples, 2, 1, written), "both channels carry it");
        Assert.AreEqual(100, RisingZeroCrossings(Channel(samples, 2, 0, written)), 1);
    }

    [TestMethod]
    public void TheChannelWalk_SoundsEachChannelAlone_InOrder()
    {
        var walk = new ChannelWalkSignal(1000, 0.5, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50));
        int slot = Rate * 150 / 1000;
        var heard = new List<int>();
        var buffer = new float[slot * 6];
        for (int read; (read = walk.Read(buffer, 6, Rate)) > 0;)
        {
            // Ten-millisecond windows, each judged by its loudest sample: a single sample can land on a zero crossing.
            for (int start = 0; start < read; start += 480)
            {
                int end = Math.Min(start + 480, read);
                var loud = Enumerable.Range(0, 6)
                    .Where(c => Enumerable.Range(start, end - start).Max(f => Math.Abs(buffer[f * 6 + c])) > 0.05)
                    .ToList();
                Assert.IsTrue(loud.Count <= 1, "never two channels at once");
                if (loud.Count == 1 && (heard.Count == 0 || heard[^1] != loud[0])) heard.Add(loud[0]);
            }
        }
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, heard);
        Assert.AreEqual(-1, walk.CurrentChannel, "and says nothing is sounding once it is over");
    }

    [TestMethod]
    public void AFullScaleSine_ReadsZeroPeak_AndMinusThreeRms()
    {
        var sine = Render(new ToneSignal(1000, 1, ChannelSelection.Only(0)), 2, Rate, out _);
        var peak = new float[2];
        var rms = new float[2];
        AudioLevels.Measure(sine, 2, peak, rms);

        Assert.AreEqual(0, peak[0], 0.01);
        Assert.AreEqual(-3.01, rms[0], 0.05);
        Assert.AreEqual(AudioLevels.Floor, peak[1], "silence reads as the floor, not minus infinity");
        Assert.AreEqual(-20, AudioLevels.Decibels(AudioLevels.Amplitude(-20)), 0.001);
    }

    [TestMethod]
    public void ChannelNames_FollowTheSpeakerMask()
    {
        CollectionAssert.AreEqual(new[] { "Mono" }, AudioChannels.Names(1, 0).ToArray());
        CollectionAssert.AreEqual(new[] { "Front left", "Front right" }, AudioChannels.Names(2, 0).ToArray(), "stereo with no mask is front left and right");
        CollectionAssert.AreEqual(new[] { "Front left", "Front right", "Front centre", "Low frequency", "Side left", "Side right" },
            AudioChannels.Names(6, 0x3 | 0x4 | 0x8 | 0x200 | 0x400).ToArray(), "5.1 in its channel order");
        CollectionAssert.AreEqual(new[] { "Front left", "Channel 2", "Channel 3" }, AudioChannels.Names(3, 0x1).ToArray(), "channels the mask leaves out are numbered");
    }

    [TestMethod]
    public void AFloatWav_KeepsEveryChannel_AndEverySample()
    {
        float[] samples = [0f, 0.5f, -0.25f, 1f, -1f, 0.125f];
        var clip = new AudioClip(samples, 3, 44_100);

        var back = WavAudio.ReadClip(WavAudio.WriteFloat(clip));

        Assert.AreEqual(3, back.Channels);
        Assert.AreEqual(44_100, back.Rate);
        CollectionAssert.AreEqual(samples, back.Samples);
        Assert.AreEqual(TimeSpan.FromSeconds(2.0 / 44_100), back.Length);
    }

    [TestMethod]
    public void A16BitWav_ReadsAsFloat()
    {
        var pcm = new byte[] { 0x00, 0x40, 0x00, 0xC0 };   // +0.5, -0.5
        var clip = WavAudio.ReadClip(WavAudio.Write(pcm, 8_000));

        Assert.AreEqual(1, clip.Channels);
        Assert.AreEqual(0.5f, clip.Samples[0], 0.0001);
        Assert.AreEqual(-0.5f, clip.Samples[1], 0.0001);
    }
}
