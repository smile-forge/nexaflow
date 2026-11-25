using Nexaflow.Features.Common;
using Nexaflow.Features.Console.Controls;
using Nexaflow.Localization;
using Nexaflow.Visuals.Terminal.Models;

namespace Nexaflow.Features.Console;

[CustomControl(typeof(ConsoleEnvironmentsEditorControl))]
[WorkspaceScopedConfig]
public sealed class ConsoleConfig : IFeatureConfig
{
    public string ConfigName   => "console";
    public string FriendlyName => Str.Get("Console.Config.Console");

    public List<TerminalEnvironment> Environments   { get; set; } = [];

    /// <summary>Per-folder "always use this environment here" memory, set from the launch picker.</summary>
    public List<TerminalEnvBinding>  FolderBindings { get; set; } = [];

    /// <summary>First interval (seconds) the background watcher waits before re-checking a command that
    /// didn't return to a prompt; it then backs off (doubling) up to <see cref="WatcherMaxSeconds"/>.</summary>
    public int WatcherInitialSeconds { get; set; } = 5;

    /// <summary>Cap (seconds) on the background watcher's back-off interval.</summary>
    public int WatcherMaxSeconds { get; set; } = 300;

    public ConsoleConfig() => EnsureSeeded();

    public TerminalEnvironment? FindEnvByName(string name)
        => Environments.FirstOrDefault(e =>
               string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The environment pinned to <paramref name="folderPath"/>, its path compared by <paramref name="comparison"/> —
    /// the file system's for that folder (<see cref="Nexaflow.IO.Common.IVirtualFileSystem.GetNameComparison"/>).</summary>
    public string? FindBoundEnvName(string folderPath, StringComparison comparison)
        => FolderBindings
            .FirstOrDefault(b => string.Equals(b.FolderPath, folderPath, comparison))
            ?.EnvName;

    /// <summary>Pins <paramref name="envName"/> to <paramref name="folderPath"/>, replacing the pin of any path that is the
    /// same folder by <paramref name="comparison"/>.</summary>
    public void BindFolder(string folderPath, string envName, StringComparison comparison)
    {
        FolderBindings.RemoveAll(b => string.Equals(b.FolderPath, folderPath, comparison));
        FolderBindings.Add(new TerminalEnvBinding { FolderPath = folderPath, EnvName = envName });
    }

    /// <summary>Guarantees at least one environment so the launch picker is meaningful on first run.</summary>
    private void EnsureSeeded()
    {
        if (Environments.Count == 0)
            Environments.Add(new TerminalEnvironment
            {
                Name     = Str.Get("Console.Environments.CommandPromptName"),
                TabTitle = Str.Get("Console.Page.Title"),
            });
    }
}
