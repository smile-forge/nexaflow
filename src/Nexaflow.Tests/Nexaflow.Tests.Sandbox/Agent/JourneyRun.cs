using System.Diagnostics;
using System.Text;
using System.Xml.Linq;
using Nexaflow.Tests.Fixtures;

namespace Nexaflow.Tests.Sandbox.Agent;

/// <summary>
/// One run of the journey suite inside the sandbox: the built runner, filtered as asked, its console written to the run's
/// folder line by line — the host follows it from there — and its TRX report beside it.
/// </summary>
internal sealed class JourneyRun(string id, string? filter, string folder)
{
    public const string ConsoleLogName = "console.log";
    private const string ReportName = "journeys.trx";

    /// <summary>
    /// The journeys' own unattended switch (<c>UiTakeoverPrompt.SkipVariable</c>). There is nobody in the sandbox to
    /// interrupt, so the prompt would only sit out its thirty seconds.
    /// </summary>
    private const string NoPromptVariable = "NEXAFLOW_UITESTS_NOPROMPT";

    /// <summary>Keeps the test platform's telemetry notice out of every run's console.</summary>
    private const string TelemetryOptOutVariable = "TESTINGPLATFORM_TELEMETRY_OPTOUT";

    private const int FailuresListed = 20;

    private Process? _process;

    private volatile bool _cancelled;

    public string Id { get; } = id;
    public string? Filter { get; } = filter;
    public string Folder { get; } = folder;

    /// <summary>Starts the runner; <paramref name="exited"/> gets its exit code once its output is all written.</summary>
    public void Begin(Action<int> exited)
    {
        Directory.CreateDirectory(Folder);
        var start = new ProcessStartInfo(BuiltOutput.JourneyRunner)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Folder,
        };
        foreach (var argument in (string[])["--results-directory", Folder, "--report-trx", "--report-trx-filename", ReportName, "--no-ansi", "--progress", "off"])
            start.ArgumentList.Add(argument);
        if (Filter is { Length: > 0 })
        {
            start.ArgumentList.Add("--filter");
            start.ArgumentList.Add(Filter);
        }
        start.EnvironmentVariables[NoPromptVariable] = "1";
        start.EnvironmentVariables[TelemetryOptOutVariable] = "1";

        var log = new StreamWriter(Path.Combine(Folder, ConsoleLogName), append: false, new UTF8Encoding(false)) { AutoFlush = true };
        var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => Append(log, e.Data);
        process.ErrorDataReceived += (_, e) => Append(log, e.Data);
        process.Exited += (_, _) =>
        {
            process.WaitForExit();   // drains the redirected output before the log closes
            var code = process.ExitCode;
            lock (log) log.Dispose();
            process.Dispose();
            exited(code);
        };

        _process = process;
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public void Cancel()
    {
        _cancelled = true;
        try { _process?.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* already gone */ }
    }

    /// <summary>The report's counts and failed tests; the exit code alone when the runner wrote no report.</summary>
    public string Summary(int exitCode)
    {
        if (_cancelled) return $"Cancelled. What it printed is in {Path.Combine(Folder, ConsoleLogName)}.";
        var report = Directory.EnumerateFiles(Folder, ReportName, SearchOption.AllDirectories).FirstOrDefault();
        if (report is null) return $"The runner exited {exitCode} without a report. Its console is {Path.Combine(Folder, ConsoleLogName)}.";

        var trx = XDocument.Load(report);
        var counters = trx.Descendants().FirstOrDefault(e => e.Name.LocalName == "Counters");
        string Count(string name) => counters?.Attribute(name)?.Value ?? "0";

        var text = new StringBuilder($"Journeys: {Count("total")} run — {Count("passed")} passed, {Count("failed")} failed, "
                                     + $"{Count("inconclusive")} inconclusive, {Count("notExecuted")} not executed (exit {exitCode}).");
        var failed = trx.Descendants()
            .Where(e => e.Name.LocalName == "UnitTestResult" && e.Attribute("outcome")?.Value == "Failed")
            .Select(e => e.Attribute("testName")?.Value)
            .OfType<string>()
            .ToList();
        foreach (var name in failed.Take(FailuresListed)) text.AppendLine().Append("  failed: ").Append(name);
        if (failed.Count > FailuresListed) text.AppendLine().Append($"  … and {failed.Count - FailuresListed} more");
        return text.ToString();
    }

    private static void Append(StreamWriter log, string? line)
    {
        if (line is null) return;
        lock (log) log.WriteLine(line);
    }
}
