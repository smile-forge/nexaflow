using Nexaflow.Features.Common;
using Nexaflow.Features.Console.ViewModels;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Features.Console.ChatHandlers;

/// <summary>
/// Mirrors what you type in the AI bar (a <c>&gt;</c>-prefixed command) into the active cmd terminal, so
/// the console shows the draft command before you press Enter.
/// </summary>
public sealed class ConsoleInputPreview : IChatInputPreview
{
    public bool CanPreview(IPageViewModel? pageVm) => pageVm is CmdTerminalViewModel;

    public void OnInputChanged(string text, IPageViewModel? pageVm)
    {
        if (pageVm is not TerminalViewModel vm) return;
        // The console symbol is '>'; show the command that will run (without the prefix), else clear.
        var draft = text.StartsWith('>') ? text[1..].TrimStart() : null;
        vm.SetInputPreview(draft);
    }
}
