using Nexaflow.Core;
using Nexaflow.Core.AI;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The Image Editing row: a third row of image models that takes only a model that works from pictures — judged by its
/// provider for each model, before any is bound — in the grid, in the starting chains a workspace's setup gives, and in the
/// Local column's picture-editing job; and a grid saved before the row existed starting it on the Local column.
/// </summary>
[TestClass]
[CoversNode("ai-ability-image-editing")]
public class ImageEditingRowTests
{
    private static readonly string Local = LocalColumns.IdOf(LocalSlot.ImageEditing);

    private static ProviderModelPair Images(string id, string model) =>
        new() { Id = id, ProviderName = "Studio", Model = model, Kind = ModelKind.ImageGeneration };

    private static ProviderSet Providers() =>
        new([new Studio()], new Dictionary<string, IAiProvider>(), [], new Dictionary<string, string>(), []);

    [TestMethod]
    public void AGridThatHasNeverHadImageEditing_StartsOnTheLocalColumn_AndOneTheUserEmptied_IsLeftEmpty()
    {
        var fresh = new AiConfig();
        Assert.IsTrue(fresh.AddDefaultImageEditing());
        CollectionAssert.AreEqual(new[] { Local }, fresh.Assignments["ImageEditing"].ColumnIds.ToArray());
        Assert.IsTrue(new AiConfig().BringUpToDate(), "a grid read from an earlier version is brought up to the row");

        var drawing = new AiConfig { Assignments = { ["ImageGeneration"] = AbilityChain.Of("gpt", Local), ["HighQualityImageGeneration"] = AbilityChain.Of("gemini", "gpt") } };
        Assert.IsTrue(drawing.AddDefaultImageEditing());
        CollectionAssert.AreEqual(new[] { Local, "gpt", "gemini" }, drawing.Assignments["ImageEditing"].ColumnIds.ToArray(),
                                  "the picture rows' models go on, each once: those that work from pictures still do");

        var emptied = new AiConfig { Assignments = { ["ImageEditing"] = AbilityChain.Empty } };
        Assert.IsFalse(emptied.AddDefaultImageEditing());
        Assert.AreEqual(0, emptied.Assignments["ImageEditing"].ColumnIds.Count);
    }

    [TestMethod]
    public void OnlyAModelThatWorksFromPictures_ServesImageEditing_AsItsProviderSaysOfEachModel()
    {
        var studio = new Studio();

        Assert.IsTrue(AiAbilityNames.Serves(AiAbility.ImageEditing, studio, "edits"));
        Assert.IsFalse(AiAbilityNames.Serves(AiAbility.ImageEditing, studio, "draws"));
        Assert.IsFalse(AiAbilityNames.Serves(AiAbility.ImageEditing, null, "edits"), "a model whose provider is not known cannot be judged");
        Assert.IsTrue(AiAbilityNames.Serves(AiAbility.ImageGeneration, studio, "draws"), "the drawing rows ask only an image model");
        Assert.AreEqual(ModelKind.ImageGeneration, AiAbilityNames.Kind(AiAbility.ImageEditing));
    }

    [TestMethod]
    public void AModelThatOnlyChangesPictures_ServesImageEditing_AndNeitherDrawingRow()
    {
        var studio = new Studio();

        Assert.IsTrue(AiAbilityNames.Serves(AiAbility.ImageEditing, studio, "only-edits"));
        Assert.IsFalse(AiAbilityNames.Serves(AiAbility.ImageGeneration, studio, "only-edits"), "it draws nothing from a prompt alone");
        Assert.IsFalse(AiAbilityNames.Serves(AiAbility.HighQualityImageGeneration, studio, "only-edits"));
        Assert.IsTrue(AiAbilityNames.Serves(AiAbility.ImageGeneration, studio, "edits"), "a model that does both serves both");
    }

    [TestMethod]
    public void TheGrid_OffersImageEditingOnlyTheColumnsWhoseModelEdits_AndDropsTheRestFromItsChain()
    {
        var config = new AiConfig
        {
            Columns     = [Images("draw", "draws"), Images("edit", "edits")],
            Assignments = { ["ImageEditing"] = AbilityChain.Of("draw", "edit"), ["ImageGeneration"] = AbilityChain.Of("draw", "edit") },
            Providers   = Providers(),
        };

        var grid    = new AiAbilityGridViewModel(config);
        var editing = grid.Rows.Single(r => r.Ability == AiAbility.ImageEditing);

        CollectionAssert.AreEqual(new[] { "edit" }, editing.Chain.ToArray());
        Assert.IsFalse(editing.Cells.Single(c => c.Column.Id == "draw").IsUsable);
        Assert.IsTrue(editing.Cells.Single(c => c.Column.Id == "edit").IsUsable);
        CollectionAssert.AreEqual(new[] { "draw", "edit" }, grid.Rows.Single(r => r.Ability == AiAbility.ImageGeneration).Chain.ToArray(),
                                  "a model that edits draws too");
    }

    [TestMethod]
    public void AModelChosenAtSetup_LeadsImageEditing_OnlyWhereItEdits()
    {
        var config = new AiConfig { Providers = Providers() };

        WorkspaceAiDefaults.AssignKinds(config, [Images("draw", "draws"), Images("edit", "edits")]);

        CollectionAssert.AreEqual(new[] { "edit" }, config.Assignments["ImageEditing"].ColumnIds.ToArray());
        CollectionAssert.AreEqual(new[] { "edit", "draw" }, config.Assignments["ImageGeneration"].ColumnIds.ToArray());
    }

    [TestMethod]
    public void TheImageEditingRow_IsServedByThePictureEditingJob_OfImageModels()
    {
        Assert.AreEqual(LocalSlot.ImageEditing, LocalColumns.SlotOf(AiAbility.ImageEditing));
        Assert.AreEqual(ModelKind.ImageGeneration, LocalColumns.KindOf(LocalSlot.ImageEditing));
        CollectionAssert.AreEqual(new[] { AiAbility.ImageEditing }, LocalColumns.AbilitiesOf(LocalSlot.ImageEditing).ToArray());
    }

    /// <summary>A provider of two image models: one that works from pictures, one that only draws.</summary>
    private sealed class Studio : IImageGenerationProvider
    {
        public string Name => "Studio";
        public DataResidency Residency => DataResidency.LeavesMachine;
        public int MaxSources => 0;
        public int MaxSourcesOf(string model) => model is "edits" or "only-edits" ? 2 : 0;
        public bool DrawsFromPromptsOf(string model) => model != "only-edits";
        public bool Masks => false;
        public ImageSettings Settings => new([new(1, 1)]);
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["draws", "edits"]);
        public Task<GeneratedMedia> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
