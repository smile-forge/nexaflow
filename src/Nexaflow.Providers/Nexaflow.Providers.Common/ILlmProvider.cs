namespace Nexaflow.Providers.Common;

/// <summary>
/// Abstraction over an LLM backend bound to a single model. One instance is created per
/// (config, model) the user assigns — the model is injected at construction via
/// <see cref="ProviderModel"/>, so completion calls don't pass it. Providers report activity through
/// an <see cref="IBackgroundActivityManager"/> supplied at construction, so they need no knowledge of
/// WPF or observable properties.
/// <para>
/// A model-agnostic "capability" instance (constructed with an empty <see cref="ProviderModel"/>) is
/// used by the options UI only to call <see cref="GetAvailableModelsAsync"/>; it never completes.
/// </para>
/// </summary>
public interface ILlmProvider
{
    string Name { get; }

    /// <summary>
    /// Whether this provider's bound model can accept image attachments (vision input). When true, the
    /// host may pass image <see cref="LlmAttachment"/>s and the provider encodes them as vision content;
    /// when false, callers should not send images (the provider would only stringify their paths).
    /// Best-effort per provider — default false (text-only).
    /// </summary>
    bool SupportsImages => false;

    /// <summary>
    /// Sends an ordered list of messages to this provider's bound model and returns the completion.
    /// The first message may carry <see cref="LlmRole.System"/> to set the system prompt; subsequent
    /// messages alternate <see cref="LlmRole.User"/> / <see cref="LlmRole.Assistant"/>. A message may
    /// carry <see cref="LlmMessage.Attachments"/> (e.g. an image a tool captured); providers that report
    /// <see cref="SupportsImages"/> encode image attachments inline, others list their file paths in text.
    /// </summary>
    Task<LlmResponse?> CompleteAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default);

    /// <summary>
    /// Returns the list of model identifiers available from this provider (independent of the bound
    /// model — this is a provider/config-level capability). Implementations may query a remote API or
    /// return a static list. Returns an empty list on failure; never throws.
    /// </summary>
    Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns metadata about the bound model (context window etc.), or null if the provider doesn't
    /// know. Default implementation returns null.
    /// </summary>
    Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
        => Task.FromResult<ModelInfo?>(null);

    /// <summary>
    /// Called when this instance is created because its (config, model) became referenced by the first
    /// active workspace. Lets the provider pre-load / cache anything it needs — e.g. a local backend can
    /// load the model into memory. Best-effort and fire-and-forget: implementations must never throw.
    /// Default implementation is a no-op (remote providers need no warm-up).
    /// </summary>
    Task WarmupAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Called when this instance is being released because its (config, model) is no longer referenced
    /// by ANY active workspace. Lets the provider release resources — e.g. a local backend can unload
    /// the model from memory. Best-effort and fire-and-forget: implementations must never throw.
    /// Default implementation is a no-op.
    /// </summary>
    Task CooldownAsync(CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>Metadata about a specific LLM model.</summary>
/// <param name="ContextWindowTokens">Maximum prompt + completion tokens the model accepts.</param>
/// <param name="DisplayName">Optional human-readable name.</param>
public sealed record ModelInfo(int ContextWindowTokens, string? DisplayName = null);
