using System.Text;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Common.Terminal;
using Nexaflow.IO.Terminal;

namespace Nexaflow.IO.Location.Wsl;

/// <summary>
/// A distro's shell: <c>wsl.exe</c> in this computer's ConPTY, starting in the folder asked — its own home when none is.
/// bash and zsh start through a shim beside the helper that runs the user's own startup files and then reports the
/// working folder (OSC 7) and marks prompts and output (OSC 133), so the terminal follows <c>cd</c>; any other shell
/// starts as the distro starts it. Variables the launch lays over are passed into Linux through <c>WSLENV</c>.
/// </summary>
/// <param name="shims">The distro's own path of the folder holding the shims.</param>
/// <param name="loginShell">The user's login shell, as the helper read it.</param>
internal sealed class WslShell(string distro, string shims, string loginShell) : ILocationShell
{
    public ValueTask<ITerminalTransport> LaunchAsync(TerminalLaunch launch, CancellationToken ct)
    {
        var arguments = new StringBuilder()
            .Append("-d ").Append(Quote(distro))
            .Append(" --cd ").Append(Quote(launch.WorkingDirectory ?? "~"));

        switch (loginShell[(loginShell.LastIndexOf('/') + 1)..])
        {
            case "bash":
                arguments.Append(" --exec ").Append(Quote(loginShell))
                         .Append(" --rcfile ").Append(Quote($"{shims}/nexaflow.bashrc")).Append(" -i");
                break;
            case "zsh":
                arguments.Append(" --exec env ").Append(Quote($"ZDOTDIR={shims}/zsh"))
                         .Append(' ').Append(Quote(loginShell)).Append(" -l -i");
                break;
        }

        var launcher = new PseudoConsoleLauncher(WslHelperDeployment.WslExecutable, arguments.ToString());
        return launcher.LaunchAsync(launch with { WorkingDirectory = null, Environment = Shared(launch.Environment) }, ct);
    }

    // WSLENV names the variables wsl.exe carries into Linux; the launch's own are added to any the user already shares.
    private static IReadOnlyDictionary<string, string>? Shared(IReadOnlyDictionary<string, string>? environment)
    {
        if (environment is null || environment.Count == 0) return environment;
        var shared = environment.TryGetValue("WSLENV", out var given) ? given : Environment.GetEnvironmentVariable("WSLENV");
        var names  = string.Join(':', environment.Keys.Where(k => !string.Equals(k, "WSLENV", StringComparison.OrdinalIgnoreCase)));
        return new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase)
        {
            ["WSLENV"] = string.IsNullOrEmpty(shared) ? names : $"{shared}:{names}",
        };
    }

    /// <summary>One argument as a Windows command line carries it to <c>wsl.exe</c>.</summary>
    internal static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny([' ', '\t', '"']) < 0) return argument;
        var quoted = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in argument)
        {
            if (c == '\\') { slashes++; continue; }
            if (c == '"') quoted.Append('\\', slashes * 2 + 1).Append('"');
            else quoted.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        return quoted.Append('\\', slashes * 2).Append('"').ToString();
    }
}
