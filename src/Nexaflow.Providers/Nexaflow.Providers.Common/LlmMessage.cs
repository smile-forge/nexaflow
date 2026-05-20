namespace Nexaflow.Providers.Common;

/// <summary>The role of a participant in an LLM conversation.</summary>
public enum LlmRole { System, User, Assistant }

/// <summary>A single message in an LLM conversation turn.</summary>
public record LlmMessage(LlmRole Role, string Text);
