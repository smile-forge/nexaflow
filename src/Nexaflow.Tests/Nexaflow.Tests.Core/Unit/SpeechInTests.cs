using System.IO;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// Speech In: the chain a workspace starts with, the model an earlier version's Voice settings chose, and how the
/// shell learns that a speech model has become available.
/// </summary>
[TestClass]
[CoversNode("voice-input")]
public class SpeechInTests
{
    [TestMethod]
    public void AGridThatHasNeverHadSpeechIn_StartsOnWhisper()
    {
        var config = new AiConfig();

        Assert.IsTrue(config.AddDefaultSpeechIn("small.en"));

        var column = config.Columns.Single();
        Assert.AreEqual(("Whisper", "small.en", ModelKind.SpeechToText), (column.ProviderName, column.Model, column.Kind));
        CollectionAssert.AreEqual(new[] { column.Id }, config.Assignments["SpeechIn"].ColumnIds.ToArray());
    }

    [TestMethod]
    public void AChainTheUserEmptied_IsLeftEmpty()
    {
        var config = new AiConfig { Assignments = { ["SpeechIn"] = AbilityChain.Empty } };

        Assert.IsFalse(config.AddDefaultSpeechIn(AiConfig.DefaultSpeechModel));
        Assert.AreEqual(0, config.Columns.Count);
    }

    [TestMethod]
    public void TheLocalColumnsSpeechModel_IsUsedRatherThanAnotherAdded()
    {
        var id       = LocalColumns.IdOf(LocalSlot.SpeechToText);
        var existing = new ProviderModelPair { Id = id, ProviderName = "Nexaflow", Model = "nemotron-whisper-turbo", Kind = ModelKind.SpeechToText };
        var config   = new AiConfig { Columns = [existing] };

        config.AddDefaultSpeechIn("base.en");

        Assert.AreEqual("nemotron-whisper-turbo", config.Columns.Single().Model);
        CollectionAssert.AreEqual(new[] { id }, config.Assignments["SpeechIn"].ColumnIds.ToArray());
    }

    [TestMethod]
    [DataRow("""{ "ModelSize": "Small", "Language": "Multilingual" }""", "small")]
    [DataRow("""{ "ModelSize": "Medium", "Language": "EnglishOnly" }""", "medium.en")]
    [DataRow("""{ "ModelSize": 3, "Language": 0 }""", "large-v3")]
    [DataRow("""{ "ModelSize": 0, "Language": 0 }""", "base.en")]
    [DataRow("""not json""", "base.en")]
    public void TheModelAnEarlierVersionsVoiceSettingsChose_IsKept(string saved, string model)
    {
        var root = Path.Combine(Path.GetTempPath(), "nexaflow-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "voice"));
            File.WriteAllText(Path.Combine(root, "voice", "config_1.6.0.0.json"), saved);

            Assert.AreEqual(model, LegacyVoiceSettings.WhisperModel(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public void NoEarlierVoiceSettings_MeansTheDefaultModel()
        => Assert.AreEqual(AiConfig.DefaultSpeechModel, LegacyVoiceSettings.WhisperModel(Path.GetTempPath() + Guid.NewGuid().ToString("N")));

    private sealed class Listener : ISpeechToTextProvider
    {
        public bool Ready;
        public string Name => "Whisper";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public bool IsAvailable => Ready;
        public event EventHandler? AvailabilityChanged;
        public void Arrive() { Ready = true; AvailabilityChanged?.Invoke(this, EventArgs.Empty); }
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<ISpeechToTextSession> StartSessionAsync(SpeechToTextOptions options, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [TestMethod]
    public void ASpeechModelThatFinishesDownloading_IsOfferedAndTheRuntimeSaysSo()
    {
        var runtime  = new WorkspaceRuntime();
        var service  = new AIService(runtime, Path.GetTempPath());
        var listener = new Listener();
        service.Register("stt", listener);
        service.LoadAbilityConfig(new AiConfig
        {
            Columns     = [new ProviderModelPair { Id = "stt", ProviderName = "Whisper", Model = "base.en", Kind = ModelKind.SpeechToText }],
            Assignments = { ["SpeechIn"] = AbilityChain.Of("stt") },
        });
        var changes = 0;
        runtime.ProvidersChanged += (_, _) => changes++;

        Assert.IsNull(service.SpeechToText, "nothing to listen with while the model is still downloading");

        listener.Arrive();

        Assert.AreSame(listener, service.SpeechToText);
        Assert.AreEqual(1, changes);
    }
}
