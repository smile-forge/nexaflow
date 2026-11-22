using Nexaflow.IO.Common;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Common.Terminal;

namespace Nexaflow.Visuals.Terminal.Shells;

/// <summary>
/// The shell a terminal runs: what starts it, how to speak to it, and the folder it starts in — named as its dialect names
/// folders, or null for wherever the shell starts on its own.
/// </summary>
public sealed record TerminalShellRoute(ITerminalLauncher Launcher, TerminalShellDialect Dialect, string? StartFolder)
{
    /// <summary>
    /// The shell for a terminal opened on <paramref name="folder"/>. On a location with a shell
    /// (<see cref="LocationCapabilities.Shell"/>) it is the location's own, reached as the terminal launches, in the
    /// <see cref="PosixShellDialect"/>; anywhere else it is <paramref name="localShell"/>, started where this disk holds the
    /// folder, or in the user's profile when the disk holds it nowhere. Decided from what the file system knows without
    /// the network: reaching the location — and failing to — is the launch's.
    /// </summary>
    public static TerminalShellRoute ForFolder(IVirtualFileSystem files, string? folder,
                                               ITerminalLauncher localShell, TerminalShellDialect localDialect)
    {
        if (folder is null) return new TerminalShellRoute(localShell, localDialect, null);

        if ((files.GetCapabilities(folder) & LocationCapabilities.Shell) != 0)
            return new TerminalShellRoute(new LocationShellLauncher(files, folder),
                                          PosixShellDialect.ForLocation(files, folder), folder);

        return new TerminalShellRoute(localShell, localDialect,
            files.ResolveOnDisk(folder) ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    }
}

/// <summary>Starts the shell of the location holding a folder, asked of the file system as the terminal launches — the
/// location may still be connecting when the terminal is made. Its failures — unreachable, no shell — are the launch's.</summary>
internal sealed class LocationShellLauncher(IVirtualFileSystem files, string folder) : ITerminalLauncher
{
    public async ValueTask<ITerminalTransport> LaunchAsync(TerminalLaunch launch, CancellationToken ct)
    {
        var shell = await files.GetTerminalLauncherAsync(folder, ct).ConfigureAwait(false)
                    ?? throw new NotSupportedException($"'{folder}' is not on a location with a shell.");
        return await shell.LaunchAsync(launch, ct).ConfigureAwait(false);
    }
}
