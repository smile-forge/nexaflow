using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.IO.Common;
using Nexaflow.Localization;
using System.IO;
using System.Threading.Tasks;

namespace Nexaflow.Features.WindowsFileSystem;

/// <summary>
/// Lets any feature open a file/folder the way the file list does, without referencing this
/// feature. Discovered by reflection and dispatched via
/// <see cref="IShellServices.HandleObject"/>: a path to an existing file opens with its
/// default action (<see cref="DefaultFileOpener"/>); a directory opens a File Explorer tab there.
/// <para>
/// A path is claimed by its shape — rooted on this machine, one the file system names (<c>::id\…</c>), or a location's
/// address (<c>sftp://…</c>) — because the shell asks every handler in turn, synchronously, and on a location whether
/// anything is there is a round trip. The open asks, and says so when nothing is.
/// </para>
/// </summary>
public sealed class FileSystemObjectHandler(
    IShellServices shell,
    IVirtualFileSystem vfs,
    FileSystemFeatureRegistry registry) : IGenericObjectHandler
{
    public bool CanHandleObject(object obj) => ToPath(obj is FileViewRequest view ? view.Path : obj) is not null;

    public void Handle(object obj)
    {
        var view = obj as FileViewRequest;
        if (ToPath(view?.Path ?? obj) is { } path) _ = OpenAsync(path, onlyToView: view is not null);
    }

    /// <summary>Opens <paramref name="path"/> as a double-click in the file list would. Nothing awaits a handled object,
    /// so a failure is shown here, naming the path, rather than thrown.</summary>
    private async Task OpenAsync(string path, bool onlyToView)
    {
        var shown = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) is { Length: > 0 } n
            ? n : path;
        try
        {
            if (!await vfs.ExistsAsync(path))
            {
                shell.ShowError(Str.Format("WindowsFileSystem.Navigate.PathNotFoundFormat", path));
                return;
            }

            // Open a file-browser tab only when browsing-as-a-folder is the default: a real directory, a folder
            // *inside* a container, or a container (real or nested) that expands in place — a real archive. A
            // container that keeps its own viewer (.docx, .eml) falls through to DefaultFileOpener, matching the
            // file list's double-click (FileSystemViewModel.OpenEntry).
            if (await OpensAsFolderAsync(vfs, path))
            {
                shell.OpenTab("FileSystem", new()
                {
                    ["mode"]  = "path",
                    ["path"]  = path,
                    ["label"] = shown,
                });
                return;
            }

            // The registry the file list uses — the runtime's one — so the action set matches.
            var opener = new DefaultFileOpener(registry);
            if (!onlyToView) await opener.OpenAsync(path);
            else if (!await opener.OpenInViewerAsync(path))
                shell.ShowError(Str.Format("WindowsFileSystem.Open.NoViewerFormat", shown));
        }
        catch (Exception ex)
        {
            shell.ShowError(Str.Format("WindowsFileSystem.Open.FailedFormat", shown, ex.Message));
        }
    }

    /// <summary>
    /// Whether <paramref name="path"/> should open as a file-browser tab rather than fall through to the
    /// default file opener. True for a folder, a container (real or nested) that expands in place
    /// (a real archive), or a plain directory inside a container. A container that keeps its own viewer
    /// (<c>.docx</c>, <c>.eml</c>) is false — governed solely by <see cref="DefaultFileOpener.ExpandsInPlace"/>,
    /// never by the directory flag, because the file system reports a nested container's root as a directory and
    /// would otherwise fold a <c>.eml</c>/<c>.docx</c> attachment into a folder tab instead of its viewer.
    /// </summary>
    internal static async Task<bool> OpensAsFolderAsync(IVirtualFileSystem vfs, string path)
    {
        bool containerLike =
            await vfs.IsContainerAsync(path) ||
            (vfs.IsContainerName(Path.GetFileName(path)) && await vfs.ExistsAsync(path));
        return containerLike
            ? DefaultFileOpener.ExpandsInPlace(path)
            : await vfs.GetEntryAsync(path) is { IsDirectory: true };
    }

    /// <summary>The path an object names, by its shape alone: a raw rooted path, a <c>file:</c> URI (post-it links carry
    /// the latter), a path in the file system's own namespace, or a location's address. Null for anything else — an
    /// http URL, a relative name.</summary>
    private string? ToPath(object obj)
    {
        if (obj is not string s || string.IsNullOrWhiteSpace(s)) return null;
        if (s.StartsWith("::", StringComparison.Ordinal)) return s;
        if (vfs.TryParseDisplayPath(s) is { Length: > 0 } onLocation) return onLocation;
        // A location's Windows folder opens the location, whose names, links and delete are its own.
        if (vfs.IsOnLocation(s) && vfs.TryToVirtual(s) is { Length: > 0 } byFolder) return byFolder;
        if (Uri.TryCreate(s, UriKind.Absolute, out var u))
            return u.IsFile ? u.LocalPath : null;   // a real URL (http, etc.) is not ours
        return Path.IsPathFullyQualified(s) ? s : null;
    }
}
