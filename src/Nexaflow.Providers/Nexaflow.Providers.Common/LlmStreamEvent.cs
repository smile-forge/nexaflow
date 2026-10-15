namespace Nexaflow.Providers.Common;

/// <summary>
/// One event of a model's streamed reply, as <see cref="ILlmProvider.StreamAsync"/> yields it. A closed set
/// that grows by adding a record: a consumer switches on what it understands and passes over the rest, so a
/// provider can start yielding a new kind of event without breaking one.
/// </summary>
public abstract record LlmStreamEvent;

/// <summary>The next piece of the reply's text, in order; concatenated, the pieces are the whole reply.</summary>
public sealed record LlmTextDelta(string Text) : LlmStreamEvent;

/// <summary>The model called one of the tools it was offered. Its reply ends with its calls: what came of each goes
/// back as a <see cref="LlmRole.Tool"/> message answering its <see cref="LlmToolCall.Id"/>.</summary>
public sealed record LlmToolCallRequested(LlmToolCall Call) : LlmStreamEvent;

/// <summary>
/// The reply is over, and why: the model finished, or something stopped it first. A reply stopped before its end may
/// hold no answer at all — its whole length gone on reasoning — so a caller never takes an empty reply for one.
/// </summary>
/// <param name="ContextTokens">How much of the model's context the conversation now fills, reply included; null where
/// the provider does not say.</param>
public sealed record LlmReplyEnded(LlmReplyEnd Reason, int? ContextTokens = null) : LlmStreamEvent;

/// <summary>Why a reply ended.</summary>
public enum LlmReplyEnd
{
    /// <summary>The model finished: its answer, or its calls, are whole.</summary>
    Finished,
    /// <summary>It reached the most a reply may be.</summary>
    LengthLimit,
    /// <summary>The conversation filled the model's context.</summary>
    ContextFull,
    /// <summary>The model was going in circles, saying the same over and over, and was stopped.</summary>
    Circling,
}
