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

    /// <summary>
    /// Images this tool captured for the model to see (e.g. a web page screenshot). The harness
    /// forwards them to the next model turn as vision input when the conversation model supports
    /// images; otherwise it tells the model they couldn't be shown. Transient — not persisted as
    /// conversation artifacts like <see cref="Attachments"/>.
    /// </summary>
    public IReadOnlyList<ContextImage>? Images { get; init; }

    /// <summary>
    /// When true the agent run ends immediately and silently after this result: the harness feeds NO
    /// tool-results text back to the model and shows no final message — it just dismisses the response
    /// UI. Used when a tool has handed work off to a background process and there is nothing to report yet.
    /// </summary>
    public bool EndsRun { get; init; }

    /// <summary>A successful result. <paramref name="modelText"/> defaults to the summary.</summary>
    public static ToolResult Ok(string summary, string? modelText = null) =>
        new(true, summary, modelText ?? summary);

    /// <summary>A failed result the model should see and react to.</summary>
    public static ToolResult Error(string summary, string? modelText = null) =>
        new(false, summary, modelText ?? summary, IsError: true);

    /// <summary>
    /// Ends the run immediately with nothing said to the user or the model (see <see cref="EndsRun"/>).
    /// </summary>
    public static ToolResult EndSilently(string summary) =>
        new(true, summary, summary) { EndsRun = true };
}
