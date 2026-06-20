using Nexaflow.Features.Common;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Features.Console.ChatHandlers;

/// <summary>
/// Routes a command typed in the AI bar to the active terminal instead of the AI: an explicit
/// <c>&gt;</c> prefix (via the handler symbol) or a bare line the terminal recognises as a command.
/// Gated on <see cref="TerminalViewModel.IsConsoleCommand"/> so natural language still reaches the
/// agent — unlike the pre-PR#95 handler, it never claims arbitrary input while a console is active.
/// The logic lives on <see cref="TerminalViewModel"/> so a future PowerShell feature ships the same shim.
/// </summary>
public sealed class ConsoleQueryHandler : IQueryHandler
{
    public string Description => "Runs a command line in the active terminal.";

    public string? Symbol => ">";

    public float CanProcess(string input, IPageViewModel? pageVm = null)
        => pageVm is TerminalViewModel vm && vm.IsConsoleCommand(input) ? 0.95f : 0f;

    public Task<string?> ProcessAsync(string input, IPageViewModel? pageVm = null)
    {
        (pageVm as TerminalViewModel)?.SendCommand(input);
        return Task.FromResult<string?>(null);   // silent — the output is the terminal itself
    }
}
