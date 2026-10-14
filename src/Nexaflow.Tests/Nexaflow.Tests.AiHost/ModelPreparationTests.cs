using System.Text.Json.Nodes;
using Nexaflow.AiHost;
using Nexaflow.AiHost.Models;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// Turning downloaded files into a model onnxruntime-genai runs: an export's own config made provider-neutral, the
/// config written for the runtime the host is on, and each folder prepared once per adapter version.
/// </summary>
[TestClass]
[CoversNode("ai-host")]
public class ModelPreparationTests
{
    private string _folder = null!;

    [TestInitialize]
    public void Init() => _folder = Directory.CreateTempSubdirectory("nexaflow-model-").FullName;

    [TestCleanup]
    public void Done() => Directory.Delete(_folder, recursive: true);

    private const string Native = """
        { "model": {
            "encoder": { "filename": "encoder.onnx", "session_options": { "log_id": "w", "provider_options": [ { "cuda": {} } ] } },
            "decoder": { "filename": "decoder.onnx", "session_options": { "provider_options": [ { "cuda": {} } ] } } },
          "search": { "max_length": 448 } }
        """;

    [TestMethod]
    public void ANativeExport_HasItsProvidersTakenOut_AndTheRestKept()
    {
        File.WriteAllText(Path.Combine(_folder, GenAiNativeAdapter.NativeConfig), Native);

        ModelAdapters.Default.EnsurePrepared(_folder, new GenAiNativeAdapter().Id);

        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_folder, "genai_config.base.json")))!;
        Assert.IsNull(config["model"]!["decoder"]!["session_options"]!["provider_options"]);
        Assert.AreEqual("w", (string)config["model"]!["encoder"]!["session_options"]!["log_id"]!, "the rest of the native config stays");
        Assert.AreEqual(448, (int)config["search"]!["max_length"]!);
    }

    [TestMethod]
    [DataRow(1, 1)]
    [DataRow(null, 0)]
    public void TheLiveConfig_PutsEverySubModelOnTheGpuItsSpecNames_OrTheCpu(int? device, int providers)
    {
        GenAiConfigFile.WriteBase(_folder, JsonNode.Parse(Native)!);

        GenAiConfigFile.WriteFor(_folder, device);

        var model = JsonNode.Parse(File.ReadAllText(Path.Combine(_folder, "genai_config.json")))!["model"]!;
        foreach (var subModel in new[] { "encoder", "decoder" })
        {
            var options = model[subModel]!["session_options"]!["provider_options"]!.AsArray();
            Assert.AreEqual(providers, options.Count, subModel);
            if (providers == 0) continue;
            Assert.AreEqual("0", (string)options[0]!["cuda"]!["enable_cuda_graph"]!);
            Assert.AreEqual("1", (string)options[0]!["cuda"]!["device_id"]!, "on the GPU the spec names");
        }
    }

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
        var ex = Assert.ThrowsExactly<HostRequestException>(() =>
            ModelAdapters.Default.EnsurePrepared(_folder, new GenAiNativeAdapter().Id));
        Assert.AreEqual(Nexaflow.AiHost.Contracts.HostErrorKind.NotFound, ex.Kind);
    }

    private sealed class CountingAdapter : IModelAdapter
    {
        public string Id => "counting";
        public int Version { get; set; } = 1;
        public int Prepared { get; private set; }
        public void Prepare(string folder) => Prepared++;
    }
}
