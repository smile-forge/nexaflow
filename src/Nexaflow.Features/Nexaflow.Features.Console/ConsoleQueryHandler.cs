using Nexaflow.Features.Common;
using Nexaflow.Features.Console.ViewModels;

namespace Nexaflow.Features.Console;

public sealed class ConsoleQueryHandler : IQueryHandler
{
    public string Description =>
        "Executes terminal/shell commands in the active Console tab. " +
        "Prefix with '>' to run, e.g. '>dir'.";

    public string? Symbol => ">";

    public float CanProcess(string input, IPageView? page = null) =>
        page?.ViewModel is ConsoleViewModel ? 1.0f : 0f;

    public Task<string?> ProcessAsync(string input, IPageView? page = null)
    {
        if (page?.ViewModel is not ConsoleViewModel vm)
            return Task.FromResult<string?>(
                "No Console tab is active. Open a Console tab and try again.");

        vm.SendCommand(input);
        return Task.FromResult<string?>(null);
    }
}
