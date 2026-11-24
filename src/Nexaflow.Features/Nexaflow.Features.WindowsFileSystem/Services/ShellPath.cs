using Nexaflow.IO.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Turns a browser path into one Windows can use — for the clipboard, a drag-out, a shell verb, an external program or a
/// properties dialog — asked of the workspace's file system, which knows its locations. Everything leaving Nexaflow for
/// the OS goes through here.
/// <para>
/// A mounted path maps straight onto its real file (no copy), and a file on a location this machine reaches as a Windows
/// folder (a WSL distro) onto that folder's path. An in-archive path has no real file at all, so it is materialised to a
/// temp copy — good enough to hand to Explorer or a viewer, which is why clipboard and drag work from inside a zip.
/// Actions that need the file's <i>neighbours</i> must not rely on that: they declare
/// <c>IFileAction.RequiresFullyBackedPath</c> and are withheld instead.
/// </para>
/// </summary>
/// <param name="files">The workspace's file system. The process-wide one knows no locations, so a distro's file would leave
/// as a <c>::</c> path no program can open.</param>
internal sealed class ShellPath(IVirtualFileSystem files)
{
    /// <summary>A real, launchable path. Returns the input unchanged if nothing can be produced, so callers fail the way
    /// they always did rather than on a null. Completes at once unless an archive entry has to be extracted.</summary>
    public async ValueTask<string> RealizeAsync(string path, CancellationToken ct = default)
    {
        if (files.TryResolveReal(path) is { } real) return real;
        try { return await files.MaterializeFileAsync(path, ct).ConfigureAwait(false); }
        catch (Exception unrealized) when (unrealized is not OperationCanceledException) { return path; }
    }

    public async ValueTask<string[]> RealizeAsync(IEnumerable<string> paths, CancellationToken ct = default)
    {
        var realized = new List<string>();
        foreach (var path in paths) realized.Add(await RealizeAsync(path, ct).ConfigureAwait(false));
        return [.. realized];
    }

    /// <summary>
    /// The path to <i>change</i> — rename, delete, or paste into. Unlike <see cref="RealizeAsync(string, CancellationToken)"/>
    /// this never materialises, because a temp copy is precisely the wrong thing to rename or delete: the user would see
    /// success and the original would be untouched. An in-archive path is returned unchanged so it fails at the OS call
    /// exactly as it always has. A file on a location is named by the location's path, though it came by the Windows
    /// folder showing it (a paste from Explorer, a cut from here), so the change goes through the location — that folder
    /// would lose what Linux keeps.
    /// </summary>
    public string RealForMutation(string path)
        => files.IsOnLocation(path) ? files.TryToVirtual(path) ?? path : files.TryResolveReal(path) ?? path;

    public string[] RealForMutation(IEnumerable<string> paths) => [.. paths.Select(RealForMutation)];

    /// <summary>The path as Windows names it where Windows reaches it — a mount's real folder, a location's Windows
    /// folder — else as given: what a clipboard or a drag names a file by, so a plan compares like with like.</summary>
    public string AsWindowsNames(string path) => files.TryResolveReal(path) ?? path;
}
