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
    }
}
