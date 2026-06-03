using Nexaflow.Providers.Common;
using System.Text.Json.Serialization;

namespace Nexaflow.Providers.Ollama;

public sealed class OllamaConfig : IProviderConfig
{
    public string ConfigName   => "ollama";
    public string FriendlyName => "Ollama";

    [ConfigDisplayName("Ollama URL")]
    public string Url { get; set; } = "http://localhost:11434";

    /// <summary>When set, models stay resident for the lifetime of the server (keep_alive = -1).</summary>
    [ConfigDisplayName("Keep loaded while running")]
    [DisabledIfSet(nameof(KeepAliveMinutes))]
    public bool KeepWhileRunning { get; set; }

    /// <summary>
    /// Idle minutes before Ollama unloads a model. 0 = server default. Mutually exclusive with
    /// <see cref="KeepWhileRunning"/> — set one or the other.
    /// </summary>
    [ConfigDisplayName("Keep-alive (minutes, 0 = default)")]
    [DisabledIfSet(nameof(KeepWhileRunning))]
    public int KeepAliveMinutes { get; set; }

    /// <summary>Enables the model's thinking mode (think=true); the thinking output is hidden.</summary>
    [ConfigDisplayName("Thinking mode")]
    public bool ThinkingMode { get; set; }

    /// <summary>
    /// The <c>keep_alive</c> value to pass to the API: <c>"-1"</c> while-running, <c>"{n}m"</c> for a
    /// minute count, or null to leave the Ollama default. <see cref="KeepWhileRunning"/> wins.
    /// </summary>
    [JsonIgnore]
    public string? KeepAliveValue =>
        KeepWhileRunning      ? "-1"
        : KeepAliveMinutes > 0 ? $"{KeepAliveMinutes}m"
        : null;
}
