using Nexaflow.Core.AI;
using Nexaflow.Core.Services;
using System.Text.Json.Serialization;

namespace Nexaflow.Core.Models;

public class WorkContext
{
    public string    Name     { get; set; } = "Default";
    public string    Color    { get; set; } = "#5B8CFF";
    public string    Icon     { get; set; } = "⬡";

    /// <summary>Per-context AI ability configuration (serialised inside WorkContextsConfig).</summary>
    public AiConfig  AiConfig { get; set; } = new();

    /// <summary>Runtime-only AIService instance — not serialised.</summary>
    [JsonIgnore]
    public AIService? AiService { get; internal set; }

    /// <summary>Runtime-only ShellServices instance — not serialised.</summary>
    [JsonIgnore]
    public ShellServices? ShellServices { get; internal set; }
}
