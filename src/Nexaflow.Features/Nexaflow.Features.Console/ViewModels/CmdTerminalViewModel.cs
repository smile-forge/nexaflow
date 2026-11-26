using Nexaflow.Features.Common;
using Nexaflow.IO.Common;

using Nexaflow.IO.Terminal;
using Nexaflow.IO.Terminal.Shells;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Features.Console.ViewModels;

/// <summary>
/// The Console's terminal: cmd.exe on this computer (<see cref="TerminalShellRoute.ForThisComputer"/>). The reusable
/// terminal mechanics live in <see cref="TerminalViewModel"/>;
/// this thin subclass sources its environments / per-folder bindings from <see cref="ConsoleConfig"/>, a folder compared
/// as the file system holding it compares names. A future PowerShell feature is the same shape over its own shell +
/// config, with no dependency on this assembly.
/// </summary>
public sealed class CmdTerminalViewModel : TerminalViewModel
{
    private readonly ConsoleConfig      _config;
    private readonly IVirtualFileSystem _files;

    internal CmdTerminalViewModel(
        ConsoleConfig        config,
        TerminalShellRoute   route,
        IVirtualFileSystem   files,
        IShellServices       shell,
        IAIService           ai,
        TerminalEnvironment? initialEnv,
        bool                 pickerPending = false)
        : base(route, shell, ai)
    {
        _config = config;
        _files  = files;
        SetupInitialState(initialEnv, pickerPending);
    }

    protected override IReadOnlyList<TerminalEnvironment> Environments => _config.Environments;

    // The Configure button deep-links to this feature's section in the workspace Configure overlay.
    protected override string? ConfigSectionName => _config.ConfigName;

    // Watcher backoff comes from this feature's config (the base in Visuals.Terminal has no config type).
    protected override int WatcherInitialMs => Math.Max(1, _config.WatcherInitialSeconds) * 1000;
    protected override int WatcherMaxMs     => Math.Max(_config.WatcherInitialSeconds, _config.WatcherMaxSeconds) * 1000;

    protected override string? FindBoundEnvName(string folderPath)
        => _config.FindBoundEnvName(folderPath, _files.GetNameComparison(folderPath));

    protected override void PersistFolderBinding(string folderPath, string envName)
    {
        _config.BindFolder(folderPath, envName, _files.GetNameComparison(folderPath));
        _shell.SaveFeatureConfig(_config);
    }
}
