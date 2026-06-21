using Nexaflow.Providers.Common;
using System.IO;
using System.Text.Json.Serialization;

namespace Nexaflow.Providers.Local;

/// <summary>
/// Configuration for the local (LlamaSharp) provider. Stored per-profile under
/// <c>Contexts\&lt;profile&gt;\local\</c>. The editable model <c>catalog.json</c> and the downloaded
/// GGUF files both live under <see cref="ModelsDir"/>.
/// </summary>
[CustomControl(typeof(Controls.LocalConfigControl))]
public sealed class LocalConfig : IProviderConfig
{
    public string ConfigName   => "local";
    public string FriendlyName => "Local";

    /// <summary>Folder holding <c>catalog.json</c> and the downloaded model files.</summary>
    [ConfigDisplayName("Models folder")]
    public string ModelsDir { get; set; } = DefaultModelsDir;

    /// <summary>Context window (tokens). 0 = use the per-variant value from the catalog.</summary>
    [ConfigDisplayName("Context window (0 = model default)")]
    public int ContextSize { get; set; }

    /// <summary>Layers to offload to the GPU. -1 = all (CUDA when available, else CPU); 0 = force CPU;
    /// a positive value caps the number offloaded.</summary>
    [ConfigDisplayName("GPU layers (-1 = all, 0 = CPU)")]
    public int GpuLayerCount { get; set; } = -1;

    /// <summary>Enable the model's native thinking channel.</summary>
    [ConfigDisplayName("Thinking mode")]
    public bool ThinkingMode { get; set; }

    /// <summary>Server-side tools the local model may call (resolved inside the provider). Defaults to the calculator.</summary>
    public List<string> EnabledServerTools { get; set; } = ["calculator"];

    /// <summary>MCP servers the user has configured as server-side tool sources (wired in a later phase).</summary>
    public List<McpServerEntry> McpServers { get; set; } = [];

    /// <summary><see cref="ModelsDir"/> or the default when blank. Not persisted.</summary>
    [JsonIgnore]
    public string ResolvedModelsDir =>
        string.IsNullOrWhiteSpace(ModelsDir) ? DefaultModelsDir : ModelsDir;

    private static string DefaultModelsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Smile", "nexaflow", "local-models");
}

/// <summary>A user-configured MCP server entry (a future server-side tool source).</summary>
public sealed class McpServerEntry
{
    public string Name      { get; set; } = string.Empty;
    /// <summary>Launch command (stdio transport) or endpoint URL.</summary>
    public string Command   { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public bool   Enabled   { get; set; } = true;
}
