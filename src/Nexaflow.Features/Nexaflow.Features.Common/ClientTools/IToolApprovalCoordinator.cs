namespace Nexaflow.Features.Common.ClientTools;

/// <summary>
/// Lets the agent harness ask the shell UI to approve client-tool batches and plans, and report
/// progress — without the harness depending on WPF. Implemented by the shell ViewModel.
/// </summary>
public interface IToolApprovalCoordinator
{
    /// <summary>
    /// Shows the pending batch (with the model's explanation) and awaits the user's decision.
    /// Returns true to run the batch, false to deny. Honours <paramref name="ct"/>.
    /// </summary>
    Task<bool> RequestToolBatchApprovalAsync(
        string explanationMarkdown, IReadOnlyList<ToolCall> batch, CancellationToken ct);

    /// <summary>
    /// Shows a proposed plan and its diagram, and awaits a single approval covering the whole plan.
    /// Returns true to proceed, false to decline.
    /// </summary>
    Task<bool> RequestPlanApprovalAsync(ClientPlan plan, CancellationToken ct);

    /// <summary>Updates the running indicator while tools execute.</summary>
    void ReportProgress(string message);

    /// <summary>Replaces any tool / approval UI with the model's final answer.</summary>
    void ShowFinal(string finalMarkdown);
}
