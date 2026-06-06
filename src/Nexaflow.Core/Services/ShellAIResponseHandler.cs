using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nexaflow.Core.ViewModels;
using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Core.Services;

public enum AiOverlayState { Message, Approval, Running }

/// <summary>
/// The default <see cref="IAIResponseHandler"/>: renders the AI agent's activity in the shell's modal
/// popup overlay (progress, approvals, final answer, "Continue as Conversation"). Used whenever the
/// active page does not render AI responses itself. The overlay (<c>AiResponseOverlay.xaml</c>) binds to
/// this object's observable state. One instance per <see cref="ShellViewModel"/>.
/// </summary>
public partial class ShellAIResponseHandler : ObservableObject, IAIResponseHandler
{
    private readonly ShellViewModel _shell;

    public ShellAIResponseHandler(ShellViewModel shell) => _shell = shell;

    // ── Overlay state (bound by AiResponseOverlay.xaml) ───────────────────
    [ObservableProperty] private bool   _aiResponseOverlayOpen;
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
    private Page? _originTab;

    /// <summary>Files the agent's tools touched this run; carried into a "Continue as Conversation".</summary>
    public IReadOnlyList<string> AgentContextPaths { get; set; } = [];

    // ── IAIResponseHandler ────────────────────────────────────────────────

    public void BeginResponse(string userText) => Dispatch(() =>
    {
        SyncAiName();
        AiResponsePrompt      = userText;
        _originTab            = _shell.ActiveTab;
        AgentContextPaths     = [];
        AiProgressText        = "Considering";
        AiOverlayState        = AiOverlayState.Running;
        AiResponseOverlayOpen = true;
    });

    public void ReportProgress(string message) => Dispatch(() =>
    {
        AiProgressText        = message;
        AiOverlayState        = AiOverlayState.Running;
        AiResponseOverlayOpen = true;
    });

    public Task<bool> RequestToolBatchApprovalAsync(
        string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct)
    {
        var d = Application.Current?.Dispatcher;
        if (d is not null && !d.CheckAccess())
            return d.Invoke(() => RequestToolBatchApprovalAsync(explanationMarkdown, batch, ct));

        SyncAiName();
        AiResponseText        = string.IsNullOrWhiteSpace(explanationMarkdown)
            ? $"{AiResponseAiName} would like to run the following:"
            : explanationMarkdown;
        AiToolApprovalSummary = BuildBatchSummary(batch);
        return ShowApprovalAndWait(ct);
    }

    public Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct)
    {
        var d = Application.Current?.Dispatcher;
        if (d is not null && !d.CheckAccess())
            return d.Invoke(() => RequestPlanApprovalAsync(plan, ct));

        SyncAiName();
        AiResponseText        = BuildPlanMarkdown(plan);
        AiToolApprovalSummary = $"Approve plan: {plan.Title}";
        return ShowApprovalAndWait(ct);
    }

    public void ShowFinal(string finalMarkdown) => Dispatch(() =>
    {
        AiResponseText        = finalMarkdown;
        AiOverlayState        = AiOverlayState.Message;
        AiResponseOverlayOpen = true;
    });

    public void Abort() => Dispatch(() => AiResponseOverlayOpen = false);

    // ── Overlay commands ──────────────────────────────────────────────────

    [RelayCommand]
    private void CloseAiResponseOverlay()
    {
        _toolApprovalTcs?.TrySetResult(false);   // closing a pending approval = deny
        AiResponseOverlayOpen = false;
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
        AiResponseOverlayOpen = false;

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

        try { await _shell.CurrentWorkspace.AiService!.SaveConversationAsync(record); }
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
        AiOverlayState        = AiOverlayState.Approval;
        AiResponseOverlayOpen = true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _toolApprovalTcs = tcs;
        ct.Register(() => tcs.TrySetResult(false));
        return tcs.Task;
    }

    private void SyncAiName()
    {
        var persona = _shell.CurrentWorkspace?.Profile?.Persona;
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

    private static void Dispatch(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d is not null && !d.CheckAccess()) d.Invoke(action);
        else action();
    }
}
