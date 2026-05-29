using System;
using System.Collections.Generic;
using System.Text;

namespace Nexaflow.Features.Common
{
    public interface IAIService
    {
        ConversationRecord? ActiveConversation { get; }

        Task<IEnumerable<ConversationRecord>> LoadAllAsync();
        Task SaveAsync(ConversationRecord activeConversation);

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
        /// One-shot contextual call that lets the LLM choose between executing an action,
        /// suggesting a prefill for the user to confirm, or replying conversationally.
        /// </summary>
        Task<AiResponse?> ContextChat(IPageViewModel? pageVm, string input);

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
    }
}
