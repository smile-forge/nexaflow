using Nexaflow.Core.AI;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A grid saved before the Local column: its columns of models on this machine fold into the Local column's job by job — the
/// first of a job's models in each chain taking the job's place, the rest leaving — and other columns stay as they were.
/// </summary>
[TestClass]
[CoversNode("ai-local-models")]
public class LocalColumnsTests
{
    private static ProviderModelPair Column(string id, string provider, string model, ModelKind kind)
        => new() { Id = id, ProviderName = provider, Model = model, Kind = kind };

    private static bool OnThisMachine(ProviderModelPair column) => column.ProviderName is "Nexaflow" or "Whisper";

    [TestMethod]
    public void ColumnsOfModelsOnThisMachine_FoldIntoTheirJobs_InTheirPlaces()
    {
        var config = new AiConfig
        {
            Columns =
            [
                Column("claude", "Claude", "sonnet", ModelKind.Language),
                Column("gemma", "Nexaflow", "gemma-4-e4b-it", ModelKind.Language),
                Column("nemotron", "Nexaflow", "nemotron", ModelKind.SpeechToText),
                Column("whisper", "Whisper", "base.en", ModelKind.SpeechToText),
            ],
            Assignments =
            {
                ["Conversation"]   = AbilityChain.Of("claude", "gemma"),
                ["Disambiguation"] = AbilityChain.Of("gemma", "claude"),
                ["SpeechIn"]       = AbilityChain.Of("nemotron", "whisper"),
            },
        };

        Assert.IsTrue(LocalColumns.Gather(config, OnThisMachine));

        var language = LocalColumns.IdOf(LocalSlot.Language);
        var speech   = LocalColumns.IdOf(LocalSlot.SpeechToText);
        CollectionAssert.AreEquivalent(new[] { "claude", language, speech }, config.Columns.Select(c => c.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "claude", language }, config.Assignments["Conversation"].ColumnIds.ToArray());
        CollectionAssert.AreEqual(new[] { language, "claude" }, config.Assignments["Disambiguation"].ColumnIds.ToArray());
        CollectionAssert.AreEqual(new[] { speech }, config.Assignments["SpeechIn"].ColumnIds.ToArray(), "a job holds one model here");
        Assert.AreEqual("nemotron", config.Columns.Single(c => c.Id == speech).Model, "the first of them");
        Assert.AreEqual("gemma-4-e4b-it", config.Columns.Single(c => c.Id == language).Model);
    }

    [TestMethod]
    public void AGridAlreadyFolded_IsLeftAlone()
    {
        var speech = LocalColumns.IdOf(LocalSlot.SpeechToText);
        var config = new AiConfig
        {
            Columns     = [Column(speech, "Whisper", "base.en", ModelKind.SpeechToText)],
            Assignments = { ["SpeechIn"] = AbilityChain.Of(speech) },
        };

        Assert.IsFalse(LocalColumns.Gather(config, OnThisMachine));
    }

    [TestMethod]
    public void TheLocalColumn_IsTurnedOnOnce_ForEveryJobThisMachineRunsAModelFor_PreferringOneThatIsHere()
    {
        var gemma  = Column("x", "Nexaflow", "gemma", ModelKind.Language);
        var qwen   = Column("x", "Nexaflow", "qwen", ModelKind.Language);
        var config = new AiConfig { Columns = [Column("claude", "Claude", "sonnet", ModelKind.Language)], Assignments = { ["Conversation"] = AbilityChain.Of("claude") } };

        var changed = LocalColumns.Seed(config, slot => slot == LocalSlot.Language ? [(gemma, true, false), (qwen, true, true)] : []);

        Assert.IsTrue(changed);
        Assert.IsTrue(config.LocalSeeded);
        var language = LocalColumns.IdOf(LocalSlot.Language);
        Assert.AreEqual("qwen", config.Columns.Single(c => c.Id == language).Model, "one already here first");
        CollectionAssert.AreEqual(new[] { "claude", language }, config.Assignments["Conversation"].ColumnIds.ToArray(), "ticked after what the row had");
        CollectionAssert.AreEqual(new[] { language }, config.Assignments["Disambiguation"].ColumnIds.ToArray());
        Assert.IsFalse(config.Assignments.ContainsKey("SpeechIn"), "a job this machine runs nothing for is left alone");

        config.Assignments["Conversation"] = AbilityChain.Of("claude");   // the user takes it out of a row
        Assert.IsFalse(LocalColumns.Seed(config, _ => [(gemma, true, true)]), "and it stays out");
    }

    [TestMethod]
    public void EveryAbility_HasAJob_WhoseKindOfModelItTakes()
    {
        foreach (var ability in Enum.GetValues<AiAbility>())
            Assert.AreEqual(AiAbilityNames.Kind(ability), LocalColumns.KindOf(LocalColumns.SlotOf(ability)), ability.ToString());
    }
}
