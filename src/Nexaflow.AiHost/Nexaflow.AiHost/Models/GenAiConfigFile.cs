using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.ML.OnnxRuntimeGenAI;
using Nexaflow.AiHost.Contracts;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// A prepared model's <c>genai_config.json</c>. onnxruntime-genai reads a model's providers from it, per sub-model
/// (decoder, embedding, vision, speech), and its API can only change the decoder's — so the adapter writes a
/// provider-neutral <c>genai_config.base.json</c>, and the host writes the config for the runtime it is on before
/// loading. One host runs one runtime, so the folder never needs two.
/// </summary>
internal static class GenAiConfigFile
{
    private const string Base = "genai_config.base.json";
    private const string Live = "genai_config.json";

    private static readonly string[] SubModels = ["decoder", "embedding", "vision", "speech", "encoder", "joiner"];

    /// <summary>ONNX Runtime's warnings about node placement and shape merging are expected on these exports; only
    /// errors are worth the log.</summary>
    private const int ErrorsOnly = 3;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The CUDA options every sub-model runs with: no CUDA graphs (they produced garbage on this export), and
    /// an arena that grows only by what is asked for.</summary>
    private static readonly JsonObject CudaOptions = new()
    {
        ["enable_cuda_graph"]                   = "0",
        ["arena_extend_strategy"]               = "1",
        ["enable_skip_layer_norm_strict_mode"]  = "1",
    };

    public static void WriteBase(string folder, JsonNode config)
    {
        foreach (var name in SubModels)
            if (config["model"]?[name]?["session_options"] is JsonObject options) options.Remove("provider_options");
        Write(Path.Combine(folder, Base), config.ToJsonString(Indented));
    }

    /// <summary>Writes the config for <paramref name="executionProvider"/> (<c>cuda</c> or <c>cpu</c>), when it differs
    /// from what is there; <paramref name="cudaOptions"/> are what the model needs on CUDA beyond the host's own.</summary>
    public static void WriteFor(string folder, string executionProvider, IReadOnlyDictionary<string, string>? cudaOptions = null)
    {
        var config = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, Base)))!;
        foreach (var name in SubModels)
        {
            if (config["model"]?[name] is not JsonObject subModel) continue;
            var options = subModel["session_options"] as JsonObject ?? (JsonObject)(subModel["session_options"] = new JsonObject());
            options["log_severity_level"] = ErrorsOnly;
            options["provider_options"] = executionProvider switch
            {
                ExecutionRuntime.Cpu  => new JsonArray(),
                ExecutionRuntime.Cuda => new JsonArray(new JsonObject { ["cuda"] = Cuda(cudaOptions) }),
                _ => throw new ArgumentOutOfRangeException(nameof(executionProvider), executionProvider, "not a provider this host runs models on"),
            };
        }

        string text = config.ToJsonString(Indented);
        string live = Path.Combine(folder, Live);
        if (!File.Exists(live) || File.ReadAllText(live) != text) Write(live, text);
    }

    private static JsonObject Cuda(IReadOnlyDictionary<string, string>? extra)
    {
        var options = (JsonObject)CudaOptions.DeepClone();
        foreach (var (key, value) in extra ?? new Dictionary<string, string>()) options[key] = value;
        return options;
    }

    /// <summary>Writes the model's config for <paramref name="executionProvider"/> and opens it; the config must live as
    /// long as the model.</summary>
    public static (Config Config, Model Model) Open(ModelSpec spec, string executionProvider)
    {
        WriteFor(spec.Folder, executionProvider, spec.CudaOptions);
        var config = new Config(spec.Folder);
        try
        {
            return (config, new Model(config));
        }
        catch
        {
            config.Dispose();
            throw;
        }
    }

    private static void Write(string path, string text)
    {
        File.WriteAllText(path + ".tmp", text);
        File.Move(path + ".tmp", path, overwrite: true);
    }
}
