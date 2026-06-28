using System;
using System.Collections.Generic;
using System.Text;
using Nexaflow.Features.Common.ClientTools;

namespace Nexaflow.Features.Common
{
    /// <summary>
    /// The per-<c>Workspace</c> AI service injected into feature registrations (alongside
    /// <see cref="IShellServices"/>). It owns the client-side agent loop
    /// (<see cref="RunAgentAsync"/>), query-handler scoring/disambiguation for the AI input bar,
    /// conversation load/save plus its context pages and artifacts, and one-shot analysis/disambiguation
    /// completions. Resolves each ability through the active profile's model assignments and the
    /// workspace's acquired providers, so a feature never talks to a provider directly.
    /// </summary>
    public interface IAIService
    {
        ConversationRecord? ActiveConversation { get; }

        Task<IEnumerable<ConversationRecord>> LoadConversationsAsync();
        Task SaveConversationAsync(ConversationRecord conversation);

        /// <summary>Permanently deletes a conversation (transcript + any artifacts) by id.</summary>
        Task DeleteConversationAsync(string conversationId);

        /// <summary>
        /// Recreates the (unrealized) context page definitions saved with <paramref name="conversation"/>
        /// so the conversation view can re-pin them; the caller owns realizing and closing them. Centralised
        /// here so feature view-models never construct page definitions themselves.
        /// </summary>
        IReadOnlyList<Page> RestoreContextPages(ConversationRecord conversation);

        /// <summary>
        /// Captures <paramref name="contextPages"/> into <paramref name="conversation"/> as durable
        /// references (page kind + params + the owning assembly version), replacing any existing context.
        /// </summary>
        void SetConversationContext(ConversationRecord conversation, IEnumerable<Page> contextPages);

        /// <summary>
        /// Fetches all query handlers for the owning context, applies symbol-prefix filtering,
        /// scores survivors against <paramref name="text"/>, and returns the positive-score
        /// candidates (sorted descending), the clear winner (top score ≥ 0.8 leading the next
        /// by &gt; 0.2), and the effective text after any symbol prefix has been stripped.
        /// </summary>
        (IReadOnlyList<(IQueryHandler Handler, float Score)> Scored,
         IQueryHandler? ClearWinner,
         string EffectiveText)
            ScoreHandlers(string text, IPageViewModel? pageVm);

        /// <summary>
        /// Asks the LLM which of <paramref name="candidates"/> best fits the user's
        /// <paramref name="input"/> given the current page context.
        /// Each candidate carries the rule-based score (0–1) so the LLM can weight
        /// how confidently the handler's own rules matched the input.
        /// Returns null when the LLM selects "none of these apply".
        /// </summary>
        Task<IQueryHandler?> DisambiguateToolSelection(
            IPageViewModel? pageVm, string input,
            IReadOnlyList<(IQueryHandler Handler, float Score)> candidates);

        /// <summary>
        /// Runs the client-side agent loop: the LLM may call the active page's client tools (and
        /// built-in tools), see their results, and continue, until it produces a final message or
        /// a prefill. Tool batches and plans are approved through <paramref name="handler"/>.
        /// When <paramref name="includeContext"/> is false the active page's context and tools are
        /// omitted. Returns null when no Conversation provider is configured or on cancellation.
        /// </summary>
        Task<AiResponse?> RunAgentAsync(
            IPageViewModel? pageVm,
            string input,
            bool includeContext,
            IAIResponseHandler handler,
            CancellationToken ct = default);

        /// <summary>
        /// Context window (in tokens) for the model assigned to the Conversation ability,
        /// or null if the provider doesn't know or no provider is configured.
        /// </summary>
        Task<int?> GetConversationContextWindowAsync(CancellationToken ct = default);

        /// <summary>
        /// One-shot generic disambiguation: asks the Disambiguation-ability model to pick
        /// one of <paramref name="options"/> given <paramref name="contextDescription"/>
        /// and <paramref name="question"/>. Returns the chosen 0-based index, or null if
        /// the model picks "none of these" or no provider is configured.
        /// </summary>
        Task<int?> DisambiguateOptionAsync(
            string contextDescription,
            string question,
            IReadOnlyList<(string Label, string Detail)> options,
            CancellationToken ct = default);

        /// <summary>
        /// Runs a one-shot completion on the model assigned to the Analysis ability and returns its
        /// raw text. Returns null when no Analysis provider is configured. Used by background
        /// conversation analysis.
        /// </summary>
        Task<string?> RunAnalysisAsync(string systemPrompt, string userPrompt, CancellationToken ct = default);

        /// <summary>
        /// Persists an arbitrary JSON artifact (e.g. a conversation analysis) beside the
        /// conversation's transcript as <c>{name}.json</c>. Never throws on IO failure.
        /// </summary>
        Task SaveConversationArtifactAsync(string conversationId, string name, string json);

        /// <summary>
        /// Raised after a conversation artifact is successfully saved (argument = conversation id).
        /// Lets the conversation browser refresh a row when its background analysis finishes.
        /// May fire on a background thread.
        /// </summary>
        event Action<string>? ConversationArtifactSaved;

        /// <summary>
        /// Loads a previously-saved conversation artifact's JSON, or null if absent/unreadable.
        /// </summary>
        Task<string?> LoadConversationArtifactAsync(string conversationId, string name);
    }
}
