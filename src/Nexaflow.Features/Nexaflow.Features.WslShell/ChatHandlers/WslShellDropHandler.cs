using System.Windows;
using Nexaflow.Features.Common;
using Nexaflow.Features.WslShell.ViewModels;

namespace Nexaflow.Features.WslShell.ChatHandlers;

/// <summary>Turns a file dropped onto the AI bar over a WSL shell into the path the distro names it by — its Linux path,
/// single-quoted — however it was named.</summary>
public sealed class WslShellDropHandler : IChatDropHandler
{
    public IReadOnlyList<string> AcceptedFormats { get; } = [DataFormats.FileDrop];

    public bool CanHandle(IPageViewModel? pageVm) => pageVm is WslShellViewModel;

    public string? BuildInsertText(IDataObject data, IPageViewModel? pageVm)
        => (pageVm as WslShellViewModel)?.FormatDroppedPaths(data);
}
