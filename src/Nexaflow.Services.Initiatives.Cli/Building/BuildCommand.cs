using Nexaflow.Services.Initiatives.Cli.Daemon;

namespace Nexaflow.Services.Initiatives.Cli.Building;

/// <summary><c>nfi build</c>: what it builds, where it builds it, and what it says about it.</summary>
internal static class BuildCommand
{
    private const int ShownErrors   = 30;
    private const int ShownWarnings = 20;

    private static readonly string[] Buildable = [".csproj", ".vbproj", ".fsproj", ".proj", ".slnx", ".sln"];

    /// <summary>
    /// Builds <paramref name="order"/> with the resident process's pool. Run anywhere else — a test calling the verb in
    /// its own process — it gets a pool of its own, whose workers go when it does: nobody would reuse them.
    /// </summary>
    public static BuildReport Run(BuildOrder order)
    {
        if (RequestScope.Builds is { } shared) return shared.Build(order, RequestScope.Cancellation);

        using var own = new BuildPool(new HostedMSBuild("nfi", BuildPool.DefaultNodes, reuseNodes: false), BuildPool.Linger);
        return own.Build(order, RequestScope.Cancellation);
    }

    /// <summary>
    /// The full path of the project or solution <paramref name="named"/> means: a <c>file:</c> id in the tree, or a path
    /// from where the caller stands and failing that from the tree's root. Null when it names nothing buildable.
    /// </summary>
    public static string? Resolve(string named, string codeRoot)
    {
        IEnumerable<string> candidates = named.StartsWith("file:", StringComparison.Ordinal)
            ? [Path.GetFullPath(Path.Combine(codeRoot, named["file:".Length..]))]
            : [CallerPath.Of(named), Path.GetFullPath(Path.Combine(codeRoot, named))];

        return candidates.FirstOrDefault(c => File.Exists(c)
                                           && Buildable.Contains(Path.GetExtension(c), StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>One line for the build, then what it raised — every error up to a limit, and the warnings after them.</summary>
    public static void Print(BuildReport report, IReadOnlyList<string> projects, bool all)
    {
        var names = string.Join(", ", projects.Select(Path.GetFileNameWithoutExtension));
        if (report.Problem is { } problem)
        {
            Console.WriteLine($"build: {names} — not built: {problem}");
            return;
        }

        var waited    = report.WaitedSeconds >= 0.5 ? $", waited {report.WaitedSeconds:F1}s for the tree's last build" : "";
        var count     = report.Projects.Count(p => p.Restore == BuiltProject.Unchanged);
        var unchanged = count == 0 ? ""
                      : count == report.Projects.Count ? "; not restored, nothing a restore reads has changed"
                      : $"; {count} not restored, nothing their restore reads has changed";
        Console.WriteLine($"build: {names} — {(report.Succeeded ? "ok" : "FAILED")} ({report.TookSeconds:F1}s{waited}{unchanged})");

        var errors   = report.Diagnostics.Where(d => d.Severity == "error").ToList();
        var warnings = report.Diagnostics.Where(d => d.Severity != "error").ToList();
        Show(errors, all ? int.MaxValue : ShownErrors, "error");
        Show(warnings, all ? int.MaxValue : ShownWarnings, "warning");
        if (!all && (errors.Count > ShownErrors || warnings.Count > ShownWarnings))
            Console.WriteLine("  (--all lists every one)");
    }

    private static void Show(List<BuildDiagnostic> raised, int limit, string severity)
    {
        foreach (var d in raised.Take(limit))
        {
            var at   = d.File.Length == 0 ? d.Project : d.Line > 0 ? $"{d.File}({d.Line},{d.Column})" : d.File;
            var code = d.Code.Length > 0 ? $" {d.Code}" : "";
            Console.WriteLine($"  {at}: {severity}{code}: {d.Message}");
        }
        if (raised.Count > limit) Console.WriteLine($"  … and {raised.Count - limit} more {severity}(s)");
    }
}
