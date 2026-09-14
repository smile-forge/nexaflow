using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Nexaflow.Syntax.Compiler;

/// <summary>
/// The compiler command line a project is built with, as MSBuild's design-time build reports it: every source,
/// reference, analyzer, define and switch, resolved exactly as <c>dotnet build</c> would resolve them.
/// <para>
/// Asked for with <c>-getItem:CscCommandLineArgs</c> on the <c>Compile</c> target with compiler execution skipped,
/// which is the same request an IDE makes to populate IntelliSense — so it writes nothing a build would not, and
/// runs in about a second. The answer is kept on disk against the files that decide it (the project, its
/// restore output, the <c>Directory.Build.*</c> files above it), so a fresh process does not pay for it again.
/// </para>
/// </summary>
internal static class DesignTimeBuild
{
    /// <summary>How long one project's design-time build may take before it counts as not loadable.</summary>
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

    internal sealed record Arguments(IReadOnlyList<string> Args, string Stamp);

    /// <summary>The project's compiler arguments, from the cache when nothing that decides them has changed.</summary>
    public static (Arguments? Arguments, string? Error) For(string csproj, CancellationToken cancellation)
    {
        var stamp = StampOf(csproj);
        var cache = CacheFileFor(csproj);

        try
        {
            if (File.Exists(cache)
                && JsonSerializer.Deserialize<Arguments>(File.ReadAllText(cache)) is { } kept
                && kept.Stamp == stamp && kept.Args.Count > 0)
                return (kept, null);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }

        var (args, error) = Run(csproj, cancellation);
        if (args is null) return (null, error);

        var answer = new Arguments(args, stamp);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            File.WriteAllText(cache, JsonSerializer.Serialize(answer));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* next time pays again */ }

        return (answer, null);
    }

    private static (IReadOnlyList<string>? Args, string? Error) Run(string csproj, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            WorkingDirectory       = Path.GetDirectoryName(csproj)!,
        };
        foreach (var arg in new[]
                 {
                     "msbuild", csproj, "-t:Compile", "-getItem:CscCommandLineArgs", "-nologo", "-v:q",
                     // No node reuse: a node left behind by the resident process would outlive it, holding files.
                     "-nodeReuse:false",
                     "-p:DesignTimeBuild=true", "-p:SkipCompilerExecution=true", "-p:ProvideCommandLineArgs=true",
                     "-p:BuildProjectReferences=false",
                     // CoreCompile is skipped when its outputs look current, and a skipped target hands out no command line. An input
                     // that can never exist makes it always run - the same property the compiler's own workspace sets for this.
                     @"-p:NonExistentFile=__NonExistentSubDir__\__NonExistentFile__",
                 })
            info.ArgumentList.Add(arg);
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        info.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(info);
        if (process is null) return (null, "dotnet could not be started");

        var output = process.StandardOutput.ReadToEndAsync(cancellation);
        var errors = process.StandardError.ReadToEndAsync(cancellation);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        stop.CancelAfter(Deadline);
        try { process.WaitForExitAsync(stop.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return (null, cancellation.IsCancellationRequested ? "cancelled" : $"its design-time build took over {Deadline.TotalMinutes:F0} minutes");
        }

        var json = output.GetAwaiter().GetResult();
        var start = json.IndexOf('{');
        if (process.ExitCode != 0 || start < 0)
            return (null, FirstError(json + "\n" + errors.GetAwaiter().GetResult())
                          ?? $"its design-time build failed (exit {process.ExitCode})");

        try
        {
            using var document = JsonDocument.Parse(json[start..]);
            var items = document.RootElement.GetProperty("Items").GetProperty("CscCommandLineArgs");
            var args  = items.EnumerateArray().Select(item => item.GetProperty("Identity").GetString() ?? "")
                             .Where(arg => arg.Length > 0).ToList();
            return args.Count > 0 ? (args, null) : (null, "its design-time build produced no compiler command line");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (null, $"its design-time build's answer could not be read ({ex.Message})");
        }
    }

    /// <summary>The first line of MSBuild output that says what went wrong — usually a missing restore.</summary>
    private static string? FirstError(string output) =>
        output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains(": error ", StringComparison.Ordinal))
            is { } line ? line : null;

    /// <summary>
    /// What the compiler arguments are a function of, as write times: the project, what restore produced for it, and
    /// every <c>Directory.Build.props</c> / <c>.targets</c> above it. Any of them moving means the arguments might
    /// have, and one design-time build is cheaper than reasoning about which.
    /// </summary>
    private static string StampOf(string csproj)
    {
        var parts = new StringBuilder();
        void Add(string path)
        {
            if (File.Exists(path)) parts.Append(path).Append('=').Append(File.GetLastWriteTimeUtc(path).Ticks).Append(';');
        }

        Add(csproj);
        var dir = Path.GetDirectoryName(csproj);
        foreach (var assets in Directory.Exists(Path.Combine(dir!, "obj"))
                     ? Directory.EnumerateFiles(Path.Combine(dir!, "obj"), "project.assets.json", SearchOption.AllDirectories)
                     : [])
            Add(assets);

        // Which sources exist, by name: a default glob picks up a file the moment it is created, and the arguments
        // listing it are then out of date although nothing above has moved.
        foreach (var source in Directory.EnumerateFiles(dir!, "*.cs", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                                        .Where(p => !IsBuildOutput(p, dir!)).Order(StringComparer.OrdinalIgnoreCase))
            parts.Append(source).Append(';');

        for (var up = dir; up is { Length: > 0 }; up = Path.GetDirectoryName(up))
        {
            Add(Path.Combine(up, "Directory.Build.props"));
            Add(Path.Combine(up, "Directory.Build.targets"));
            Add(Path.Combine(up, "Directory.Packages.props"));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(parts.ToString())));
    }

    private static bool IsBuildOutput(string path, string projectDirectory)
    {
        var relative = Path.GetRelativePath(projectDirectory, path);
        return relative.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string CacheFileFor(string csproj)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(csproj).ToUpperInvariant())))[..24];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            "Smile", "nfi", "compile", key + ".json");
    }
}
