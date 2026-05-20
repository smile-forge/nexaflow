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
    /// Sends an ordered list of messages to the provider and returns the completion.
    /// The first message may carry <see cref="LlmRole.System"/> to set the system prompt;
    /// subsequent messages alternate <see cref="LlmRole.User"/> / <see cref="LlmRole.Assistant"/>.
    /// <para>
    /// <paramref name="model"/> is the specific model identifier for this call, taken from
    /// the user's AI ability assignments rather than baked into the provider config.
    /// </para>
    /// </summary>
    Task<LlmResponse?> CompleteAsync(
        IReadOnlyList<LlmMessage>     messages,
        string                        model,
        IReadOnlyList<LlmAttachment>? attachments = null,
        CancellationToken             ct = default);

    /// <summary>
    /// Returns the list of model identifiers available from this provider.
    /// Implementations may query a remote API or return a static list.
    /// Returns an empty list on failure; never throws.
    /// </summary>
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default);
}
