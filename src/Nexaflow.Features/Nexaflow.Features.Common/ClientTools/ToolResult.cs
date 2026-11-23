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
    /// Recordings this tool produced for the model to hear (e.g. a stretch of the track the audio player has open).
    /// The harness forwards them as audio when the conversation model can hear, or as a transcript from the Speech
    /// In model when it cannot. Transient, like <see cref="Images"/>.
    /// </summary>
    public IReadOnlyList<ContextAudio>? Audio { get; init; }

    /// <summary>
    /// The work this call set going that it could not wait out, handed to the assistant's background tasks (<see cref="HandOff"/>);
    /// null for a call that finished.
    /// </summary>
    public HandedOffWork? HandedOff { get; init; }

    /// <summary>A successful result. <paramref name="modelText"/> defaults to the summary.</summary>
    public static ToolResult Ok(string summary, string? modelText = null) =>
        new(true, summary, modelText ?? summary);

    /// <summary>A failed result the model should see and react to.</summary>
    public static ToolResult Error(string summary, string? modelText = null) =>
        new(false, summary, modelText ?? summary, IsError: true);

    /// <summary>
    /// The call set work going it cannot wait out, and hands the rest to the assistant: the work becomes one of the assistant's
    /// background tasks, which the user sees and can stop, and the model is told at once that it carries on in the background.
    /// When it ends, the user is told — or, while the user is still where the work was asked for, the assistant picks it up
    /// there. Work a tool knows from the start to be slow says so instead (<see cref="IClientTool.RunsInBackground"/>).
    /// </summary>
    /// <param name="summary">What the call has done so far, for the run's progress.</param>
    /// <param name="doing">What the work is, as the background tasks list it.</param>
    /// <param name="finish">Carries the work to its end (see <see cref="HandedOffWork.Finish"/>).</param>
    public static ToolResult HandOff(string summary, string doing, Func<CancellationToken, Task<ToolResult>> finish) =>
        new(true, summary, summary) { HandedOff = new HandedOffWork(doing, finish) };

    /// <summary>
    /// The call set work going that carries on in the background (<see cref="IClientTool.RunsInBackground"/>): it has
    /// started, not finished, and is never told as done.
    /// </summary>
    public bool InBackground { get; init; }
}
