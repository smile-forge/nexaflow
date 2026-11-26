using Nexaflow.Features.Common;
using Nexaflow.Features.Common.ClientTools;
using Nexaflow.Providers.Common;

namespace Nexaflow.Assistant.Requests;

/// <summary>What answering a request from the AI bar asks of the workspace's assistant: the query handlers scored against the
/// words, the model's pick among them, the agent's run, and whether a model can answer at all and, where not, why.</summary>
public interface IAnswersRequests
{
    /// <inheritdoc cref="IAIService.ScoreHandlers"/>
    (IReadOnlyList<(IQueryHandler Handler, float Score)> Scored, IQueryHandler? ClearWinner, string EffectiveText, bool Prefixed)
        ScoreHandlers(string text, IPageViewModel? pageVm);

    /// <inheritdoc cref="IAIService.DisambiguateToolSelection"/>
    Task<IQueryHandler?> DisambiguateToolSelection(IPageViewModel? page, string input, IReadOnlyList<(IQueryHandler Handler, float Score)> candidates);

    /// <summary>Runs the agent on <paramref name="input"/> about <paramref name="page"/>, driving <paramref name="approval"/>: its
    /// progress, the approvals it asks for, and the reply. Null when no model answered.</summary>
    Task<AiResponse?> RunAgentAsync(IPageViewModel? page, string input, bool includeContext, IAIResponseHandler approval,
                                    AgentRunOptions options, CancellationToken ct = default);

    /// <inheritdoc cref="IAIService.StatusOf"/>
    AiAbilityStatus StatusOf(AiAbility ability);

    /// <summary>Why the workspace's models of Nexaflow's own cannot run now, where it uses any; null otherwise.</summary>
    string? OwnModelsUnreachable { get; }
}
