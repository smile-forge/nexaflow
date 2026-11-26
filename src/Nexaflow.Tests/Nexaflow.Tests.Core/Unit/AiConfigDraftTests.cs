using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// An edit of the ability grid: a saved chain keeps only columns that exist and serve its ability; cells clicked build a chain in
/// order; a removed column leaves every chain; a Local model chosen joins every row of its job that does not use it yet, and is
/// written at once; a column can be added only for a provider set up and not Nexaflow's own; and nothing else reaches the config
/// until it is applied.
/// </summary>
[TestClass]
[CoversNode("opt-ai-ability-grid")]
public class AiConfigDraftTests
{
    private static ProviderModelPair Column(string id, ModelKind kind = ModelKind.Language) => new() { Id = id, ProviderName = "Prov", Model = "m", Kind = kind };

    private sealed class Provider(string name, ModelHarness harness, params ModelKind[] kinds) : ILlmProvider
    {
        public string Name => name;
        public DataResidency Residency => DataResidency.LeavesMachine;
        public ModelHarness Harness => harness;
        public bool Serves(ModelKind kind) => kinds.Contains(kind);
        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default) => AsyncEnumerable.Empty<LlmStreamEvent>();
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class Keyed(string name, string key) : IProviderConfig
    {
        public string ConfigName   => name;
        public string FriendlyName => name;
        [System.ComponentModel.DataAnnotations.Required] public string ApiKey { get; set; } = key;
    }

    [TestMethod]
    public void ASavedChain_KeepsOnlyColumnsThatExist_AndServeItsAbility()
    {
        var config = new AiConfig
        {
            Columns     = [Column("llm"), Column("stt", ModelKind.SpeechToText)],
            Assignments = { ["GeneralIntelligence"] = AbilityChain.Of("gone", "llm", "stt"), ["SpeechIn"] = AbilityChain.Of("llm", "stt") },
        };
        var draft = new AiConfigDraft(config);

        CollectionAssert.AreEqual(new[] { "llm" }, draft.ChainOf(AiAbility.GeneralIntelligence).ToArray());
        CollectionAssert.AreEqual(new[] { "stt" }, draft.ChainOf(AiAbility.SpeechIn).ToArray(), "a language model cannot hear");
    }

    [TestMethod]
    public void Toggling_BuildsTheChainInOrder_AndTakesAColumnOutAgain_ButOnlyApplyWritesIt()
    {
        var config = new AiConfig { Columns = [Column("a"), Column("b")] };
        var draft  = new AiConfigDraft(config);

        draft.Toggle(AiAbility.GeneralIntelligence, "b");
        draft.Toggle(AiAbility.GeneralIntelligence, "a");
        Assert.IsTrue(draft.HasChanges);
        Assert.IsFalse(config.Assignments.ContainsKey("GeneralIntelligence"), "nothing is written until it is applied");

        draft.Apply();
        CollectionAssert.AreEqual(new[] { "b", "a" }, config.Assignments["GeneralIntelligence"].ColumnIds.ToArray());
        Assert.IsFalse(draft.HasChanges);

        draft.Toggle(AiAbility.GeneralIntelligence, "b");
        CollectionAssert.AreEqual(new[] { "a" }, draft.ChainOf(AiAbility.GeneralIntelligence).ToArray());
    }

    [TestMethod]
    public void ARemovedColumn_LeavesEveryChain()
    {
        var config = new AiConfig
        {
            Columns     = [Column("a"), Column("b")],
            Assignments = { ["GeneralIntelligence"] = AbilityChain.Of("a", "b"), ["Disambiguation"] = AbilityChain.Of("a") },
        };
        var draft = new AiConfigDraft(config);

        draft.RemoveColumn("a");

        CollectionAssert.AreEqual(new[] { "b" }, draft.ChainOf(AiAbility.GeneralIntelligence).ToArray());
        Assert.AreEqual(0, draft.ChainOf(AiAbility.Disambiguation).Count);
        Assert.IsFalse(draft.Columns.Any(c => c.Id == "a"));
    }

    [TestMethod]
    public void ALocalModelChosen_JoinsEveryRowOfItsJob_AtTheEnd_AndIsWrittenAtOnce()
    {
        var config = new AiConfig
        {
            Columns     = [Column("cloud")],
            Assignments = { ["GeneralIntelligence"] = AbilityChain.Of("cloud") },
        };
        var draft = new AiConfigDraft(config);
        var local = LocalColumns.IdOf(LocalSlot.Language);

        draft.ChooseLocal(LocalSlot.Language, new ProviderModelPair { ProviderName = "Nexaflow", Model = "gemma", Kind = ModelKind.Language });

        CollectionAssert.AreEqual(new[] { "cloud", local }, draft.ChainOf(AiAbility.GeneralIntelligence).ToArray());
        Assert.IsTrue(draft.ChainOf(AiAbility.Disambiguation).Contains(local), "every row of the job takes it");
        Assert.AreEqual("gemma", config.Columns.Single(c => c.Id == local).Model, "a Local choice takes effect at once");
    }

    [TestMethod]
    public void ALocalModelChosenAgain_LeavesTheRowsAsTheUserArrangedThem()
    {
        var draft = new AiConfigDraft(new AiConfig());
        var local = LocalColumns.IdOf(LocalSlot.Language);
        draft.ChooseLocal(LocalSlot.Language, new ProviderModelPair { ProviderName = "Nexaflow", Model = "gemma", Kind = ModelKind.Language });
        draft.Toggle(AiAbility.Disambiguation, local);

        draft.ChooseLocal(LocalSlot.Language, new ProviderModelPair { ProviderName = "Nexaflow", Model = "qwen", Kind = ModelKind.Language });

        Assert.IsFalse(draft.ChainOf(AiAbility.Disambiguation).Contains(local), "a job already used keeps the rows the user took it out of");
        Assert.AreEqual("qwen", draft.LocalModelFor(LocalSlot.Language)?.Model);
    }

    [TestMethod]
    public void AColumnCanBeAdded_OnlyForAProviderSetUp_AndNotNexaflowsOwn()
    {
        var providers = new ProviderSet(
            [new Provider("Cloud", ModelHarness.Bridged, ModelKind.Language), new Provider("Keyless", ModelHarness.Bridged, ModelKind.Language),
             new Provider("Nexaflow", ModelHarness.Owned, ModelKind.Language)],
            new Dictionary<string, IAiProvider>(), [new Keyed("Cloud", "sk-1"), new Keyed("Keyless", "")], new Dictionary<string, string>(), []);

        var offers = new AiConfigDraft(new AiConfig(), providers).Offers();

        CollectionAssert.AreEqual(new[] { ("Cloud", ModelKind.Language) }, offers.ToArray());
    }
}
