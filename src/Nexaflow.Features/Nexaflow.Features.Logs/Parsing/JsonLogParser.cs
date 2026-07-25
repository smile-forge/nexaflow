using System.Globalization;
using System.Text.Json;

namespace Nexaflow.Features.Logs.Parsing;

/// <summary>
/// Parser for JSON-lines log files — one JSON object per line, as emitted by Serilog (both the compact
/// <c>@t</c>/<c>@l</c> formatter and the verbose one), Bunyan / pino, python-json-logger, structlog and the
/// ECS / GCP structured formats.
/// <para>
/// There is no single schema, so the level and timestamp are found by name from the sets below rather than
/// by a fixed shape, and their values are accepted in every form those producers use: a level as a word or
/// as a number on either of the two numeric ladders, a timestamp as ISO-8601 or as a Unix epoch in seconds
/// or milliseconds. A line that is not a JSON object at all (a stack-trace continuation, a truncated tail)
/// is returned unparsed rather than failing the file.
/// </para>
/// </summary>
public sealed class JsonLogParser : ILogParser
{
    public string FormatName => "JSON";

    // Matched case-insensitively. "@l"/"@t" are Serilog compact; "log.level"/"@timestamp" are ECS;
    // "levelname"/"asctime" are Python; "severity" is GCP/syslog; "msg"/"time" are Bunyan and pino.
    private static readonly string[] LevelFields =
        ["@l", "level", "levelname", "level_name", "log.level", "loglevel", "severity", "severitytext", "lvl"];

    private static readonly string[] TimeFields =
        ["@t", "timestamp", "@timestamp", "time", "ts", "asctime", "datetime", "date", "eventtime"];

    // Serilog's compact formatter omits @l entirely for Information — the commonest level in most files.
    // Seeing its message-template marker with no level therefore means Information, not "no level".
    private static readonly string[] SerilogCompactMarkers = ["@mt", "@m", "@i", "@x"];

    public float Confidence(ReadOnlySpan<byte> headerBytes, string firstLine)
    {
        if (!TryReadObject(firstLine, out var doc)) return 0f;
        using (doc)
        {
            // A JSON-lines file we can read severity or time out of is unambiguously this format. One we
            // can't is still JSON lines — worth claiming over raw text, but not worth claiming loudly.
            return Read(doc!.RootElement).Recognised ? 0.9f : 0.3f;
        }
    }

    public LogLine ParseLine(string rawText)
    {
        if (!TryReadObject(rawText, out var doc)) return new LogLine(LogLevel.Unknown, null, rawText);
        using (doc)
        {
            var (level, timestamp, _) = Read(doc!.RootElement);
            return new LogLine(level, timestamp, rawText);
        }
    }

    private static bool TryReadObject(string line, out JsonDocument? doc)
    {
        doc = null;
        var trimmed = line.AsSpan().Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{') return false;

        try
        {
            var parsed = JsonDocument.Parse(trimmed.ToString());
            if (parsed.RootElement.ValueKind != JsonValueKind.Object) { parsed.Dispose(); return false; }
            doc = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;   // a partial line (the tail of a file still being written) is not an error
        }
    }

    /// <summary>
    /// One pass over the object's properties, taking the first level and time field it recognises.
    /// <c>Recognised</c> says whether either field was present at all — the difference between "this file
    /// has no severities" and "we didn't understand this file".
    /// </summary>
    private static (LogLevel Level, DateTime? Timestamp, bool Recognised) Read(JsonElement root)
    {
        JsonElement? levelValue = null, timeValue = null;
        var compactSerilog = false;

        foreach (var property in root.EnumerateObject())
        {
            if (levelValue is null && Matches(property.Name, LevelFields)) levelValue = property.Value;
            else if (timeValue is null && Matches(property.Name, TimeFields)) timeValue = property.Value;
            else if (!compactSerilog && Matches(property.Name, SerilogCompactMarkers)) compactSerilog = true;
        }

        var level = levelValue is { } lv ? ReadLevel(lv) : LogLevel.Unknown;
        if (level == LogLevel.Unknown && levelValue is null && compactSerilog) level = LogLevel.Info;

        return (level, timeValue is { } tv ? ReadTimestamp(tv) : null,
                levelValue is not null || timeValue is not null || compactSerilog);
    }

    private static bool Matches(string name, string[] candidates)
    {
        foreach (var candidate in candidates)
            if (name.Equals(candidate, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static LogLevel ReadLevel(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => LogLevels.FromName(value.GetString()),
        JsonValueKind.Number => value.TryGetDouble(out var n) ? LogLevels.FromNumber(n) : LogLevel.Unknown,
        _                    => LogLevel.Unknown,
    };

    private static DateTime? ReadTimestamp(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                // AssumeLocal matches the raw-text parser, so a UTC stamp lands in the same local frame the
                // From/To filter boxes are typed in.
                return DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                                         DateTimeStyles.AssumeLocal, out var parsed)
                    ? parsed
                    : null;

            case JsonValueKind.Number when value.TryGetDouble(out var epoch):
                // pino writes milliseconds, plenty of others write seconds; the magnitude separates them
                // (a seconds value that large would be the year 5138).
                try
                {
                    var utc = epoch > 100_000_000_000
                        ? DateTimeOffset.FromUnixTimeMilliseconds((long)epoch)
                        : DateTimeOffset.FromUnixTimeSeconds((long)epoch);
                    return utc.LocalDateTime;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return null;   // not an epoch at all — some other numeric field wearing the name
                }

            default:
                return null;
        }
    }
}
