using Nexaflow.Core.AI;
using Nexaflow.Features.Common;
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
                ["GeneralIntelligence"]   = AbilityChain.Of("claude", "gemma"),
                ["Disambiguation"] = AbilityChain.Of("gemma", "claude"),
                ["SpeechIn"]       = AbilityChain.Of("nemotron", "whisper"),
            },
        };

        Assert.IsTrue(LocalColumns.Gather(config, OnThisMachine));

        var language = LocalColumns.IdOf(LocalSlot.Language);
        var speech   = LocalColumns.IdOf(LocalSlot.SpeechToText);
        CollectionAssert.AreEquivalent(new[] { "claude", language, speech }, config.Columns.Select(c => c.Id).ToArray());
        CollectionAssert.AreEqual(new[] { "claude", language }, config.Assignments["GeneralIntelligence"].ColumnIds.ToArray());
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
        var config = new AiConfig { Columns = [Column("claude", "Claude", "sonnet", ModelKind.Language)], Assignments = { ["GeneralIntelligence"] = AbilityChain.Of("claude") } };

        var changed = LocalColumns.Seed(config, slot => slot == LocalSlot.Language ? [(gemma, true, false, true), (qwen, true, true, true)] : []);

        Assert.IsTrue(changed);
        Assert.IsTrue(config.LocalSeeded);
        var language = LocalColumns.IdOf(LocalSlot.Language);
        Assert.AreEqual("qwen", config.Columns.Single(c => c.Id == language).Model, "one already here first");
        CollectionAssert.AreEqual(new[] { "claude", language }, config.Assignments["GeneralIntelligence"].ColumnIds.ToArray(), "ticked after what the row had");
        CollectionAssert.AreEqual(new[] { language }, config.Assignments["Disambiguation"].ColumnIds.ToArray());
        Assert.IsFalse(config.Assignments.ContainsKey("SpeechIn"), "a job this machine runs nothing for is left alone");

        config.Assignments["GeneralIntelligence"] = AbilityChain.Of("claude");   // the user takes it out of a row
        Assert.IsFalse(LocalColumns.Seed(config, _ => [(gemma, true, true, true)]), "and it stays out");
    }

    [TestMethod]
    public void HighQualityPictures_StartOnTheModelMadeForThem_EvenWhenOnlyTheEverydayOneIsHere()
    {
        var zImage  = Column("x", "Nexaflow", "z-image-turbo", ModelKind.ImageGeneration);
        var hiDream = Column("x", "Nexaflow", "hidream-o1-image", ModelKind.ImageGeneration);
        var config  = new AiConfig();

        LocalColumns.Seed(config, slot => slot switch
        {
            LocalSlot.ImageGeneration            => [(zImage, true, true, true), (hiDream, true, false, false)],
            LocalSlot.HighQualityImageGeneration => [(zImage, true, true, false), (hiDream, true, false, true)],
            _                                    => [],
        });

        Assert.AreEqual("z-image-turbo", config.Columns.Single(c => c.Id == LocalColumns.IdOf(LocalSlot.ImageGeneration)).Model);
        Assert.AreEqual("hidream-o1-image", config.Columns.Single(c => c.Id == LocalColumns.IdOf(LocalSlot.HighQualityImageGeneration)).Model,
                        "made for the job before one already here");
        Assert.AreEqual(ModelTier.HighQuality, LocalColumns.TierOf(LocalSlot.HighQualityImageGeneration));
    }

    [TestMethod]
    public void EveryAbility_HasAJob_WhoseKindOfModelItTakes()
    {
        foreach (var ability in Enum.GetValues<AiAbility>())
            Assert.AreEqual(AiAbilityNames.Kind(ability), LocalColumns.KindOf(LocalColumns.SlotOf(ability)), ability.ToString());
    }

    [TestMethod]
    public void AJobOnAModelItNoLongerOffers_MovesToItsFirst_PreferringOneThatIsHere()
    {
        var speech = LocalColumns.IdOf(LocalSlot.SpeechToText);
        var config = new AiConfig
        {
            Columns     = [Column(speech, "Whisper", "large-v3-turbo", ModelKind.SpeechToText)],
            Assignments = { ["SpeechIn"] = AbilityChain.Of(speech) },
            LocalSeeded = true,
        };
        var turbo = Column("", "Nexaflow", "nemotron-whisper-turbo", ModelKind.SpeechToText);
        var small = Column("", "Nexaflow", "nemotron-whisper-small", ModelKind.SpeechToText);

        Assert.IsTrue(LocalColumns.Settle(config, slot => slot == LocalSlot.SpeechToText ? [(turbo, true, false, true), (small, true, true, true)] : []));

        Assert.AreEqual(("Nexaflow", "nemotron-whisper-small"), (config.Columns.Single().ProviderName, config.Columns.Single().Model),
                        "whisper.cpp's large-v3-turbo is not offered where Nexaflow's speech models run");
        CollectionAssert.AreEqual(new[] { speech }, config.Assignments["SpeechIn"].ColumnIds.ToArray(), "the row keeps its place for it");
    }

    [TestMethod]
    public void WhereOnlyDictationRuns_NexaflowsSpeechModel_GivesWayToIt()
    {
        var speech = LocalColumns.IdOf(LocalSlot.SpeechToText);
        var config = new AiConfig
        {
            Columns     = [Column(speech, "Nexaflow", "nemotron-whisper-turbo", ModelKind.SpeechToText)],
            Assignments = { ["SpeechIn"] = AbilityChain.Of(speech) },
        };

        Assert.IsTrue(LocalColumns.Settle(config, _ => [(LocalColumns.Dictation, true, false, true)]));

        Assert.IsTrue(LocalColumns.Dictates(config.Columns.Single()));
        Assert.AreEqual(LocalColumns.Dictation.Model, config.Columns.Single().Model);
    }

    [TestMethod]
    public void AJobARowUsesWithNoModelYet_GetsOne_AndAJobOnAModelItOffers_IsLeftAlone()
    {
        var speech   = LocalColumns.IdOf(LocalSlot.SpeechToText);
        var language = LocalColumns.IdOf(LocalSlot.Language);
        var gemma    = Column(language, "Nexaflow", "gemma-4-e4b-it", ModelKind.Language);
        var config   = new AiConfig
        {
            Columns     = [gemma],
            Assignments = { ["SpeechIn"] = AbilityChain.Of(speech), ["GeneralIntelligence"] = AbilityChain.Of(language) },
        };
        var turbo = Column("", "Nexaflow", "nemotron-whisper-turbo", ModelKind.SpeechToText);
        var qwen  = Column("", "Nexaflow", "qwen-3.5-9b", ModelKind.Language);

        Assert.IsTrue(LocalColumns.Settle(config, slot => slot switch
        {
            LocalSlot.SpeechToText => [(turbo, true, false, true)],
            LocalSlot.Language     => [(qwen, true, true, true), (Column("", "Nexaflow", "gemma-4-e4b-it", ModelKind.Language), true, false, true)],
            _                      => [],
        }));

        Assert.AreEqual("nemotron-whisper-turbo", config.Columns.Single(c => c.Id == speech).Model);
        Assert.AreEqual("gemma-4-e4b-it", config.Columns.Single(c => c.Id == language).Model, "a model the job offers stays, even with another here");
        Assert.IsFalse(LocalColumns.Settle(config, _ => []), "a job that offers nothing here is left as it is");
    }
}
