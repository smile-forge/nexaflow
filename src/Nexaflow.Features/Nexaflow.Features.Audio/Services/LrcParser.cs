using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Nexaflow.Features.Audio.Models;

namespace Nexaflow.Features.Audio.Services;

/// <summary>
/// Parses LRC (synced lyrics) text into ordered <see cref="LyricLine"/>s. Pure and side-effect-free
/// so it is straightforward to unit-test. Supports the common LRC grammar:
/// <list type="bullet">
///   <item>time tags <c>[mm:ss.xx]</c> / <c>[mm:ss.xxx]</c> / <c>[mm:ss]</c>, several per line,</item>
///   <item>id tags <c>[ar:]</c> / <c>[ti:]</c> / <c>[al:]</c> / <c>[offset:]</c> (offset in ms, applied to every line),</item>
/// </list>
/// Malformed lines and lines with no time tag are skipped. Output is sorted by time.
/// </summary>
public static class LrcParser
{
    private static readonly Regex TimeTag =
        new(@"\[(?<m>\d{1,3}):(?<s>\d{1,2})(?:[.:](?<f>\d{1,3}))?\]", RegexOptions.Compiled);

    private static readonly Regex OffsetTag =
        new(@"\[offset:\s*(?<v>[-+]?\d+)\s*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Reads and parses <paramref name="path"/>; returns an empty list if missing or unreadable.</summary>
    public static IReadOnlyList<LyricLine> ParseFile(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Parses raw LRC <paramref name="content"/> into ordered, de-duplicated timed lines.</summary>
    public static IReadOnlyList<LyricLine> Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];

        var offset = TimeSpan.Zero;
        if (OffsetTag.Match(content) is { Success: true } om &&
            int.TryParse(om.Groups["v"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
            offset = TimeSpan.FromMilliseconds(ms);

        var lines = new List<LyricLine>();
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var stamps = TimeTag.Matches(line);
            if (stamps.Count == 0) continue;

            // Lyric text is whatever follows the final time tag on the line.
            var last = stamps[^1];
            var text = line[(last.Index + last.Length)..].Trim();

            foreach (Match m in stamps)
            {
                if (TryReadStamp(m, out var t))
                {
                    var when = t + offset;
                    if (when < TimeSpan.Zero) when = TimeSpan.Zero;
                    lines.Add(new LyricLine(when, text));
                }
            }
        }

        return lines
            .OrderBy(l => l.Time)
            .ToList();
    }

    private static bool TryReadStamp(Match m, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (!int.TryParse(m.Groups["m"].Value, out var min)) return false;
        if (!int.TryParse(m.Groups["s"].Value, out var sec)) return false;

        double frac = 0;
        if (m.Groups["f"].Success)
        {
            var f = m.Groups["f"].Value;
            // "[mm:ss.x]" → tenths, ".xx" → hundredths, ".xxx" → milliseconds.
            if (int.TryParse(f, out var fv))
                frac = fv / Math.Pow(10, f.Length);
        }

        time = new TimeSpan(0, 0, min, sec, (int)Math.Round(frac * 1000));
        return true;
    }
}
