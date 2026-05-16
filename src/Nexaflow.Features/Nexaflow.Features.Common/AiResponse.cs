namespace Nexaflow.Features.Common;

public enum AiResponseKind { Action, Prefill, Message }

/// <summary>
/// Discriminated union returned by <see cref="IAIService.ContextChat"/>.
/// Callers switch on <see cref="Kind"/> to dispatch the result correctly.
/// </summary>
public sealed class AiResponse
{
    public AiResponseKind    Kind   { get; init; }
    public ActionDescriptor? Action { get; init; }
    public string?           Text   { get; init; }

    public static AiResponse AsAction(ActionDescriptor action) =>
        new() { Kind = AiResponseKind.Action, Action = action };

    public static AiResponse AsPrefill(string text) =>
        new() { Kind = AiResponseKind.Prefill, Text = text };

    public static AiResponse AsMessage(string text) =>
        new() { Kind = AiResponseKind.Message, Text = text };
}
