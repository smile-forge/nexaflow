using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.ViewModels;
using Nexaflow.Visuals.Text.Markdown;

namespace Nexaflow.Core.Services;

/// <summary>
/// The default <see cref="Nexaflow.Features.Common.ClientTools.IAIResponseHandler"/>, used whenever the active page does
/// not render AI responses itself. It answers in the AI bar's own line: what the assistant is doing, an approval asked as a
/// question with Allow and Deny beside it, and the reply with Continue as conversation and a close. The whole of a reply — or
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

    /// <summary>A new request starts with the last one's card put away.</summary>
    public override void BeginResponse(string userText)
    {
        Dispatch(Card.Close);
        base.BeginResponse(userText);
    }

    protected override void Open() => InBar = true;

    protected override void Close()
    {
        InBar = false;
        Card.Close();
        Card.HasContent = false;
    }

    private static string Line(string markdown) => MarkdownClipboard.ToPlainText(markdown).ReplaceLineEndings(" ").Trim();

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
