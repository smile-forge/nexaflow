using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.ViewModels;
using Nexaflow.Providers.Common;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Core.Services;

/// <summary>
/// The default <see cref="Nexaflow.Features.Common.ClientTools.IAIResponseHandler"/>, used whenever the active page does
/// not render AI responses itself. It answers in the AI bar's own line: what the assistant is doing, an approval asked as a
/// question with Allow and Deny beside it, and the reply with Continue as conversation and a close. Requests typed into the bar
/// are one conversation, each continuing the last, until the reply is dismissed or continued as a conversation. The whole of a reply — or
/// of what an approval explains, or a plan — is rendered in the reply card above the bar (<see cref="ReplyCard"/>), shown on
/// hover, pinned by a click, and opened by itself for a plan to approve. Nothing it shows is modal. The state machine and
/// commands live in <see cref="AiResponseHandlerBase"/>; this type decides what the line and the card show.
/// </summary>
public partial class ShellAIResponseHandler : AiResponseHandlerBase
{
    /// <summary>How a request answered in the bar is to be written: the answer first, briefly, the rest only when it is needed.</summary>
    internal const string BarStyle =
        "Your reply is shown on one line of the AI bar, the whole of it a hover away: lead with the answer in a sentence or " +
        "two, and write more only when the request needs it.";

    public ShellAIResponseHandler(ShellViewModel shell) : base(shell) => PropertyChanged += OnStateChanged;

    /// <summary>The reply card above the bar.</summary>
    public ReplyCard Card { get; } = new();

    /// <summary>The conversation the requests typed into the bar hold, until the reply is dismissed or continued as a conversation.</summary>
    public BarConversation Conversation { get; } = new();

    /// <summary>Whether the response is in the AI bar's line.</summary>
    [ObservableProperty] private bool _inBar;

    /// <summary>What the bar's line says: what the assistant is doing, the approval it asks for, or its reply as plain text.</summary>
    public string BarLine => AiOverlayState switch
    {
        AiOverlayState.Running  => AiProgressText.EndsWith('…') ? AiProgressText : AiProgressText + "…",
        AiOverlayState.Approval => AiToolApprovalSummary,
        _                       => Line(AiResponseText),
    };

    /// <summary>Whether the bar shows work in progress — its newest words in view as they come.</summary>
    public bool BarShowsWork => InBar && AiOverlayState is AiOverlayState.Running or AiOverlayState.Writing;

    /// <summary>Whether the bar shows something said — a reply or an approval — from its start.</summary>
    public bool BarShowsSaid => InBar && !BarShowsWork;

    /// <summary>A new request starts with the last one's card put away, the files its run touched kept with the conversation.</summary>
    public override void BeginResponse(string userText)
    {
        Dispatch(() =>
        {
            Conversation.Touched(AgentContextPaths);
            Card.Close();
        });
        base.BeginResponse(userText);
    }

    /// <summary>The reply is shown, and is the newest exchange of the bar's conversation.</summary>
    public override void ShowFinal(string finalMarkdown)
    {
        base.ShowFinal(finalMarkdown);
        Dispatch(() => Conversation.Answered(AiResponsePrompt, finalMarkdown));
    }

    /// <summary>A request that ends without a reply — stopped, failed, or answered in the input box — leaves the bar's
    /// conversation as it was: its last reply back in the line, or the line put away when it has none.</summary>
    public override void Abort() => Dispatch(() =>
    {
        if (Conversation.Exchanges.Count == 0) { Close(); return; }
        (AiResponsePrompt, AiResponseText) = Conversation.Exchanges[^1];
        AiOverlayState = AiOverlayState.Message;
    });

    /// <summary>How a request typed into the bar runs: continuing the bar's conversation, its reply written for the line.</summary>
    public AgentRunOptions BarRun() => Conversation.Run(BarStyle, Dispatch);

    protected override IReadOnlyList<(string Request, string Reply)> ExchangesToContinue()
        => Conversation.Exchanges.Count > 0 ? [.. Conversation.Exchanges] : base.ExchangesToContinue();

    protected override IReadOnlyList<string> ContextPathsToContinue() => [.. Conversation.Paths.Union(AgentContextPaths)];

    protected override void Open() => InBar = true;

    /// <summary>The reply dismissed, or continued as a conversation: the line is put away and the bar's conversation ends with it.</summary>
    protected override void Close()
    {
        InBar = false;
        Card.Close();
        Card.HasContent = false;
    Conversation.End();
    ContextUse = null;   // the next request starts a conversation of its own
    }

    private static string Line(string markdown) => MarkdownProse.RunTogether(markdown);

    private void OnStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AiOverlayState) or nameof(AiProgressText) or nameof(AiResponseText) or nameof(AiToolApprovalSummary))
            OnPropertyChanged(nameof(BarLine));
        if (e.PropertyName is nameof(AiOverlayState) or nameof(InBar))
        {
            OnPropertyChanged(nameof(BarShowsWork));
            OnPropertyChanged(nameof(BarShowsSaid));
        }
        if (e.PropertyName is nameof(AiOverlayState) or nameof(AiResponseText) or nameof(InBar))
        {
            // The card holds the reply, what an approval explains, or a plan; a plan waiting for its answer is shown unasked.
            Card.HasContent = InBar && AiOverlayState != AiOverlayState.Running && AiResponseText.Trim().Length > 0;
            Card.IsRequired = InBar && AiOverlayState == AiOverlayState.Approval && ApprovalIsPlan;
        }
    }
}
