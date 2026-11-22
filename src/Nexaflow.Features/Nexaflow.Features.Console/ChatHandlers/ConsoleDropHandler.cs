using Nexaflow.Features.Common;
using Nexaflow.Visuals.Terminal;
using Nexaflow.Interaction;

namespace Nexaflow.Features.Console.ChatHandlers;

/// <summary>
/// Turns a file dropped onto the AI bar into its quoted path(s). Kind-matched (not page-scoped), so a
/// path can be dropped in regardless of which tab is active.
/// </summary>
public sealed class ConsoleDropHandler : IChatDropHandler
{
    public IReadOnlyList<string> AcceptedKinds { get; } = [DragKinds.Files, DragKinds.FileContents];

    public string? BuildInsertText(IDragPayload payload, IPageViewModel? pageVm)
        => TerminalDropLogic.BuildInsertText(payload);
}
