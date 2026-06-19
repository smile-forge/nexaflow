namespace Nexaflow.Features.Processes.Models;

/// <summary>
/// A whole-system sample: every process plus the system-wide CPU / memory gauges that feed the
/// toolbar graph. Immutable so it can be built off the UI thread and handed across without copying.
/// </summary>
public sealed record ProcessSnapshot
{
    public required IReadOnlyList<ProcessSample> Processes { get; init; }

    /// <summary>System-wide CPU load 0–100 (derived from kernel/user/idle deltas).</summary>
    public double SystemCpuPercent { get; init; }

    /// <summary>Physical memory load 0–100.</summary>
    public double MemoryLoadPercent { get; init; }

    public long TotalPhysicalBytes { get; init; }
    public long UsedPhysicalBytes { get; init; }
    public int ProcessorCount { get; init; }

    public static ProcessSnapshot Empty { get; } = new() { Processes = [] };
}
