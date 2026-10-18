using Nexaflow.Core;
using Nexaflow.Core.AI;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The ability grid: each row's chain of models, built by clicking cells, and what it saves.
/// </summary>
[TestClass]
[CoversNode("opt-ai-ability-grid")]
public class AiAbilityGridViewModelTests
{
    private static ProviderModelPair Column(string id, string provider = "Prov")
        => new() { Id = id, ProviderName = provider, Model = "m" };

    private static AiAbilityRowViewModel GeneralIntelligence(AiAbilityGridViewModel grid)
        => grid.Rows.Single(r => r.Ability == AiAbility.GeneralIntelligence);

    private static AiAbilityCellViewModel Cell(AiAbilityRowViewModel row, string columnId)
        => row.Cells.Single(c => c.Column.Id == columnId);

    [TestMethod]
    public void ClickingCells_BuildsTheChainInTheOrderClicked_AndApplySavesIt()
    {
        var config = new AiConfig { Columns = [Column("a"), Column("b")] };
        var grid   = new AiAbilityGridViewModel(config);
        var row    = GeneralIntelligence(grid);

        Cell(row, "b").ToggleCommand.Execute(null);
        Cell(row, "a").ToggleCommand.Execute(null);

        Assert.AreEqual(1, Cell(row, "b").Position);
        Assert.AreEqual(2, Cell(row, "a").Position);
        Assert.IsTrue(grid.HasChanges);

        grid.Apply();

        CollectionAssert.AreEqual(new[] { "b", "a" }, config.Assignments["GeneralIntelligence"].ColumnIds.ToArray());
        Assert.IsFalse(grid.HasChanges, "the grid now matches what it saved");
    }

    [TestMethod]
    public void TakingAModelOut_MovesTheOnesAfterItUp()
    {
        var config = new AiConfig
        {
            Columns     = [Column("a"), Column("b"), Column("c")],
            Assignments = { ["GeneralIntelligence"] = AbilityChain.Of("a", "b", "c") },
        };
        var row = GeneralIntelligence(new AiAbilityGridViewModel(config));

        Cell(row, "a").ToggleCommand.Execute(null);

        Assert.AreEqual(0, Cell(row, "a").Position);
        Assert.AreEqual(1, Cell(row, "b").Position);
        Assert.AreEqual(2, Cell(row, "c").Position);
    }

    [TestMethod]
    public void RemovingAColumn_TakesItOutOfEveryChain()
    {
        var config = new AiConfig
        {
            Columns     = [Column("a"), Column("b")],
            Assignments = { ["GeneralIntelligence"] = AbilityChain.Of("a", "b"), ["ImageRecognition"] = AbilityChain.Of("a") },
        };
        var grid = new AiAbilityGridViewModel(config);

        grid.RemoveColumnCommand.Execute(grid.Columns.Single(c => c.Id == "a"));
        grid.Apply();

        CollectionAssert.AreEqual(new[] { "b" }, config.Assignments["GeneralIntelligence"].ColumnIds.ToArray());
        Assert.IsTrue(config.Assignments["ImageRecognition"].IsEmpty);
    }

    [TestMethod]
    public void AChainNamingAColumnThatIsGone_KeepsTheRest()
    {
        var config = new AiConfig
        {
            Columns     = [Column("b")],
            Assignments = { ["GeneralIntelligence"] = AbilityChain.Of("gone", "b") },
        };
        var row = GeneralIntelligence(new AiAbilityGridViewModel(config));

        CollectionAssert.AreEqual(new[] { "b" }, row.Chain.ToArray());
    }

    [TestMethod]
    public void AColumnWhoseDataStaysOnThisMachine_IsMarked()
    {
        var config = new AiConfig
        {
            Columns   = [Column("here", "Local"), Column("away", "Cloud")],
            Providers = new ProviderSet(
                [new Residing("Local", DataResidency.StaysOnMachine), new Residing("Cloud", DataResidency.LeavesMachine)],
                new Dictionary<string, IAiProvider>(), [], new Dictionary<string, string>(), []),
        };
        var grid = new AiAbilityGridViewModel(config);

        Assert.IsTrue(grid.Columns.Single(c => c.Id == "here").StaysOnMachine);
        Assert.IsFalse(grid.Columns.Single(c => c.Id == "away").StaysOnMachine);
    }

    [TestMethod]
    public void ARowTakesOnlyColumnsOfItsKindOfModel()
    {
        var config = new AiConfig
        {
            Columns     = [Column("llm"), new ProviderModelPair { Id = "stt", ProviderName = "Prov", Model = "t", Kind = ModelKind.SpeechToText }],
            Assignments = { ["SpeechIn"] = AbilityChain.Of("llm", "stt") },
        };
        var grid     = new AiAbilityGridViewModel(config);
        var speechIn = grid.Rows.Single(r => r.Ability == AiAbility.SpeechIn);

        CollectionAssert.AreEqual(new[] { "stt" }, speechIn.Chain.ToArray(), "a language model cannot hear");
        Assert.IsFalse(Cell(speechIn, "llm").IsUsable);
        Assert.IsFalse(Cell(speechIn, "llm").ToggleCommand.CanExecute(null));
        Assert.IsTrue(Cell(GeneralIntelligence(grid), "llm").IsUsable);
        Assert.IsFalse(Cell(GeneralIntelligence(grid), "stt").IsUsable);
    }

    [TestMethod]
    public void TheGenerationRows_TakeOnlyModelsThatMakeTheirKindOfThing()
    {
        var config = new AiConfig
        {
            Columns =
            [
                Column("llm"),
                new ProviderModelPair { Id = "draw", ProviderName = "Prov", Model = "i", Kind = ModelKind.ImageGeneration },
                new ProviderModelPair { Id = "film", ProviderName = "Prov", Model = "v", Kind = ModelKind.VideoGeneration },
            ],
            Assignments =
            {
                ["ImageGeneration"] = AbilityChain.Of("llm", "draw", "film"),
                ["VideoGeneration"] = AbilityChain.Of("draw", "film"),
                ["HighQualityImageGeneration"] = AbilityChain.Of("film", "draw"),
            },
        };
        var grid = new AiAbilityGridViewModel(config);
        CollectionAssert.AreEqual(new[] { "draw" }, grid.Rows.Single(r => r.Ability == AiAbility.HighQualityImageGeneration).Chain.ToArray(),
                                  "the high-quality row takes image models, as the standard one does");

        CollectionAssert.AreEqual(new[] { "draw" }, grid.Rows.Single(r => r.Ability == AiAbility.ImageGeneration).Chain.ToArray());
        CollectionAssert.AreEqual(new[] { "film" }, grid.Rows.Single(r => r.Ability == AiAbility.VideoGeneration).Chain.ToArray());
        Assert.IsFalse(Cell(GeneralIntelligence(grid), "draw").IsUsable, "a model that draws does not converse");
    }

    private sealed class Residing(string name, DataResidency residency) : ILlmProvider
    {
        public string Name => name;
        public DataResidency Residency => residency;
        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
            => AsyncEnumerable.Empty<LlmStreamEvent>();
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }

    [TestMethod]
    public void ARowWithNothingAssigned_IsMarkedDisabled_UntilACellIsClicked_AndAgainWhenItIsClickedOff()
    {
        var config = new AiConfig { Columns = [Column("a")] };
        var row    = GeneralIntelligence(new AiAbilityGridViewModel(config));
        Assert.IsTrue(row.IsUnassigned);

        Cell(row, "a").ToggleCommand.Execute(null);
        Assert.IsFalse(row.IsUnassigned);

        Cell(row, "a").ToggleCommand.Execute(null);
        Assert.IsTrue(row.IsUnassigned, "clicking a model the row has takes it out");
    }
}
