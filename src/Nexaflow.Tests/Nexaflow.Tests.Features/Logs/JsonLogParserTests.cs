using System;
using System.Text;
using Nexaflow.Features.Logs.Parsing;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Features.Logs;

/// <summary>
/// The JSON-lines parser. There is no single schema for structured logs, so what is asserted here is that
/// each of the shapes actually in the wild yields a severity and a timestamp: Serilog compact and verbose,
/// Bunyan / pino (numeric levels, epoch times), Python's logging module, and the ECS / GCP field names.
/// <para>
/// The failure this guards against is the quiet one. The parser wins the format vote on any braced first
/// line, so if it cannot read a field the status bar still says "JSON" while the severity toggles and the
/// time-range filter silently match nothing — the file looks parsed and isn't.
/// </para>
/// </summary>
[TestClass]
public class JsonLogParserTests
{
    private static readonly JsonLogParser Parser = new();

    private static LogLine Parse(string line) => Parser.ParseLine(line);

    private static float Confidence(string line) =>
        Parser.Confidence(Encoding.UTF8.GetBytes(line).AsSpan(0, Math.Min(16, line.Length)), line);

    // ── Serilog ───────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("log-filetype-json")]
    public void SerilogCompact_ReadsTheLevelAndTheTimestamp()
    {
        var line = Parse("""{"@t":"2026-06-18T09:00:06.1100000Z","@l":"Error","@mt":"Provider {P} failed","P":"ollama"}""");

        Assert.AreEqual(LogLevel.Error, line.Level);
        Assert.AreEqual(new DateTime(2026, 6, 18, 9, 0, 6, 110, DateTimeKind.Utc).ToLocalTime(), line.Timestamp);
    }

    [TestMethod]
    [CoversNode("log-filetype-json")]
    public void SerilogCompact_TreatsAMissingLevelAsInformation()
    {
        // The compact formatter omits @l for Information — the commonest level in most files. Reading that
        // as "no level" would leave the majority of a Serilog log uncoloured while the rest lit up.
        var line = Parse("""{"@t":"2026-06-18T09:00:02.0010000Z","@mt":"Workspace {Name} ready","Name":"default"}""");

        Assert.AreEqual(LogLevel.Info, line.Level);
    }

    [TestMethod]
    [CoversNode("log-filetype-json")]
    public void PlainJson_WithNoLevelAndNoSerilogMarkers_StaysUnknown()
    {
        // No @mt to justify the assumption — claiming Information here would be inventing severity.
        var line = Parse("""{"timestamp":"2026-06-18T09:00:02Z","message":"something happened"}""");

        Assert.AreEqual(LogLevel.Unknown, line.Level);
        Assert.IsNotNull(line.Timestamp, "the timestamp is still readable");
    }

    [TestMethod]
    [CoversNode("log-filetype-json")]
    public void SerilogVerbose_UsesTheLongFieldNames()
    {
        var line = Parse("""{"Timestamp":"2026-06-18T09:00:05.7320000+00:00","Level":"Warning","MessageTemplate":"retrying"}""");

        Assert.AreEqual(LogLevel.Warning, line.Level);
        Assert.IsNotNull(line.Timestamp);
    }

    // ── Bunyan / pino ─────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("log-filetype-json")]
    public void Pino_ReadsTheNumericLadderAndEpochMilliseconds()
    {
        var line = Parse("""{"level":50,"time":1781946006110,"msg":"connection refused"}""");

        Assert.AreEqual(LogLevel.Error, line.Level, "50 is error on the Bunyan / pino ladder");
        Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1781946006110).LocalDateTime, line.Timestamp);
    }

    [TestMethod]
    [CoversNode("log-filetype-json")]
    public void EpochSeconds_AreNotReadAsMilliseconds()
    {
        var line = Parse("""{"level":30,"time":1781946006,"msg":"ok"}""");

        Assert.AreEqual(LogLevel.Info, line.Level);
        Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1781946006).LocalDateTime, line.Timestamp,
                        "read as milliseconds this would land in 1970 and collapse the whole time filter");
    }

    // ── Python / ECS / GCP ────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("log-filetype-json")]
    public void PythonJsonLogger_UsesLevelnameAndAsctime()
    {
        var line = Parse("""{"asctime":"2026-06-18 09:00:06,110","levelname":"CRITICAL","message":"boom"}""");

        Assert.AreEqual(LogLevel.Fatal, line.Level);
    }

    [TestMethod]
    [CoversNode("log-filetype-json")]
    public void EcsAndGcpFieldNames_AreRecognised()
    {
        var ecs = Parse("""{"@timestamp":"2026-06-18T09:00:06.110Z","log.level":"WARN","message":"slow"}""");
        Assert.AreEqual(LogLevel.Warning, ecs.Level);
        Assert.IsNotNull(ecs.Timestamp);

        // GCP writes syslog severity as a number: 3 is err, 4 is warning.
        var gcp = Parse("""{"severity":3,"time":"2026-06-18T09:00:06Z","message":"failed"}""");
        Assert.AreEqual(LogLevel.Error, gcp.Level, "3 is error on the syslog ladder");
    }

    // ── Vocabulary + the numeric ladders ──────────────────────────────────────

    [TestMethod]
    [CoversNode("log-viewer-parser")]
    [DataRow("FATAL", LogLevel.Fatal)]
    [DataRow("critical", LogLevel.Fatal)]
    [DataRow("Err", LogLevel.Error)]
    [DataRow("wrn", LogLevel.Warning)]
    [DataRow("Information", LogLevel.Info)]
    [DataRow("notice", LogLevel.Info)]
    [DataRow("VERBOSE", LogLevel.Debug)]
    [DataRow("trace", LogLevel.Debug)]
    [DataRow("nonsense", LogLevel.Unknown)]
    public void LevelWords_AreReadTheSameWhicheverParserSeesThem(string word, LogLevel expected)
        => Assert.AreEqual(expected, LogLevels.FromName(word));

    [TestMethod]
    [CoversNode("log-viewer-parser")]
    public void TheTwoNumericLaddersAreSeparatedByMagnitude()
    {
        // Bunyan / pino, 10..60.
        Assert.AreEqual(LogLevel.Debug, LogLevels.FromNumber(20));
        Assert.AreEqual(LogLevel.Warning, LogLevels.FromNumber(40));
        Assert.AreEqual(LogLevel.Fatal, LogLevels.FromNumber(60));

        // RFC 5424 syslog, 0..7 — where the same digits mean something else entirely.
        Assert.AreEqual(LogLevel.Fatal, LogLevels.FromNumber(2));
        Assert.AreEqual(LogLevel.Warning, LogLevels.FromNumber(4));
        Assert.AreEqual(LogLevel.Debug, LogLevels.FromNumber(7));

        // Between the two ladders nothing is claimed rather than guessed.
        Assert.AreEqual(LogLevel.Unknown, LogLevels.FromNumber(8));
        Assert.AreEqual(LogLevel.Unknown, LogLevels.FromNumber(-1));
    }

    // ── Confidence ────────────────────────────────────────────────────────────

    [TestMethod]
    [CoversNode("log-viewer-parser")]
    public void AReadableJsonLine_WinsTheFormatVote()
    {
        Assert.IsTrue(Confidence("""{"@t":"2026-06-18T09:00:01Z","@l":"Info","@mt":"hi"}""") > 0.5f);
    }

    [TestMethod]
    [CoversNode("log-viewer-parser")]
    public void AJsonLineWithNothingToReadStillClaimsTheFormat_ButOnlyQuietly()
    {
        var confidence = Confidence("""{"a":1,"b":2}""");

        Assert.IsTrue(confidence > 0, "it is genuinely JSON lines, so raw text is the wrong name for it");
        Assert.IsTrue(confidence < 0.5f, "but nothing in it is a severity or a time, and the vote should say so");
    }

    [TestMethod]
    [CoversNode("log-viewer-parser")]
    public void NonJsonNeverWins()
    {
        Assert.AreEqual(0f, Confidence("2026-06-18 09:00:01 INFO plain text"));
        Assert.AreEqual(0f, Confidence("[1, 2, 3]"), "a JSON array is not a log line");
        Assert.AreEqual(0f, Confidence("{"), "a pretty-printed JSON file is not JSON lines");
    }

    // ── Lines that aren't objects ─────────────────────────────────────────────

    [TestMethod]
    [CoversNode("log-filetype-json")]
    public void ALineThatIsNotJson_IsKeptVerbatimRatherThanFailingTheFile()
    {
        // Stack-trace continuations and the half-written tail of a growing file both look like this.
        const string raw = "   at Nexaflow.Shell.Start() in Shell.cs:line 42";

        var line = Parse(raw);

        Assert.AreEqual(raw, line.RawText, "the user still has to be able to read it");
        Assert.AreEqual(LogLevel.Unknown, line.Level);
        Assert.IsNull(line.Timestamp);
    }

    [TestMethod]
    [CoversNode("log-filetype-json")]
    public void ATruncatedTrailingLine_DoesNotThrow()
    {
        var line = Parse("""{"@t":"2026-06-18T09:00:01Z","@l":"Inf""");

        Assert.AreEqual(LogLevel.Unknown, line.Level);
        Assert.IsNull(line.Timestamp);
    }

    [TestMethod]
    [CoversNode("log-filetype-json")]
    public void AFieldWearingATimeNameButHoldingNonsense_IsIgnored()
    {
        Assert.IsNull(Parse("""{"level":"Info","time":"whenever"}""").Timestamp);
        Assert.IsNull(Parse("""{"level":"Info","time":{"nested":true}}""").Timestamp);
    }
}
