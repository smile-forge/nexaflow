namespace Nexaflow.Features.Common;

public enum AiResponseKind { Prefill, Message }

/// <summary>
/// Terminal outcome of an agent run: either text to prefill into the input box, or a final
/// message to show the user. Tool calls are handled inside the harness loop, not surfaced here.
/// </summary>
public sealed class AiResponse
{
    public AiResponseKind Kind { get; init; }
    public string?        Text { get; init; }

    /// <summary>Files surfaced by the agent run's tools (read/created/affected), for conversation context.</summary>
    public IReadOnlyList<string> Attachments { get; init; } = [];

    public static AiResponse AsPrefill(string text) =>
        new() { Kind = AiResponseKind.Prefill, Text = text };

    public static AiResponse AsMessage(string text, IReadOnlyList<string>? attachments = null) =>
        new() { Kind = AiResponseKind.Message, Text = text, Attachments = attachments ?? [] };
}
