using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.ViewModels;
using Nexaflow.Markdown.Prose;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Core.Services;

/// <summary>
/// The AI bar's answer as the WPF window draws it: the line, Allow and Deny, Continue as conversation and the close, and the
/// reply card above the bar (<see cref="ReplyCard"/>), shown on hover, pinned by a click, and opened by itself for a plan to
/// approve. What the answer is — where it stands, its text, the approval waited on, the bar's conversation — is
/// <see cref="BarReply"/>'s; this keeps the card and the names the window binds.
/// </summary>
public partial class ShellAIResponseHandler : ObservableObject
{
    public ShellAIResponseHandler(BarReply reply)
    {
        Reply = reply;
        reply.PropertyChanged += OnReplyChanged;
        reply.Started += (_, _) => Card.Close();   // a new request starts with the last one's card put away
    }

    /// <summary>The answer this draws.</summary>
    public BarReply Reply { get; }

    /// <summary>The reply card above the bar.</summary>
    public ReplyCard Card { get; } = new();

    /// <summary>Whether the answer is in the AI bar's line.</summary>
    public bool InBar => Reply.IsOpen;

    public ContextUse? ContextUse => Reply.ContextUse;

    public string AiResponseAiName => Reply.AssistantName;

    public string AiResponseText => Reply.Text;

    public bool AiOverlayIsApproval => Reply.Stage == BarReplyStage.Approval;

    public bool AiOverlayIsMessage => Reply.Stage == BarReplyStage.Message;

    /// <summary>What the bar's line says.</summary>
    public string BarLine => Reply.Line;

    /// <summary>Whether the bar shows work in progress — its newest words in view as they come.</summary>
    public bool BarShowsWork => InBar && Reply.Stage is BarReplyStage.Running or BarReplyStage.Writing;

    /// <summary>Whether the bar shows something said — a reply or an approval — from its start.</summary>
    public bool BarShowsSaid => InBar && !BarShowsWork;

    [RelayCommand]
    private void AcceptToolBatch() => Reply.Approve();

    [RelayCommand]
    private void DenyToolBatch() => Reply.Deny();

    [RelayCommand]
    private void CloseAiResponseOverlay() => Reply.Dismiss();

    [RelayCommand]
    private Task ContinueAsConversation() => Reply.ContinueAsConversationAsync();

    private void OnReplyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Each of the answer's changes raises only the names drawn from it, as each would have changed on its own.
        switch (e.PropertyName)
        {
            case nameof(BarReply.IsOpen):
                OnPropertyChanged(nameof(InBar));
                OnPropertyChanged(nameof(BarShowsWork));
                OnPropertyChanged(nameof(BarShowsSaid));
                break;
            case nameof(BarReply.Stage):
                OnPropertyChanged(nameof(AiOverlayIsApproval));
                OnPropertyChanged(nameof(AiOverlayIsMessage));
                OnPropertyChanged(nameof(BarShowsWork));
                OnPropertyChanged(nameof(BarShowsSaid));
                break;
            case nameof(BarReply.Text):          OnPropertyChanged(nameof(AiResponseText));   break;
            case nameof(BarReply.AssistantName): OnPropertyChanged(nameof(AiResponseAiName)); break;
            case nameof(BarReply.ContextUse):    OnPropertyChanged(nameof(ContextUse));       break;
            case nameof(BarReply.Line):          OnPropertyChanged(nameof(BarLine));          break;
        }

        // The card holds the reply, what an approval explains, or a plan; a plan waiting for its answer is shown unasked. It is put
        // away with the answer.
        if (e.PropertyName is nameof(BarReply.IsOpen) or nameof(BarReply.Stage) or nameof(BarReply.Text) or nameof(BarReply.AsksAboutPlan))
        {
            if (!Reply.IsOpen) Card.Close();
            Card.HasContent = Reply.IsOpen && Reply.Stage != BarReplyStage.Running && Reply.Text.Trim().Length > 0;
            Card.IsRequired = Reply.IsOpen && Reply.Stage == BarReplyStage.Approval && Reply.AsksAboutPlan;
        }
    }
}
