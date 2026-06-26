using System.Collections.Generic;

namespace Nexaflow.Providers.Common;

/// <summary>The role of a participant in an LLM conversation.</summary>
public enum LlmRole { System, User, Assistant }

/// <summary>A single message in an LLM conversation turn.</summary>
/// <param name="Role">Who is speaking.</param>
/// <param name="Text">The message text.</param>
public record LlmMessage(LlmRole Role, string Text)
{
    /// <summary>
    /// Files/images carried by this message (e.g. a screenshot a tool captured, included alongside the
    /// tool-result text). The attachment travels with the message it belongs to — providers that can
    /// process images encode them inline; others fall back to listing file paths in the text. Default: none.
    /// </summary>
    public IReadOnlyList<LlmAttachment>? Attachments { get; init; }
}
