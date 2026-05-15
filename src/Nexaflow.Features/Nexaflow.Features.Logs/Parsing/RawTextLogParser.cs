using System.Globalization;
using System.Text.RegularExpressions;

namespace Nexaflow.Features.Logs.Parsing;

/// <summary>
/// Fallback parser for plain-text log files. Detects log level and common
/// timestamp formats via regex. Always has a non-zero confidence score so
/// it is always available as a fallback.
/// </summary>
public sealed class RawTextLogParser : ILogParser
{
    public string FormatName => "Raw Text";

    // Matches level keywords with or without enclosing brackets/pipes
    private static readonly Regex LevelRegex = new(
        @"[\[|]?\s*(FATAL|CRITICAL|ERR(?:OR)?|WRN|WARN(?:ING)?|INFO(?:RMATION)?|INF|DEBUG|DBG|TRACE|VERBOSE)\s*[\]|]?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Timestamp patterns tried in order; first match wins
    private static readonly (Regex Pattern, string ParseHint)[] TimestampPatterns =
    [
        // ISO 8601: 2024-01-15T10:23:45 / 2024-01-15 10:23:45.123
        (new Regex(@"^(\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})?)", RegexOptions.Compiled), "ISO"),
        // US: 01/15/2024 10:23:45
        (new Regex(@"^(\d{2}/\d{2}/\d{4} \d{2}:\d{2}:\d{2})", RegexOptions.Compiled), "US"),
        // Syslog: Jan 15 10:23:45
        (new Regex(@"^(\w{3}\s+\d{1,2} \d{2}:\d{2}:\d{2})", RegexOptions.Compiled), "SYSLOG"),
    ];

    private static readonly string[] SyslogFormats = ["MMM  d HH:mm:ss", "MMM d HH:mm:ss"];

    public float Confidence(ReadOnlySpan<byte> headerBytes, string firstLine) => 0.1f;

    public LogLine ParseLine(string rawText)
    {
        var level     = DetectLevel(rawText);
        var timestamp = DetectTimestamp(rawText);
        return new LogLine(level, timestamp, rawText);
    }

    private static LogLevel DetectLevel(string text)
    {
        var m = LevelRegex.Match(text);
        if (!m.Success) return LogLevel.Unknown;

        return m.Groups[1].Value.ToUpperInvariant() switch
        {
            "FATAL" or "CRITICAL"     => LogLevel.Fatal,
            "ERROR" or "ERR"                             => LogLevel.Error,
            "WARN"  or "WARNING" or "WRN"                => LogLevel.Warning,
            "INFO"  or "INFORMATION" or "INF"            => LogLevel.Info,
            "DEBUG" or "DBG" or "TRACE"
                    or "VERBOSE"                 => LogLevel.Debug,
            _                                    => LogLevel.Unknown,
        };
    }

    private static DateTime? DetectTimestamp(string text)
    {
        foreach (var (pattern, hint) in TimestampPatterns)
        {
            var m = pattern.Match(text);
            if (!m.Success) continue;

            var raw = m.Groups[1].Value;
            if (hint == "SYSLOG")
            {
                if (DateTime.TryParseExact(raw, SyslogFormats,
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var syslog))
                    return syslog.Year == 1 ? syslog.AddYears(DateTime.Now.Year - 1) : syslog;
            }
            else
            {
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeLocal, out var dt))
                    return dt;
            }
        }
        return null;
    }

    // ── Static helpers used by LogViewModel for timestamp detection ───────────

    public static bool LineHasTimestamp(string line)
    {
        foreach (var (pattern, _) in TimestampPatterns)
            if (pattern.IsMatch(line)) return true;
        return false;
    }
}
