using Nexaflow.Core.AI;
using Nexaflow.Core.Services;

namespace Nexaflow.Core.Models;

/// <summary>
/// Runtime container for one workspace session: owns an <see cref="AIService"/>,
/// per-context providers, a tab/window registry (<see cref="ShellServices"/>), and a ribbon layout.
/// Created lazily when a window first opens for a given <see cref="WorkContextConfig"/>, and
/// removed when that window closes. Multiple instances can share the same config.
/// </summary>
public class WorkContext
{
    public WorkContextConfig Config { get; }

    public string Name  => Config.Name;
    public string Color => Config.Color;
    public string Icon  => Config.Icon;

    public AiConfig         AiConfig    { get; set; } = new();
    public ProviderSet?     Providers   { get; internal set; }
    public AIService?       AiService   { get; internal set; }
    public ShellServices?   ShellServices  { get; internal set; }
    public RibbonLayoutService? RibbonService { get; internal set; }

    public WorkContext(WorkContextConfig config) => Config = config;

    /// <summary>Convenience constructor for tests and defaults.</summary>
    public WorkContext() : this(new WorkContextConfig()) { }
}
