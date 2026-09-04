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

    /// <summary>Media transport time: <c>m:ss</c> under an hour, else <c>h:mm:ss</c> (negative clamps
    /// to zero). The shared form of the formatter the audio and video players each carried.</summary>
    public static string FormatMediaTime(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return t.TotalHours >= 1
            ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
            : $"{t.Minutes}:{t.Seconds:00}";
    }


    /// <summary>How much longer something has to run, rounded to what is worth saying. An estimate that
    /// claims "7 minutes 42 seconds" is precise about a number it cannot know, so this stays coarse:
    /// <c>less than a minute</c>, <c>about 12 minutes</c>, <c>about 2 hours</c>.</summary>
    public static string FormatEta(TimeSpan left)
    {
        if (left < TimeSpan.FromMinutes(1)) return "less than a minute";
        if (left < TimeSpan.FromHours(1))   return $"about {(int)Math.Round(left.TotalMinutes)} minutes";
        if (left < TimeSpan.FromDays(1))    return $"about {(int)Math.Round(left.TotalHours)} hours";
        return "over a day";
    }
}
