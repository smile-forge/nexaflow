namespace Nexaflow.IO.Common.Terminal;

/// <summary>
/// Starts a shell and hands over its terminal transport, running: ConPTY on this computer (<c>PseudoConsoleLauncher</c>),
/// a location's shell elsewhere (<see cref="Locations.ILocationShell"/>). A terminal view is given a launcher, never a
/// shell of its own, so it draws a local and a remote shell the same way.
/// </summary>
public interface ITerminalLauncher
{
    ValueTask<ITerminalTransport> LaunchAsync(TerminalLaunch launch, CancellationToken ct);

    /// <summary>The programs the shells this launcher starts run by name, as their host has them — what tells a command
    /// from a request where this computer's PATH says nothing. Empty when the launcher cannot say. A launcher that wraps
    /// another passes it on.</summary>
    ValueTask<IReadOnlySet<string>> ListCommandsAsync(CancellationToken ct)
        => new(System.Collections.Frozen.FrozenSet<string>.Empty);
}

/// <summary>What a shell starts with.</summary>
/// <param name="WorkingDirectory">The folder it starts in — a provider path on a location — or null for the launcher's
/// own.</param>
/// <param name="Environment">Variables laid over the shell's environment, or null for none.</param>
/// <param name="Columns">Its width in character cells.</param>
/// <param name="Rows">Its height in character cells.</param>
public sealed record TerminalLaunch(string? WorkingDirectory, IReadOnlyDictionary<string, string>? Environment, int Columns, int Rows);
