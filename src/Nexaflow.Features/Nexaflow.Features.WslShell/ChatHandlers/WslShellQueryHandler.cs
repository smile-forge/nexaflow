using Nexaflow.Features.WslShell.ViewModels;
using Nexaflow.Visuals.Terminal.ChatHandlers;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Features.WslShell.ChatHandlers;

/// <summary>The AI bar's way into a WSL distro's shell (<see cref="TerminalQueryHandler"/>).</summary>
public sealed class WslShellQueryHandler : TerminalQueryHandler
{
    public override string Description =>
        "Runs the typed text, exactly as typed, as a command line in the active WSL distro's Linux shell. Choose it only if " +
        "that text is itself a valid shell command. Choose 0 if it is words asking for something to be done or explained, " +
        "even something a command could do: the assistant then does it.";

    protected override bool Serves(TerminalViewModel terminal) => terminal is WslShellViewModel;
}
