using System.Text;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Core.Services;

public enum AiOverlayState { Message, Approval, Running }

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AiOverlayIsMessage))]
    [NotifyPropertyChangedFor(nameof(AiOverlayIsApproval))]
    [NotifyPropertyChangedFor(nameof(AiOverlayIsRunning))]
    [NotifyPropertyChangedFor(nameof(AiOverlayShowsMarkdown))]
    private AiOverlayState _aiOverlayState = AiOverlayState.Message;

    public bool AiOverlayIsMessage  => AiOverlayState == AiOverlayState.Message;
    public bool AiOverlayIsApproval => AiOverlayState == AiOverlayState.Approval;
    public bool AiOverlayIsRunning  => AiOverlayState == AiOverlayState.Running;
    /// <summary>True in Message/Approval states (markdown body + footer); false while Running.</summary>
    public bool AiOverlayShowsMarkdown => AiOverlayState != AiOverlayState.Running;

    private TaskCompletionSource<bool>? _toolApprovalTcs;
    protected Page? _originTab;

    /// <summary>Files the agent's tools touched this run; carried into a "Continue as Conversation".</summary>
    public IReadOnlyList<string> AgentContextPaths { get; set; } = [];

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
        _originTab        = OriginTab();
        AgentContextPaths = [];
        AiProgressText    = "Considering";
        AiOverlayState    = AiOverlayState.Running;
        Open();
    });

    public virtual void ReportProgress(string message) => Dispatch(() =>
    {
        AiProgressText = message;
        AiOverlayState = AiOverlayState.Running;
        Open();
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
        return ShowApprovalAndWait(ct);
    }

    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
    {
        if (!_ui.CheckAccess())
            return _ui.Invoke(() => RequestPlanApprovalAsync(plan, ct));

        SyncAiName();
        AiResponseText        = BuildPlanMarkdown(plan);
        AiToolApprovalSummary = $"Approve plan: {plan.Title}";
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
        AiProgressText = "Working";
        AiOverlayState = AiOverlayState.Running;
        var tcs = _toolApprovalTcs;
        _toolApprovalTcs = null;
        tcs?.TrySetResult(true);
    }

    [RelayCommand]
    private void DenyToolBatch()
    {
        AiProgressText = $"Declined — asking {AiResponseAiName}";
        AiOverlayState = AiOverlayState.Running;
        var tcs = _toolApprovalTcs;
        _toolApprovalTcs = null;
        tcs?.TrySetResult(false);
    }

    [RelayCommand]
    private async Task ContinueAsConversation()
    {
        var prompt   = AiResponsePrompt;
        var response = AiResponseText;
        Close();

        if (string.IsNullOrEmpty(prompt)) return;

        var record = new ConversationRecord
        {
            Id        = Guid.NewGuid().ToString(),
            StartedAt = DateTime.Now,
            Messages  =
            [
                new ConversationMessage { Text = prompt,   IsUser = true,  Timestamp = DateTime.Now },
                new ConversationMessage { Text = response, IsUser = false, Timestamp = DateTime.Now },
            ],
            Attachments = [.. AgentContextPaths]
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

    private static string BuildBatchSummary(IReadOnlyList<ToolCall> batch)
    {
        if (batch.Count == 1)
            return $"Run {batch[0].Tool}?";
        var grouped = batch
            .GroupBy(c => c.Tool)
            .Select(g => g.Count() > 1 ? $"{g.Key} ×{g.Count()}" : g.Key);
        return "Run " + string.Join(", ", grouped) + "?";
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
