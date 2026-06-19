using System;

namespace Nexaflow.Visuals.Common.Formatting;

/// <summary>Formats time spans as compact human strings (e.g. <c>3d 2h 15m</c>).</summary>
public static class DurationFormatter
{
    /// <summary>Coarse uptime: days+hours+minutes, hours+minutes, or minutes — whichever the span warrants.</summary>
    public static string FormatUptime(TimeSpan up)
    {
        if (up.TotalDays  >= 1) return $"{(int)up.TotalDays}d {up.Hours}h {up.Minutes}m";
        if (up.TotalHours >= 1) return $"{up.Hours}h {up.Minutes}m";
        return $"{up.Minutes}m";
    }
}
