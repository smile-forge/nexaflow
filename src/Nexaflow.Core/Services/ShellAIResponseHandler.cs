using CommunityToolkit.Mvvm.ComponentModel;
using Nexaflow.Core.ViewModels;

namespace Nexaflow.Core.Services;

/// <summary>
/// The default <see cref="IAIResponseHandler"/>: renders the AI agent's activity in the shell's modal
/// popup overlay (progress, approvals, final answer, "Continue as Conversation"). Used whenever the
/// active page does not render AI responses itself. The overlay (<c>AiResponseOverlay.xaml</c>) binds to
/// this object's observable state. One instance per <see cref="ShellViewModel"/>. The state machine and
/// commands live in <see cref="AiResponseHandlerBase"/>; this type only owns the overlay's visibility.
/// </summary>
public partial class ShellAIResponseHandler : AiResponseHandlerBase
{
    public ShellAIResponseHandler(ShellViewModel shell) : base(shell) { }

    /// <summary>Whether the modal overlay is shown (bound by <c>AiResponseOverlay.xaml</c>).</summary>
    [ObservableProperty] private bool _aiResponseOverlayOpen;

    protected override void Open()  => AiResponseOverlayOpen = true;
    protected override void Close() => AiResponseOverlayOpen = false;
}
