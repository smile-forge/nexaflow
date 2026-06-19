namespace Nexaflow.Features.Processes.Models;

/// <summary>One thread of a process, for the details view's Threads section.</summary>
public sealed record ThreadInfo
{
    public required int Tid { get; init; }
    public string State { get; init; } = "";
    public string Priority { get; init; } = "";
    public TimeSpan TotalProcessorTime { get; init; }
    public DateTime? StartTime { get; init; }

    // ── Display helpers (keep the XAML free of TimeSpan/date formatting) ──
    public string CpuTimeText => TotalProcessorTime.ToString(@"hh\:mm\:ss");
    public string StartedText => StartTime?.ToString("g") ?? "";

    /// <summary>Tab-separated row, for the "Copy" context action.</summary>
    public string RowText => $"{Tid}\t{State}\t{Priority}\t{CpuTimeText}\t{StartedText}";
}
