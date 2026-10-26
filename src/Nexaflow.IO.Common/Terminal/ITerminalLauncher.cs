namespace Nexaflow.IO.Common.Terminal;

/// <summary>
/// Starts a shell and hands over its terminal transport, running: ConPTY on this computer (<c>PseudoConsoleLauncher</c>),
/// a location's shell elsewhere (<see cref="Locations.ILocationShell"/>). A terminal view is given a launcher, never a
/// shell of its own, so it draws a local and a remote shell the same way.
/// </summary>
public interface ITerminalLauncher
{
    ValueTask<ITerminalTransport> LaunchAsync(TerminalLaunch launch, CancellationToken ct);
}

/// <summary>What a shell starts with.</summary>
/// <param name="WorkingDirectory">The folder it starts in — a provider path on a location — or null for the launcher's
/// own.</param>
/// <param name="Environment">Variables laid over the shell's environment, or null for none.</param>
/// <param name="Columns">Its width in character cells.</param>
/// <param name="Rows">Its height in character cells.</param>
public sealed record TerminalLaunch(string? WorkingDirectory, IReadOnlyDictionary<string, string>? Environment, int Columns, int Rows);
