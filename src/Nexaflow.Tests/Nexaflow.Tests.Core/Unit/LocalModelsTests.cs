using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Nexaflow.Core;
using Nexaflow.Core.AI;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Core.ViewModels;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The models on this machine in a workspace's setup: the grid's Local column, always first, its cells greyed where there is
/// nothing to use; the Local page choosing a model for each job — at once when it is here, after downloading it when not,
/// the job keeping its model meanwhile; and the downloaded-models page, which keeps what a workspace uses.
/// </summary>
[TestClass]
[CoversNode("ai-local-models")]
public class LocalModelsTests
{
    private static ModelAsset Asset(string path, byte[] bytes)
        => new(path, new Uri($"https://example.test/{path}"), bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes)));

    private static byte[] Bytes(int length, int seed) => [.. Enumerable.Range(0, length).Select(i => (byte)(i * seed))];

    private static readonly Dictionary<string, byte[]> Served = new()
    {
        ["whisper/small.bin"]  = Bytes(4_000, 1),
        ["whisper/large.bin"]  = Bytes(9_000, 3),
        ["models/e4b.gguf"]    = Bytes(6_000, 5),
        ["models/big.gguf"]    = Bytes(7_000, 7),
        ["runtime/cuda.dll"]   = Bytes(3_000, 11),
    };

    private static CatalogModel Model(string id, string path) => new(id, id.ToUpperInvariant(), [Asset(path, Served[path])]);

    private static readonly CatalogModel Small = Model("small", "whisper/small.bin");
    private static readonly CatalogModel Large = Model("large", "whisper/large.bin");
    private static readonly CatalogModel E4b   = Model("e4b", "models/e4b.gguf");
    private static readonly CatalogModel Big   = Model("big", "models/big.gguf");
    private static readonly LocalRuntime Cuda  = new("cuda", "CUDA runtime", [Asset("runtime/cuda.dll", Served["runtime/cuda.dll"])]);

    private sealed class Gemma : ILlmProvider, ILocalModelCatalog
    {
        public string Name => "Nexaflow";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public ModelHarness Harness => ModelHarness.Owned;
        public IReadOnlyList<CatalogModel> Catalog => [E4b];
        public IReadOnlyList<UnrunnableModel> Unrunnable => [new(Big, UnrunnableReason.TooLargeForThisMachine)];
        public LocalRuntime? RuntimeOf(string modelId) => Cuda;
        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["e4b"]);
    }

    private sealed class Whisper : ISpeechToTextProvider, ILocalModelCatalog
    {
        public string Name => "Whisper";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public ModelHarness Harness => ModelHarness.Owned;
        public IReadOnlyList<CatalogModel> Catalog => [Small, Large];
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<ISpeechToTextSession> StartSessionAsync(SpeechToTextOptions options, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Claude : ILlmProvider
    {
        public string Name => "Claude";
        public DataResidency Residency => DataResidency.LeavesMachine;
        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["sonnet"]);
    }

    /// <summary>Serves each file's bytes — but for those named in <see cref="Broken"/>, which arrive as something else.</summary>
    private sealed class Server : HttpMessageHandler
    {
        public readonly HashSet<string> Broken = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            var bytes = Broken.Contains(path) ? Bytes(Served[path].Length, 13) : Served[path];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }

    private string _root = null!;
    private Server _server = null!;
    private ModelAssetStore _store = null!;
    private int _saves;

    [TestInitialize]
    public void Init()
    {
        _root   = Path.Combine(Path.GetTempPath(), "nexaflow-test-" + Guid.NewGuid().ToString("N"));
        _server = new Server();
        _store  = new ModelAssetStore(_root, [], new HttpClient(_server));
    }

    [TestCleanup] public void Done() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private static readonly string SpeechIn = LocalColumns.IdOf(LocalSlot.SpeechToText);
    private static readonly string Language = LocalColumns.IdOf(LocalSlot.Language);

    /// <summary>A workspace conversing on Claude and listening with Whisper's small model, on the Local column.</summary>
    private (AiAbilityGridViewModel Grid, AiConfig Config, LocalModelShelf Shelf) Open()
    {
        var providers = new ProviderSet([new Gemma(), new Whisper(), new Claude()], new Dictionary<string, IAiProvider>(), [],
            new Dictionary<string, string> { ["Nexaflow"] = "Nexaflow.Providers.AiHost.dll", ["Whisper"] = "Nexaflow.Providers.Whisper.dll", ["Claude"] = "Nexaflow.Providers.Claude.dll" }, []);
        var config = new AiConfig
        {
            Providers = providers,
            Columns =
            [
                new ProviderModelPair { Id = "c", ProviderName = "Claude", Model = "sonnet", Kind = ModelKind.Language },
                new ProviderModelPair { Id = SpeechIn, ProviderName = "Whisper", Model = "small", Kind = ModelKind.SpeechToText },
            ],
            Assignments = { ["Conversation"] = AbilityChain.Of("c"), ["SpeechIn"] = AbilityChain.Of(SpeechIn) },
        };
        var shelf = new LocalModelShelf(providers, _store);
        return (new AiAbilityGridViewModel(config, shelf), config, shelf);
    }

    private static AiAbilityCellViewModel LocalCell(AiAbilityGridViewModel grid, AiAbility ability)
        => grid.Rows.Single(r => r.Ability == ability).Cells.Single(c => c.Column.IsLocal);

    private LocalJobViewModel Job(LocalModelsViewModel page, LocalSlot slot) => page.Jobs.Single(j => j.Slot == slot);

    private static async Task Settled(LocalJobViewModel job)
    {
        for (var i = 0; i < 200 && job.IsDownloading; i++) await Task.Delay(25);
        Assert.IsFalse(job.IsDownloading, "the download ended within five seconds");
    }

    // ── The grid's Local column ───────────────────────────────────────────

    [TestMethod]
    public void TheLocalColumn_ComesFirst_AndCannotBeRemoved()
    {
        var (grid, _, _) = Open();

        Assert.IsTrue(grid.Columns[0].IsLocal);
        Assert.IsFalse(grid.RemoveColumnCommand.CanExecute(grid.Columns[0]));
        Assert.AreEqual(1, grid.Columns.Count(c => c.IsLocal), "one column for every job's model");
    }

    [TestMethod]
    public void ALocalCell_IsGreyed_WhereThereIsNothingToUse_AndSaysWhy()
    {
        var (grid, _, _) = Open();

        var video = LocalCell(grid, AiAbility.VideoGeneration);
        Assert.IsTrue(video.IsGreyed, "no model for video runs here");
        Assert.AreEqual(Nexaflow.Visuals.Common.Localization.Str.Get("Shell.Config.AiGrid.Local.None"), video.Note);

        var conversation = LocalCell(grid, AiAbility.Conversation);
        Assert.IsTrue(conversation.IsGreyed, "a model runs here, but none is chosen");
        Assert.AreEqual(Nexaflow.Visuals.Common.Localization.Str.Get("Shell.Config.AiGrid.Local.Choose"), conversation.Note);

        Assert.IsTrue(LocalCell(grid, AiAbility.SpeechIn).IsUsable);
    }

    [TestMethod]
    public void ALocalCell_PutsItsJobsModelInTheRow()
    {
        var (grid, _, _) = Open();
        var cell = LocalCell(grid, AiAbility.SpeechIn);
        Assert.IsTrue(cell.IsInChain);

        cell.ToggleCommand.Execute(null);
        Assert.IsFalse(grid.Rows.Single(r => r.Ability == AiAbility.SpeechIn).Chain.Contains(SpeechIn));

        cell.ToggleCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { SpeechIn }, grid.Rows.Single(r => r.Ability == AiAbility.SpeechIn).Chain.ToArray());
    }

    [TestMethod]
    public void ModelsOnThisMachine_AreTheLocalColumns_NeverAProviderToAdd()
    {
        var (grid, _, _) = Open();

        grid.OpenAddPanelCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "Claude" }, grid.AvailableOffers.Select(o => o.ProviderName).Distinct().ToArray());
    }

    // ── The Local page ────────────────────────────────────────────────────

    [TestMethod]
    public async Task EachJob_ListsItsModels_HereToDownloadOrNotForThisMachine()
    {
        await _store.EnsureAsync(Small.Files[0]);
        var (grid, _, shelf) = Open();
        var page = new LocalModelsViewModel(shelf, grid, () => _saves++, activities: null);

        var language = Job(page, LocalSlot.Language);
        CollectionAssert.AreEqual(new[] { "E4B", "BIG" }, language.Choices.Select(c => c.DisplayName).ToArray(), "those that run come first");
        Assert.IsFalse(language.Choices[0].IsHere);
        Assert.IsFalse(language.Choices[1].Runs, "a model too large for this machine is listed, never chosen");
        Assert.IsNull(language.Selected);

        var speech = Job(page, LocalSlot.SpeechToText);
        Assert.AreEqual("small", speech.Selected!.Option.Id);
        Assert.IsTrue(speech.Selected.IsHere);

        Assert.IsFalse(Job(page, LocalSlot.VideoGeneration).CanChoose);
        Assert.IsNotNull(Job(page, LocalSlot.VideoGeneration).Note);
    }

    [TestMethod]
    public async Task AModelThatIsHere_TakesEffectAtOnce_AndIsSaved()
    {
        await _store.EnsureAsync(Large.Files[0]);
        var (grid, config, shelf) = Open();
        var page = new LocalModelsViewModel(shelf, grid, () => _saves++, activities: null);
        var speech = Job(page, LocalSlot.SpeechToText);

        speech.Selected = speech.Choices.Single(c => c.Option.Id == "large");

        Assert.AreEqual("large", grid.LocalModelFor(LocalSlot.SpeechToText)!.Model);
        Assert.AreEqual("large", config.Columns.Single(c => c.Id == SpeechIn).Model, "written into the config, not left for Apply");
        Assert.AreEqual(1, _saves);
        Assert.IsFalse(grid.HasChanges);
    }

    [TestMethod]
    public async Task AModelThatIsNotHere_IsDownloadedFirst_WithWhatItShares_ThenItsJobsRowsUseIt()
    {
        var (grid, config, shelf) = Open();
        var page = new LocalModelsViewModel(shelf, grid, () => _saves++, activities: null);
        var language = Job(page, LocalSlot.Language);

        language.Selected = language.Choices.Single(c => c.Option.Id == "e4b");
        await Settled(language);

        Assert.IsNotNull(_store.Locate(E4b.Files[0]));
        Assert.IsNotNull(_store.Locate(Cuda.Files[0]), "with the runtime it needs");
        Assert.AreEqual("e4b", grid.LocalModelFor(LocalSlot.Language)!.Model);
        CollectionAssert.AreEqual(new[] { "c", Language }, config.Assignments["Conversation"].ColumnIds.ToArray(),
            "a job no row used the Local column for starts using it, after what each row had");
        Assert.IsTrue(LocalCell(grid, AiAbility.Analysis).IsInChain);
        Assert.AreEqual(1, _saves);
    }

    [TestMethod]
    public async Task ADownloadThatFails_LeavesTheJobsModel_AndSaysWhy()
    {
        _server.Broken.Add("whisper/large.bin");
        var (grid, _, shelf) = Open();
        var page = new LocalModelsViewModel(shelf, grid, () => _saves++, activities: null);
        var speech = Job(page, LocalSlot.SpeechToText);

        speech.Selected = speech.Choices.Single(c => c.Option.Id == "large");
        await Settled(speech);

        Assert.AreEqual("small", grid.LocalModelFor(LocalSlot.SpeechToText)!.Model);
        Assert.AreEqual("small", speech.Selected!.Option.Id);
        Assert.IsNotNull(speech.Status);
        Assert.AreEqual(0, _saves);
    }

    // ── Downloaded models ─────────────────────────────────────────────────

    [TestMethod]
    public async Task WhatIsOnDisk_IsListedWithWhoUsesIt_AndOnlyWhatNothingUsesCanGo()
    {
        await _store.EnsureAllAsync([Small.Files[0], E4b.Files[0], Cuda.Files[0]]);
        var (_, config, shelf) = Open();
        var page = new DownloadedModelsViewModel(shelf, () => [("Writing", config)]);

        CollectionAssert.AreEquivalent(new[] { "SMALL", "E4B", "CUDA runtime" }, page.Models.Select(m => m.Name).ToArray());
        var small = page.Models.Single(m => m.Name == "SMALL");
        Assert.IsTrue(small.InUse);
        Assert.IsFalse(small.DeleteCommand.CanExecute(null), "Speech In listens with it");
        CollectionAssert.AreEqual(new[] { "Writing" }, small.Users.ToArray());

        await page.Models.Single(m => m.Name == "E4B").DeleteCommand.ExecuteAsync(null);

        Assert.IsNull(_store.Locate(E4b.Files[0]));
        CollectionAssert.AreEquivalent(new[] { "SMALL", "CUDA runtime" }, page.Models.Select(m => m.Name).ToArray());
        Assert.IsTrue(page.Models.Single(m => m.Name == "CUDA runtime").DeleteCommand.CanExecute(null), "no model in use needs it");
    }
}
