using Nexaflow.Interaction;
using Nexaflow.Features.Common;
using Nexaflow.Features.WslShell.ViewModels;

namespace Nexaflow.Features.WslShell.ChatHandlers;

/// <summary>Turns a file dropped onto the AI bar over a WSL shell into the path the distro names it by — its Linux path,
/// single-quoted — however it was named.</summary>
public sealed class WslShellDropHandler : IChatDropHandler
{
    public IReadOnlyList<string> AcceptedKinds { get; } = [DragKinds.Files, DragKinds.FileContents];

    public bool CanHandle(IPageViewModel? pageVm) => pageVm is WslShellViewModel;

    public string? BuildInsertText(IDragPayload payload, IPageViewModel? pageVm)
        => (pageVm as WslShellViewModel)?.FormatDroppedPaths(payload);
}
