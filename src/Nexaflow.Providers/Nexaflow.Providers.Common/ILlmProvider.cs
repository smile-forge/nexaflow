namespace Nexaflow.Providers.Common;

/// <summary>
/// Abstraction over an LLM backend.
/// Providers report activity through an <see cref="IBackgroundActivityManager"/>
/// supplied at construction time, so they need no knowledge of WPF or observable
/// properties.
/// </summary>
public interface ILlmProvider
{
    string Name { get; }

    /// <summary>
    /// One-shot query: sends a system prompt and user prompt and returns a single response.
    /// Use this from any page for contextual, stateless queries.
    /// </summary>
    Task<LlmResponse?> QueryAsync(
        string systemPrompt,
        string userPrompt,
        IReadOnlyList<string>? attachments = null,
        CancellationToken ct = default);

    /// <summary>
    /// Conversation-aware call: sends the full message history plus a new user prompt.
    /// Use this from the AI Chat page so the model has conversational context.
    /// </summary>
    Task<LlmResponse?> ChatAsync(
        IReadOnlyList<LlmMessage> history,
        string newUserPrompt,
        IReadOnlyList<string>? attachments = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the list of model identifiers available from this provider.
    /// Implementations may query a remote API or return a static list.
    /// Returns an empty list on failure; never throws.
    /// </summary>
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default);
}
