using Nexaflow.Features.Common;
using Nexaflow.IO.Terminal;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Visuals.Terminal.ChatHandlers;

/// <summary>
/// The AI bar's way into a terminal page, for the feature whose terminal it is (<see cref="Serves"/>). A <c>&gt;</c> sends the
/// line there, always. A bare line is judged (<see cref="TerminalViewModel.JudgeConsoleLine"/>): a command wins outright and
/// no model sees it; a request scores nothing and is the assistant's; an unclear one scores too low to win outright, which
/// has the router ask the Disambiguation model, and it runs only if that says it is a command.
/// </summary>
public abstract class TerminalQueryHandler : IQueryHandler
{
    private const float Certain = 0.95f;

    /// <summary>Under the router's bar for a clear winner, so a model is asked.</summary>
    private const float Doubtful = 0.5f;

    public abstract string Description { get; }

    public string? Symbol => ">";

    /// <summary>Whether <paramref name="terminal"/> is this feature's terminal.</summary>
    protected abstract bool Serves(TerminalViewModel terminal);

    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not TerminalViewModel terminal || !Serves(terminal)) return 0f;
        if (prefixed) return Certain;

        return terminal.JudgeConsoleLine(input) switch
        {
            ConsoleLineVerdict.Command => Certain,
            ConsoleLineVerdict.Unclear => Doubtful,
            _                          => 0f,
        };
    }

    public Task<string?> ProcessAsync(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is TerminalViewModel terminal && Serves(terminal)) terminal.SendCommand(input);
        return Task.FromResult<string?>(null);   // silent — the output is the terminal itself
    }
}
