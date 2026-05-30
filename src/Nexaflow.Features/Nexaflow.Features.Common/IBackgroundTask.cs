namespace Nexaflow.Features.Common;

/// <summary>
/// A self-contained unit of background work a feature can hand to the shell via
/// <see cref="Services.IShellServices.QueueBackgroundTask"/>. The shell reports it through the
/// background-activity area and runs <see cref="RunAsync"/> off the UI thread. Implementations
/// capture whatever services/state they need at construction.
/// </summary>
public interface IBackgroundTask
{
    /// <summary>Short human-readable label shown in the background-activity UI while running.</summary>
    string Description { get; }

    /// <summary>Performs the work. Runs on a background thread; throw to signal failure.</summary>
    Task RunAsync(CancellationToken ct);
}
