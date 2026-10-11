namespace Nexaflow.Features.Common.ClientTools;

/// <summary>One executed tool call and its outcome, reported to the response handler after a batch.</summary>
public sealed record ToolRunResult(string Tool, ToolResult Result);

/// <summary>
/// The single sink the AI pipeline talks to for ALL response UI: echoing the user's prompt, the live
/// progress / tool activity, approval prompts, and the final answer. The shell supplies a default
/// implementation (the popup overlay); a view-model that can render AI responses itself (e.g. the
/// conversation page) also implements this so the agent runs inline. Implementations marshal to the UI
/// thread. Only view-models implement this — selection is a plain <c>pageVm is IAIResponseHandler</c> check.
/// </summary>
public interface IAIResponseHandler
{
    /// <summary>A new request started: echo the user's prompt and show a "considering" placeholder.</summary>
    void BeginResponse(string userText);

    /// <summary>Updates the single live activity line while tools run (overwrites).</summary>
    void ReportProgress(string message);

    /// <summary>
    /// The reply the model is writing in the current step, so far: its prose, without the blocks the agent acts
    /// on. Each call carries the whole draft, not the change, and the first call of a step starts a new one. A
    /// draft ends with <see cref="EndReplyDraft"/>, or is replaced by <see cref="ShowFinal"/>. Default: no-op —
    /// a handler that shows only finished answers.
    /// </summary>
    void ShowReplyDraft(string draftMarkdown) { }

    /// <summary>
    /// The step that wrote the current draft ended without a final answer. <paramref name="keep"/>: it goes on
    /// to run tools or to wait on a plan, and the draft stands as the assistant's account of what it is doing.
    /// Otherwise the draft is superseded — an approval request carries the same words, or the model is asked to
    /// write its reply again — and is taken away. Default: no-op.
    /// </summary>
    void EndReplyDraft(bool keep) { }

    /// <summary>A batch of tool calls is about to execute. Default: no-op.</summary>
    void OnToolBatchStarting(IReadOnlyList<ToolCall> batch) { }

    /// <summary>A batch finished — render the collapsed "ran N tools" summary. Default: no-op.</summary>
    void OnToolBatchFinished(IReadOnlyList<ToolRunResult> results) { }

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

    /// <summary>Renders the model's final answer (and, for a conversation, persists the exchange).</summary>
    void ShowFinal(string finalMarkdown);

    /// <summary>Aborts the in-flight response UI (cancel / error / prefill): clears the placeholder.</summary>
    void Abort();

    /// <summary>
    /// Queues a message the user submitted WHILE this response was being generated. The agent loop
    /// delivers it at the next turn boundary (see <see cref="TakeInterjections"/>) so the model can
    /// steer mid-task. Default: no-op (handlers that don't support queuing).
    /// </summary>
    void Enqueue(string text) { }

    /// <summary>
    /// Drains queued interjections, committing them into the CURRENT turn, and returns their text for
    /// the loop to feed the model. Called at each turn boundary. Default: none.
    /// </summary>
    IReadOnlyList<string> TakeInterjections() => [];

    /// <summary>
    /// Drains any interjections still queued AFTER the run finished (a late submission the loop never
    /// delivered), clearing the pending UI without committing — the shell re-runs them as a fresh turn.
    /// Default: none.
    /// </summary>
    IReadOnlyList<string> DrainQueue() => [];
}
