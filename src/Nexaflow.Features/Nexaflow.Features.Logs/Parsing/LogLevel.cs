namespace Nexaflow.Features.Logs.Parsing;

public enum LogLevel
{
    Unknown,
    Debug,
    Info,
    Warning,
    Error,
    Fatal,
}

/// <summary>
/// The severity vocabulary shared by every parser, so a line reading <c>WRN</c>, <c>warning</c> or
/// <c>"level": 4</c> lights up the same toolbar toggle whatever produced the file.
/// </summary>
public static class LogLevels
{
    /// <summary>Maps a level word to a severity, case-insensitively. An unrecognised word is Unknown.</summary>
    public static LogLevel FromName(string? name) => name?.Trim().ToUpperInvariant() switch
    {
        "FATAL" or "CRITICAL" or "CRIT" or "EMERG" or "EMERGENCY" or "ALERT" or "PANIC" => LogLevel.Fatal,
        "ERROR" or "ERR" or "SEVERE"                                                    => LogLevel.Error,
        "WARN" or "WARNING" or "WRN"                                                    => LogLevel.Warning,
        "INFO" or "INFORMATION" or "INFORMATIONAL" or "INF" or "NOTICE"                 => LogLevel.Info,
        "DEBUG" or "DBG" or "TRACE" or "VERBOSE" or "FINE" or "FINER" or "FINEST"       => LogLevel.Debug,
        _                                                                               => LogLevel.Unknown,
    };

    /// <summary>
    /// Maps a numeric level to a severity. Two conventions are in the wild and their ranges overlap, so the
    /// scale is chosen by magnitude: 10 and above is the Bunyan / pino ladder (10 trace … 60 fatal), while
    /// 0–7 is RFC 5424 syslog severity (0 emerg … 7 debug), which is what systemd- and GCP-style JSON emits.
    /// Anything outside both is Unknown rather than a guess — a wrongly-coloured line is worse than a plain one.
    /// </summary>
    public static LogLevel FromNumber(double value) => value switch
    {
        >= 60          => LogLevel.Fatal,
        >= 50          => LogLevel.Error,
        >= 40          => LogLevel.Warning,
        >= 30          => LogLevel.Info,
        >= 10          => LogLevel.Debug,
        >= 0 and <= 2  => LogLevel.Fatal,     // syslog: emerg / alert / crit
        3              => LogLevel.Error,
        4              => LogLevel.Warning,
        5 or 6         => LogLevel.Info,      // syslog: notice / informational
        7              => LogLevel.Debug,
        _              => LogLevel.Unknown,   // 8, 9, negatives — neither ladder claims them
    };
}
