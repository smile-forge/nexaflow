namespace Nexaflow.Features.Processes.Services;

/// <summary>The CPU-percent delta formula, factored out so it can be unit-tested without live processes.</summary>
internal static class CpuMath
{
    /// <summary>
    /// CPU usage as a percentage of total machine capacity between two samples:
    /// <c>(Δprocessor-time / Δwall-clock) / cores × 100</c>, clamped to 0–100. Returns 0 when there is no
    /// elapsed time (the first sample) or inputs are degenerate.
    /// </summary>
    public static double Percent(TimeSpan prevCpu, TimeSpan nowCpu, long prevTicks, long nowTicks,
                                 long frequency, int cores)
    {
        if (nowTicks <= prevTicks || frequency <= 0 || cores <= 0) return 0;
        double seconds = (nowTicks - prevTicks) / (double)frequency;
        if (seconds <= 0) return 0;
        return Math.Clamp((nowCpu - prevCpu).TotalSeconds / seconds / cores * 100.0, 0, 100);
    }
}
