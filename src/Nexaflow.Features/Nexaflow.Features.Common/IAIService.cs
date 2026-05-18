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
        /// Asks the basic LLM which of <paramref name="candidates"/> best fits the user's
        /// <paramref name="input"/> given the current page context.
        /// Returns null when the LLM selects "none of these apply".
        /// </summary>
        Task<IQueryHandler?> DisambiguateToolSelection(
            IPageViewModel? pageVm, string input, IReadOnlyList<IQueryHandler> candidates);

        /// <summary>
        /// One-shot contextual call that lets the LLM choose between executing an action,
        /// suggesting a prefill for the user to confirm, or replying conversationally.
        /// </summary>
        Task<AiResponse?> ContextChat(IPageViewModel? pageVm, string input);
    }
}
