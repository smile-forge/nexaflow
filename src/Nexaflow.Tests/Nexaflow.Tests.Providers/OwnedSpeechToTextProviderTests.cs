using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>"Nexaflow" listening: speech to text run in the AI host.</summary>
[TestClass]
public class OwnedSpeechToTextProviderTests
{
    private const string ModelId = "nemotron-whisper-turbo";

    private static ComputeHardwareReport Amd() => new([new ComputeAdapter("gpu", ComputeVendor.Amd, 24L << 30, IsIntegrated: false, IsNpu: false)], null);

    private static ComputeHardwareReport Nvidia() => new([new ComputeAdapter("gpu", ComputeVendor.Nvidia, 24L << 30, IsIntegrated: false, IsNpu: false)], new Version(13, 0));

    private static OwnedSpeechToTextProvider Provider(FakeModelStore store, FakeAiHost host, ComputeHardwareReport? hardware = null)
        => new(new FakeActivityManager(), store, host, hardware ?? Amd(), new ProviderModel(ModelId));

    private static IReadOnlyList<ModelAsset> FilesOn(ComputeHardwareReport hardware)
        => Provider(new FakeModelStore(), new FakeAiHost(), hardware).Catalog.Single(m => m.Id == ModelId).Files;

    [TestMethod]
    public void ItIsNexaflowsOwnSpeechToText_AndNothingLeavesTheMachine()
    {
        var provider = Provider(new FakeModelStore(), new FakeAiHost());

        Assert.AreEqual(("Nexaflow", ModelHarness.Owned, DataResidency.StaysOnMachine), (provider.Name, provider.Harness, provider.Residency));
        CollectionAssert.AreEqual(new[] { ModelKind.SpeechToText }, ModelKinds.Of(typeof(OwnedSpeechToTextProvider)).ToArray());
    }

    [TestMethod]
    public void TheModel_IsAvailableOnceTheFilesThisMachineNeedsAreHere()
    {
        var store = new FakeModelStore();
        var provider = Provider(store, new FakeAiHost());
        Assert.IsFalse(provider.IsAvailable);

        store.EnsureAllAsync(FilesOn(Amd())).Wait();

        Assert.IsTrue(provider.IsAvailable);
        Assert.IsFalse(FilesOn(Amd()).Any(f => f.RelativePath.Contains("-cuda/")), "a machine without the CUDA pack fetches the CPU build");
    }

    [TestMethod]
    public async Task ListeningForTurns_OpensAListenInTheHost_AtTheVoicePriority_WithEveryPart()
    {
        var host = new FakeAiHost();
        var provider = Provider(new FakeModelStore(FilesOn(Nvidia())), host, Nvidia());

        await using var session = await provider.StartSessionAsync(new SpeechToTextOptions("de", SpeechSegmentation.Turns));

        var listen = (Listen)host.Requests.Single();
        Assert.AreEqual(("de", UtteranceEnd.Turn, RequestPriority.Voice), (listen.Language, listen.End, listen.Priority));
        Assert.AreEqual(("silero-vad", "silero_vad.onnx"), (listen.Models.VoiceActivity.Runner, listen.Models.VoiceActivity.File));
        Assert.AreEqual(@"C:\ai\models\nemotron-3.5-asr-streaming", listen.Models.Recognizer.Folder, "the folder is where the store keeps its files");
        Assert.AreEqual(@"C:\ai\models\whisper-large-v3-turbo-cuda", listen.Models.Transcriber!.Folder, "the CUDA build, on a machine the pack suits");
        Assert.AreEqual("1", listen.Models.Transcriber.CudaOptions!["sdpa_kernel"]);
        Assert.AreEqual("smart-turn", listen.Models.TurnDetector!.Runner);
    }

    [TestMethod]
    public async Task Dictation_EndsAtEachPause()
    {
        var host = new FakeAiHost();
        var provider = Provider(new FakeModelStore(FilesOn(Amd())), host);

        await using var session = await provider.StartSessionAsync(new SpeechToTextOptions());

        var listen = (Listen)host.Requests.Single();
        Assert.AreEqual((null, UtteranceEnd.Pause, RequestPriority.Interactive), (listen.Language, listen.End, listen.Priority));
        Assert.AreEqual(@"C:\ai\models\whisper-large-v3-turbo-cpu", listen.Models.Transcriber!.Folder);
    }

    [TestMethod]
    public async Task AudioGoesIn_AndWhatIsHeardComesBack()
    {
        var host = new FakeAiHost
        {
            Answer = input => input switch
            {
                AudioInput a => [new ListenActivity(a.RequestId, true), new ListenPartial(a.RequestId, "buy")],
                InputEnd e   => [new ListenFinal(e.RequestId, "Buy milk."), new ListenActivity(e.RequestId, false), new Completed(e.RequestId)],
                _            => [],
            },
        };
        var provider = Provider(new FakeModelStore(FilesOn(Amd())), host);
        await using var session = await provider.StartSessionAsync(new SpeechToTextOptions());

        await session.AppendAudioAsync(new byte[] { 1, 2, 3, 4 });
        await session.CompleteAsync();
        var events = new List<SpeechToTextEvent>();
        await foreach (var e in session.ReadEventsAsync()) events.Add(e);

        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, ((AudioInput)host.Inputs[0]).Pcm);
        CollectionAssert.AreEqual(new SpeechToTextEvent[]
        {
            new SpeechActivity(true), new TranscriptPartial("buy"), new TranscriptFinal("Buy milk."), new SpeechActivity(false),
        }, events);
    }

    [TestMethod]
    public async Task AHostThatFails_EndsTheSessionAsAProviderFailure()
    {
        var host = new FakeAiHost { Answer = input => [new Failed(input.RequestId, HostErrorKind.Runtime, "the recogniser crashed")] };
        var provider = Provider(new FakeModelStore(FilesOn(Amd())), host);
        await using var session = await provider.StartSessionAsync(new SpeechToTextOptions());

        await session.AppendAudioAsync(new byte[] { 1, 2 });

        var ex = await Assert.ThrowsExactlyAsync<LlmProviderException>(async () => { await foreach (var _ in session.ReadEventsAsync()) { } });
        StringAssert.Contains(ex.Message, "the recogniser crashed");
    }

    [TestMethod]
    public async Task ListeningBeforeTheModelIsHere_SaysWhy()
        => await Assert.ThrowsExactlyAsync<LlmProviderException>(() =>
            Provider(new FakeModelStore(), new FakeAiHost()).StartSessionAsync(new SpeechToTextOptions()));
}
