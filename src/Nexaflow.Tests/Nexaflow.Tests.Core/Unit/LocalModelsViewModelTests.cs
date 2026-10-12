using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Nexaflow.Core.AI;
using Nexaflow.Core.Services;
using Nexaflow.Core.ViewModels;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// A workspace page for a provider's model files: what is on this machine, choosing the one Speech In listens with,
/// fetching and removing.
/// </summary>
[TestClass]
[CoversNode("aibar-voice-config")]
public class LocalModelsViewModelTests
{
    private static readonly byte[] Small = [.. Enumerable.Range(0, 4_000).Select(i => (byte)i)];
    private static readonly byte[] Large = [.. Enumerable.Range(0, 9_000).Select(i => (byte)(i * 3))];

    private static CatalogModel Model(string id, byte[] weights) => new(id, id,
        [new ModelAsset($"whisper/{id}.bin", new Uri($"https://example.test/{id}.bin"), weights.Length,
                        Convert.ToHexStringLower(SHA256.HashData(weights)))]);

    private static readonly CatalogModel SmallModel = Model("small", Small);
    private static readonly CatalogModel LargeModel = Model("large", Large);

    private sealed class Whisper : ISpeechToTextProvider, ILocalModelCatalog
    {
        public string Name => "Whisper";
        public DataResidency Residency => DataResidency.StaysOnMachine;
        public IReadOnlyList<CatalogModel> Catalog => [SmallModel, LargeModel];
        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<ISpeechToTextSession> StartSessionAsync(SpeechToTextOptions options, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Serves whichever model's weights the request names.</summary>
    private sealed class Server : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
               { Content = new ByteArrayContent(request.RequestUri!.AbsolutePath.Contains("large") ? Large : Small) });
    }

    private string _root = null!;
    private ModelAssetStore _store = null!;

    [TestInitialize]
    public void Init()
    {
        _root  = Path.Combine(Path.GetTempPath(), "nexaflow-test-" + Guid.NewGuid().ToString("N"));
        _store = new ModelAssetStore(_root, [], new HttpClient(new Server()));
    }

    [TestCleanup] public void Done() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    private (LocalModelsViewModel Page, AiAbilityGridViewModel Grid, AiConfig Config) Open()
    {
        var config = new AiConfig
        {
            Columns     = [new ProviderModelPair { Id = "w", ProviderName = "Whisper", Model = "small", Kind = ModelKind.SpeechToText }],
            Assignments = { ["SpeechIn"] = AbilityChain.Of("w") },
        };
        var grid = new AiAbilityGridViewModel(config);
        var whisper = new Whisper();
        return (new LocalModelsViewModel(whisper, whisper, grid, _store, activities: null), grid, config);
    }

    private static LocalModelRowViewModel Row(LocalModelsViewModel page, string id) => page.Models.Single(m => m.Id == id);

    [TestMethod]
    public async Task EveryModelIsListed_WithWhetherItIsHere_AndTheOneInUse()
    {
        await _store.EnsureAsync(SmallModel.Files[0]);
        var (page, _, _) = Open();

        Assert.AreEqual(AiAbility.SpeechIn, page.Ability, "a speech model serves Speech In");
        Assert.IsTrue(Row(page, "small").IsOnDisk);
        Assert.IsFalse(Row(page, "large").IsOnDisk);
        Assert.IsTrue(Row(page, "small").IsInUse);
        Assert.IsFalse(Row(page, "small").RemoveCommand.CanExecute(null), "the model in use stays");
    }

    [TestMethod]
    public async Task Use_ChoosesTheModelForSpeechIn_AndFetchesIt()
    {
        var (page, grid, config) = Open();

        await Row(page, "large").UseCommand.ExecuteAsync(null);

        Assert.AreEqual("large", grid.ModelFor(AiAbility.SpeechIn, "Whisper"));
        Assert.IsTrue(Row(page, "large").IsInUse);
        Assert.IsTrue(Row(page, "large").IsOnDisk, "a model chosen and not here is fetched");
        Assert.IsTrue(grid.HasChanges);
        Assert.AreEqual("small", config.Columns.Single().Model, "the choice is saved when the page is applied, not before");
    }

    [TestMethod]
    public async Task Remove_FreesAModelThatIsNotInUse()
    {
        await _store.EnsureAsync(LargeModel.Files[0]);
        var (page, _, _) = Open();

        await Row(page, "large").RemoveCommand.ExecuteAsync(null);

        Assert.IsFalse(Row(page, "large").IsOnDisk);
        Assert.IsNull(_store.Locate(LargeModel.Files[0]));
    }
}
