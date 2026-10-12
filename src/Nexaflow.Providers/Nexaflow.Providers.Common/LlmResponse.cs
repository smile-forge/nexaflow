namespace Nexaflow.Providers.Common;

/// <summary>A finished reply — the stream of an <see cref="ILlmProvider"/> read to its end.</summary>
public record LlmResponse(string RawText);
