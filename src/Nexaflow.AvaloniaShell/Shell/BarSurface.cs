using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.AvaloniaShell.Shell;

/// <summary>
/// Where the AI bar shows a reply: the request, the agent's progress, its reply, and an approval it waits on — the
/// <see cref="IAIResponseHandler"/> <see cref="AssistantRequests"/> answers on when the page in front of the user does not show the
/// assistant itself. The agent loop calls it from any thread; what it shows changes on the UI thread.
/// <para>
/// Words sent while a run is under way wait here until the loop takes them (<see cref="TakeInterjections"/>) or the run ends
/// (<see cref="DrainQueue"/>) — the queue Nexaflow.Core's <c>AiResponseHandlerBase</c> keeps too.
/// </para>
/// </summary>
internal sealed partial class BarSurface(IUiThread ui) : ObservableObject, IAIResponseHandler
{
    private readonly List<string> _queued = [];
    private TaskCompletionSource<bool>? _pending;

    /// <summary>The bar's conversation: each request continues it, until the user ends it.</summary>
    public BarConversation Conversation { get; } = new();

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _request = "";
    [ObservableProperty] private string _progress = "";
    [ObservableProperty] private string _reply = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasApproval))]
    private string? _approval;

    /// <summary>The agent waits on the user's yes or no.</summary>
    public bool HasApproval => Approval is not null;

    public string? ConversationName => Conversation.Name;

    public void BeginResponse(string userText) => On(() =>
    {
        Request  = userText;
        Reply    = "";
        Progress = "Considering…";
        IsOpen   = true;
    });

    public void ReportProgress(string message) => On(() => Progress = message);

    public void ShowReplyDraft(string draftMarkdown) => On(() => Reply = draftMarkdown);

    public void EndReplyDraft(bool keep) => On(() => { if (!keep) Reply = ""; });

    public void OnToolBatchStarting(IReadOnlyList<ToolCall> batch)
        => On(() => Progress = "Running " + string.Join(", ", batch.Select(call => call.Tool)));

    public Task<bool> RequestToolBatchApprovalAsync(string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct)
        => ui.RunAsync(() => Ask($"{explanationMarkdown}\n\nTools: {string.Join(", ", batch.Select(call => call.Tool))}", ct));

    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
        => ui.RunAsync(() => Ask($"{plan.Title}\n" + string.Join("\n", plan.Steps.Select((step, i) => $"{i + 1}. {step.Title}")), ct));

    public Task<bool> RequestContextReleaseAsync(IReadOnlyList<string> destinations, CancellationToken ct)
        => ui.RunAsync(() => Ask("Send what this page holds to " + string.Join(", ", destinations) + "?", ct));

    public void ShowFinal(string finalMarkdown) => On(() =>
    {
        Reply    = finalMarkdown;
        Progress = "";
        IsOpen   = true;
        Conversation.Answered(Request, finalMarkdown);
    });

    public void Abort() => On(() =>
    {
        Progress = "";
        if (Conversation.Exchanges.Count == 0 && Reply.Length == 0) IsOpen = false;
    });

    public void BackgroundJobEnded(string outcome) => On(() => Progress = outcome);

    public void BeginFollowUp(string about, string outcome) => On(() =>
    {
        Request  = about;
        Progress = outcome;
        IsOpen   = true;
    });

    public void Enqueue(string text)
    {
        lock (_queued) _queued.Add(text);
    }

    public IReadOnlyList<string> TakeInterjections() => Take();

    public IReadOnlyList<string> DrainQueue() => Take();

    /// <summary>Says yes to what the agent waits on.</summary>
    [RelayCommand]
    private void Approve() { if (_pending is { } pending) Answer(pending, true); }

    /// <summary>Says no to what the agent waits on.</summary>
    [RelayCommand]
    private void Deny() { if (_pending is { } pending) Answer(pending, false); }

    /// <summary>Ends the bar's conversation: the next request starts a new one.</summary>
    [RelayCommand]
    private void EndConversation()
    {
        Conversation.End();
        Request  = "";
        Reply    = "";
        Progress = "";
        IsOpen   = false;
    }

    private IReadOnlyList<string> Take()
    {
        lock (_queued)
        {
            var taken = _queued.ToList();
            _queued.Clear();
            return taken;
        }
    }

    /// <summary>On the UI thread: asks the user <paramref name="question"/>; a cancelled request is a no.</summary>
    private Task<bool> Ask(string question, CancellationToken ct)
    {
        if (_pending is { } earlier) Answer(earlier, false);
        var asked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = asked;
        Approval = question;
        IsOpen   = true;
        ct.Register(() => On(() => Answer(asked, false)));
        return asked.Task;
    }

    private void Answer(TaskCompletionSource<bool> asked, bool yes)
    {
        if (!asked.TrySetResult(yes) || !ReferenceEquals(_pending, asked)) return;
        _pending = null;
        Approval = null;
    }

    private void On(Action action)
    {
        if (ui.IsCurrent) action();
        else ui.Post(action);
    }
}
