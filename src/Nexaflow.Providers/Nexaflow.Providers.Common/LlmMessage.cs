namespace Nexaflow.Providers.Common;

/// <summary>A single message in an LLM conversation turn.</summary>
public record LlmMessage(bool IsUser, string Text);
