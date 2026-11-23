using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Client;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>"Nexaflow" listening: speech to text run in the AI host.</summary>
[TestClass]
public class OwnedSpeechToTextProviderTests
{
    private const string ModelId = "nemotron-whisper-turbo";

    private static MachineCards Amd() => MachineCards.None;

    private static MachineCards Nvidia() => new([new MachineCard("gpu", 24L << 30)], new Version(13, 0));

    private static OwnedSpeechToTextProvider Provider(FakeModelStore store, FakeAiHost host, MachineCards? hardware = null)
        => new(new FakeActivityManager(), store, host, new HostHolds(host), hardware ?? Amd(), new ProviderModel(ModelId));

    private static IReadOnlyList<ModelAsset> FilesOn(MachineCards hardware)
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
    public async Task ListeningForTurns_OpensAListenInTheHost_AtTheVoicePriority_HoldingEveryPartForTheSession()
    {
        var host = new FakeAiHost();
        var provider = Provider(new FakeModelStore([.. FilesOn(Nvidia()), .. CudaRuntimePack.Files]), host, Nvidia());
        string[] parts = [.. new[] { HostedRoles.VoiceActivity, HostedRoles.Recognizer, HostedRoles.Transcriber, HostedRoles.TurnDetector }.Select(role => $"{ModelId}/{role}")];

        await using (var session = await provider.StartSessionAsync(new SpeechToTextOptions("de", SpeechSegmentation.Turns)))
        {
            var listen = host.Requests.OfType<Listen>().Single();
            Assert.AreEqual((ModelId, "de", UtteranceEnd.Turn, RequestPriority.Voice), (listen.Model, listen.Language, listen.End, listen.Priority),
                            "the speech model by the catalog's id: its parts' builds, and where they lie, are the host's to take from it");
            Assert.IsTrue(host.Environment.Cuda, "the host runs from the pack, on a machine it suits");
            var voice = host.Holds.Single(h => h.Priority == RequestPriority.Voice);
            CollectionAssert.AreEquivalent(parts, voice.Models.Select(m => m.Id).ToArray(), "every part held ahead of everything else placed while it listens");
            var held = (await new HostHolds(host).DescribeAsync()).Held!;
            Assert.AreEqual((0, (int?)null), (held.Single(p => p.Part.Part == HostedRoles.Transcriber).Device, held.Single(p => p.Part.Part == HostedRoles.VoiceActivity).Device),
                            "on the GPU, but for the parts that always run on the CPU");
        }

        Assert.IsFalse(host.Holds.Any(h => h.Priority == RequestPriority.Voice), "the voice's hold ends with the session");
        CollectionAssert.AreEquivalent(parts, host.Holds.Single().Models.Select(m => m.Id).ToArray(), "the model's own hold stays while it is warm");
    }

    [TestMethod]
    public async Task Dictation_EndsAtEachPause()
    {
        var host = new FakeAiHost();
        var provider = Provider(new FakeModelStore(FilesOn(Amd())), host);

        await using var session = await provider.StartSessionAsync(new SpeechToTextOptions());

        var listen = host.Requests.OfType<Listen>().Single();
        Assert.AreEqual((ModelId, (string?)null, UtteranceEnd.Pause, RequestPriority.Interactive), (listen.Model, listen.Language, listen.End, listen.Priority));
        Assert.AreEqual(AiHostEnvironment.Cpu, host.Environment, "the CPU build, on a machine the pack does not suit");
        Assert.IsFalse(host.Holds.Any(h => h.Priority == RequestPriority.Voice), "dictation is held as the model is, with no hold at the voice priority");
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
