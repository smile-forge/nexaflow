namespace Nexaflow.Features.Logs.Parsing;

/// <summary>
/// Stub parser for structured JSON log files (e.g. Serilog JSON output).
/// Not yet implemented — always returns zero confidence.
/// </summary>
public sealed class JsonLogParser : ILogParser
{
    public string FormatName => "JSON";

    public float Confidence(ReadOnlySpan<byte> headerBytes, string firstLine)
    {
        // A JSON log file typically starts with '{' on the first line
        var trimmed = firstLine.TrimStart();
        return trimmed.StartsWith('{') && trimmed.EndsWith('}') ? 0.8f : 0f;
    }

    public LogLine ParseLine(string rawText)
    {
        // TODO: parse structured JSON fields (level, timestamp, message)
        return new LogLine(LogLevel.Unknown, null, rawText);
    }
}
