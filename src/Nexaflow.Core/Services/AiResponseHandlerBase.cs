using System.Globalization;
using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Visuals.Common.Localization;

namespace Nexaflow.Core.Services;

/// <summary>Where a response surface is: working with nothing to show yet, writing a reply as it arrives,
/// waiting on an approval, or showing a finished message.</summary>
public enum AiOverlayState { Message, Approval, Running, Writing }

/// <summary>
/// Shared <see cref="IAIResponseHandler"/> machinery for the two AI response surfaces — the shell's modal
/// overlay (<see cref="ShellAIResponseHandler"/>) and the in-page banner (<see cref="InlineAiResponseHandler"/>).
/// Holds the Running/Approval/Message state machine, the approval gate, "Continue as Conversation", and the
/// observable state the surfaces bind to. Subclasses only supply placement: <see cref="Open"/> /
/// <see cref="Close"/> show or hide their own surface. Built on the UI thread; calls marshal via <see cref="Dispatch"/>.
/// </summary>
public abstract partial class AiResponseHandlerBase : ObservableObject, IAIResponseHandler
{
    protected readonly ShellViewModel _shell;

    /// <summary>The app UI dispatcher, captured at construction (built on the UI thread).</summary>
    protected readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;

    protected AiResponseHandlerBase(ShellViewModel shell) => _shell = shell;

    // ── Shared observable state (bound by the overlay / banner views) ─────
    [ObservableProperty] private string _aiResponseAiName      = "Aria";
    [ObservableProperty] private string _aiResponseText        = string.Empty;
    [ObservableProperty] private string _aiResponsePrompt      = string.Empty;
    [ObservableProperty] private string _aiToolApprovalSummary = string.Empty;
    [ObservableProperty] private string _aiProgressText        = string.Empty;

    /// <summary>How much of the model's context this handler's conversation fills, as its last step said; null until one has.</summary>
    [ObservableProperty] private ContextUse? _contextUse;

    public virtual void ReportContextUse(int tokens, int window) => ContextUse = new(tokens, window);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiOverlayIsMessage))]
    [NotifyPropertyChangedFor(nameof(AiOverlayIsApproval))]
    [NotifyPropertyChangedFor(nameof(AiOverlayIsRunning))]
    [NotifyPropertyChangedFor(nameof(AiOverlayIsWriting))]
    [NotifyPropertyChangedFor(nameof(AiOverlayShowsMarkdown))]
    [NotifyPropertyChangedFor(nameof(AiOverlayShowsFooter))]
    private AiOverlayState _aiOverlayState = AiOverlayState.Message;

    public bool AiOverlayIsMessage  => AiOverlayState == AiOverlayState.Message;
    public bool AiOverlayIsApproval => AiOverlayState == AiOverlayState.Approval;
    public bool AiOverlayIsRunning  => AiOverlayState == AiOverlayState.Running;
    public bool AiOverlayIsWriting  => AiOverlayState == AiOverlayState.Writing;
    /// <summary>The markdown body shows whenever there is text to read — everywhere but Running.</summary>
    public bool AiOverlayShowsMarkdown => AiOverlayState != AiOverlayState.Running;
    /// <summary>The footer holds what to do with a finished message or a pending approval; a reply still being
    /// written has neither.</summary>
    public bool AiOverlayShowsFooter => AiOverlayState is AiOverlayState.Message or AiOverlayState.Approval;

    private TaskCompletionSource<bool>? _toolApprovalTcs;
    protected Page? _originTab;

    /// <summary>Files the agent's tools touched this run; carried into a "Continue as Conversation".</summary>
    public IReadOnlyList<string> AgentContextPaths { get; set; } = [];

    /// <summary>Whether the approval asked for is of a plan, which wants room to show its diagram and steps.</summary>
    protected bool ApprovalIsPlan { get; private set; }

    // ── Placement (subclass shows/hides its own surface) ──────────────────
    protected abstract void Open();
    protected abstract void Close();

    /// <summary>Which tab a "Continue as Conversation" pins as context. Default: the active tab.</summary>
    protected virtual Page? OriginTab() => _shell.ActiveTab;

    // ── IAIResponseHandler ────────────────────────────────────────────────

    public virtual void BeginResponse(string userText) => Dispatch(() =>
    {
        SyncAiName();
        AiResponsePrompt  = userText;
        AiResponseText    = string.Empty;
        _originTab        = OriginTab();
        AgentContextPaths = [];
        AiProgressText    = Str.Get("Shell.AiResponse.Considering");
        AiOverlayState    = AiOverlayState.Running;
        Open();
    });

    public virtual void ReportProgress(string message) => Dispatch(() =>
    {
        AiProgressText = message;
        AiOverlayState = AiOverlayState.Running;
        Open();
    });

    public virtual void ShowReplyDraft(string draftMarkdown) => Dispatch(() =>
    {
        AiResponseText = draftMarkdown;
        AiOverlayState = AiOverlayState.Writing;
        Open();
    });

    /// <summary>One surface holds one text, so a step that goes on to tools hands it back to the working line
    /// either way; a draft that is not kept is also cleared, so it cannot surface again as a later state's body.</summary>
    public virtual void EndReplyDraft(bool keep) => Dispatch(() =>
    {
        if (!keep) AiResponseText = string.Empty;
        AiOverlayState = AiOverlayState.Running;
    });

    public Task<bool> RequestToolBatchApprovalAsync(
        string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct)
    {
        if (!_ui.CheckAccess())
            return _ui.Invoke(() => RequestToolBatchApprovalAsync(explanationMarkdown, batch, ct));

        SyncAiName();
        AiResponseText        = string.IsNullOrWhiteSpace(explanationMarkdown)
            ? $"{AiResponseAiName} would like to run the following:"
            : explanationMarkdown;
        AiToolApprovalSummary = BuildBatchSummary(batch);
        ApprovalIsPlan        = false;
        return ShowApprovalAndWait(ct);
    }

    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
    {
        if (!_ui.CheckAccess())
            return _ui.Invoke(() => RequestPlanApprovalAsync(plan, ct));

        SyncAiName();
        AiResponseText        = BuildPlanMarkdown(plan);
        AiToolApprovalSummary = Str.Format("Shell.AiResponse.ApprovePlanFormat", plan.Title);
        ApprovalIsPlan        = true;
        return ShowApprovalAndWait(ct);
    }

    public Task<bool> RequestContextReleaseAsync(IReadOnlyList<string> destinations, CancellationToken ct)
    {
        if (!_ui.CheckAccess())
            return _ui.Invoke(() => RequestContextReleaseAsync(destinations, ct));

        SyncAiName();
        var to = string.Join(", ", destinations);
        AiResponseText        = Str.Format("Shell.AiResponse.ContextRelease.Explanation", to);
        AiToolApprovalSummary = Str.Format("Shell.AiResponse.ContextRelease.Question", to);
        ApprovalIsPlan        = false;
        return ShowApprovalAndWait(ct);
    }

    public virtual void ShowFinal(string finalMarkdown) => Dispatch(() =>
    {
        AiResponseText = finalMarkdown;
        AiOverlayState = AiOverlayState.Message;
        Open();
    });

    public virtual void Abort() => Dispatch(Close);

    // ── Commands (bound by the overlay / banner views) ────────────────────

    [RelayCommand]
    private void CloseAiResponseOverlay() => Dismiss();

    /// <summary>Hides the surface, resolving any pending approval as a deny. Used by the close button and
    /// by a banner that dissolves because the page took the next action.</summary>
    protected void Dismiss()
    {
        _toolApprovalTcs?.TrySetResult(false);   // closing a pending approval = deny
        Close();
    }

    [RelayCommand]
    private void AcceptToolBatch()
    {
        AiProgressText = Str.Get("Shell.AiResponse.Working");
        AiOverlayState = AiOverlayState.Running;
        var tcs = _toolApprovalTcs;
        _toolApprovalTcs = null;
        tcs?.TrySetResult(true);
    }

    [RelayCommand]
    private void DenyToolBatch()
    {
        AiProgressText = Str.Format("Shell.AiResponse.DeclinedFormat", AiResponseAiName);
        AiOverlayState = AiOverlayState.Running;
        var tcs = _toolApprovalTcs;
        _toolApprovalTcs = null;
        tcs?.TrySetResult(false);
    }

    /// <summary>The requests and replies a "Continue as Conversation" opens with, oldest first: by default the one shown.</summary>
    protected virtual IReadOnlyList<(string Request, string Reply)> ExchangesToContinue()
        => string.IsNullOrEmpty(AiResponsePrompt) ? [] : [(AiResponsePrompt, AiResponseText)];

    /// <summary>The files a "Continue as Conversation" attaches: by default those the shown reply's run touched.</summary>
    protected virtual IReadOnlyList<string> ContextPathsToContinue() => AgentContextPaths;

    [RelayCommand]
    private async Task ContinueAsConversation()
    {
        var exchanges = ExchangesToContinue();
        var paths     = ContextPathsToContinue();
        Close();

        if (exchanges.Count == 0) return;

        var record = new ConversationRecord
        {
            Id        = Guid.NewGuid().ToString(),
            StartedAt = DateTime.Now,
            Messages  =
            [
                .. exchanges.SelectMany(e => new[]
                {
                    new ConversationMessage { Text = e.Request, IsUser = true,  Timestamp = DateTime.Now },
                    new ConversationMessage { Text = e.Reply,   IsUser = false, Timestamp = DateTime.Now },
                })
            ],
            Attachments = [.. paths]
        };
        record.DeriveTitle();

        try { await _shell.CurrentRuntime.AiService!.SaveConversationAsync(record); }
        catch { /* persistence failures shouldn't block opening the tab */ }

        _shell.ShellServices.OpenTab("Conversation", new Dictionary<string, string>
        {
            ["conversationId"] = record.Id
        });

        // Add the origin tab as a live context item — via the interface, no feature type reference.
        if (_originTab is not null &&
            (_shell.CurrentPage as IPageView)?.ViewModel is IContextItemReceiver receiver)
            receiver.AddContextItem(_originTab);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private Task<bool> ShowApprovalAndWait(CancellationToken ct)
    {
        AiOverlayState = AiOverlayState.Approval;
        Open();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _toolApprovalTcs = tcs;
        ct.Register(() => tcs.TrySetResult(false));
        return tcs.Task;
    }

    private void SyncAiName()
    {
        var persona = _shell.CurrentRuntime?.Workspace?.Persona;
        AiResponseAiName = string.IsNullOrWhiteSpace(persona?.Name) ? "Aria" : persona!.Name!.Trim();
    }

    /// <summary>The question an approval asks, in the words a voice conversation says the actions: "Draw a high-quality
    /// picture?"; a tool that does not say how it is announced is named by its name.</summary>
    private static string BuildBatchSummary(IReadOnlyList<ToolCall> batch)
    {
        var calls = Voice.AnnouncedActions.Of(batch).Calls;
        var asked = string.Join(Str.Get("Shell.Voice.And"), calls.Select(c => $"{c.Action} {c.Target}"));
        return Str.Format("Shell.AiResponse.ApproveFormat", asked.Length == 0 ? asked : char.ToUpper(asked[0], CultureInfo.CurrentCulture) + asked[1..]);
    }

    private static string BuildPlanMarkdown(ClientPlan plan)
    {
        var sb = new StringBuilder();
        sb.Append("### ").Append(plan.Title).Append("\n\n");
        if (!string.IsNullOrWhiteSpace(plan.Mermaid))
            sb.Append("```mermaid\n").Append(plan.Mermaid!.Trim()).Append("\n```\n\n");
        if (plan.Steps.Count > 0)
        {
            sb.Append("**Steps**\n\n");
            var n = 1;
            foreach (var s in plan.Steps)
            {
                sb.Append(n++).Append(". ").Append(s.Title);
                if (s.IsDecisionPoint)               sb.Append("  _(decision)_");
                else if (!string.IsNullOrEmpty(s.Tool)) sb.Append("  `").Append(s.Tool).Append('`');
                sb.Append('\n');
            }
        }
        return sb.ToString();
    }

    protected void Dispatch(Action action)
    {
        if (_ui.CheckAccess()) action();
        else _ui.Invoke(action);
    }
}
