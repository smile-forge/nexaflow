namespace Nexaflow.Features.Common.ClientTools;

/// <summary>
/// One step in a proposed plan. A decision step (<see cref="IsDecisionPoint"/>) returns control to
/// the model after the preceding steps run, so it can choose what to do next.
/// </summary>
public sealed record ClientPlanStep(string Title, string? Tool = null, bool IsDecisionPoint = false);

/// <summary>
/// A multi-step plan the model proposes up-front for the user to approve once. The
/// <see cref="Mermaid"/> diagram is rendered in the approval overlay. Once approved, the plan's
/// tool steps run without per-step approval until a decision point or completion.
/// </summary>
public sealed class ClientPlan
{
    public required string Title { get; init; }
    public string? Mermaid { get; init; }
    public IReadOnlyList<ClientPlanStep> Steps { get; init; } = [];
}
