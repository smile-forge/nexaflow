using Nexaflow.Core;
using Nexaflow.Core.AI;
using Nexaflow.Core.ViewModels;
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

    private static AiAbilityRowViewModel Conversation(AiAbilityGridViewModel grid)
        => grid.Rows.Single(r => r.Ability == AiAbility.Conversation);

    private static AiAbilityCellViewModel Cell(AiAbilityRowViewModel row, string columnId)
        => row.Cells.Single(c => c.Column.Id == columnId);

    [TestMethod]
    public void ClickingCells_BuildsTheChainInTheOrderClicked_AndApplySavesIt()
    {
        var config = new AiConfig { Columns = [Column("a"), Column("b")] };
        var grid   = new AiAbilityGridViewModel(config);
        var row    = Conversation(grid);

        Cell(row, "b").ToggleCommand.Execute(null);
        Cell(row, "a").ToggleCommand.Execute(null);

        Assert.AreEqual(1, Cell(row, "b").Position);
        Assert.AreEqual(2, Cell(row, "a").Position);
        Assert.IsTrue(grid.HasChanges);

        grid.Apply();

        CollectionAssert.AreEqual(new[] { "b", "a" }, config.Assignments["Conversation"].ColumnIds.ToArray());
        Assert.IsFalse(grid.HasChanges, "the grid now matches what it saved");
    }

    [TestMethod]
    public void TakingAModelOut_MovesTheOnesAfterItUp()
    {
        var config = new AiConfig
        {
            Columns     = [Column("a"), Column("b"), Column("c")],
            Assignments = { ["Conversation"] = AbilityChain.Of("a", "b", "c") },
        };
        var row = Conversation(new AiAbilityGridViewModel(config));

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
            Assignments = { ["Conversation"] = AbilityChain.Of("a", "b"), ["Analysis"] = AbilityChain.Of("a") },
        };
        var grid = new AiAbilityGridViewModel(config);

        grid.RemoveColumnCommand.Execute(grid.Columns.Single(c => c.Id == "a"));
        grid.Apply();

        CollectionAssert.AreEqual(new[] { "b" }, config.Assignments["Conversation"].ColumnIds.ToArray());
        Assert.IsTrue(config.Assignments["Analysis"].IsEmpty);
    }

    [TestMethod]
    public void AChainNamingAColumnThatIsGone_KeepsTheRest()
    {
        var config = new AiConfig
        {
            Columns     = [Column("b")],
            Assignments = { ["Conversation"] = AbilityChain.Of("gone", "b") },
        };
        var row = Conversation(new AiAbilityGridViewModel(config));

        CollectionAssert.AreEqual(new[] { "b" }, row.Chain.ToArray());
    }

    [TestMethod]
    public void AColumnWhoseDataStaysOnThisMachine_IsMarked()
    {
        var config = new AiConfig
        {
            Columns   = [Column("here", "Local"), Column("away", "Cloud")],
            Providers = new ProviderSet(
                new Dictionary<string, ILlmProvider>
                {
                    ["Local"] = new Residing(DataResidency.StaysOnMachine),
                    ["Cloud"] = new Residing(DataResidency.LeavesMachine),
                },
                new Dictionary<string, ILlmProvider>(), [], new Dictionary<string, string>(), []),
        };
        var grid = new AiAbilityGridViewModel(config);

        Assert.IsTrue(grid.Columns.Single(c => c.Id == "here").StaysOnMachine);
        Assert.IsFalse(grid.Columns.Single(c => c.Id == "away").StaysOnMachine);
    }

    private sealed class Residing(DataResidency residency) : ILlmProvider
    {
        public string Name => "r";
        public DataResidency Residency => residency;
        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default)
            => AsyncEnumerable.Empty<LlmStreamEvent>();
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
    }
}
