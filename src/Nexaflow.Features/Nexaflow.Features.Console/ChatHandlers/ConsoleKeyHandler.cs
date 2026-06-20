using Nexaflow.Features.Common;
using Nexaflow.Features.Console.ViewModels;
using Nexaflow.Visuals.Terminal.ViewModels;
using System.Windows.Input;

namespace Nexaflow.Features.Console.ChatHandlers;

/// <summary>
/// Routes AI-bar keys to the active cmd terminal (Up/Down history, Tab completion). The logic lives on
/// <see cref="TerminalViewModel"/> so a future PowerShell feature ships the same thin shim.
/// </summary>
public sealed class ConsoleKeyHandler : IChatKeyHandler
{
    public bool CanHandle(IPageViewModel? pageVm) => pageVm is CmdTerminalViewModel;

    public ChatKeyResult HandleKey(Key key, ModifierKeys modifiers, string currentText, int caretIndex,
                                   IPageViewModel? pageVm)
        => pageVm is TerminalViewModel vm
            ? vm.HandleChatKey(key, modifiers, currentText, caretIndex)
            : ChatKeyResult.NotHandled;
}
