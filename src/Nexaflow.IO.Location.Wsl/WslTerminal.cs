using System.Text.RegularExpressions;
using Nexaflow.IO.Common.Locations;
using Nexaflow.IO.Common.Terminal;
using Nexaflow.IO.Location.Wsl.Protocol;

namespace Nexaflow.IO.Location.Wsl;

/// <summary>
/// A distro's shell in this computer's pseudo-console, which says where it is from Linux's own record: <c>wsl.exe</c>'s
/// folder says nothing of the shell's. The launch leaves a link to the shell's <c>/proc/&lt;pid&gt;/cwd</c>; the helper
/// reads it once and removes it, then reads where that points. Nothing the shell prints takes part.
/// </summary>
/// <param name="console">The pseudo-console <c>wsl.exe</c> runs in.</param>
/// <param name="link">The distro's helper.</param>
/// <param name="folderLink">The distro's path of the link the launch leaves; null for a shell started without one, whose
/// folder is then unknown.</param>
internal sealed partial class WslTerminal(ITerminalTransport console, WslHelperLink link, string? folderLink) : ITerminalTransport
{
    private const string Removed = " (deleted)";

    private volatile string? _shellFolder;   // /proc/<pid>/cwd, once known

    [GeneratedRegex(@"\A/proc/[0-9]+/cwd\z")]
    private static partial Regex ShellFolderPattern();

    public event Action<ReadOnlyMemory<byte>>? Output { add => console.Output += value; remove => console.Output -= value; }
    public event Action<string>? Failed { add => console.Failed += value; remove => console.Failed -= value; }
    public event Action<int>? Exited { add => console.Exited += value; remove => console.Exited -= value; }

    public bool IsRunning => console.IsRunning;
    public void Write(ReadOnlySpan<byte> input) => console.Write(input);
    public void Resize(int columns, int rows) => console.Resize(columns, rows);
    public void Interrupt() => console.Interrupt();
    public void TerminateForeground() => console.TerminateForeground();

    public async ValueTask<string?> GetWorkingDirectoryAsync(CancellationToken ct)
    {
        if (folderLink is null || !console.IsRunning) return null;
        try
        {
            if (_shellFolder is null)
            {
                if (await ReadTargetAsync(folderLink, ct).ConfigureAwait(false) is not { } named
                    || !ShellFolderPattern().IsMatch(named))
                    return null;
                _shellFolder = named;
                await RemoveLinkAsync(folderLink).ConfigureAwait(false);
            }

            var folder = await ReadTargetAsync(_shellFolder, ct).ConfigureAwait(false);
            // Asked again after the read: an ended shell's pid is free for another process.
            return console.IsRunning ? NamedExactly(folder) : null;
        }
        catch (Exception unknown) when (unknown is LocationException or IOException)
        {
            return null;   // not written yet, another user's since an exec, or the helper is gone
        }
    }

    /// <summary>A link target that names a folder exactly: not one removed under the shell, and no name that lost bytes as
    /// it was read.</summary>
    internal static string? NamedExactly(string? folder)
        => folder is ['/', ..] && !folder.EndsWith(Removed, StringComparison.Ordinal) && !folder.Contains('\uFFFD') ? folder : null;

    private async ValueTask<string?> ReadTargetAsync(string path, CancellationToken ct)
    {
        var frame = await link.CallAsync(WslOp.Stat, w => w.String(path), path, ct).ConfigureAwait(false);
        return frame.Read().Entry().LinkTarget;
    }

    private async Task RemoveLinkAsync(string path)
    {
        try { await link.CallAsync(WslOp.Delete, w => w.String(path).Bool(false), path, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception gone) when (gone is LocationException or IOException) { /* not written, or removed already */ }
    }

    public void Dispose()
    {
        console.Dispose();
        if (folderLink is not null && _shellFolder is null) _ = RemoveLinkAsync(folderLink);
    }
}
