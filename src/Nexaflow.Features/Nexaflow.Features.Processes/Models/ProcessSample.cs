namespace Nexaflow.Features.Processes.Models;

/// <summary>
/// An immutable point-in-time sample of one process — everything the main list needs to render and
/// sort a row. Produced by <see cref="Services.IProcessSource.Snapshot"/>.
/// </summary>
public sealed record ProcessSample
{
    public required int Pid { get; init; }

    /// <summary>Parent process id (0 when none / unknown). Drives the tree.</summary>
    public required int ParentPid { get; init; }

    /// <summary>Image name, e.g. <c>chrome.exe</c>.</summary>
    public required string Name { get; init; }

    /// <summary>CPU usage as a percentage of total machine capacity (already divided by core count).</summary>
    public double CpuPercent { get; init; }

    public long PrivateBytes { get; init; }
    public long WorkingSet { get; init; }

    public string Description { get; init; } = "";
    public string Company { get; init; } = "";

    /// <summary>Full image path when readable (used by Open-file-location / Copy-details / AI tools).</summary>
    public string Path { get; init; } = "";

    /// <summary>
    /// True when image metadata (description / company / path) couldn't be read — typically a protected
    /// or cross-bitness process accessed without elevation.
    /// </summary>
    public bool AccessDenied { get; init; }

    /// <summary>Process start time when readable; used to guard against PID reuse across samples.</summary>
    public DateTime? StartTime { get; init; }
}
