namespace Nexaflow.Features.Common.ClientTools;

/// <summary>
/// Outcome of a single client tool invocation.
/// </summary>
/// <param name="Success">Whether the tool achieved its purpose.</param>
/// <param name="Summary">Short, human-facing line for the approval / progress overlay.</param>
/// <param name="ModelText">Full text fed back to the model as the tool result.</param>
/// <param name="IsError">True when the tool failed; the model is told so it can recover.</param>
public sealed record ToolResult(bool Success, string Summary, string ModelText, bool IsError = false)
{
    /// <summary>
    /// Files this tool read, created, or affected. The harness collects these across a run so the
    /// resulting conversation can carry them as context (see <c>AiResponse.Attachments</c>).
    /// </summary>
    public IReadOnlyList<string>? Attachments { get; init; }

    /// <summary>A successful result. <paramref name="modelText"/> defaults to the summary.</summary>
    public static ToolResult Ok(string summary, string? modelText = null) =>
        new(true, summary, modelText ?? summary);

    /// <summary>A failed result the model should see and react to.</summary>
    public static ToolResult Error(string summary, string? modelText = null) =>
        new(false, summary, modelText ?? summary, IsError: true);
}
