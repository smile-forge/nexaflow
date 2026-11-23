using System.Text;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Common.Terminal;

namespace Nexaflow.IO.Location.Wsl;

/// <summary>
/// A distro shell's environment, read beside the shell. Live: the shell integration writes what <c>env -0</c> prints to a
/// file at each prompt, read through the distro. Startup: the user's login shell is started on its own to print the same once
/// its startup files have run. A variable kept for new shells goes in the startup file that login shell reads — bash's
/// first of <c>.bash_profile</c>, <c>.bash_login</c> and <c>.profile</c>, zsh's <c>.zshenv</c>, a plain sh's <c>.profile</c>.
/// </summary>
/// <param name="liveFile">The distro's path of the file the shell writes at each prompt; null for a shell started without
/// the integration, whose live variables are unknown.</param>
internal sealed class WslShellEnvironment(string distro, string loginShell, string home, ILocationSession files, string? liveFile)
    : IShellEnvironment
{
    // Ends whatever a profile prints, so a greeting is not taken for a variable.
    private const string StartupMark = "--nexaflow-environment--";
    private const string PrintStartup = "printf '\\n%s\\0' '" + StartupMark + "'; env -0";

    private static readonly TimeSpan StartupPatience = TimeSpan.FromSeconds(15);

    private string Shell => loginShell[(loginShell.LastIndexOf('/') + 1)..];

    public IReadOnlyList<ShellEnvironmentView> Views { get; } =
        liveFile is null ? [ShellEnvironmentView.Startup] : [ShellEnvironmentView.Live, ShellEnvironmentView.Startup];

    public async ValueTask<IReadOnlyDictionary<string, string>?> ReadAsync(ShellEnvironmentView view, CancellationToken ct)
    {
        if (view == ShellEnvironmentView.Live)
        {
            if (liveFile is null) return null;
            try
            {
                await using var stream = await files.OpenReadAsync(liveFile, ReadIntent.Whole, ct).ConfigureAwait(false);
                using var bytes = new MemoryStream();
                await stream.CopyToAsync(bytes, ct).ConfigureAwait(false);
                return Parse(Encoding.UTF8.GetString(bytes.ToArray()));
            }
            catch (LocationException unwritten) when (unwritten.Error == LocationError.NotFound)
            {
                return null;   // the shell has not reached its first prompt
            }
        }

        using var patience = CancellationTokenSource.CreateLinkedTokenSource(ct);
        patience.CancelAfter(StartupPatience);
        var printed = await WslHelperDeployment.RunAsync(distro, patience.Token, "--exec", loginShell, "-l", "-c", PrintStartup)
                                               .ConfigureAwait(false);
        return AfterMark(printed);
    }

    public async ValueTask<string?> FindStartupFileAsync(CancellationToken ct)
    {
        string[] candidates = Shell switch
        {
            "bash"                         => [".bash_profile", ".bash_login", ".profile"],
            "zsh"                          => [".zshenv"],
            "sh" or "dash" or "ksh" or "mksh" => [".profile"],
            _                              => [],
        };
        if (candidates.Length == 0) return null;

        var folder = home.TrimEnd('/');
        foreach (var name in candidates)
            if (await files.GetEntryAsync($"{folder}/{name}", ct).ConfigureAwait(false) is { IsDirectory: false })
                return $"{folder}/{name}";
        // None there yet: the last named is made — for bash, the .profile sh reads too.
        return $"{folder}/{candidates[^1]}";
    }

    /// <summary>The variables a login shell printed after the mark; none when there is no mark.</summary>
    internal static IReadOnlyDictionary<string, string> AfterMark(string printed)
    {
        var mark = printed.IndexOf("\n" + StartupMark + "\0", StringComparison.Ordinal);
        return mark < 0 ? new Dictionary<string, string>(StringComparer.Ordinal)
                        : Parse(printed[(mark + StartupMark.Length + 2)..]);
    }

    /// <summary>What <c>env -0</c> prints: <c>NAME=value</c>, each ended by a NUL — a value may hold any other character.</summary>
    internal static IReadOnlyDictionary<string, string> Parse(string printed)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in printed.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals > 0) variables[pair[..equals]] = pair[(equals + 1)..];
        }
        return variables;
    }
}
