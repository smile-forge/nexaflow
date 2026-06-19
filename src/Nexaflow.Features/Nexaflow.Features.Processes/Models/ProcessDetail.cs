namespace Nexaflow.Features.Processes.Models;

/// <summary>
/// Full per-process detail backing the details tab (General / Performance / Threads / Modules). The
/// per-section <c>*Denied</c> flags let the view render what it could read and offer an elevated retry
/// for the rest, rather than blanking the tab.
/// </summary>
public sealed record ProcessDetail
{
    public required int Pid { get; init; }
    public required string Name { get; init; }
    public int ParentPid { get; init; }
    public string ParentName { get; init; } = "";

    // ── General ──────────────────────────────────────────────────────────────
    public string Path { get; init; } = "";
    public string CommandLine { get; init; } = "";
    public string User { get; init; } = "";
    public string Description { get; init; } = "";
    public string Company { get; init; } = "";
    public string FileVersion { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string PriorityClass { get; init; } = "";
    public DateTime? StartTime { get; init; }

    // ── Performance ───────────────────────────────────────────────────────────
    public double CpuPercent { get; init; }
    public long PrivateBytes { get; init; }
    public long WorkingSet { get; init; }
    public long PeakWorkingSet { get; init; }
    public int ThreadCount { get; init; }
    public int HandleCount { get; init; }
    public int GdiObjects { get; init; }
    public int UserObjects { get; init; }

    // ── Collections ───────────────────────────────────────────────────────────
    public IReadOnlyList<ThreadInfo> Threads { get; init; } = [];
    public bool ThreadsDenied { get; init; }
    public IReadOnlyList<ModuleInfo> Modules { get; init; } = [];
    public bool ModulesDenied { get; init; }
}
