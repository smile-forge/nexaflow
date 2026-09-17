using System;
using System.Collections.Generic;
using Nexaflow.Markdown.Mermaid;

namespace Nexaflow.Visuals.Text.Markdown.Mermaid;

/// <summary>
/// Dates along a time axis, as d3 marks them: on the round boundaries of a unit — every 15 minutes, every 2 days, every month,
/// every 5 years — the unit and step chosen so there are about as many marks as asked for; or every so many of a unit somebody
/// named, where a chart says its interval.
/// </summary>
internal static class DiagramTime
{
    /// <summary>The units an interval can be counted in, shortest first.</summary>
    public static readonly IReadOnlyList<string> Units = ["millisecond", "second", "minute", "hour", "day", "week", "month", "year"];

    /// <summary>The most marks an interval somebody named may make before it is taken to be a mistake.</summary>
    public const int Most = 10_000;

    private const double Second = 1000, Minute = Second * 60, Hour = Minute * 60, Day = Hour * 24, Week = Day * 7, Month = Day * 30, Year = Day * 365;

    private static readonly (string Unit, int Step, double Length)[] Steps =
    [
        ("second", 1, Second), ("second", 5, 5 * Second), ("second", 15, 15 * Second), ("second", 30, 30 * Second),
        ("minute", 1, Minute), ("minute", 5, 5 * Minute), ("minute", 15, 15 * Minute), ("minute", 30, 30 * Minute),
        ("hour", 1, Hour), ("hour", 3, 3 * Hour), ("hour", 6, 6 * Hour), ("hour", 12, 12 * Hour),
        ("day", 1, Day), ("day", 2, 2 * Day), ("week", 1, Week), ("month", 1, Month), ("month", 3, 3 * Month), ("year", 1, Year),
    ];

    /// <summary>About <paramref name="count"/> marks from <paramref name="start"/> to <paramref name="stop"/>, both included, on round boundaries.</summary>
    public static IReadOnlyList<DateTime> Ticks(DateTime start, DateTime stop, int count = 10)
    {
        if (stop < start) (start, stop) = (stop, start);

        var (from, to) = (MermaidDate.Milliseconds(start), MermaidDate.Milliseconds(stop));
        var target = (to - from) / (double)Math.Max(1, count);

        var index = 0;
        while (index < Steps.Length && Steps[index].Length <= target) index++;

        if (index == Steps.Length)
            return Every(start, stop, (int)Math.Max(1, Step(from / Year, to / Year, count)), "year", DayOfWeek.Sunday) ?? [];

        if (index == 0)
            return Every(start, stop, (int)Math.Max(1, Step(from, to, count)), "millisecond", DayOfWeek.Sunday) ?? [];

        var (unit, step, _) = target / Steps[index - 1].Length < Steps[index].Length / target ? Steps[index - 1] : Steps[index];
        return Every(start, stop, step, unit, DayOfWeek.Sunday) ?? [];
    }

    /// <summary>
    /// The marks every <paramref name="step"/> of <paramref name="unit"/> from <paramref name="start"/> to <paramref name="stop"/>,
    /// both included: the unit's boundaries whose count is a multiple of the step — the 1st, 3rd and 5th of a month every 2 days, a
    /// week starting on <paramref name="weekday"/>. Null where the step is no step, the unit none of <see cref="Units"/>, or there
    /// would be more than <see cref="Most"/>.
    /// </summary>
    public static IReadOnlyList<DateTime>? Every(DateTime start, DateTime stop, int step, string unit, DayOfWeek weekday)
    {
        if (step < 1 || !Units.Contains(unit) || stop < start) return null;

        var marks = new List<DateTime>();

        if (unit == "millisecond")
        {
            var (from, to) = (MermaidDate.Milliseconds(start), MermaidDate.Milliseconds(stop));
            if ((to - from) / step > Most) return null;

            for (var at = (long)Math.Ceiling(from / (double)step) * step; at <= to; at += step)
                marks.Add(MermaidDate.FromMilliseconds(at));

            return marks;
        }

        var epoch = Floor(new DateTime(1970, 1, 1), "week", weekday);
        var mark = Floor(start, unit, weekday);
        if (mark < start) mark = Next(mark, unit);

        for (var looked = 0; mark <= stop; mark = Next(mark, unit))
        {
            if (++looked > Most * (long)step) return null;

            var counted = unit switch
            {
                "second" => mark.Second,
                "minute" => mark.Minute,
                "hour" => mark.Hour,
                "day" => mark.Day - 1,
                "week" => (int)Math.Round((mark - epoch).TotalDays / 7),
                "month" => mark.Month - 1,
                _ => mark.Year,
            };

            if (counted % step != 0) continue;
            if (marks.Count == Most) return null;
            marks.Add(mark);
        }

        return marks;
    }

    /// <summary>How far along from <paramref name="start"/> to <paramref name="stop"/> a date is, from nought to one.</summary>
    public static double At(DateTime value, DateTime start, DateTime stop) =>
        stop > start ? (value - start).TotalMilliseconds / (stop - start).TotalMilliseconds : 0;

    private static DateTime Floor(DateTime date, string unit, DayOfWeek weekday) => unit switch
    {
        "second" => new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second),
        "minute" => new DateTime(date.Year, date.Month, date.Day, date.Hour, date.Minute, 0),
        "hour" => new DateTime(date.Year, date.Month, date.Day, date.Hour, 0, 0),
        "day" => date.Date,
        "week" => date.Date.AddDays(-((7 + (int)date.DayOfWeek - (int)weekday) % 7)),
        "month" => new DateTime(date.Year, date.Month, 1),
        _ => new DateTime(date.Year, 1, 1),
    };

    private static DateTime Next(DateTime date, string unit) => unit switch
    {
        "second" => date.AddSeconds(1),
        "minute" => date.AddMinutes(1),
        "hour" => date.AddHours(1),
        "day" => date.AddDays(1),
        "week" => date.AddDays(7),
        "month" => date.AddMonths(1),
        _ => date.AddYears(1),
    };

    /// <summary>d3's step between round numbers: one, two or five times a power of ten, making about <paramref name="count"/> steps.</summary>
    private static double Step(double start, double stop, int count)
    {
        var raw = Math.Abs(stop - start) / Math.Max(1, count);
        if (!(raw > 0)) return 1;

        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var error = raw / power;
        return power * (error >= Math.Sqrt(50) ? 10 : error >= Math.Sqrt(10) ? 5 : error >= Math.Sqrt(2) ? 2 : 1);
    }
}
