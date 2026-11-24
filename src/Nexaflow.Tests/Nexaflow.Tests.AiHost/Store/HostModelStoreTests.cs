using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Nexaflow.AiHost.Catalog;
using Nexaflow.AiHost.Contracts;
using Nexaflow.AiHost.Provisioning;
using Nexaflow.AiHost.Store;
using Nexaflow.Tests.Fixtures;
using Nexaflow.AiHost;

namespace Nexaflow.Tests.AiHost.Store;

/// <summary>
/// The store the host keeps: it tells of every file its catalog names, fetches and removes only those, takes no path that leaves
/// it, and lets an earlier CUDA pack go once nothing will read it.
/// </summary>
[TestClass]
[CoversNode("ai-model-assets")]
public class HostModelStoreTests
{
    private static readonly MachineCards OnCuda = new([new MachineCard("A test card", 16L << 30)], CudaRuntimePack.MinimumDriver);

    private string _root = null!;

    [TestInitialize]
    public void Init() => _root = TestTemp.CreateSubdirectory("nexaflow-store-").FullName;

    [TestCleanup]
    public void Done()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a retire still under way */ }
    }

    /// <summary>A server holding nothing, counting what it is asked.</summary>
    private sealed class Server : HttpMessageHandler
    {
        private int _requests;
        public int Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private HostModelStore Store(MachineCards? machine = null, bool downloads = true, Server? server = null)
        => new(_root, [], machine ?? OnCuda, downloads, new HttpClient(server ?? new Server()));

    private string Earlier(string id)
    {
        var folder = Directory.CreateDirectory(Path.Combine(_root, "runtime", id)).FullName;
        File.WriteAllText(Path.Combine(folder, "cublas64_12.dll"), "an earlier pack's");
        return folder;
    }

    /// <summary>
    /// Lays the CUDA pack in the store as a fetch leaves it: each file at its length, marked verified. What the store checks of a
    /// verified file is its length and its mark, so sparse files take none of the 1.5 GB a real pack does.
    /// </summary>
    private void LayPack()
    {
        foreach (var file in CudaRuntimePack.Files)
        {
            var path = StorePath.Resolve(_root, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
            {
                if (!DeviceIoControl(stream.SafeFileHandle, SetSparse, 0, 0, 0, 0, out _, 0)) throw new Win32Exception();
                stream.SetLength(file.Size);
            }
            File.WriteAllText(path + ".verified", file.Sha256);
        }
    }

    private const uint SetSparse = 0x900C4;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, nint input, int inputSize, nint output, int outputSize,
                                               out int returned, nint overlapped);

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("the condition did not come about within 10 seconds");
            await Task.Delay(20);
        }
    }

    [TestMethod]
    public void TheStore_TellsOfEveryFileItsCatalogNames_NoneHereInAnEmptyOne()
    {
        var state = Store().Describe();

        var told = state.Files.Select(f => f.Path).ToHashSet(StringComparer.Ordinal);
        var named = HostCatalog.Models.SelectMany(m => m.Files)
                               .Concat(HostCatalog.Models.SelectMany(m => m.Loras).SelectMany(l => l.Files))
                               .Concat(HostCatalog.Tools.SelectMany(t => t.Files))
                               .Concat(CudaRuntimePack.Files)
                               .Select(f => f.RelativePath)
                               .ToHashSet(StringComparer.Ordinal);
        CollectionAssert.AreEquivalent(named.ToList(), told.ToList(), "every model's, LoRA's and tool's file, and the pack's — each once");
        Assert.IsFalse(state.Files.Any(f => f.Here || f.Adoptable || f.Partial > 0));
        Assert.AreEqual(Path.GetPathRoot(_root), state.Drive);
        Assert.IsFalse(state.EarlierRuntimePack);
    }

    [TestMethod]
    public async Task AFileTheCatalogDoesNotName_IsNeitherFetchedNorRemoved()
    {
        var server = new Server();
        var store  = Store(server: server);
        const string stray = "models/elsewhere/weights.bin";
        var strayPath = StorePath.Resolve(_root, stray);
        Directory.CreateDirectory(Path.GetDirectoryName(strayPath)!);
        File.WriteAllText(strayPath, "not the host's");
        var named = HostCatalog.Models.SelectMany(m => m.Files).First().RelativePath;

        var fetch  = await Assert.ThrowsExactlyAsync<HostRequestException>(() => store.FetchAsync([named, stray], null, CancellationToken.None));
        var remove = await Assert.ThrowsExactlyAsync<HostRequestException>(() => store.RemoveAsync([stray], CancellationToken.None));

        Assert.AreEqual((HostErrorKind.NotFound, HostErrorKind.NotFound), (fetch.Kind, remove.Kind));
        Assert.AreEqual(0, server.Requests, "the request is refused whole, before the file the catalog does name is asked for");
        Assert.IsTrue(File.Exists(strayPath), "a file the catalog does not name is not the host's to remove");
    }

    [TestMethod]
    [DataRow("../outside.bin")]
    [DataRow(@"models\..\..\outside.bin")]
    [DataRow("models/./kokoro.onnx")]
    [DataRow("models//kokoro.onnx")]
    [DataRow(@"C:\Windows\notepad.exe")]
    [DataRow("C:weights.bin")]
    [DataRow(@"\\server\share\weights.bin")]
    [DataRow("/models/kokoro.onnx")]
    [DataRow("")]
    public void APathThatIsNotPlainlyInsideTheStore_IsRefused(string path)
    {
        var refused = Assert.ThrowsExactly<HostRequestException>(() => Store().PathOf(path));

        Assert.AreEqual(HostErrorKind.Invalid, refused.Kind);
    }

    [TestMethod]
    public void APlainPath_LiesUnderTheStore()
        => Assert.AreEqual(Path.Combine(_root, "models", "kokoro-82m", "voices", "af_heart.bin"), Store().PathOf("models/kokoro-82m/voices/af_heart.bin"));

    [TestMethod]
    public async Task WithDownloadsOff_AFileNotHere_IsRefused_AndNothingIsAsked()
    {
        var server = new Server();
        var file   = HostCatalog.Models.SelectMany(m => m.Files).Where(f => f.Archive is null).MinBy(f => f.Size)!;

        await Assert.ThrowsExactlyAsync<ModelDownloadsOffException>(
            () => Store(downloads: false, server: server).FetchAsync([file.RelativePath], null, CancellationToken.None));

        Assert.AreEqual(0, server.Requests);
    }

    [TestMethod]
    public async Task OnAMachineNoPackSuits_EarlierPacksAreDeleted_AndNothingElseUnderRuntime()
    {
        var earlier = Earlier("cuda-ort1.20.0");
        var other   = Directory.CreateDirectory(Path.Combine(_root, "runtime", "directml")).FullName;

        var store = Store(MachineCards.None);

        await WaitFor(() => !Directory.Exists(earlier));
        Assert.IsTrue(Directory.Exists(other), "only a CUDA pack's folder is a pack's");
        Assert.IsNull(store.RuntimePack());
    }

    [TestMethod]
    public async Task AnEarlierPack_StaysUntilTheCurrentOneIsWhole_ThenGoes()
    {
        var earlier = Earlier("cuda-ort1.20.0");
        var server  = new Server();
        var store   = Store(server: server);

        Assert.IsTrue(store.Describe().EarlierRuntimePack, "the app hears this machine had the pack");
        Assert.IsNull(store.RuntimePack());
        await Task.Delay(200);
        Assert.IsTrue(Directory.Exists(earlier), "its files are what the current pack's are taken from");

        LayPack();
        await store.FetchAsync([.. CudaRuntimePack.Files.Select(f => f.RelativePath)], null, CancellationToken.None);

        Assert.AreEqual(0, server.Requests, "every file is here");
        Assert.IsFalse(Directory.Exists(earlier));
        Assert.AreEqual(Path.GetDirectoryName(StorePath.Resolve(_root, CudaRuntimePack.Files[0].RelativePath)), store.RuntimePack());
        Assert.IsFalse(store.Describe().EarlierRuntimePack);
    }

    [TestMethod]
    public async Task AnEarlierPackLeftBesideAWholeOne_GoesWhenTheHostStarts()
    {
        LayPack();
        var earlier = Earlier("cuda-ort1.20.0");

        _ = Store();

        await WaitFor(() => !Directory.Exists(earlier));
    }
}
