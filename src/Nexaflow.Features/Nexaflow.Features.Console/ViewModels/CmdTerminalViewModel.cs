using Nexaflow.Features.Common;
using Nexaflow.IO.Terminal;
using Nexaflow.Visuals.Terminal.Models;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Features.Console.ViewModels;

/// <summary>
/// The cmd.exe terminal. The reusable terminal mechanics live in <see cref="TerminalViewModel"/>; this
/// thin subclass supplies the cmd shell (the default <see cref="PseudoConsoleHostService"/>) and sources
/// its environments / per-folder bindings from <see cref="ConsoleConfig"/>. A future PowerShell feature
/// is the same shape over its own shell + config, with no dependency on this assembly.
/// </summary>
public sealed class CmdTerminalViewModel : TerminalViewModel
{
    private readonly ConsoleConfig _config;

    internal CmdTerminalViewModel(
        ConsoleConfig        config,
        IShellServices       shell,
        string?              initialPath,
        TerminalEnvironment? initialEnv,
        bool                 pickerPending = false)
        : base(new PseudoConsoleHostService(), shell)
    {
        _config = config;
        SetupInitialState(initialEnv, initialPath, pickerPending);
    }

    protected override IReadOnlyList<TerminalEnvironment> Environments => _config.Environments;

    protected override string? FindBoundEnvName(string folderPath) => _config.FindBoundEnvName(folderPath);

    protected override void PersistFolderBinding(string folderPath, string envName)
    {
        _config.BindFolder(folderPath, envName);
        _shell.SaveFeatureConfig(_config);
    }

    // cmd's `cd /d "…"` and `set "NAME=value"` are the base-class defaults — no override needed.
}
