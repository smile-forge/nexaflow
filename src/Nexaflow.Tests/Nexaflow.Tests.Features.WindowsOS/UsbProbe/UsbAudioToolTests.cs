using System.IO;
using Nexaflow.Features.UsbProbe.AudioTools;
using Nexaflow.IO.Common.Audio;
using Nexaflow.IO.Usb.Tools;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.UsbProbe;

/// <summary>
/// The audio tools over endpoints the test makes up: matched to the device by the hardware behind each endpoint,
/// the output tester sending the signal asked for to the channel asked for, and the input meter and recorder.
/// </summary>
[TestClass]
[CoversNode("usbprobe-audio")]
public class UsbAudioToolTests
{
    sealed class Meter : IAudioLevelMeter
    {
        public int Channels => 2;
        public int Rate => 48_000;
        public bool Disposed;
        public event Action<AudioLevelReading>? Reading;

        public Task StartAsync()
        {
            Reading?.Invoke(new AudioLevelReading([-6f, -12f], [-9f, -15f]));
            return Task.CompletedTask;
        }

        public Task<AudioClip> RecordAsync(TimeSpan length, CancellationToken ct) =>
            Task.FromResult(new AudioClip([.. Enumerable.Range(0, (int)(length.TotalSeconds * 48_000) * 2).Select(i => (i % 100) / 100f)], 2, 48_000));

        public void Dispose() => Disposed = true;
    }

    sealed class Devices(params AudioEndpointInfo[] endpoints) : IAudioDevices
    {
        public readonly List<(string Endpoint, IAudioSignal Signal)> Played = [];
        public Meter? LastMeter;

        public IReadOnlyList<AudioEndpointInfo> Endpoints() => endpoints;

        public Task PlayAsync(string endpointId, IAudioSignal signal, CancellationToken ct)
        {
            Played.Add((endpointId, signal));
            return Task.CompletedTask;
        }

        public IAudioLevelMeter Listen(string endpointId) => LastMeter = new Meter();
    }

    static readonly AudioEndpointInfo Speaker = new("{0.0.0.00000000}.{speaker}", "Speakers (USB Audio)", AudioFlow.Playback, 2, 48_000, 0x3, UsbProbeSamples.MicrophoneFunction);
    static readonly AudioEndpointInfo Mic = new("{0.0.1.00000000}.{mic}", "Microphone (USB Audio)", AudioFlow.Recording, 2, 48_000, 0x3, UsbProbeSamples.MicrophoneFunction);
    static readonly AudioEndpointInfo Laptop = new("{0.0.0.00000000}.{laptop}", "Speakers (Realtek)", AudioFlow.Playback, 2, 48_000, 0x3, @"HDAUDIO\FUNC_01\4&1");

    RecordingHost _host = null!;
    string _dir = "";

    [TestInitialize]
    public void Setup()
    {
        _host = new RecordingHost();
        _dir = Directory.CreateTempSubdirectory("nexaflow-usbaudio-").FullName;
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_dir, recursive: true);

    [TestMethod]
    public void EachEndpoint_TheDeviceDrives_GetsATool_AndNoOtherEndpointDoes()
    {
        var tools = new UsbAudioToolProvider(new Devices(Laptop, Mic, Speaker)).Offer(UsbProbeSamples.UsbMicrophone(), _host);

        Assert.AreEqual(2, tools.Count, "the laptop's own speakers are not this device's");
        Assert.IsInstanceOfType<AudioOutputTool>(tools[0], "outputs first");
        Assert.IsInstanceOfType<AudioInputTool>(tools[1]);
        Assert.IsTrue(tools.All(t => t.FunctionInstanceId == UsbProbeSamples.MicrophoneFunction));
        Assert.AreEqual(0, new UsbAudioToolProvider(new Devices(Speaker)).Offer(UsbProbeSamples.Lg(), _host).Count, "a device with no audio function gets none");
    }

    [TestMethod]
    public async Task TheOutput_PlaysWhatIsAsked_WhereItIsAsked()
    {
        var devices = new Devices(Speaker);
        var output = new UsbAudioToolProvider(devices).Offer(UsbProbeSamples.UsbMicrophone(), _host).OfType<AudioOutputTool>().Single();
        CollectionAssert.AreEqual(new[] { "All channels", "Front left", "Front right" }, output.Channels.Select(c => c.Label).ToArray());

        output.Frequency = 440;
        output.Level = -12;
        output.Channel = 1;
        await output.PlayCommand.ExecuteAsync(null);

        var (endpoint, signal) = devices.Played.Single();
        Assert.AreEqual(Speaker.Id, endpoint);
        var frames = new float[48_000 * 2];
        signal.Read(frames, 2, 48_000);
        Assert.IsTrue(Enumerable.Range(0, 48_000).All(f => frames[f * 2] == 0), "nothing on the left");
        Assert.AreEqual(AudioLevels.Amplitude(-12), Enumerable.Range(0, 48_000).Max(f => frames[f * 2 + 1]), 0.001, "the right at -12 dBFS");
        Assert.AreEqual("Idle", output.Status, "and idle again once it has played");
    }

    [TestMethod]
    public async Task TheFrequencySlider_TravelsEqually_PerOctave()
    {
        var output = new UsbAudioToolProvider(new Devices(Speaker)).Offer(UsbProbeSamples.UsbMicrophone(), _host).OfType<AudioOutputTool>().Single();
        output.FrequencyPosition = 0;
        Assert.AreEqual(20, output.Frequency);
        output.FrequencyPosition = 1;
        Assert.AreEqual(20_000, output.Frequency);
        output.FrequencyPosition = 0.5;
        Assert.AreEqual(632, output.Frequency, 1, "the middle of 20 Hz – 20 kHz on a log scale");
        Assert.AreEqual("632 Hz", output.FrequencyText);
        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task TheAgent_PlaysATone_OrWalksTheChannels()
    {
        var devices = new Devices(Speaker);
        var output = new UsbAudioToolProvider(devices).Offer(UsbProbeSamples.UsbMicrophone(), _host).OfType<AudioOutputTool>().Single();
        var tone = output.Operations.Single(o => o.Name == "play_tone");
        Assert.IsTrue(tone.ChangesSomething, "making a sound is acting, not reading");

        await tone.RunAsync(new Dictionary<string, string> { ["frequency_hz"] = "250", ["channel"] = "Front left", ["seconds"] = "1" }, CancellationToken.None);
        Assert.IsInstanceOfType<ToneSignal>(devices.Played[^1].Signal);

        await Assert.ThrowsExactlyAsync<ArgumentException>(() =>
            tone.RunAsync(new Dictionary<string, string> { ["channel"] = "Rear left" }, CancellationToken.None), "a channel the endpoint has not got");

        var walked = await output.Operations.Single(o => o.Name == "identify_channels").RunAsync(new Dictionary<string, string>(), CancellationToken.None);
        Assert.IsInstanceOfType<ChannelWalkSignal>(devices.Played[^1].Signal);
        StringAssert.Contains(walked, "Front left, Front right");
    }

    [TestMethod]
    public async Task TheInput_MetersWhileOpen_AndStopsWhenShut()
    {
        var devices = new Devices(Mic);
        var input = new UsbAudioToolProvider(devices).Offer(UsbProbeSamples.UsbMicrophone(), _host).OfType<AudioInputTool>().Single();

        await input.ActivateAsync(CancellationToken.None);
        Assert.IsTrue(input.IsListening);
        Assert.AreEqual(-6f, input.Meters[0].PeakDb);
        Assert.AreEqual(-15f, input.Meters[1].RmsDb);
        Assert.AreEqual("-6.0 dB", input.Meters[0].Text);

        input.Deactivate();
        Assert.IsFalse(input.IsListening);
        Assert.IsTrue(devices.LastMeter!.Disposed, "shutting the tile lets go of the microphone");
    }

    [TestMethod]
    public async Task ARecording_IsSavedAsAFloatWav_WithEveryChannel()
    {
        var input = new UsbAudioToolProvider(new Devices(Mic)).Offer(UsbProbeSamples.UsbMicrophone(), _host).OfType<AudioInputTool>().Single();
        var path = Path.Combine(_dir, "take.wav");

        var said = await input.Operations.Single(o => o.Name == "record")
            .RunAsync(new Dictionary<string, string> { ["path"] = path, ["seconds"] = "1" }, CancellationToken.None);

        var clip = WavAudio.ReadClip(File.ReadAllBytes(path));
        Assert.AreEqual(2, clip.Channels);
        Assert.AreEqual(48_000, clip.Rate);
        Assert.AreEqual(TimeSpan.FromSeconds(1), clip.Length);
        StringAssert.Contains(said, "2 channel(s) at 48000 Hz");
        Assert.IsFalse(input.IsListening, "recorded with the tile shut, the microphone is let go afterwards");
        Assert.AreEqual(1, _host.Logged.Count);
    }
}
