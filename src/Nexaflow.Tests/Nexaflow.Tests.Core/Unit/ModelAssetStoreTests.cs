using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using Nexaflow.Core.Services;
using Nexaflow.Providers.Common;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Core.Unit;

/// <summary>
/// The store local models' files live in: what it hands out, what it fetches, and what it takes over from the folder
/// earlier versions kept voice models in.
/// </summary>
[TestClass]
[CoversNode("ai-model-assets")]
public class ModelAssetStoreTests
{
    private static readonly byte[] Weights = [.. Enumerable.Range(0, 50_000).Select(i => (byte)(i * 7))];
    private static readonly ModelAsset Asset = new("whisper/ggml-test.bin", new Uri("https://example.test/ggml-test.bin"),
        Weights.Length, Convert.ToHexStringLower(SHA256.HashData(Weights)));

    private string _root = null!, _legacy = null!;

    [TestInitialize]
    public void Init()
    {
        var dir = Path.Combine(Path.GetTempPath(), "nexaflow-test-" + Guid.NewGuid().ToString("N"));
        _root   = Path.Combine(dir, "ai");
        _legacy = Path.Combine(dir, "voice", "models");
        Directory.CreateDirectory(_legacy);
    }

    [TestCleanup] public void Done() => Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true);

    private sealed class Server : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Weights) });
        }
    }

    [TestMethod]
    public async Task AMissingFile_IsFetched_ThenFoundWithoutFetchingAgain()
    {
        var server = new Server();
        var store  = new ModelAssetStore(_root, [_legacy], new HttpClient(server));
        Assert.IsNull(store.Locate(Asset));

        var path = await store.EnsureAsync(Asset);
        await store.EnsureAsync(Asset);

        Assert.AreEqual(path, store.Locate(Asset));
        Assert.AreEqual(1, server.Requests);
        StringAssert.StartsWith(path, _root);
    }

    [TestMethod]
    public async Task AModelAnEarlierVersionDownloaded_IsMovedIn_NotFetchedAgain()
    {
        File.WriteAllBytes(Path.Combine(_legacy, "ggml-test.bin"), Weights);
        var server = new Server();

        var path = await new ModelAssetStore(_root, [_legacy], new HttpClient(server)).EnsureAsync(Asset);

        Assert.AreEqual(0, server.Requests);
        CollectionAssert.AreEqual(Weights, File.ReadAllBytes(path));
        Assert.IsFalse(File.Exists(Path.Combine(_legacy, "ggml-test.bin")), "the old copy would cost its size twice");
    }

    [TestMethod]
    public async Task AnOldFileThatIsNotTheSame_IsLeftAndTheModelFetched()
    {
        var damaged = Weights.ToArray();
        damaged[100] ^= 0xFF;
        File.WriteAllBytes(Path.Combine(_legacy, "ggml-test.bin"), damaged);
        var server = new Server();

        await new ModelAssetStore(_root, [_legacy], new HttpClient(server)).EnsureAsync(Asset);

        Assert.AreEqual(1, server.Requests);
        Assert.IsTrue(File.Exists(Path.Combine(_legacy, "ggml-test.bin")));
    }

    [TestMethod]
    public async Task AFileChangedOnDisk_IsNoLongerHandedOut()
    {
        var store = new ModelAssetStore(_root, [], new HttpClient(new Server()));
        var path  = await store.EnsureAsync(Asset);

        File.WriteAllBytes(path, Weights[..1000]);

        Assert.IsNull(store.Locate(Asset));
    }
}
