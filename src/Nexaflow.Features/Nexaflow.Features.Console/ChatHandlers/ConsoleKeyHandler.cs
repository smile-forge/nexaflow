using Nexaflow.Features.Common;
using Nexaflow.Features.Console.ViewModels;
using Nexaflow.Interaction;
using Nexaflow.Visuals.Terminal.ChatHandlers;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Features.Console.ChatHandlers;

/// <summary>Routes AI-bar keys to the active cmd terminal (<see cref="TerminalKeyHandler"/>).</summary>
public sealed class ConsoleKeyHandler : TerminalKeyHandler
{
    protected override bool Serves(TerminalViewModel terminal) => terminal is CmdTerminalViewModel;
}
