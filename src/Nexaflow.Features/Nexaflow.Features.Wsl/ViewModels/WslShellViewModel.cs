using Nexaflow.Features.Common;
using Nexaflow.IO.Terminal;
using Nexaflow.IO.Terminal.Shells;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Features.Wsl.ViewModels;

/// <summary>
/// A WSL distro's shell: the shared terminal (<see cref="TerminalViewModel"/>) over the distro's own shell, its folder,
/// files and variables the distro's. It keeps no named environments of its own — the distro's startup files are where a
/// shell is set up, and the Environment tab keeps a variable there for new shells.
/// </summary>
public sealed class WslShellViewModel : TerminalViewModel
{
    internal WslShellViewModel(TerminalShellRoute route, IShellServices shell, IAIService ai)
        : base(route, shell, ai)
        => SetupInitialState(null);

    protected override IReadOnlyList<TerminalEnvironment> Environments => [];

    protected override string? FindBoundEnvName(string folderPath) => null;

    protected override void PersistFolderBinding(string folderPath, string envName) { }
}
