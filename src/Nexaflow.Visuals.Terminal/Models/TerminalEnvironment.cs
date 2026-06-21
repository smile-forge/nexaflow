namespace Nexaflow.Visuals.Terminal.Models;

/// <summary>
/// A named terminal configuration: what it runs on entry plus optional environment-variable overrides.
/// Shared by all terminal features (cmd, and a future PowerShell sibling); each feature's
/// <c>IFeatureConfig</c> stores a list of these. The shell always opens in the launch path, so an
/// environment carries no folder of its own.
/// </summary>
public sealed class TerminalEnvironment
{
    public string  Name           { get; set; } = string.Empty;
    public string  TabTitle       { get; set; } = string.Empty;

    /// <summary>Command sent automatically once the shell reaches its first prompt.</summary>
    public string? InitialCommand { get; set; }

    /// <summary>Variables overlaid on the process environment for this environment's shell.</summary>
    public Dictionary<string, string> EnvOverrides { get; set; } = [];
}

/// <summary>Remembers which environment a folder was last launched with ("always use here").</summary>
public sealed class TerminalEnvBinding
{
    public string FolderPath { get; set; } = string.Empty;
    public string EnvName    { get; set; } = string.Empty;
}
