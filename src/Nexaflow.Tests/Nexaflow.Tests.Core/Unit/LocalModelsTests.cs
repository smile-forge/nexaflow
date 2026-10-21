using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Windows.Threading;
using Nexaflow.Core;
using Nexaflow.Core.AI;
using Nexaflow.Core.Models;
using Nexaflow.Core.Services;
using Nexaflow.Core.Services.LocalAi;
using Nexaflow.Core.ViewModels;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;
using Str = Nexaflow.Visuals.Common.Localization.Str;
using Nexaflow.Core.Services.Tools;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Common.Formatting;
using Nexaflow.Visuals.Common.Localization;


namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The models on this machine in a workspace's setup: the grid's Local column, always first — ticked where a model is chosen,
/// marked where it is not downloaded yet, with the grid offering to download them all, or saying the machine runs none; the
/// Local page choosing a model for each job — at once when it is here, after downloading it when not, each model downloaded
/// once however many jobs chose it, a download still under way when the page opens again, with how long is left; and the
/// downloaded-models page, which keeps what a workspace uses.
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
        ["whisper/small.bin"] = Bytes(4_000, 1),
        ["whisper/large.bin"] = Bytes(9_000, 3),
        ["whisper/base.bin"]  = Bytes(2_000, 15),
        ["whisper/ggml-large.bin"] = Bytes(2_500, 17),
        ["models/e4b.gguf"]   = Bytes(6_000, 5),
        ["models/big.gguf"]   = Bytes(7_000, 7),
        ["models/z.onnx"]     = Bytes(5_000, 9),
        ["runtime/cuda.dll"]  = Bytes(3_000, 11),
        ["models/clip/encoder.onnx"] = Bytes(6_000, 19),
        ["models/clip/fast.onnx"]    = Bytes(1_000, 21),
        ["models/clip/full.onnx"]    = Bytes(1_000, 23),
        ["tools/picture/upscaler.onnx"] = Bytes(2_000, 25),
    };

    private static CatalogModel Model(string id, string path) => new(id, id.ToUpperInvariant(), [Asset(path, Served[path])]);

    private static readonly CatalogModel Small = Model("small", "whisper/small.bin");
    private static readonly CatalogModel Large = Model("large", "whisper/large.bin");

    private static readonly CatalogModel Base  = Model("base", "whisper/base.bin");
    private static readonly CatalogModel CppLarge = Model("large-v3-turbo", "whisper/ggml-large.bin");
    private static readonly CatalogModel E4b   = Model("e4b", "models/e4b.gguf");
    private static readonly CatalogModel Big   = Model("big", "models/big.gguf");
    private static readonly CatalogModel Z     = Model("z", "models/z.onnx");
    private static readonly LocalRuntime Cuda  = new("cuda", "CUDA runtime", [Asset("runtime/cuda.dll", Served["runtime/cuda.dll"])]);

    /// <summary>A tool's models: one file.</summary>
    private static readonly CatalogModel Upscaler = Model("upscaler", "tools/picture/upscaler.onnx");

    /// <summary>Two builds of one video model, sharing every file but their denoisers.</summary>
    private static readonly CatalogModel FastClip = new("fast", "FAST", [Asset("models/clip/encoder.onnx", Served["models/clip/encoder.onnx"]),
                                                                         Asset("models/clip/fast.onnx", Served["models/clip/fast.onnx"])]);
    private static readonly CatalogModel FullClip = new("full", "FULL", [Asset("models/clip/encoder.onnx", Served["models/clip/encoder.onnx"]),
                                                                         Asset("models/clip/full.onnx", Served["models/clip/full.onnx"])]);

    private sealed class Gemma(bool runs = true) : ILlmProvider, ILocalModelCatalog
    {
        public string Name => "Nexaflow";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public ModelHarness Harness => ModelHarness.Owned;
        public IReadOnlyList<CatalogModel> Catalog => runs ? [E4b] : [];
        public IReadOnlyList<UnrunnableModel> Unrunnable => runs ? [new(Big, UnrunnableReason.TooLargeForThisMachine)]
            : [new(E4b, UnrunnableReason.TooLargeForThisMachine), new(Big, UnrunnableReason.TooLargeForThisMachine)];
        public LocalRuntime? RuntimeOf(string modelId) => Cuda;
        public IAsyncEnumerable<LlmStreamEvent> StreamAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["e4b"]);
    }

    private sealed class Pictures(bool runs = true) : IImageGenerationProvider, ILocalModelCatalog
    {
        public string Name => "Nexaflow";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public ModelHarness Harness => ModelHarness.Owned;
        public IReadOnlyList<CatalogModel> Catalog => runs ? [Z] : [];
        public IReadOnlyList<UnrunnableModel> Unrunnable => runs ? [] : [new(Z, UnrunnableReason.TooLargeForThisMachine)];
        public LocalRuntime? RuntimeOf(string modelId) => Cuda;
        public int MaxSources => 0;
        public bool Masks => false;
        public Task<GeneratedMedia> GenerateImageAsync(ImageGenerationRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["z"]);
    }

    private sealed class Clips : IVideoGenerationProvider, ILocalModelCatalog
    {
        public string Name => "Nexaflow";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public ModelHarness Harness => ModelHarness.Owned;
        public IReadOnlyList<CatalogModel> Catalog => [FastClip, FullClip];
        public IReadOnlyList<UnrunnableModel> Unrunnable => [];
        public IReadOnlyList<int> Lengths => [5];
        public bool StartsFromImages => true;
        public (int Width, int Height)? FirstFrameSize(MediaShape shape) => (1280, 704);
        public Task<GeneratedMedia> GenerateVideoAsync(VideoGenerationRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(["fast", "full"]);
    }

    /// <summary>Nexaflow's own speech models, which hold a conversation.</summary>
    private sealed class Listening(bool runs = true) : ISpeechToTextProvider, ILocalModelCatalog
    {
        public string Name => "Nexaflow";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public ModelHarness Harness => ModelHarness.Owned;
        public IReadOnlyList<CatalogModel> Catalog => runs ? [Small, Large] : [];
        public IReadOnlyList<UnrunnableModel> Unrunnable => runs ? [] : [new(Small, UnrunnableReason.NoBuildForThisMachine)];
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<ISpeechToTextSession> StartSessionAsync(SpeechToTextOptions options, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>whisper.cpp, on any machine: base, which dictates, and a larger size an earlier version could choose.</summary>
    private sealed class WhisperCpp : ISpeechToTextProvider, ILocalModelCatalog
    {
        public string Name => "Whisper";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public ModelHarness Harness => ModelHarness.Owned;
        public IReadOnlyList<CatalogModel> Catalog => [Base, CppLarge];
        public IReadOnlyList<UnrunnableModel> Unrunnable => [];
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

    /// <summary>Serves each file's bytes — those named in <see cref="Broken"/> as something else — counting what it serves, and
    /// holding every answer while <see cref="Held"/> is unset.</summary>
    private sealed class Server : HttpMessageHandler
    {
        public readonly HashSet<string> Broken = [];
        public readonly List<string> Served = [];
        public TaskCompletionSource Held = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Server() => Held.SetResult();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath.TrimStart('/');
            lock (Served) Served.Add(path);
            await Held.Task;
            var bytes = Broken.Contains(path) ? Bytes(LocalModelsTests.Served[path].Length, 13) : LocalModelsTests.Served[path];
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
        }
    }

    private string _root = null!;
    private Server _server = null!;
    private ModelAssetStore _store = null!;
    private int _saves;
    private readonly List<(LocalSlot Slot, string Model)> _used = [];

    [TestInitialize]
    public void Init()
    {
        _root   = Path.Combine(TestTemp.Root, "nexaflow-test-" + Guid.NewGuid().ToString("N"));
        _server = new Server();
        _store  = new ModelAssetStore(_root, [], new HttpClient(_server));
    }

    [TestCleanup] public void Done() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private static readonly string SpeechIn = LocalColumns.IdOf(LocalSlot.SpeechToText);
    private static readonly string Language = LocalColumns.IdOf(LocalSlot.Language);

    private sealed record Setup(AiAbilityGridViewModel Grid, AiConfig Config, LocalModelShelf Shelf, LocalModelDownloads Downloads, Workspace Workspace);

    /// <summary>A workspace conversing on Claude and listening with Nexaflow's small speech model, on the Local column.</summary>
    private Setup Open(bool runs = true, LocalModelDownloads? downloads = null)
    {
        var providers = new ProviderSet([new Gemma(runs), new Listening(runs), new WhisperCpp(), new Pictures(runs), new Claude()], new Dictionary<string, IAiProvider>(), [],
            new Dictionary<string, string> { ["Nexaflow"] = "Nexaflow.Providers.AiHost.dll", ["Whisper"] = "Nexaflow.Providers.Whisper.dll", ["Claude"] = "Nexaflow.Providers.Claude.dll" }, []);
        var config = new AiConfig
        {
            Providers = providers,
            Columns =
            [
                new ProviderModelPair { Id = "c", ProviderName = "Claude", Model = "sonnet", Kind = ModelKind.Language },
                new ProviderModelPair { Id = SpeechIn, ProviderName = "Nexaflow", Model = "small", Kind = ModelKind.SpeechToText },
            ],
            Assignments = { ["GeneralIntelligence"] = AbilityChain.Of("c"), ["SpeechIn"] = AbilityChain.Of(SpeechIn) },
            LocalSeeded = true,
        };
        var shelf = new LocalModelShelf(providers, _store);
        downloads ??= Downloads();
        return new(new AiAbilityGridViewModel(config, shelf, downloads), config, shelf, downloads, new Workspace());
    }

    private LocalModelDownloads Downloads() => new(_store, activities: null, (_, slot, column) => _used.Add((slot, column.Model)));

    private LocalModelsViewModel Page(Setup setup, params ILocalTool[] tools) => new(setup.Shelf, setup.Grid, setup.Workspace, setup.Downloads, () => _saves++, tools);

    private static AiAbilityCellViewModel LocalCell(AiAbilityGridViewModel grid, AiAbility ability)
        => grid.Rows.Single(r => r.Ability == ability).Cells.Single(c => c.Column.IsLocal);

    private static LocalJobViewModel Job(LocalModelsViewModel page, LocalSlot slot) => page.Jobs.Single(j => j.Slot == slot);

    private static async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(25);
        Assert.IsTrue(condition(), "it came about within five seconds");
    }

    // ── The grid's Local column ───────────────────────────────────────────

    // ── What a set of models comes to ─────────────────────────────────────

    private static LocalModelOption OptionOf(Setup setup, LocalSlot slot, CatalogModel model)
        => setup.Shelf.OptionsFor(slot).Single(o => o.Id == model.Id);

    [TestMethod]
    public void ThePlan_ListsEachModelNotHere_AndTheRuntimeTheyShare_Once()
    {
        var setup = Open();

        var plan = LocalDownloadPlan.For([OptionOf(setup, LocalSlot.Language, E4b), OptionOf(setup, LocalSlot.ImageGeneration, Z)], _store);

        CollectionAssert.AreEqual(new[] { ("E4B", 6_000L), ("Z", 5_000L), ("CUDA runtime", 3_000L) }, plan.Rows.Select(r => (r.Name, r.Bytes)).ToArray());
        Assert.IsNull(plan.Rows[^1].Option, "the runtime comes with the models");
        Assert.AreEqual(14_000, plan.Bytes, "the runtime both need is counted once");
        Assert.AreEqual(14_000, plan.Room.Bytes);
    }

    [TestMethod]
    public async Task ThePlan_LeavesOutWhatIsHere()
    {
        var setup = Open();
        await _store.EnsureAllAsync([.. Cuda.Files, .. Z.Files]);

        var plan = LocalDownloadPlan.For([OptionOf(setup, LocalSlot.Language, E4b), OptionOf(setup, LocalSlot.ImageGeneration, Z)], _store);

        CollectionAssert.AreEqual(new[] { "E4B" }, plan.Rows.Select(r => r.Name).ToArray(), "a model all here has no row, nor a runtime that is here");
        Assert.AreEqual(6_000, plan.Bytes);
    }

    [TestMethod]
    public void ThePlan_SaysWhatTheDriveIsShort()
    {
        _store = new ModelAssetStore(_root, [], new HttpClient(_server), weigh: (path, bytes) => new(path, bytes, 10_000, null, null, Nexaflow.IO.Common.SaveTrouble.None));
        var setup = Open();

        var plan = LocalDownloadPlan.For([OptionOf(setup, LocalSlot.Language, E4b), OptionOf(setup, LocalSlot.ImageGeneration, Z)], _store);

        Assert.IsFalse(plan.Room.Fits);
        Assert.AreEqual(4_000, plan.Room.Shortfall);
    }

    [TestMethod]
    public async Task TheModelsAwaited_AreThoseTheLocalColumnHoldsInRowsThatUseIt_NotHereYet()
    {
        var setup = Open();
        LocalSlot[] slots = [LocalSlot.Language, LocalSlot.SpeechToText, LocalSlot.TextToSpeech];

        CollectionAssert.AreEqual(new[] { "small" }, LocalDownloadPlan.Awaited(setup.Config, setup.Shelf, slots).Select(o => o.Id).ToArray(),
                                  "the speech job's model; the language job has none chosen");

        await _store.EnsureAllAsync([.. Small.Files]);
        Assert.AreEqual(0, LocalDownloadPlan.Awaited(setup.Config, setup.Shelf, slots).Count);
    }

    [TestMethod]
    public void TheGrid_OffersNoDownloadTheDriveHasNoRoomFor_AndSaysWhy()
    {
        _store = new ModelAssetStore(_root, [], new HttpClient(_server), weigh: (path, bytes) => new(path, bytes, 1_000, null, null, Nexaflow.IO.Common.SaveTrouble.None));

        var grid = Open().Grid;

        Assert.IsFalse(grid.CanDownloadLocal);
        StringAssert.StartsWith(grid.LocalNotice, Str.Get("Shell.Ai.Room.ShortFormat").Split('{')[0], "the refusal, saying how much it needs");
    }

    [TestMethod]
    public void TheLocalColumn_ComesFirst_AndCannotBeRemoved()
    {
        var grid = Open().Grid;

        Assert.IsTrue(grid.Columns[0].IsLocal);
        Assert.IsFalse(grid.RemoveColumnCommand.CanExecute(grid.Columns[0]));
        Assert.AreEqual(1, grid.Columns.Count(c => c.IsLocal), "one column for every job's model");
    }

    [TestMethod]
    public void ALocalCell_IsGreyed_WhereThereIsNothingToUse_AndSaysWhy()
    {
        var grid = Open().Grid;

        var video = LocalCell(grid, AiAbility.VideoGeneration);
        Assert.IsTrue(video.IsGreyed, "no model for video runs here");
        Assert.AreEqual(Str.Get("Shell.Config.AiGrid.Local.None"), video.Note);

        var conversation = LocalCell(grid, AiAbility.GeneralIntelligence);
        Assert.IsTrue(conversation.IsGreyed, "a model runs here, but none is chosen");
        Assert.AreEqual(Str.Get("Shell.Config.AiGrid.Local.Choose"), conversation.Note);
    }

    [TestMethod]
    public async Task ATickedLocalCell_WhoseModelIsNotHere_IsMarked_AndTheGridOffersToDownloadThem()
    {
        var setup = Open();
        var cell  = LocalCell(setup.Grid, AiAbility.SpeechIn);

        Assert.IsTrue(cell.IsInChain);
        Assert.IsTrue(cell.NeedsDownload, "chosen, but not downloaded yet");
        Assert.IsTrue(setup.Grid.CanDownloadLocal);
        Assert.IsNotNull(setup.Grid.LocalNotice);

        setup.Grid.DownloadLocalCommand.Execute(null);
        await setup.Downloads.Of(setup.Shelf.OptionsFor(LocalSlot.SpeechToText).Single(o => o.Id == "small"))!.Done;
        setup.Grid.RefreshLocal();

        Assert.IsFalse(cell.NeedsDownload);
        Assert.IsNull(setup.Grid.LocalNotice, "nothing left to download");
    }

    [TestMethod]
    public void AMachineThatRunsNoLocalModel_IsToldToAddACloudService()
    {
        var grid = Open(runs: false).Grid;

        Assert.AreEqual(Str.Get("Shell.Config.AiGrid.Local.NoHardware"), grid.LocalNotice);
        Assert.IsFalse(grid.CanDownloadLocal);
    }

    [TestMethod]
    public void ALocalCell_PutsItsJobsModelInTheRow()
    {
        var grid = Open().Grid;
        var cell = LocalCell(grid, AiAbility.SpeechIn);

        cell.ToggleCommand.Execute(null);
        Assert.IsFalse(grid.Rows.Single(r => r.Ability == AiAbility.SpeechIn).Chain.Contains(SpeechIn));

        cell.ToggleCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { SpeechIn }, grid.Rows.Single(r => r.Ability == AiAbility.SpeechIn).Chain.ToArray());
    }

    [TestMethod]
    public void ModelsOnThisMachine_AreTheLocalColumns_NeverAProviderToAdd()
    {
        var grid = Open().Grid;

        grid.OpenAddPanelCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "Claude" }, grid.AvailableOffers.Select(o => o.ProviderName).Distinct().ToArray());
    }

    // ── The Local page ────────────────────────────────────────────────────

    [TestMethod]
    public void EachJob_ListsItsModels_HereToDownloadOrNotForThisMachine() => OnUi(async () =>
    {
        await _store.EnsureAsync(Small.Files[0]);
        var page = Page(Open());

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
    });

    [TestMethod]
    public void SpeechToText_OffersNexaflowsOwnSpeechModels_NeverWhisperCpps_WhereOneRunsHere() => OnUi(() =>
    {
        var speech = Job(Page(Open()), LocalSlot.SpeechToText);

        CollectionAssert.AreEqual(new[] { "SMALL", "LARGE" }, speech.Choices.Select(c => c.DisplayName).ToArray(), "every one of them holds a conversation");
        Assert.IsFalse(speech.IsDictationOnly);
        Assert.IsTrue(speech.CanChoose);
        return Task.CompletedTask;
    });

    [TestMethod]
    public void AMachineThatRunsNoneOfNexaflowsSpeechModels_OnlyDictates_WithWhisperBase() => OnUi(() =>
    {
        var setup  = Open(runs: false);
        var speech = Job(Page(setup), LocalSlot.SpeechToText);

        Assert.AreEqual(LocalColumns.Dictation.Model, setup.Shelf.OptionsFor(LocalSlot.SpeechToText).Single().Id);
        Assert.IsTrue(speech.IsDictationOnly);
        Assert.IsFalse(speech.CanChoose, "there is nothing to choose");
        Assert.AreEqual(Str.Get("Shell.Config.Local.DictationNote"), speech.Note);
        Assert.AreEqual(Str.Get("Shell.Config.AiGrid.Local.NoHardware"), setup.Grid.LocalNotice, "dictation answers nothing: a cloud service still has to");

        LocalColumns.Choose(setup.Config, LocalSlot.SpeechToText, LocalColumns.Dictation);
        var grid = new AiAbilityGridViewModel(setup.Config, setup.Shelf, setup.Downloads);
    Assert.AreEqual(Str.Format("Shell.Config.AiGrid.Local.NotHereFormat", Str.Get("Shell.Config.Local.Dictation")), LocalCell(grid, AiAbility.SpeechIn).Note,
                        "the grid names the job, not whisper.cpp's model");
        return Task.CompletedTask;
    });

    [TestMethod]
    public void AModelThatIsHere_TakesEffectAtOnce_AndIsSaved() => OnUi(async () =>
    {
        await _store.EnsureAsync(Large.Files[0]);
        var setup  = Open();
        var speech = Job(Page(setup), LocalSlot.SpeechToText);

        speech.Selected = speech.Choices.Single(c => c.Option.Id == "large");

        Assert.AreEqual("large", setup.Grid.LocalModelFor(LocalSlot.SpeechToText)!.Model);
        Assert.AreEqual("large", setup.Config.Columns.Single(c => c.Id == SpeechIn).Model, "written into the config, not left for Apply");
        Assert.AreEqual(1, _saves);
        Assert.IsFalse(setup.Grid.HasChanges);
    });

    [TestMethod]
    public void AModelThatIsNotHere_IsDownloadedFirst_WithWhatItShares_ThenTheJobUsesIt() => OnUi(async () =>
    {
        var setup    = Open();
        var language = Job(Page(setup), LocalSlot.Language);

        language.Selected = language.Choices.Single(c => c.Option.Id == "e4b");
        Assert.IsTrue(language.IsDownloading);
        await Until(() => !language.IsDownloading);

        Assert.IsNotNull(_store.Locate(E4b.Files[0]));
        Assert.IsNotNull(_store.Locate(Cuda.Files[0]), "with the runtime it needs");
        CollectionAssert.Contains(_used, (LocalSlot.Language, "e4b"), "the downloads made the choice good");
        await Until(() => setup.Grid.LocalModelFor(LocalSlot.Language)?.Model == "e4b");
        CollectionAssert.AreEqual(new[] { "c", Language }, setup.Config.Assignments["GeneralIntelligence"].ColumnIds.ToArray(),
            "a job no row used the Local column for starts using it, after what each row had");
    });

    [TestMethod]
    public void AModelChosenForTwoJobs_IsDownloadedOnce() => OnUi(async () =>
    {
        _server.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var page     = Page(Open());
        var pictures = Job(page, LocalSlot.ImageGeneration);
        var finest   = Job(page, LocalSlot.HighQualityImageGeneration);

        pictures.Selected = pictures.Choices.Single(c => c.Option.Id == "z");
        finest.Selected   = finest.Choices.Single(c => c.Option.Id == "z");
        Assert.IsTrue(pictures.IsDownloading && finest.IsDownloading, "both wait on it");

        _server.Held.SetResult();
        await Until(() => !pictures.IsDownloading && !finest.IsDownloading);

        Assert.AreEqual(1, _server.Served.Count(p => p == "models/z.onnx"), "fetched once for both");
        CollectionAssert.IsSubsetOf(new[] { (LocalSlot.ImageGeneration, "z"), (LocalSlot.HighQualityImageGeneration, "z") }, _used);
    });

    [TestMethod]
    public void ADownloadStillUnderWay_IsShownWhenThePageOpensAgain_AndTheJobKeepsItsModelTillItLands() => OnUi(async () =>
    {
        _server.Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var setup  = Open();
        var first  = Job(Page(setup), LocalSlot.SpeechToText);
        first.Selected = first.Choices.Single(c => c.Option.Id == "large");

        var again = Job(Page(setup), LocalSlot.SpeechToText);   // the page closed, and opened again

        Assert.IsTrue(again.IsDownloading);
        Assert.AreEqual("large", again.Selected!.Option.Id, "the model it is fetching");
        Assert.IsNotNull(again.Status);
        Assert.AreEqual("small", setup.Grid.LocalModelFor(LocalSlot.SpeechToText)!.Model, "the job keeps its model meanwhile");

        _server.Held.SetResult();
        await Until(() => !again.IsDownloading);
        CollectionAssert.Contains(_used, (LocalSlot.SpeechToText, "large"));
    });

    [TestMethod]
    public void ADownloadThatFails_LeavesTheJobsModel_AndSaysWhy() => OnUi(async () =>
    {
        _server.Broken.Add("whisper/large.bin");
        var setup  = Open();
        var speech = Job(Page(setup), LocalSlot.SpeechToText);

        speech.Selected = speech.Choices.Single(c => c.Option.Id == "large");
        await Until(() => !speech.IsDownloading);

        Assert.AreEqual("small", setup.Grid.LocalModelFor(LocalSlot.SpeechToText)!.Model);
        Assert.AreEqual("small", speech.Selected!.Option.Id);
        Assert.IsNotNull(speech.Status);
        Assert.AreEqual(0, _used.Count);
    });

    // ── How long a download has left ──────────────────────────────────────

    /// <summary>A clock that moves when told.</summary>
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [TestMethod]
    public void HowLongIsLeft_IsReadFromHowFastItHasCome_OnceItHasComeForAFewSeconds()
    {
        var clock    = new Clock();
        var download = new LocalDownload(new LocalModelOption("Nexaflow", "", ModelKind.Language, E4b, null, null), 100_000_000, clock);

        download.Report(new AssetProgress(0, 100_000_000));
        Assert.IsNull(download.Left, "not till it has come for a moment");

        clock.Now += TimeSpan.FromSeconds(2);
        download.Report(new AssetProgress(10_000, 100_000_000));
        Assert.IsNull(download.Left, "a model's first files are small, each a request: their speed would read as days");

        clock.Now += TimeSpan.FromSeconds(8);
        download.Report(new AssetProgress(20_000_000, 100_000_000));   // two megabytes a second

        Assert.AreEqual(TimeSpan.FromSeconds(40), download.Left);
        Assert.AreEqual(20, download.Percent);
    }

    [TestMethod]
    public void ADownloadsFigures_ChangeAFewTimesASecond_HoweverOftenItReports_AndWhenItIsAllHere()
    {
        var clock    = new Clock();
        var download = new LocalDownload(new LocalModelOption("Nexaflow", "", ModelKind.Language, E4b, null, null), 1_000_000, clock);
        var changes  = 0;
        download.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LocalDownload.Received)) changes++; };

        for (int piece = 1; piece < 100; piece++)
        {
            clock.Now += TimeSpan.FromMilliseconds(10);   // a piece every 10 ms: a second of pieces
            download.Report(new AssetProgress(piece * 10_000, 1_000_000));
        }
        Assert.AreEqual(4, changes, "every quarter of a second, not every piece");

        download.Report(new AssetProgress(1_000_000, 1_000_000));
        Assert.AreEqual(1_000_000, download.Received, "the last piece is always told");
    }

    [TestMethod]
    public void ADownloadAskedFromTheUiThread_DoesNoneOfItsWorkThere() => OnUi(async () =>
    {
        var ui      = Environment.CurrentManagedThreadId;
        var threads = new HashSet<int>();

        await _store.EnsureAllAsync([Large.Files[0]], new Told(_ => { lock (threads) threads.Add(Environment.CurrentManagedThreadId); }));

        Assert.IsTrue(threads.Count > 0);
        Assert.IsFalse(threads.Contains(ui), "reading, writing and hashing each piece on the UI thread froze the app");
        Assert.AreEqual(ui, Environment.CurrentManagedThreadId, "the caller carries on where it asked");
    });

    /// <summary>Progress told inline, on whichever thread the work is on.</summary>
    private sealed class Told(Action<AssetProgress> report) : IProgress<AssetProgress>
    {
        public void Report(AssetProgress value) => report(value);
    }

    // ── Downloaded models ─────────────────────────────────────────────────

    [TestMethod]
    public async Task WhatIsOnDisk_IsListedWithWhoUsesIt_AndOnlyWhatNothingUsesCanGo()
    {
        await _store.EnsureAllAsync([Small.Files[0], E4b.Files[0], Cuda.Files[0]]);
        var setup = Open();
        var page  = new DownloadedModelsViewModel(setup.Shelf, () => [("Writing", setup.Config)], []);

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

    [TestMethod]
    public async Task TwoBuildsSharingFiles_TakeTheirRoomOnce_AndDeletingOneKeepsWhatTheOtherNeeds()
    {
        await _store.EnsureAllAsync([.. FastClip.Files, .. FullClip.Files]);
        var providers = new ProviderSet([new Clips()], new Dictionary<string, IAiProvider>(), [],
                                        new Dictionary<string, string> { ["Nexaflow"] = "Nexaflow.Providers.AiHost.dll" }, []);
        var page = new DownloadedModelsViewModel(new LocalModelShelf(providers, _store), () => [], []);

        Assert.AreEqual(6_000 + 1_000 + 1_000, page.TotalBytes, "the encoder they share is counted once");

        await page.Models.Single(m => m.Name == "FAST").DeleteCommand.ExecuteAsync(null);

        Assert.IsNull(_store.Locate(FastClip.Files[1]), "its own denoiser goes");
        Assert.IsNotNull(_store.Locate(FastClip.Files[0]), "the encoder stays: the full build still needs it");
        CollectionAssert.AreEqual(new[] { "FULL" }, page.Models.Select(m => m.Name).ToArray());

        await page.Models.Single(m => m.Name == "FULL").DeleteCommand.ExecuteAsync(null);

        Assert.IsNull(_store.Locate(FullClip.Files[0]), "the last build to go takes what they shared");
        Assert.IsTrue(page.IsEmpty);
    }

    [TestMethod]
    public async Task AModelNoJobOffersHere_IsStillListedWhileItIsOnDisk_SoItCanGo()
    {
        var cpp = CppLarge;
        await _store.EnsureAllAsync([.. cpp.Files]);
        var page = new DownloadedModelsViewModel(Open().Shelf, () => [], []);

        var listed = page.Models.Single(m => m.Name == "LARGE-V3-TURBO");
        Assert.IsTrue(listed.DeleteCommand.CanExecute(null));
    }

    // ── Tools ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void ATool_IsOfferedBesideTheJobs_DownloadingWhatItLacks_AndIsHereOnceItCame() => OnUi(async () =>
    {
        var tool = new FakeTool(_store, Upscaler);
        var row  = Page(Open(), tool).Tools.Single();

        Assert.AreEqual("UPSCALER", row.Name);
        Assert.AreEqual(Str.Format("Shell.Config.Local.DownloadFormat", SizeFormatter.FormatBytes(2_000)), row.DownloadLabel, "what the one file it lacks takes");
        Assert.IsTrue(row.DownloadCommand.CanExecute(null));

        row.DownloadCommand.Execute(null);
        await tool.Fetching;
        row.Refresh();

        Assert.AreEqual(1, tool.Downloads);
        Assert.IsNotNull(_store.Locate(Upscaler.Files[0]));
        Assert.IsFalse(row.DownloadCommand.CanExecute(null), "nothing left to fetch");
        Assert.AreEqual(Str.Get("Shell.Config.Local.Here"), row.Status);
    });

    [TestMethod]
    public async Task AToolOnDisk_IsListedWithTheModels_AndCanGo_UsedByNoWorkspace()
    {
        await _store.EnsureAllAsync([.. Upscaler.Files]);
        var tool = new FakeTool(_store, Upscaler);
        var page = new DownloadedModelsViewModel(Open().Shelf, () => [], [tool]);

        var listed = page.Models.Single(m => m.Name == "UPSCALER");
        Assert.IsFalse(listed.InUse, "a tool belongs to no workspace");
        await listed.DeleteCommand.ExecuteAsync(null);

        Assert.IsNull(_store.Locate(Upscaler.Files[0]));
        Assert.AreEqual(ToolModelsState.NotDownloaded, tool.State, "it downloads again when next asked for");
        Assert.IsFalse(page.Models.Any(m => m.Name == "UPSCALER"));
    }

    /// <summary>A tool whose models are files in the store, fetched when asked.</summary>
    private sealed class FakeTool(ModelAssetStore store, CatalogModel models) : ILocalTool
    {
        private Task? _fetching;

        public string Name => models.DisplayName;
        public string About => "Restores pictures.";
        public IReadOnlyList<ModelAsset> OwnFiles => models.Files;
        public int Downloads { get; private set; }
        public Task Fetching => _fetching ?? Task.CompletedTask;

        public ToolModelsState State
            => models.Files.All(f => store.Locate(f) is not null) ? ToolModelsState.Ready
             : _fetching is { IsCompleted: false } ? ToolModelsState.Downloading
             : ToolModelsState.NotDownloaded;

        public long DownloadBytes => models.Files.Where(f => store.Locate(f) is null).Sum(f => f.Size);
        public int DownloadPercent => 0;

        public string? DownloadFailure { get; set; }

        public string ModelsVersion => "fake-1";
        public event EventHandler? Changed;

        public void Download()
        {
            Downloads++;
            _fetching = store.EnsureAllAsync(models.Files);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Runs <paramref name="body"/> on a thread with a dispatcher of its own, as the page runs on the UI thread.</summary>
    private static void OnUi(Func<Task> body)
    {
        Exception? failed = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            dispatcher.InvokeAsync(async () =>
            {
                try { await body(); }
                catch (Exception ex) { failed = ex; }
                finally { frame.Continue = false; }
            });
            Dispatcher.PushFrame(frame);
            dispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30))) Assert.Fail("It did not finish within thirty seconds.");
        if (failed is not null) ExceptionDispatchInfo.Capture(failed).Throw();
    }
}
