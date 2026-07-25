using System.Text;

namespace Nexaflow.Tests.Fixtures;

/// <summary>
/// Log-file fixtures for the log viewer: a short hand-written log with mixed levels, a long one
/// (4,000 timestamped lines) so the tail reader and background head-load are both exercised, and a
/// JSON-lines log in Serilog's compact shape for the structured parser.
/// </summary>
internal sealed class LogSamples : ISampleSet
{
    public string SubDirectory => "logs";

    public IReadOnlyList<SampleFile> Files { get; } =
    [
        SampleFile.Text("app_short.log", Short),
        SampleFile.Text("app_long.log",  BuildLong()),
        SampleFile.Text("app_structured.log", Structured),
    ];

    private const string Short =
        """
        2026-06-18 09:00:01.123 INFO  Startup: Nexaflow shell initialising
        2026-06-18 09:00:01.456 DEBUG FeatureManager: discovered 19 features
        2026-06-18 09:00:02.001 INFO  Workspace 'default' ready
        2026-06-18 09:00:05.732 WARN  Provider 'ollama' unreachable, retrying
        2026-06-18 09:00:06.110 ERROR Provider 'ollama' failed after 3 attempts
        2026-06-18 09:00:06.111 INFO  Falling back to provider 'claude'
        2026-06-18 09:00:12.998 INFO  AI request completed in 842ms

        """;

    /// <summary>
    /// JSON lines in Serilog's compact shape. Note the Information lines carry no <c>@l</c> at all — that is
    /// the format, not an omission in the fixture, and it is the case a naive JSON parser gets wrong.
    /// </summary>
    private const string Structured =
        """
        {"@t":"2026-06-18T09:00:01.1230000Z","@mt":"Startup: {Component} initialising","Component":"shell"}
        {"@t":"2026-06-18T09:00:01.4560000Z","@l":"Debug","@mt":"FeatureManager: discovered {Count} features","Count":19}
        {"@t":"2026-06-18T09:00:02.0010000Z","@mt":"Workspace {Name} ready","Name":"default"}
        {"@t":"2026-06-18T09:00:05.7320000Z","@l":"Warning","@mt":"Provider {Provider} unreachable, retrying","Provider":"ollama"}
        {"@t":"2026-06-18T09:00:06.1100000Z","@l":"Error","@mt":"Provider {Provider} failed after {Attempts} attempts","Provider":"ollama","Attempts":3}
        {"@t":"2026-06-18T09:00:06.1110000Z","@mt":"Falling back to provider {Provider}","Provider":"claude"}
        {"@t":"2026-06-18T09:00:12.9980000Z","@l":"Fatal","@mt":"Unrecoverable: {Reason}","Reason":"disk full"}

        """;

    /// <summary>4,000 timestamped lines cycling through levels — generated deterministically.</summary>
    private static string BuildLong()
    {
        var sb = new StringBuilder(4000 * 64);
        string[] levels = ["INFO ", "DEBUG", "WARN ", "ERROR"];
        for (int i = 0; i < 4000; i++)
        {
            int sec = i % 60, min = (i / 60) % 60, hour = (i / 3600) % 24;
            string level = levels[i % levels.Length];
            sb.Append("2026-06-18 ")
              .Append(hour.ToString("D2")).Append(':')
              .Append(min.ToString("D2")).Append(':')
              .Append(sec.ToString("D2")).Append(".000 ")
              .Append(level).Append(" event #").Append(i)
              .Append(" processed batch of ").Append((i * 13) % 500).Append(" items\n");
        }
        return sb.ToString();
    }
}
