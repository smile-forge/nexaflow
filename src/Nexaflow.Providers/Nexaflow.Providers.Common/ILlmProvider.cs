namespace Nexaflow.Providers.Common;

/// <summary>
/// A language model bound to a single model (see <see cref="IAiProvider"/> for what every provider shares): messages
/// in, a reply streamed out.
/// </summary>
public interface ILlmProvider : IAiProvider
{
    /// <summary>
    /// Whether this provider's bound model can accept image attachments (vision input). When true, the
    /// host may pass image <see cref="LlmAttachment"/>s and the provider encodes them as vision content;
    /// when false, callers should not send images (the provider would only stringify their paths).
    /// Best-effort per provider — default false (text-only).
    /// </summary>
    bool SupportsImages => false;

    /// <summary>
    /// Whether this provider's bound model can hear audio attachments. When true, the host may pass
    /// <see cref="LlmAttachment.IsAudio"/> attachments and the provider encodes them as audio input; when false, a
    /// recording reaches the model only as a transcript. Default false.
    /// </summary>
    bool SupportsAudio => false;

    /// <summary>
    /// Whether the bound model calls tools in a protocol of its own — the tools offered with
    /// <see cref="StreamAsync(IReadOnlyList{LlmMessage}, IReadOnlyList{LlmTool}, LlmUrgency, CancellationToken)"/>, its
    /// calls coming back as <see cref="LlmToolCallRequested"/>. Without it, a caller that wants the model to act describes
    /// its tools in the conversation itself. Default false.
    /// </summary>
    bool SupportsToolCalls => false;

    /// <summary>
    /// Sends an ordered list of messages to this provider's bound model and streams its reply as it is written.
    /// The first message may carry <see cref="LlmRole.System"/> to set the system prompt; subsequent
    /// messages alternate <see cref="LlmRole.User"/> / <see cref="LlmRole.Assistant"/>. A message may
    /// carry <see cref="LlmMessage.Attachments"/> (e.g. an image a tool captured); providers that report
    /// <see cref="SupportsImages"/> encode image attachments inline, others list their file paths in text.
    /// <para>
    /// Cancelling <paramref name="ct"/> ends the stream with an <see cref="OperationCanceledException"/>; any
    /// other failure is an <see cref="LlmProviderException"/>. A caller that wants the finished reply reads the
    /// stream to its end — <see cref="LlmProviderExtensions.CompleteAsync"/>.
    /// </para>
    /// </summary>
    IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        CancellationToken         ct = default);

    /// <summary>
    /// The same, for a request of the given urgency. A provider whose model shares one GPU among requests schedules by
    /// it — a reply being read goes before analysis nobody is watching; one whose requests go elsewhere has nothing to
    /// schedule. Default: the urgency is not used.
    /// </summary>
    IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        LlmUrgency                urgency,
        CancellationToken         ct = default)
            => StreamAsync(messages, ct);

    /// <summary>
    /// The same, offering the model <paramref name="tools"/> to call in its own protocol. A call comes back as an
    /// <see cref="LlmToolCallRequested"/>; what came of it goes back as an <see cref="LlmRole.Tool"/> message that
    /// <see cref="LlmMessage.Answers"/> it, after the <see cref="LlmRole.Assistant"/> message carrying the calls. Only for
    /// a provider that <see cref="SupportsToolCalls"/>; the default offers none.
    /// </summary>
    IAsyncEnumerable<LlmStreamEvent> StreamAsync(
        IReadOnlyList<LlmMessage> messages,
        IReadOnlyList<LlmTool>    tools,
        LlmUrgency                urgency,
        CancellationToken         ct = default)
        => StreamAsync(messages, urgency, ct);

    /// <summary>
    /// Returns metadata about the bound model (context window etc.), or null if the provider doesn't
    /// know. Default implementation returns null.
    /// </summary>
    Task<ModelInfo?> GetModelInfoAsync(CancellationToken ct = default)
        => Task.FromResult<ModelInfo?>(null);
}

/// <summary>Who is waiting on a request.</summary>
public enum LlmUrgency
{
    /// <summary>Someone is reading the reply, or waiting on what it decides.</summary>
    Interactive,
    /// <summary>Work nobody is watching — page analysis.</summary>
    Background,
}

/// <summary>Metadata about a specific LLM model.</summary>
/// <param name="ContextWindowTokens">Maximum prompt + completion tokens the model accepts.</param>
/// <param name="DisplayName">Optional human-readable name.</param>
public sealed record ModelInfo(int ContextWindowTokens, string? DisplayName = null);
