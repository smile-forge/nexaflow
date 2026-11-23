using Nexaflow.Features.Wsl.ViewModels;
using Nexaflow.Visuals.Terminal.ChatHandlers;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Features.Wsl.ChatHandlers;

/// <summary>Routes AI-bar keys to the active WSL shell (<see cref="TerminalKeyHandler"/>).</summary>
public sealed class WslShellKeyHandler : TerminalKeyHandler
{
    protected override bool Serves(TerminalViewModel terminal) => terminal is WslShellViewModel;
}
