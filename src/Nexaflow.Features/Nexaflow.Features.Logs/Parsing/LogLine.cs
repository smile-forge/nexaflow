namespace Nexaflow.Features.Logs.Parsing;

public sealed record LogLine(LogLevel Level, DateTime? Timestamp, string RawText);
