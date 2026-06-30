using System.Windows.Input;

namespace Nexaflow.Core.ViewModels.Overlays;

/// <summary>
/// Content view-model for the shell confirmation modal: the title/prompt text plus the shell's
/// existing confirm/cancel commands. A plain carrier — the state and "invoke-once-then-clear" logic
/// stay on <see cref="ShellViewModel"/> (<c>ShowConfirmation</c> / <c>ConfirmShellConfirmation</c>).
/// </summary>
public sealed class ConfirmationOverlay
{
    public required string  Title          { get; init; }
    public required string  Prompt         { get; init; }
    public required ICommand ConfirmCommand { get; init; }
    public required ICommand CancelCommand  { get; init; }
}

/// <summary>
/// Content view-model for the shell input-prompt modal. The editable value deliberately stays on
/// <see cref="ShellViewModel.PromptValue"/> (the template two-way-binds to it via the window's
/// DataContext) so <c>ConfirmShellPrompt</c> reads the same field it always did.
/// </summary>
public sealed class PromptOverlay
{
    public required string  Title          { get; init; }
    public required string  Label          { get; init; }
    public required ICommand ConfirmCommand { get; init; }
    public required ICommand CancelCommand  { get; init; }
}
