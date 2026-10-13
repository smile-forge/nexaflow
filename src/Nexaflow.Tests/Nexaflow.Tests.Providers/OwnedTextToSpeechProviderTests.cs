using Nexaflow.AiHost.Contracts;
using Nexaflow.IO.Common.Hardware;
using Nexaflow.Providers.AiHost;
using Nexaflow.Providers.Common;
using Nexaflow.Providers.OpenAI;
using Nexaflow.Tests.Providers.Support;

namespace Nexaflow.Tests.Providers;

/// <summary>"Nexaflow" speaking: Kokoro run in the AI host — and OpenAI's speech, the bridged voice.</summary>
[TestClass]
public class OwnedTextToSpeechProviderTests
{
    private const string ModelId = "kokoro-82m";

    private static ComputeHardwareReport Amd() => new([new ComputeAdapter("gpu", ComputeVendor.Amd, 24L << 30, IsIntegrated: false, IsNpu: false)], null);

    private static OwnedTextToSpeechProvider Provider(FakeModelStore store, FakeAiHost host)
        => new(new FakeActivityManager(), store, host, Amd(), new ProviderModel(ModelId));

    private static IReadOnlyList<ModelAsset> Files => Provider(new FakeModelStore(), new FakeAiHost()).Catalog.Single(m => m.Id == ModelId).Files;

    [TestMethod]
    public void ItIsNexaflowsOwnVoice_SpeakingTheLanguagesItHasVoicesFor()
    {
        var provider = Provider(new FakeModelStore(), new FakeAiHost());

        Assert.AreEqual(("Nexaflow", ModelHarness.Owned, DataResidency.StaysOnMachine), (provider.Name, provider.Harness, provider.Residency));
        CollectionAssert.AreEqual(new[] { ModelKind.TextToSpeech }, ModelKinds.Of(typeof(OwnedTextToSpeechProvider)).ToArray());
        Assert.IsTrue(provider.Speaks("en"));
        Assert.IsFalse(provider.Speaks("ja"), "a reply in a language with no voice is shown, not misread");
    }

    [TestMethod]
    public async Task Speaking_OpensASpeakInTheHost_AtTheVoicePriority_WithItsVoices()
    {
        var host = new FakeAiHost();
        var provider = Provider(new FakeModelStore(Files), host);

        await using var session = await provider.StartSessionAsync(new TextToSpeechOptions(Speed: 1.1f));

        var speak = (Speak)host.Requests.Single();
        Assert.AreEqual((RequestPriority.Voice, 1.1f), (speak.Priority, speak.Speed));
        var voice = speak.Voices.Single(v => v.Language == "en");
        Assert.AreEqual(("kokoro", "onnx/model.onnx", "voices/af_heart.bin"), (voice.Model.Runner, voice.Model.File, voice.VoiceFile));
        Assert.AreEqual(@"C:\ai\models\kokoro-82m", voice.Model.Folder, "the folder is where the store keeps its files");
    }

    [TestMethod]
    public async Task Segments_AreNumberedInOrder_AndTheirAudioComesBack()
    {
        var host = new FakeAiHost
        {
            Answer = input => input switch
            {
                TextToSpeak t => [new SpokenAudio(t.RequestId, t.Segment, [1, 2]), new SpokenSegmentEnd(t.RequestId, t.Segment)],
                InputEnd e    => [new Completed(e.RequestId)],
                _             => [],
            },
        };
        var provider = Provider(new FakeModelStore(Files), host);
        await using var session = await provider.StartSessionAsync(new TextToSpeechOptions());

        Assert.AreEqual(0, await session.SpeakAsync("Good morning.", "en"));
        Assert.AreEqual(1, await session.SpeakAsync("Bonjour.", "fr"));
        await session.CompleteAsync();
        var events = new List<TextToSpeechEvent>();
        await foreach (var e in session.ReadAudioAsync()) events.Add(e);

        Assert.AreEqual(("Bonjour.", "fr"), (((TextToSpeak)host.Inputs[1]).Text, ((TextToSpeak)host.Inputs[1]).Language));
        CollectionAssert.AreEqual(new[] { 0, 1 }, events.OfType<SpeechSegmentEnd>().Select(e => e.Segment).ToArray());
        CollectionAssert.AreEqual(new byte[] { 1, 2 }, events.OfType<SpeechAudioChunk>().First().Pcm);
    }

    [TestMethod]
    public async Task AHostThatFails_EndsTheSessionAsAProviderFailure()
    {
        var host = new FakeAiHost { Answer = input => [new Failed(input.RequestId, HostErrorKind.Runtime, "the voice is missing")] };
        var provider = Provider(new FakeModelStore(Files), host);
        await using var session = await provider.StartSessionAsync(new TextToSpeechOptions());

        await session.SpeakAsync("Hello.");

        var ex = await Assert.ThrowsExactlyAsync<LlmProviderException>(async () => { await foreach (var _ in session.ReadAudioAsync()) { } });
        StringAssert.Contains(ex.Message, "the voice is missing");
    }

    [TestMethod]
    [DataRow("gpt-4o-mini-tts", true, true)]
    [DataRow("tts-1-hd", true, false)]
    [DataRow("gpt-4o-transcribe", false, true)]
    [DataRow("gpt-5", false, true)]
    public void OpenAIsSpeechModels_AreTheTtsFamily_AndTheGptOnesStream(string model, bool speaks, bool streams)
    {
        Assert.AreEqual(speaks, OpenAITextToSpeechProvider.IsTextToSpeech(model));
        if (speaks) Assert.AreEqual(streams, OpenAITextToSpeechProvider.Streams(model));
    }

    [TestMethod]
    public void OpenAI_ShipsAVoiceUnderTheSameName()
    {
        CollectionAssert.AreEqual(new[] { ModelKind.TextToSpeech }, ModelKinds.Of(typeof(OpenAITextToSpeechProvider)).ToArray());
        Assert.AreEqual(OpenAILlmProvider.ProviderName,
            new OpenAITextToSpeechProvider(new FakeActivityManager(), new OpenAIConfig(), new ProviderModel("tts-1")).Name);
    }
}
