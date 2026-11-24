using Nexaflow.Interaction;
using Nexaflow.Features.Common;
using Nexaflow.Visuals.Terminal.ViewModels;

namespace Nexaflow.Visuals.Terminal.ChatHandlers;

/// <summary>Routes AI-bar keys — Up/Down history, Tab completion — to a terminal page, for the feature whose terminal it is
/// (<see cref="Serves"/>). The logic is the terminal's own (<see cref="TerminalViewModel.HandleChatKey"/>).</summary>
public abstract class TerminalKeyHandler : IChatKeyHandler
{
    /// <summary>Whether <paramref name="terminal"/> is this feature's terminal.</summary>
    protected abstract bool Serves(TerminalViewModel terminal);

    public bool CanHandle(IPageViewModel? pageVm) => pageVm is TerminalViewModel terminal && Serves(terminal);

    public ChatKeyResult HandleKey(ConsoleKey key, KeyModifiers modifiers, string currentText, int caretIndex, IPageViewModel? pageVm)
        => pageVm is TerminalViewModel terminal && Serves(terminal)
            ? terminal.HandleChatKey(key, modifiers, currentText, caretIndex)
            : ChatKeyResult.NotHandled;
}
