using System.Diagnostics;
using System.Text.Json;

namespace Nexaflow.Syntax.Compiler;

/// <summary>
/// What a design-time build is asked for, whoever runs it: the target, the properties that make it hand out the
/// compiler command line without compiling, and the item the command line comes back as.
/// </summary>
public static class DesignTimeRequest
{
    public const string Target = "Compile";

    public const string CommandLineItem = "CscCommandLineArgs";

    /// <summary>How long one project's design-time build may take before it counts as not loadable.</summary>
    public static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

    public static readonly IReadOnlyDictionary<string, string> Properties = new Dictionary<string, string>
    {
        ["DesignTimeBuild"]        = "true",
        ["SkipCompilerExecution"]  = "true",
        ["ProvideCommandLineArgs"] = "true",
        ["BuildProjectReferences"] = "false",
        // CoreCompile is skipped when its outputs look current, and a skipped target hands out no command line. An input
        // that can never exist makes it always run - the same property the compiler's own workspace sets for this.
        ["NonExistentFile"]        = @"__NonExistentSubDir__\__NonExistentFile__",
    };
}

/// <summary>
/// Runs a project's design-time build (<see cref="DesignTimeRequest"/>) and hands back its compiler command line. A host
/// that keeps MSBuild workers of its own runs it on them; <see cref="ProcessDesignTimeBuilder"/> starts an MSBuild for each.
/// </summary>
public interface IDesignTimeBuilder
{
    /// <summary>The project's compiler arguments, or why there are none.</summary>
    (IReadOnlyList<string>? Args, string? Error) CommandLineOf(string csproj, CancellationToken cancellation);
}

/// <summary>
/// A design-time build in a <c>dotnet msbuild</c> of its own, with node reuse off: a worker it left behind would outlive
/// whoever asked, holding files. Each one starts MSBuild cold, which is what a host with workers of its own avoids.
/// </summary>
public sealed class ProcessDesignTimeBuilder : IDesignTimeBuilder
{
    public static readonly ProcessDesignTimeBuilder Instance = new();

    private ProcessDesignTimeBuilder() { }

    public (IReadOnlyList<string>? Args, string? Error) CommandLineOf(string csproj, CancellationToken cancellation)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            WorkingDirectory       = Path.GetDirectoryName(csproj)!,
        };
        foreach (var arg in new[] { "msbuild", csproj, $"-t:{DesignTimeRequest.Target}", $"-getItem:{DesignTimeRequest.CommandLineItem}",
                                    "-nologo", "-v:q", "-nodeReuse:false" })
            info.ArgumentList.Add(arg);
        foreach (var (name, value) in DesignTimeRequest.Properties) info.ArgumentList.Add($"-p:{name}={value}");
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        info.Environment["DOTNET_NOLOGO"] = "1";

        using var process = Process.Start(info);
        if (process is null) return (null, "dotnet could not be started");

        var output = process.StandardOutput.ReadToEndAsync(cancellation);
        var errors = process.StandardError.ReadToEndAsync(cancellation);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        stop.CancelAfter(DesignTimeRequest.Deadline);
        try { process.WaitForExitAsync(stop.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return (null, cancellation.IsCancellationRequested
                ? "cancelled"
                : $"its design-time build took over {DesignTimeRequest.Deadline.TotalMinutes:F0} minutes");
        }

        var json = output.GetAwaiter().GetResult();
        var start = json.IndexOf('{');
        if (process.ExitCode != 0 || start < 0)
            return (null, FirstError(json + "\n" + errors.GetAwaiter().GetResult())
                          ?? $"its design-time build failed (exit {process.ExitCode})");

        try
        {
            using var document = JsonDocument.Parse(json[start..]);
            var items = document.RootElement.GetProperty("Items").GetProperty(DesignTimeRequest.CommandLineItem);
            var args  = items.EnumerateArray().Select(item => item.GetProperty("Identity").GetString() ?? "")
                             .Where(arg => arg.Length > 0).ToList();
            return args.Count > 0 ? (args, null) : (null, "its design-time build produced no compiler command line");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return (null, $"its design-time build's answer could not be read ({ex.Message})");
        }
    }

    private static string? FirstError(string output) =>
        output.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains(": error ", StringComparison.Ordinal))
            is { } line ? line : null;
}
