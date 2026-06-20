using Nexaflow.Features.Common;
using Nexaflow.Features.Console.Controls;
using Nexaflow.Visuals.Terminal.Models;

namespace Nexaflow.Features.Console;

[CustomControl(typeof(ConsoleEnvironmentsEditorControl))]
[WorkspaceScopedConfig]
public sealed class ConsoleConfig : IFeatureConfig
{
    public string ConfigName   => "console";
    public string FriendlyName => "Console";

    public List<TerminalEnvironment> Environments   { get; set; } = [];

    /// <summary>Per-folder "always use this environment here" memory, set from the launch picker.</summary>
    public List<TerminalEnvBinding>  FolderBindings { get; set; } = [];

    /// <summary>First interval (seconds) the background watcher waits before re-checking a command that
    /// didn't return to a prompt; it then backs off (doubling) up to <see cref="WatcherMaxSeconds"/>.</summary>
    public int WatcherInitialSeconds { get; set; } = 5;

    /// <summary>Cap (seconds) on the background watcher's back-off interval.</summary>
    public int WatcherMaxSeconds { get; set; } = 300;

    public ConsoleConfig() => EnsureSeeded();

    public TerminalEnvironment? GetDefaultEnv()
        => Environments.FirstOrDefault(e => e.IsDefault)
        ?? Environments.FirstOrDefault();

    public TerminalEnvironment? FindEnvByName(string name)
        => Environments.FirstOrDefault(e =>
               string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    public string? FindBoundEnvName(string folderPath)
        => FolderBindings
            .FirstOrDefault(b => string.Equals(b.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase))
            ?.EnvName;

    public void BindFolder(string folderPath, string envName)
    {
        FolderBindings.RemoveAll(b =>
            string.Equals(b.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase));
        FolderBindings.Add(new TerminalEnvBinding { FolderPath = folderPath, EnvName = envName });
    }

    /// <summary>Guarantees at least one environment so the default/picker are meaningful on first run.</summary>
    private void EnsureSeeded()
    {
        if (Environments.Count == 0)
            Environments.Add(new TerminalEnvironment
            {
                Name = "Command Prompt", TabTitle = "Console", LocationFilter = "*", IsDefault = true
            });
    }
}
