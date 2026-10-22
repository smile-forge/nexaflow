using System.IO;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Core.Unit.Voice;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Speech In: the chain a workspace starts with, whether the mic holds a conversation or dictates, and how the shell learns
/// that a speech model has become available.
/// </summary>
[TestClass]
[CoversNode("voice-input")]
public class SpeechInTests
{
    private static readonly string Local = LocalColumns.IdOf(LocalSlot.SpeechToText);

    [TestMethod]
    public void AGridThatHasNeverHadSpeechIn_StartsOnTheLocalColumn_ItsModelLeftForTheWorkspaceToSettle()
    {
        var config = new AiConfig();

        Assert.IsTrue(config.AddDefaultSpeechIn());

        CollectionAssert.AreEqual(new[] { Local }, config.Assignments["SpeechIn"].ColumnIds.ToArray());
        Assert.AreEqual(0, config.Columns.Count, "no speech model is assumed: the workspace settles on one it offers here");
    }

    [TestMethod]
    public void AChainTheUserEmptied_IsLeftEmpty()
    {
        var config = new AiConfig { Assignments = { ["SpeechIn"] = AbilityChain.Empty } };

        Assert.IsFalse(config.AddDefaultSpeechIn());
        Assert.AreEqual(0, config.Assignments["SpeechIn"].ColumnIds.Count);
    }

    [TestMethod]
    public void TheLocalColumnsSpeechModel_IsKept()
    {
        var existing = new ProviderModelPair { Id = Local, ProviderName = "Nexaflow", Model = "nemotron-whisper-turbo", Kind = ModelKind.SpeechToText };
        var config   = new AiConfig { Columns = [existing] };

        config.AddDefaultSpeechIn();

        Assert.AreEqual("nemotron-whisper-turbo", config.Columns.Single().Model);
        CollectionAssert.AreEqual(new[] { Local }, config.Assignments["SpeechIn"].ColumnIds.ToArray());
    }

    private sealed class Listener : ISpeechToTextProvider
    {
        public bool Ready;
        public string Name => "Nexaflow";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public bool IsAvailable => Ready;
        public event EventHandler? AvailabilityChanged;
        public void Arrive() { Ready = true; AvailabilityChanged?.Invoke(this, EventArgs.Empty); }
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<ISpeechToTextSession> StartSessionAsync(SpeechToTextOptions options, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Answerer : ILlmProvider
    {
        public string Name => "Nexaflow";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    [TestMethod]
    public void ASpeechModelThatFinishesDownloading_IsOfferedAndTheRuntimeSaysSo()
    {
        var runtime  = new WorkspaceRuntime();
        var service  = new AIService(runtime, TestTemp.Root);
        var listener = new Listener();
        service.Register("stt", listener);
        service.LoadAbilityConfig(new AiConfig
        {
            Columns     = [new ProviderModelPair { Id = "stt", ProviderName = "Nexaflow", Model = "nemotron-whisper-small", Kind = ModelKind.SpeechToText }],
            Assignments = { ["SpeechIn"] = AbilityChain.Of("stt") },
        });
        var changes = 0;
        runtime.ProvidersChanged += (_, _) => changes++;

        Assert.IsNull(service.SpeechToText, "nothing to listen with while the model is still downloading");

        listener.Arrive();

        Assert.AreSame(listener, service.SpeechToText);
        Assert.AreEqual(1, changes);
    }

    [TestMethod]
    public void TheMic_HoldsAConversation_OnNexaflowsSpeechModels_AndDictates_OnWhisperBase()
    {
        AIService Service(ProviderModelPair listening)
        {
            var service = new AIService(new WorkspaceRuntime(), TestTemp.Root);
            service.Register(Local, new Listener { Ready = true });
            service.Register("voice", new FakeSpeaker());
            service.Register("chat", new Answerer());
            service.LoadAbilityConfig(new AiConfig
            {
                Columns =
                [
                    listening,
                    new ProviderModelPair { Id = "voice", ProviderName = "Nexaflow", Model = "kokoro-82m", Kind = ModelKind.TextToSpeech },
                    new ProviderModelPair { Id = "chat", ProviderName = "Nexaflow", Model = "gemma-4-e4b-it", Kind = ModelKind.Language },
                ],
                Assignments = { ["SpeechIn"] = AbilityChain.Of(Local), ["SpeechOut"] = AbilityChain.Of("voice"), ["GeneralIntelligence"] = AbilityChain.Of("chat") },
            });
            return service;
        }

        var whisper = new ProviderModelPair { Id = Local, ProviderName = "Nexaflow", Model = "nemotron-whisper-small", Kind = ModelKind.SpeechToText };
        Assert.IsTrue(Service(whisper).CanConverse("en"), "every size of Nexaflow's Whisper hears when a turn is over");

        var dictation = new ProviderModelPair
        {
            Id = Local, ProviderName = LocalColumns.Dictation.ProviderName, Model = LocalColumns.Dictation.Model, Kind = ModelKind.SpeechToText,
        };
        Assert.IsFalse(Service(dictation).CanConverse("en"), "whisper.cpp hears words, not when a speaker has finished");
    }
}
