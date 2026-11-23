namespace Nexaflow.Features.Common.ClientTools;

/// <summary>
/// The rest of a call's work, handed to the assistant's background tasks (<see cref="ToolResult.HandOff"/>): what it is doing,
/// for the user, and how to carry it to its end.
/// </summary>
/// <param name="Doing">What the work is, as the background tasks list it, e.g. "Running npm install in the console".</param>
/// <param name="Finish">
/// Carries the work to its end and says how it ended: the result's <see cref="ToolResult.Summary"/> is what the user is told,
/// its <see cref="ToolResult.ModelText"/> what the assistant reads. Cancelled when the user stops the task, or when what it
/// waited on was taken over; then it throws <see cref="OperationCanceledException"/>.
/// </param>
public sealed record HandedOffWork(string Doing, Func<CancellationToken, Task<ToolResult>> Finish);
