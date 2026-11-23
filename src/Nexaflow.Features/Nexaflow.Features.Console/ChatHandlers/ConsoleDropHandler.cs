using Nexaflow.Features.Common;
using Nexaflow.Visuals.Terminal;
using Nexaflow.Visuals.Terminal.ViewModels;
using System.Windows;

namespace Nexaflow.Features.Console.ChatHandlers;

/// <summary>
/// Turns a file dropped onto the AI bar into its quoted path(s) — as the console's shell names them when a console is the
/// page, so a WSL distro's shell is given its own paths. Format-matched (not page-scoped), so a path can be dropped in
/// regardless of which tab is active.
/// </summary>
public sealed class ConsoleDropHandler : IChatDropHandler
{
    public IReadOnlyList<string> AcceptedFormats { get; } = [DataFormats.FileDrop];

    public string? BuildInsertText(IDataObject data, IPageViewModel? pageVm)
        => pageVm is TerminalViewModel terminal ? terminal.FormatDroppedPaths(data) : TerminalDropLogic.BuildInsertText(data);
}
