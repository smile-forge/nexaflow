using System.Windows.Input;

namespace Nexaflow.Features.Common;

/// <summary>
/// Lets a feature claim key presses in the shell's AI input bar when its page is active — e.g. the
/// terminal binding Up/Down to its command history and Tab to path completion. Discovered by reflection
/// and instantiated per workspace, like <see cref="IQueryHandler"/>; the shell consults each handler
/// (in registration order) before its own built-in key handling and stops at the first that claims the key.
/// </summary>
public interface IChatKeyHandler
{
    /// <summary>True if this handler wants keys while <paramref name="pageVm"/> is the active page.</summary>
    bool CanHandle(IPageViewModel? pageVm);

    /// <summary>
    /// Handles a key. Return <see cref="ChatKeyResult.NotHandled"/> to let the shell's default handling
    /// (and other handlers) run; otherwise return a handled result, optionally rewriting the bar text.
    /// </summary>
    ChatKeyResult HandleKey(Key key, ModifierKeys modifiers, string currentText, int caretIndex,
                            IPageViewModel? pageVm);
}

/// <summary>Outcome of <see cref="IChatKeyHandler.HandleKey"/>.</summary>
/// <param name="Handled">True if the key was consumed (the shell suppresses its own handling).</param>
/// <param name="NewText">When non-null, replaces the bar text.</param>
/// <param name="NewCaretIndex">Caret position after a text replacement; -1 = end of text.</param>
public readonly record struct ChatKeyResult(bool Handled, string? NewText = null, int NewCaretIndex = -1)
{
    public static readonly ChatKeyResult NotHandled = new(false);
}
