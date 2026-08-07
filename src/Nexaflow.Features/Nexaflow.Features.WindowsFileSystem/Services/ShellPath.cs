using Nexaflow.IO.Common;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nexaflow.Features.WindowsFileSystem.Services;

/// <summary>
/// Turns a browser path into one Windows can actually use — for the clipboard, a drag-out, a shell verb,
/// or a properties dialog. Everything leaving Nexaflow for the OS goes through here.
/// <para>
/// A mounted path maps straight onto its real file (no copy). An in-archive path has no real file at all,
/// so it is materialised to a temp copy — good enough to hand to Explorer or a viewer, which is why
/// clipboard and drag work from inside a zip. Actions that need the file's <i>neighbours</i> must not
/// rely on that: they declare <c>IFileAction.RequiresFullyBackedPath</c> and are withheld instead.
/// </para>
/// </summary>
internal static class ShellPath
{
    /// <summary>A real, launchable path. Returns the input unchanged if nothing can be produced, so
    /// callers fail the way they always did rather than on a null.</summary>
    public static string Realize(string path)
    {
        if (File.Exists(path) || Directory.Exists(path)) return path;
        try { return VirtualFileSystem.Instance.MaterializeFile(path); }
        catch { return path; }
    }

    public static string[] Realize(IEnumerable<string> paths) => [.. paths.Select(Realize)];

    /// <summary>
    /// The real path to <i>mutate</i> — rename, delete, or paste into. Unlike <see cref="Realize"/> this
    /// never materialises, because a temp copy is precisely the wrong thing to rename or delete: the user
    /// would see success and the original would be untouched. An in-archive path is returned unchanged so
    /// it fails at the OS call exactly as it always has, rather than silently mutating a temp file.
    /// </summary>
    public static string RealForMutation(string path)
        => VirtualFileSystem.Instance.TryResolveReal(path) ?? path;

    public static string[] RealForMutation(IEnumerable<string> paths) => [.. paths.Select(RealForMutation)];
}
