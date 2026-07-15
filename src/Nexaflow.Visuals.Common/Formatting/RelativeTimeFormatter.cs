using System;

namespace Nexaflow.Visuals.Common.Formatting;

/// <summary>Formats a past instant the way a chat log wants it: relative while it's still today
/// (<c>just now</c>, <c>5 minutes ago</c>, <c>3 hours ago</c>), absolute once it isn't
/// (<c>14 Jul 2026, 09:32</c>) — because "22 hours ago" stops meaning anything across a day boundary.</summary>
public static class RelativeTimeFormatter
{
    /// <param name="now">Injected so the result is testable; defaults to <see cref="DateTime.Now"/>.</param>
    public static string Format(DateTime when, DateTime now)
    {
        // A different calendar day reads better as a date, however few hours ago it was.
        if (when.Date != now.Date)
            return when.ToString("d MMM yyyy, HH:mm");

        var elapsed = now - when;

        // A clock skew (or a message stamped a moment in the future) is not worth a special case.
        if (elapsed < TimeSpan.FromSeconds(60)) return "just now";

        if (elapsed < TimeSpan.FromHours(1))
        {
            var mins = (int)elapsed.TotalMinutes;
            return $"{mins} minute{(mins == 1 ? "" : "s")} ago";
        }

        var hours = (int)elapsed.TotalHours;
        return $"{hours} hour{(hours == 1 ? "" : "s")} ago";
    }

    public static string Format(DateTime when) => Format(when, DateTime.Now);
}
