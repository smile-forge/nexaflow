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
