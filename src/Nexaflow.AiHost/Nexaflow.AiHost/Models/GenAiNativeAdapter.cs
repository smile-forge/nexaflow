using System.Text.Json.Nodes;

namespace Nexaflow.AiHost.Models;

/// <summary>
/// For an export onnxruntime-genai runs as published: its own config, downloaded as <c>genai_config.native.json</c>,
/// becomes the provider-neutral base the host writes each runtime's config from.
/// </summary>
internal sealed class GenAiNativeAdapter : IModelAdapter
{
    public const string NativeConfig = "genai_config.native.json";

    public string Id => "genai-native";

    public int Version => 1;

    public void Prepare(string folder)
        => GenAiConfigFile.WriteBase(folder, JsonNode.Parse(File.ReadAllText(Path.Combine(folder, NativeConfig)))!);
}
