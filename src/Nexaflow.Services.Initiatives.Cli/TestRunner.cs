using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nexaflow.Services.Initiatives.Cli.Building;
using Nexaflow.Services.Initiatives.Cli.Daemon;
using Nexaflow.Services.Initiatives.Graph;

namespace Nexaflow.Services.Initiatives.Cli;

/// <summary>
/// <c>nfi test</c>: build the suites that exercise a node and run only the tests that do.
/// <para>
/// Choosing the tests is a question about the graph, so it is asked of the resident process (<c>test … --plan</c>), and
/// building them is asked of its build pool (<c>build … --json</c>), which every caller's builds share. Running them is
/// neither, and runs here, in the caller's own process: a test run takes minutes, and a Ctrl+C here has to end it.
/// </para>
/// </summary>
internal static class TestRunner
{
    private const int FailureLines = 8;
    private const int ShownFailures = 20;

    /// <summary>A suite's tests that failed, kept so <c>--failed</c> can run exactly those again.</summary>
    internal sealed record Failed(string Project, List<string> Filters);

    /// <summary>
    /// Runs the tests a node's change touches. What the run concludes prints as it happens; the detail - the whole of each
    /// failing test's trace, the failures past the first few - is kept by the resident process with the run's answer, so
    /// the rest of a failing run is <c>nfi more</c>, not the run again.
    /// </summary>
    public static int Run(string[] args, string productRoot, string? codeRoot)
    {
        // This process answers one command, so its console is the answer's to take for the length of the run.
        var console = Console.Out;
        var answer  = new AnswerWriter(live: console);
        Console.SetOut(answer);
        int code;
        try { code = RunSuites(args, productRoot, codeRoot, answer.Detail); }
        finally { Console.SetOut(console); }

        console.Write(answer.FinishLive(lines => KeepAnswer(args, productRoot, codeRoot, lines)));
        return code;
    }

    /// <summary>Hands a run's whole answer to the resident process to keep, and returns the number it is kept under - null
    /// when it could not be kept, and the detail is printed instead.</summary>
    private static int? KeepAnswer(string[] args, string productRoot, string? codeRoot, IReadOnlyList<string> lines)
    {
        try
        {
            var reply = DaemonClient.Capture(["keep", "nfi " + string.Join(' ', args), "--stdin"], productRoot, codeRoot,
                                             string.Join('\n', lines));
            return Regex.Match(reply.Out, @"kept as @(\d+)") is { Success: true } kept ? int.Parse(kept.Groups[1].Value) : null;
        }
        catch (DaemonUnavailableException)
        {
            return null;
        }
    }

    private static int RunSuites(string[] args, string productRoot, string? codeRoot, TextWriter detail)
    {
        var root    = codeRoot ?? productRoot;
        var noBuild = args.Contains("--no-build");

        IReadOnlyList<TestSelection.Suite> suites;
        if (args.Contains("--failed"))
        {
            suites = [.. LoadFailures(root).Select(f => new TestSelection.Suite(f.Project, f.Filters, ["failed last time"]))];
            if (suites.Count == 0)
            {
                Console.WriteLine("tests: nothing failed in the last run here, so there is nothing to run again.");
                return 0;
            }
        }
        else
        {
            // Unless nothing is to be built, the resident process starts on the node's own project while it chooses.
            string[] ahead = noBuild ? [] : ["--build-ahead"];
            var reply = DaemonClient.Capture([.. args.Where(a => a != "--no-build"), "--plan", .. ahead], productRoot, codeRoot);
            Console.Error.Write(reply.Error);
            if (reply.ExitCode != 0)
            {
                Console.Out.Write(reply.Out);
                return reply.ExitCode;
            }

            var selection = JsonSerializer.Deserialize<TestSelection.Selection>(reply.Out);
            foreach (var note in selection?.Notes ?? []) Console.Error.WriteLine($"note: {note}");
            if (selection is not null) Program.PrintJourneys(selection);

            suites = selection?.Suites ?? [];
            if (suites.Count == 0)
            {
                Console.WriteLine("tests: nothing uses it from a test, and no test declares it covers its feature — nothing to "
                                + "run. (nfi test <id> --list shows how tests are chosen.)");
                return 0;
            }
        }

        Console.WriteLine($"tests: {suites.Sum(s => s.Filters.Count)} filter(s) in {suites.Count} suite(s) — "
                        + string.Join(", ", suites.Select(s => Path.GetFileNameWithoutExtension(s.Project))));

        var projects = suites.Select(s => Path.GetFullPath(Path.Combine(root, s.Project.Replace('/', Path.DirectorySeparatorChar)))).ToList();
        if (Build(projects, noBuild, productRoot, codeRoot) is not { } built) return 1;

        var failures = new List<Failed>();
        var broken   = false;
        foreach (var (suite, project) in suites.Zip(projects))
        {
            var name = Path.GetFileNameWithoutExtension(project);
            if (built.Projects.FirstOrDefault(p => string.Equals(p.Project, project, StringComparison.OrdinalIgnoreCase))
                is not { Succeeded: true, Outputs: [var assembly, ..] })
            {
                Console.WriteLine($"run:   {name} — not built, so not run");
                broken = true;
                continue;
            }

            var outcome = Execute(project, name, assembly, suite.Filters, detail);
            if (outcome is null)
            {
                broken = true;
                continue;
            }
            if (outcome.Failed.Count > 0) failures.Add(new Failed(suite.Project, outcome.Failed));
        }

        SaveFailures(root, failures);
        var count = failures.Sum(f => f.Filters.Count);
        if (count > 0) Console.WriteLine($"tests: {count} failed — nfi test --failed runs exactly those again.");
        return broken || count > 0 ? 1 : 0;
    }

    /// <summary>
    /// Every suite built in one request to the resident process's build pool — side by side, so what they share is built
    /// once — or, with <c>--no-build</c>, only asked where each one's assembly is. Null, having said why, when there is no
    /// report to run from.
    /// </summary>
    private static BuildReport? Build(List<string> projects, bool noBuild, string productRoot, string? codeRoot)
    {
        string[] only = noBuild ? ["--target", "GetTargetPath", "--no-restore"] : [];
        var reply = DaemonClient.Capture(["build", projects[0], .. projects.Skip(1).SelectMany(p => (string[])["--project", p]), "--json", .. only],
                                         productRoot, codeRoot);
        Console.Error.Write(reply.Error);

        var report = reply.Out.TrimStart().StartsWith('{') ? JsonSerializer.Deserialize<BuildReport>(reply.Out) : null;
        if (report is null)
        {
            Console.Out.Write(reply.Out);
            return null;
        }

        if (!noBuild || !report.Succeeded) BuildCommand.Print(report, projects, all: false);
        return report.Problem is null ? report : null;
    }

    private sealed record Outcome(List<string> Failed);

    private static Outcome? Execute(string project, string name, string assembly, IReadOnlyList<string> filters, TextWriter detail)
    {
        if (!File.Exists(assembly))
        {
            Console.WriteLine($"run:   {name} — its output could not be found; build it first.");
            return null;
        }

        var filter = string.Join("|", filters.Select(f => $"FullyQualifiedName~{f}"));
        var exe    = Path.ChangeExtension(assembly, ".exe");
        var clock  = Stopwatch.StartNew();
        var (code, output) = File.Exists(exe)
            ? Capture(exe, ["--filter", filter])
            : Capture("dotnet", ["test", project, "--no-build", "--filter", filter]);

        var lines   = output.Replace("\r", "").Split('\n');
        var total   = Count(lines, "total");
        var failed  = Count(lines, "failed");
        var passed  = Count(lines, "succeeded") ?? Count(lines, "passed");
        var skipped = Count(lines, "skipped");
        var took    = $"{clock.Elapsed.TotalSeconds:F1}s";

        if (total is null)
        {
            Console.WriteLine($"run:   {name} — no summary (exit {code}, {took}); the end of its output:");
            var said = lines.Where(l => l.Trim().Length > 0).ToList();
            foreach (var line in said.SkipLast(12)) detail.WriteLine($"  {line}");
            foreach (var line in said.TakeLast(12)) Console.WriteLine($"  {line}");
            return code == 0 ? new Outcome([]) : null;
        }

        if (total == 0)
        {
            Console.WriteLine($"run:   {name} — no test matched ({took})");
            return new Outcome([]);
        }

        Console.WriteLine($"run:   {name} — {passed ?? 0} passed, {failed ?? 0} failed"
                        + (skipped is > 0 ? $", {skipped} skipped" : "") + $" ({took})");

        var names = new List<string>();
        var shown = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (Regex.Match(lines[i], @"^failed (\S+)") is not { Success: true } match) continue;

            names.Add(match.Groups[1].Value);

            // The first failures, and the first lines of each, are what the run concluded; the rest is detail, kept for nfi more.
            var printed = shown++ < ShownFailures;
            (printed ? Console.Out : detail).WriteLine($"  {lines[i].Trim()}");
            var trace = lines.Skip(i + 1).TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal)).ToList();
            for (var t = 0; t < trace.Count; t++)
                (printed && t < FailureLines ? Console.Out : detail).WriteLine($"    {trace[t].Trim()}");
        }
        return new Outcome([.. names.Distinct().Select(n => $".{n}")]);
    }

    private static int? Count(IEnumerable<string> lines, string label) =>
        lines.Select(l => Regex.Match(l, $@"^\s+{label}:\s+(\d+)\s*$")).FirstOrDefault(m => m.Success) is { } m
            ? int.Parse(m.Groups[1].Value) : null;

    private static (int Code, string Output) Capture(string file, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        info.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(info)!;
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is { } d) lock (output) output.AppendLine(d); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data is { } d) lock (output) output.AppendLine(d); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();
        return (process.ExitCode, output.ToString());
    }

    private static string FailuresFile(string root)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant())), 0, 8);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Smile", "nfi", "tests",
                            key + ".json");
    }

    private static List<Failed> LoadFailures(string root)
    {
        try { return File.Exists(FailuresFile(root)) ? JsonSerializer.Deserialize<List<Failed>>(File.ReadAllText(FailuresFile(root))) ?? [] : []; }
        catch (Exception ex) when (ex is IOException or JsonException) { return []; }
    }

    private static void SaveFailures(string root, List<Failed> failures)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FailuresFile(root))!);
            File.WriteAllText(FailuresFile(root), JsonSerializer.Serialize(failures));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
