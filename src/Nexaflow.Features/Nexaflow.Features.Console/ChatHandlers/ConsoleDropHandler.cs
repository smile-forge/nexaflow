using Nexaflow.Features.Common;
using Nexaflow.Visuals.Terminal;
using Nexaflow.Interaction;
using Nexaflow.Features.Console.ViewModels;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Features.Console.ChatHandlers;

/// <summary>
/// Turns a file dropped onto the AI bar into its quoted path(s), as cmd names them. Kind-matched, so a path can be dropped
/// in on any tab — except another terminal's, whose own feature names the path as its shell does.
/// </summary>
public sealed class ConsoleDropHandler : IChatDropHandler
{
    public IReadOnlyList<string> AcceptedKinds { get; } = [DragKinds.Files, DragKinds.FileContents];

    public bool CanHandle(IPageViewModel? pageVm) => pageVm is not TerminalViewModel || pageVm is CmdTerminalViewModel;

    public string? BuildInsertText(IDragPayload payload, IPageViewModel? pageVm)
        => pageVm is CmdTerminalViewModel terminal ? terminal.FormatDroppedPaths(payload) : TerminalDropLogic.BuildInsertText(payload);
}
