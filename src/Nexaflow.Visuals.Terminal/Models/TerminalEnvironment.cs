namespace Nexaflow.Visuals.Terminal.Models;

/// <summary>
/// A named terminal configuration: which folder it applies to, what it runs on entry, plus optional
/// start directory and environment-variable overrides. Shared by all terminal features (cmd, and a
/// future PowerShell sibling); each feature's <c>IFeatureConfig</c> stores a list of these.
/// </summary>
public sealed class TerminalEnvironment
{
    public string  Name           { get; set; } = string.Empty;
    public string  TabTitle       { get; set; } = string.Empty;

    /// <summary>Glob the shell's current path is matched against to offer this environment ("*" = any).</summary>
    public string  LocationFilter { get; set; } = "*";

    /// <summary>Command sent automatically once the shell reaches its first prompt.</summary>
    public string? InitialCommand { get; set; }

    /// <summary>Directory the shell starts in. Null = use the launch path / inherit.</summary>
    public string? StartDirectory { get; set; }

    /// <summary>Variables overlaid on the process environment for this environment's shell.</summary>
    public Dictionary<string, string> EnvOverrides { get; set; } = [];

    public bool    IsDefault      { get; set; }
}

/// <summary>Remembers which environment a folder was last launched with ("always use here").</summary>
public sealed class TerminalEnvBinding
{
    public string FolderPath { get; set; } = string.Empty;
    public string EnvName    { get; set; } = string.Empty;
}
