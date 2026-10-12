using System.Text.Json.Nodes;
using Google.Protobuf;
using Nexaflow.AiHost;
using Nexaflow.AiHost.Models;
using Nexaflow.Tests.Fixtures;
using Onnx;

namespace Nexaflow.Tests.AiHost;

/// <summary>
/// Turning downloaded files into a model onnxruntime-genai runs: the Gemma adapter's graph-header rewrites, its
/// config, and the config written for the runtime the host is on.
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

    private static ValueInfoProto Value(string name, params long[] dims)
    {
        var shape = new TensorShapeProto();
        foreach (var dim in dims) shape.Dim.Add(new TensorShapeProto.Types.Dimension { DimValue = dim });
        return new ValueInfoProto { Name = name, Type = new TypeProto { TensorType = new TypeProto.Types.Tensor { ElemType = 1, Shape = shape } } };
    }

    private static NodeProto Node(string op, string[] inputs, string[] outputs)
    {
        var node = new NodeProto { OpType = op };
        node.Input.AddRange(inputs);
        node.Output.AddRange(outputs);
        return node;
    }

    /// <summary>The embedding graph's shape as onnx-community exports it, weights aside.</summary>
    private static ModelProto Embedding()
    {
        var g = new GraphProto();
        g.Input.Add(Value("input_ids", 1, 1));
        g.Node.Add(Node("Gather", ["embed.weight", "input_ids"], ["inputs_embeds"]));
        g.Node.Add(Node("Gather", ["per_layer.weight", "input_ids"], ["/model/per_layer/Gather"]));
        g.Node.Add(Node("Reshape", ["/model/per_layer/Gather", "/model/constants/INT64/[0, 0, 42, 256]"], ["/model/embed_tokens_per_layer/Reshape/output_0"]));
        g.Node.Add(Node("Identity", ["/model/embed_tokens_per_layer/Reshape/output_0"], ["per_layer_inputs"]));
        g.Initializer.Add(new TensorProto { Name = "/model/constants/INT64/[0, 0, 42, 256]", DataType = 7 });
        g.ValueInfo.Add(Value("/model/embed_tokens_per_layer/Reshape/output_0", 1, 1, 42, 256));
        g.Output.Add(Value("inputs_embeds", 1, 1, 2560));
        g.Output.Add(Value("per_layer_inputs", 1, 1, 42, 256));
        return new ModelProto { Graph = g };
    }

    private static ModelProto Decoder()
    {
        var g = new GraphProto();
        g.Input.Add(Value("inputs_embeds", 1, 1, 2560));
        g.Input.Add(Value("per_layer_inputs", 1, 1, 42, 256));
        g.Input.Add(Value("num_logits_to_keep"));
        g.Node.Add(Node("Mul", ["per_layer_inputs", "scale"], ["/model/layers.0/per_layer"]));
        g.Node.Add(Node("Slice", ["logits_all", "num_logits_to_keep"], ["logits"]));
        return new ModelProto { Graph = g };
    }

    private static string Dims(ValueInfoProto value)
        => string.Join(",", value.Type.TensorType.Shape.Dim.Select(d => d.DimParam is { Length: > 0 } p ? p : d.DimValue.ToString()));

    [TestMethod]
    public void TheEmbedding_MergesImageAndAudioFeaturesAtTheirPlaceholders_AndFlattensThePerLayerInputs()
    {
        var g = Gemma4OnnxCommunityAdapter.AdaptEmbedding(Embedding()).Graph;

        CollectionAssert.IsSubsetOf(new[] { "image_features", "audio_features" }, g.Input.Select(i => i.Name).ToArray());
        Assert.AreEqual("text_inputs_embeds", g.Node[0].Output[0], "the token embedding feeds the merge");
        var merges = g.Node.Where(n => n.OpType == "ScatterND").ToList();
        Assert.AreEqual(2, merges.Count);
        Assert.AreEqual("inputs_embeds", merges[^1].Output[0]);
        Assert.AreEqual("genai/ple_flat_shape", g.Node.Single(n => n.OpType == "Reshape").Input[1]);
        Assert.AreEqual("batch_size,sequence_length,10752", Dims(g.Output.Single(o => o.Name == "per_layer_inputs")));
        Assert.AreEqual("batch_size,sequence_length,10752", Dims(g.ValueInfo.Single()), "what the reshape is declared to give matches what it gives");
        Assert.IsFalse(g.Initializer.Any(t => t.Name == "/model/constants/INT64/[0, 0, 42, 256]"), "the shape it no longer uses is dropped");
    }

    [TestMethod]
    public void TheDecoder_TakesTheFlattenedPerLayerInputs_AndComputesEveryPositionsLogits()
    {
        var g = Gemma4OnnxCommunityAdapter.AdaptDecoder(Decoder()).Graph;

        Assert.AreEqual("Reshape", g.Node[0].OpType);
        Assert.AreEqual("genai/per_layer_inputs_4d", g.Node[1].Input[0]);
        Assert.AreEqual("batch_size,sequence_length,10752", Dims(g.Input.Single(i => i.Name == "per_layer_inputs")));
        Assert.IsFalse(g.Input.Any(i => i.Name == "num_logits_to_keep"), "GenAI does not feed it");
        Assert.AreEqual(0L, BitConverter.ToInt64(g.Initializer.Single(t => t.Name == "num_logits_to_keep").RawData.ToByteArray()));
    }

    private const string Native = """
        { "model": {
            "decoder":   { "filename": "decoder/model.onnx", "session_options": { "log_id": "g", "provider_options": [ { "cuda": {} } ] } },
            "embedding": { "filename": "embedding/model.onnx", "session_options": { "provider_options": [ { "cuda": {} } ] } },
            "vision":    { "filename": "vision_encoder/model.onnx", "session_options": { "provider_options": [ { "cuda": {} } ] } },
            "speech":    { "filename": "audio_encoder/model.onnx", "session_options": { "provider_options": [ { "cuda": {} } ] } } },
          "search": { "max_length": 131072, "past_present_share_buffer": true } }
        """;

    [TestMethod]
    public void TheConfig_PointsAtTheAdaptedGraphs_WithoutProviders_AndAnUnsharedCache()
    {
        GenAiConfigFile.WriteBase(_folder, Gemma4OnnxCommunityAdapter.AdaptConfig(JsonNode.Parse(Native)!));
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(_folder, "genai_config.base.json")))!;

        Assert.AreEqual("onnx/decoder.onnx", (string)config["model"]!["decoder"]!["filename"]!);
        Assert.AreEqual("per_layer_inputs", (string)config["model"]!["decoder"]!["inputs"]!["per_layer_inputs"]!);
        Assert.AreEqual("onnx/vision_encoder_q4f16.onnx", (string)config["model"]!["vision"]!["filename"]!);
        Assert.IsNull(config["model"]!["decoder"]!["session_options"]!["provider_options"]);
        Assert.AreEqual("g", (string)config["model"]!["decoder"]!["session_options"]!["log_id"]!, "the rest of the native config stays");
        Assert.IsFalse((bool)config["search"]!["past_present_share_buffer"]!, "this export concatenates its cache");
    }

    [TestMethod]
    [DataRow(ExecutionRuntime.Cuda, 1)]
    [DataRow(ExecutionRuntime.Cpu, 0)]
    public void TheLiveConfig_GivesEverySubModelTheRuntimesProvider(string provider, int providers)
    {
        GenAiConfigFile.WriteBase(_folder, Gemma4OnnxCommunityAdapter.AdaptConfig(JsonNode.Parse(Native)!));

        GenAiConfigFile.WriteFor(_folder, provider);

        var model = JsonNode.Parse(File.ReadAllText(Path.Combine(_folder, "genai_config.json")))!["model"]!;
        foreach (var subModel in new[] { "decoder", "embedding", "vision", "speech" })
        {
            var options = model[subModel]!["session_options"]!["provider_options"]!.AsArray();
            Assert.AreEqual(providers, options.Count, subModel);
            if (providers > 0) Assert.AreEqual("0", (string)options[0]!["cuda"]!["enable_cuda_graph"]!);
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
            ModelAdapters.Default.EnsurePrepared(_folder, new Gemma4OnnxCommunityAdapter().Id));
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
