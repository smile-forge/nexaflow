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
        var dir = Path.Combine(TestTemp.Root, "nexaflow-test-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>A zip holding <paramref name="entries"/>, as a wheel or a NuGet package is.</summary>
    private static byte[] Zip(params (string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, bytes) in entries)
                using (var entry = zip.CreateEntry(name).Open())
                    entry.Write(bytes);
        return stream.ToArray();
    }

    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    /// <summary>Serves one archive and counts how often it is asked for.</summary>
    private sealed class ArchiveServer(byte[] archive) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(archive) });
        }
    }

    private static readonly byte[] Cublas = [.. Enumerable.Range(0, 30_000).Select(i => (byte)(i * 3))];
    private static readonly byte[] Cudnn  = [.. Enumerable.Range(0, 20_000).Select(i => (byte)(i * 11))];
    private static readonly byte[] Wheel  = Zip(("nvidia/bin/cublas.dll", Cublas), ("nvidia/bin/cudnn.dll", Cudnn), ("nvidia/bin/unwanted.dll", Weights));

    private static ModelAsset Packed(string file, string entry, byte[] bytes, byte[] archive)
        => new($"runtime/{file}", new Uri("https://pypi.test/nvidia.whl"), bytes.Length, Sha(bytes),
               new ArchiveEntry(entry, archive.Length, Sha(archive)));

    [TestMethod]
    public async Task FilesPackedInOneArchive_AreFetchedWithOneDownload_AndOnlyTheyAreKept()
    {
        var server  = new ArchiveServer(Wheel);
        var store   = new ModelAssetStore(_root, [], new HttpClient(server));
        var cublas  = Packed("cublas.dll", "nvidia/bin/cublas.dll", Cublas, Wheel);
        var cudnn   = Packed("cudnn.dll", "nvidia/bin/cudnn.dll", Cudnn, Wheel);
        var changed = new List<ModelAsset>();
        store.Changed += (_, a) => changed.Add(a);
        var progress = new List<AssetProgress>();

        var paths = await store.EnsureAllAsync([cublas, cudnn], new SyncProgress(progress));

        Assert.AreEqual(1, server.Requests);
        CollectionAssert.AreEqual(Cublas, File.ReadAllBytes(paths[0]));
        CollectionAssert.AreEqual(Cudnn, File.ReadAllBytes(paths[1]));
        CollectionAssert.AreEquivalent(new[] { cublas, cudnn }, changed);
        Assert.IsFalse(File.Exists(Path.Combine(_root, "runtime", "unwanted.dll")), "an entry nobody asked for is not kept");
        Assert.IsFalse(Directory.EnumerateFiles(Path.Combine(_root, ".downloads")).Any(), "the archive would cost its size twice");
        Assert.AreEqual(new AssetProgress(Wheel.Length, Wheel.Length), progress[^1]);

        await store.EnsureAllAsync([cublas, cudnn]);
        Assert.AreEqual(1, server.Requests, "what is here and verified is not fetched again");
    }

    [TestMethod]
    public async Task AnEntryThatIsNotWhatTheCatalogSays_IsRefused_AndNotHandedOut()
    {
        var store   = new ModelAssetStore(_root, [], new HttpClient(new ArchiveServer(Wheel)));
        var claimed = Packed("cublas.dll", "nvidia/bin/cublas.dll", Cudnn, Wheel);   // the catalog describes other bytes

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.EnsureAllAsync([claimed]));

        Assert.IsNull(store.Locate(claimed));
        Assert.IsFalse(File.Exists(Path.Combine(_root, "runtime", "cublas.dll")));
    }

    [TestMethod]
    public async Task AnArchiveThatIsNotWhatTheCatalogSays_IsRefused()
    {
        var store = new ModelAssetStore(_root, [], new HttpClient(new ArchiveServer(Wheel)));
        var other = Zip(("nvidia/bin/cublas.dll", Cublas));
        var asset = Packed("cublas.dll", "nvidia/bin/cublas.dll", Cublas, other);   // expects another archive

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => store.EnsureAllAsync([asset]));

        Assert.IsNull(store.Locate(asset));
    }

    /// <summary>Records reports as they are made — <see cref="Progress{T}"/> would post them to a context later.</summary>
    private sealed class SyncProgress(List<AssetProgress> into) : IProgress<AssetProgress>
    {
        public void Report(AssetProgress value) => into.Add(value);
    }
}
