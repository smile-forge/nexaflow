using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Nexaflow.Services.Initiatives.Cli.Daemon;
using Nexaflow.Services.Initiatives.Graph;

namespace Nexaflow.Services.Initiatives.Cli;

/// <summary>
/// <c>nfi test</c>: build the suites that exercise a node and run only the tests that do.
/// <para>
/// Choosing the tests is a question about the graph, so it is asked of the resident process (<c>test … --plan</c>).
/// Building and running them is not, and runs here, in the caller's own process: a test run takes minutes, and holding
/// the tree's lock for that long would stop every query behind it — while a Ctrl+C here ends the run the way it should.
/// </para>
/// </summary>
internal static class TestRunner
{
    private const int FailureLines = 8;
    private const int ShownFailures = 20;

    /// <summary>A suite's tests that failed, kept so <c>--failed</c> can run exactly those again.</summary>
    internal sealed record Failed(string Project, List<string> Filters);

    public static int Run(string[] args, string productRoot, string? codeRoot)
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
            var reply = DaemonClient.Capture([.. args.Where(a => a != "--no-build"), "--plan"], productRoot, codeRoot);
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

        var failures = new List<Failed>();
        var broken   = false;
        foreach (var suite in suites)
        {
            var project = Path.Combine(root, suite.Project.Replace('/', Path.DirectorySeparatorChar));
            var name    = Path.GetFileNameWithoutExtension(project);

            if (!noBuild && !Build(project, name))
            {
                broken = true;
                continue;
            }

            var outcome = Execute(project, name, suite.Filters);
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

    private static bool Build(string project, string name)
    {
        var clock = Stopwatch.StartNew();
        var (code, output) = Capture("dotnet", ["build", project, "-v", "q", "-nologo", "-clp:NoSummary"]);
        if (code == 0)
        {
            Console.WriteLine($"build: {name} ({clock.Elapsed.TotalSeconds:F1}s)");
            return true;
        }

        Console.WriteLine($"build: {name} FAILED ({clock.Elapsed.TotalSeconds:F1}s)");
        foreach (var line in output.Split('\n').Where(l => l.Contains(": error ", StringComparison.Ordinal))
                                   .Select(l => Regex.Replace(l.Trim(), @"\s*\[[^\]]+\.csproj\]$", "")).Distinct().Take(15))
            Console.WriteLine($"  {line}");
        return false;
    }

    private sealed record Outcome(List<string> Failed);

    private static Outcome? Execute(string project, string name, IReadOnlyList<string> filters)
    {
        if (TargetPath(project) is not { } assembly)
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
            foreach (var line in lines.Where(l => l.Trim().Length > 0).TakeLast(12)) Console.WriteLine($"  {line}");
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
            if (shown++ >= ShownFailures) continue;

            Console.WriteLine($"  {lines[i].Trim()}");
            foreach (var detail in lines.Skip(i + 1).TakeWhile(l => l.StartsWith("  ", StringComparison.Ordinal)).Take(FailureLines))
                Console.WriteLine($"    {detail.Trim()}");
        }
        return new Outcome([.. names.Distinct().Select(n => $".{n}")]);
    }

    private static int? Count(IEnumerable<string> lines, string label) =>
        lines.Select(l => Regex.Match(l, $@"^\s+{label}:\s+(\d+)\s*$")).FirstOrDefault(m => m.Success) is { } m
            ? int.Parse(m.Groups[1].Value) : null;

    /// <summary>The assembly a project builds to, as MSBuild evaluates it — which is where its test runner is.</summary>
    private static string? TargetPath(string project)
    {
        var (code, output) = Capture("dotnet", ["msbuild", project, "-getProperty:TargetPath", "-nologo"]);
        var path = output.Trim().Split('\n').LastOrDefault()?.Trim();
        return code == 0 && path is { Length: > 0 } && File.Exists(path) ? path : null;
    }

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
