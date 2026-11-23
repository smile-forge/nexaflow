using Nexaflow.Features.Common;
using Nexaflow.Features.Console.ViewModels;
using Nexaflow.Visuals.Terminal;
using Nexaflow.Visuals.Terminal.ViewModels;
using System.Windows;

namespace Nexaflow.Features.Console.ChatHandlers;

/// <summary>
/// Turns a file dropped onto the AI bar into its quoted path(s), as cmd names them. Format-matched, so a path can be dropped
/// in on any tab — except another terminal's, whose own feature names the path as its shell does.
/// </summary>
public sealed class ConsoleDropHandler : IChatDropHandler
{
    public IReadOnlyList<string> AcceptedFormats { get; } = [DataFormats.FileDrop];

    public bool CanHandle(IPageViewModel? pageVm) => pageVm is not TerminalViewModel || pageVm is CmdTerminalViewModel;

    public string? BuildInsertText(IDataObject data, IPageViewModel? pageVm)
        => pageVm is CmdTerminalViewModel terminal ? terminal.FormatDroppedPaths(data) : TerminalDropLogic.BuildInsertText(data);
}
