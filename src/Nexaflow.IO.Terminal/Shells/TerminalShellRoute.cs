using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Common.Terminal;

namespace Nexaflow.IO.Terminal.Shells;

/// <summary>
/// The shell a terminal runs: what starts it, how to speak to it, and the folder it starts in — named as its dialect names
/// folders, or null for wherever the shell starts on its own.
/// </summary>
/// <param name="Files">The file system the shell's folders are named in, which lists them and keeps its startup file.</param>
public sealed record TerminalShellRoute(ITerminalLauncher Launcher, TerminalShellDialect Dialect, string? StartFolder,
                                        IVirtualFileSystem Files)
{
    /// <summary>
    /// This computer's shell for a terminal opened on <paramref name="folder"/>: started where this disk holds the folder, or in
    /// the user's profile when the disk holds it nowhere — a location's folder among them, which its own shell's terminal opens.
    /// </summary>
    public static TerminalShellRoute ForThisComputer(IVirtualFileSystem files, string? folder,
                                                     ITerminalLauncher shell, TerminalShellDialect dialect)
    {
        if (folder is null) return new TerminalShellRoute(shell, dialect, null, files);
        var onDisk = files.IsOnLocation(folder) ? null : files.ResolveOnDisk(folder);
        return new TerminalShellRoute(shell, dialect, onDisk ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), files);
    }

    /// <summary>
    /// The shell of the location <paramref name="folder"/> is on (<see cref="LocationCapabilities.Shell"/>), started in the
    /// folder and spoken to in the <see cref="PosixShellDialect"/>. Reaching the location — and failing to — is the launch's.
    /// </summary>
    public static TerminalShellRoute ForLocationShell(IVirtualFileSystem files, string folder)
        => new(new LocationShellLauncher(files, folder), PosixShellDialect.ForLocation(files, folder), folder, files);
}

/// <summary>Starts the shell of the location holding a folder, asked of the file system as the terminal launches — the
/// location may still be connecting when the terminal is made. Its failures — unreachable, no shell — are the launch's.</summary>
internal sealed class LocationShellLauncher(IVirtualFileSystem files, string folder) : ITerminalLauncher
{
    public async ValueTask<ITerminalTransport> LaunchAsync(TerminalLaunch launch, CancellationToken ct)
        => await (await ShellAsync(ct).ConfigureAwait(false)).LaunchAsync(launch, ct).ConfigureAwait(false);

    public async ValueTask<IReadOnlySet<string>> ListCommandsAsync(CancellationToken ct)
        => await (await ShellAsync(ct).ConfigureAwait(false)).ListCommandsAsync(ct).ConfigureAwait(false);

    private async ValueTask<ITerminalLauncher> ShellAsync(CancellationToken ct)
        => await files.GetTerminalLauncherAsync(folder, ct).ConfigureAwait(false)
           ?? throw new NotSupportedException($"'{folder}' is not on a location with a shell.");
}
