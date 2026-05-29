namespace Nexaflow.Features.Common.ClientTools;

/// <summary>
/// The structured view of one assistant reply: its human-facing prose plus any
/// <c>client_tool</c> / <c>client_plan</c> / <c>client_prefill</c> blocks the harness must act on.
/// </summary>
/// <param name="ExplanationMarkdown">All text outside recognised fences (shown to the user).</param>
/// <param name="ToolCalls">Tool invocations to run as one batch (multiple = parallel).</param>
/// <param name="Plan">A proposed plan, if the reply contained a <c>client_plan</c> block.</param>
/// <param name="Prefill">Text to drop into the input box, if a <c>client_prefill</c> block.</param>
/// <param name="ParseErrors">Human-readable notes about malformed blocks, fed back to the model.</param>
public sealed record ParsedAssistantTurn(
    string ExplanationMarkdown,
    IReadOnlyList<ToolCall> ToolCalls,
    ClientPlan? Plan,
    string? Prefill,
    IReadOnlyList<string> ParseErrors)
{
    /// <summary>True when the reply asked the harness to do something rather than just talk.</summary>
    public bool HasActions => ToolCalls.Count > 0 || Plan is not null || Prefill is not null;
}
