using Nexaflow.Features.Common;
using Nexaflow.Visuals.Terminal;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Features.Console.ChatHandlers;

/// <summary>
/// The AI bar's way into the active terminal. A <c>&gt;</c> sends the line there, always. A bare line is judged
/// (<see cref="TerminalViewModel.JudgeConsoleLine"/>): a command wins outright and no model sees it; a request scores
/// nothing and is the assistant's; an unclear one scores too low to win outright, which has the router ask the
/// Disambiguation model, and it runs only if that says it is a command.
/// </summary>
public sealed class ConsoleQueryHandler : IQueryHandler
{
    private const float Certain = 0.95f;

    /// <summary>Under the router's bar for a clear winner, so a model is asked.</summary>
    private const float Doubtful = 0.5f;

    public string Description =>
        "Runs the typed text, exactly as typed, as a command line in the active terminal. Choose it only if that text is " +
        "itself a valid command for that shell. Choose 0 if it is words asking for something to be done or explained, " +
        "even something a command could do: the assistant then does it.";

    public string? Symbol => ">";

    public float CanProcess(string input, bool prefixed, IPageViewModel? pageVm = null)
    {
        if (pageVm is not TerminalViewModel terminal) return 0f;
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
        (pageVm as TerminalViewModel)?.SendCommand(input);
        return Task.FromResult<string?>(null);   // silent — the output is the terminal itself
    }
}
