using Nexaflow.Features.Common;
using Nexaflow.Features.Console.ViewModels;
using Nexaflow.Visuals.Terminal;
using Nexaflow.Visuals.Terminal.ChatHandlers;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Features.Console.ChatHandlers;

/// <summary>The AI bar's way into the Console's cmd prompt (<see cref="TerminalQueryHandler"/>).</summary>
public sealed class ConsoleQueryHandler : TerminalQueryHandler
{
    public override string Description =>
        "Runs the typed text, exactly as typed, as a command line in the active Windows command prompt. Choose it only if " +
        "that text is itself a valid cmd command. Choose 0 if it is words asking for something to be done or explained, " +
        "even something a command could do: the assistant then does it.";

    protected override bool Serves(TerminalViewModel terminal) => terminal is CmdTerminalViewModel;
}
