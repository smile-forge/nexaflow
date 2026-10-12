using System.Text.Json.Nodes;
using Google.Protobuf;
using Onnx;
using static Onnx.TensorProto.Types;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// The onnx-community (transformers.js) export of Gemma 4 E4B, q4f16, made runnable by onnxruntime-genai's multimodal
/// pipeline. Only graph headers change — a few hundred kilobytes — and every weight stays in the downloaded
/// <c>.onnx_data</c> files, which the new headers name by the same relative paths:
/// <list type="bullet">
/// <item><c>onnx/embedding.onnx</c>: the token embedding with image and audio features merged in at their placeholder
/// tokens, and the per-layer inputs flattened.</item>
/// <item><c>onnx/decoder.onnx</c>: takes the flattened per-layer inputs, and computes logits for every position
/// (<c>num_logits_to_keep</c> fixed at 0).</item>
/// <item><c>genai_config.base.json</c>: the native GenAI export's config, re-pointed at these graphs, without providers
/// (the host adds them for the runtime it is on), and with an unshared KV buffer — this export concatenates its cache.</item>
/// </list>
/// The vision and audio encoders run as downloaded.
/// </summary>
internal sealed class Gemma4OnnxCommunityAdapter : IModelAdapter
{
    public string Id => "gemma4-onnx-community-q4f16";
    public int Version => 1;

    private const long ImageToken = 258880, AudioToken = 258881;
    private const int Hidden = 2560, PerLayerLayers = 42, PerLayerDim = 256;

    public void Prepare(string folder)
    {
        string onnx = Path.Combine(folder, "onnx");
        Write(AdaptEmbedding(Read(Path.Combine(onnx, "embed_tokens_q4f16.onnx"))), Path.Combine(onnx, "embedding.onnx"));
        Write(AdaptDecoder(Read(Path.Combine(onnx, "decoder_model_merged_q4f16.onnx"))), Path.Combine(onnx, "decoder.onnx"));
        GenAiConfigFile.WriteBase(folder, AdaptConfig(JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "genai_config.native.json")))!));
    }

    private static ModelProto Read(string path) => ModelProto.Parser.ParseFrom(File.ReadAllBytes(path));

    private static void Write(ModelProto model, string path)
    {
        File.WriteAllBytes(path + ".tmp", model.ToByteArray());
        File.Move(path + ".tmp", path, overwrite: true);
    }

    internal static ModelProto AdaptEmbedding(ModelProto model)
    {
        var g = model.Graph;
        var producer = g.Node.First(n => n.Output.Contains("inputs_embeds"));
        producer.Output[producer.Output.IndexOf("inputs_embeds")] = "text_inputs_embeds";

        g.Input.Add(Tensor("image_features", DataType.Float, "num_image_tokens", Hidden));
        g.Input.Add(Tensor("audio_features", DataType.Float, "num_audio_tokens", Hidden));
        g.Initializer.Add(Int64Scalar("genai/image_token_id", ImageToken));
        g.Initializer.Add(Int64Scalar("genai/audio_token_id", AudioToken));
        g.Initializer.Add(Int64Vector("genai/ple_flat_shape", 0, 0, -1));

        g.Node.AddRange(Scatter("text_inputs_embeds", "genai/image_token_id", "image_features", "genai/with_images", "image"));
        g.Node.AddRange(Scatter("genai/with_images", "genai/audio_token_id", "audio_features", "inputs_embeds", "audio"));

        // The per-layer table ends in Reshape(…, [0,0,42,256]); flatten it for GenAI, which passes it on as one tensor.
        var reshape = g.Node.First(n => n.OpType == "Reshape" && n.Output[0].Contains("per_layer"));
        string oldShape = reshape.Input[1];
        reshape.Input[1] = "genai/ple_flat_shape";
        if (!g.Node.Any(n => n.Input.Contains(oldShape)) && g.Initializer.FirstOrDefault(t => t.Name == oldShape) is { } unused)
            g.Initializer.Remove(unused);
        SetShape(g.Output.First(o => o.Name == "per_layer_inputs"), "batch_size", "sequence_length", PerLayerLayers * PerLayerDim);
        if (g.ValueInfo.FirstOrDefault(v => v.Name == reshape.Output[0]) is { } declared)
            SetShape(declared, "batch_size", "sequence_length", PerLayerLayers * PerLayerDim);
        return model;
    }

    internal static ModelProto AdaptDecoder(ModelProto model)
    {
        var g = model.Graph;
        foreach (var node in g.Node)
            for (int i = 0; i < node.Input.Count; i++)
                if (node.Input[i] == "per_layer_inputs") node.Input[i] = "genai/per_layer_inputs_4d";

        g.Initializer.Add(Int64Vector("genai/ple_4d_shape", 0, 0, PerLayerLayers, PerLayerDim));
        g.Node.Insert(0, Node("Reshape", ["per_layer_inputs", "genai/ple_4d_shape"], ["genai/per_layer_inputs_4d"]));
        SetShape(g.Input.First(i => i.Name == "per_layer_inputs"), "batch_size", "sequence_length", PerLayerLayers * PerLayerDim);

        // Logits for every position: GenAI chooses what it reads.
        g.Input.Remove(g.Input.First(i => i.Name == "num_logits_to_keep"));
        g.Initializer.Add(Int64Scalar("num_logits_to_keep", 0));
        return model;
    }

    internal static JsonNode AdaptConfig(JsonNode config)
    {
        var model = config["model"]!;
        model["decoder"]!["filename"] = "onnx/decoder.onnx";
        model["decoder"]!["inputs"] = new JsonObject
        {
            ["inputs_embeds"]    = "inputs_embeds",
            ["attention_mask"]   = "attention_mask",
            ["position_ids"]     = "position_ids",
            ["per_layer_inputs"] = "per_layer_inputs",
            ["past_key_names"]   = "past_key_values.%d.key",
            ["past_value_names"] = "past_key_values.%d.value",
        };
        model["embedding"]!["filename"] = "onnx/embedding.onnx";
        model["embedding"]!["outputs"] = new JsonObject { ["inputs_embeds"] = "inputs_embeds", ["per_layer_inputs"] = "per_layer_inputs" };
        model["vision"]!["filename"] = "onnx/vision_encoder_q4f16.onnx";
        model["speech"]!["filename"] = "onnx/audio_encoder_q4f16.onnx";
        model["speech"]!["outputs"] = new JsonObject { ["audio_features"] = "audio_features" };
        config["search"]!["past_present_share_buffer"] = false;
        return config;
    }

    /// <summary>Writes feature rows into the embedding rows whose token is the placeholder, in order (batch 1).</summary>
    private static IEnumerable<NodeProto> Scatter(string source, string token, string features, string result, string tag) =>
    [
        Node("Equal", ["input_ids", token], [$"genai/{tag}_mask"]),
        Node("NonZero", [$"genai/{tag}_mask"], [$"genai/{tag}_idx_t"]),
        Node("Transpose", [$"genai/{tag}_idx_t"], [$"genai/{tag}_idx"], n => n.Attribute.Add(Ints("perm", 1, 0))),
        Node("ScatterND", [source, $"genai/{tag}_idx", features], [result]),
    ];

    private static NodeProto Node(string op, string[] inputs, string[] outputs, Action<NodeProto>? configure = null)
    {
        var node = new NodeProto { OpType = op };
        node.Input.AddRange(inputs);
        node.Output.AddRange(outputs);
        configure?.Invoke(node);
        return node;
    }

    private static AttributeProto Ints(string name, params long[] values)
    {
        var attribute = new AttributeProto { Name = name, Type = AttributeProto.Types.AttributeType.Ints };
        attribute.Ints.AddRange(values);
        return attribute;
    }

    private static TensorProto Int64Scalar(string name, long value)
        => new() { Name = name, DataType = (int)DataType.Int64, RawData = ByteString.CopyFrom(BitConverter.GetBytes(value)) };

    private static TensorProto Int64Vector(string name, params long[] values)
    {
        var tensor = new TensorProto { Name = name, DataType = (int)DataType.Int64,
                                       RawData = ByteString.CopyFrom([.. values.SelectMany(BitConverter.GetBytes)]) };
        tensor.Dims.Add(values.Length);
        return tensor;
    }

    private static ValueInfoProto Tensor(string name, DataType type, params object[] dims)
    {
        var info = new ValueInfoProto
        {
            Name = name,
            Type = new TypeProto { TensorType = new TypeProto.Types.Tensor { ElemType = (int)type, Shape = new TensorShapeProto() } },
        };
        SetShape(info, dims);
        return info;
    }

    private static void SetShape(ValueInfoProto info, params object[] dims)
    {
        var shape = info.Type.TensorType.Shape;
        shape.Dim.Clear();
        foreach (var dim in dims)
            shape.Dim.Add(dim is string symbol
                ? new TensorShapeProto.Types.Dimension { DimParam = symbol }
                : new TensorShapeProto.Types.Dimension { DimValue = Convert.ToInt64(dim) });
    }
}
