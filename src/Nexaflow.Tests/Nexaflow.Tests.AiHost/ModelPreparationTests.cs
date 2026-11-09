using Nexaflow.AiHost;
using Nexaflow.AiHost.Models;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// Turning downloaded files into what a runner reads: each folder prepared once per adapter version, and what an adapter
/// cannot find reported as missing.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class ModelPreparationTests
{
    private string _folder = null!;

    [TestInitialize]
    public void Init() => _folder = TestTemp.CreateSubdirectory("nexaflow-model-").FullName;

    [TestCleanup]
    public void Done() => Directory.Delete(_folder, recursive: true);

    [TestMethod]
    public void AFolderIsPreparedOnce_PerAdapterVersion()
    {
        var adapter = new CountingAdapter();
        var adapters = new ModelAdapters([adapter]);

        adapters.EnsurePrepared(_folder, adapter.Id);
        adapters.EnsurePrepared(_folder, adapter.Id);
        Assert.AreEqual(1, adapter.Prepared);

        adapter.Version = 2;
        adapters.EnsurePrepared(_folder, adapter.Id);
        Assert.AreEqual(2, adapter.Prepared, "a newer adapter prepares again");
    }

    [TestMethod]
    public void AnAdapterThisHostDoesNotHave_IsAnInvalidRequest()
    {
        var ex = Assert.ThrowsExactly<HostRequestException>(() => ModelAdapters.Default.EnsurePrepared(_folder, "nonesuch"));
        Assert.AreEqual(Nexaflow.AiHost.Contracts.HostErrorKind.Invalid, ex.Kind);
    }

    [TestMethod]
    public void MissingFiles_AreReportedAsNotFound()
    {
        var adapter = new MissingFileAdapter();
        var ex = Assert.ThrowsExactly<HostRequestException>(() => new ModelAdapters([adapter]).EnsurePrepared(_folder, adapter.Id));
        Assert.AreEqual(Nexaflow.AiHost.Contracts.HostErrorKind.NotFound, ex.Kind);
    }

    private sealed class CountingAdapter : IModelAdapter
    {
        public string Id => "counting";
        public int Version { get; set; } = 1;
        public int Prepared { get; private set; }
        public void Prepare(string folder) => Prepared++;
    }

    /// <summary>An adapter whose export lacks the file it reads.</summary>
    private sealed class MissingFileAdapter : IModelAdapter
    {
        public string Id => "missing";
        public int Version => 1;
        public void Prepare(string folder) => File.ReadAllText(Path.Combine(folder, "absent.json"));
    }
}
