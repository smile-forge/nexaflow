using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Services;
using Nexaflow.IO.Common;
using System.Collections.Generic;
using System.IO;

namespace Nexaflow.Features.WindowsFileSystem;

/// <summary>
/// Lets any feature open a file/folder the way the file list does, without referencing this
/// feature. Discovered by reflection and dispatched via
/// <see cref="IShellServices.HandleObject"/>: a string path to an existing file opens with its
/// default action (<see cref="DefaultFileOpener"/>); a directory opens a File Explorer tab there.
/// </summary>
public sealed class FileSystemObjectHandler(
    IShellServices shell,
    IAIService ai,
    IReadOnlyDictionary<Type, IFeatureConfig> configs) : IGenericObjectHandler
{
    public bool CanHandleObject(object obj)
        => ToLocalPath(obj) is { } p &&
           (File.Exists(p) || Directory.Exists(p) || VirtualFileSystem.Instance.Exists(p));

    public void Handle(object obj)
    {
        if (ToLocalPath(obj) is not { } path) return;

        // Open a file-browser tab only when browsing-as-a-folder is the default: a real directory, a folder
        // *inside* a container, or a container (real or nested) that expands in place — a real archive. A
        // container that keeps its own viewer (.docx, .eml) falls through to DefaultFileOpener, matching the
        // file list's double-click (FileSystemViewModel.OpenEntry). The old blanket IsDirectory check did
        // not, so a .docx/.eml opened via HandleObject wrongly got a folder tab.
        if (OpensAsFolder(VirtualFileSystem.Instance, path))
        {
            shell.OpenTab("FileSystem", new()
            {
                ["mode"]  = "path",
                ["path"]  = path,
                ["label"] = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar,
                                                          Path.AltDirectorySeparatorChar)) is { Length: > 0 } n
                            ? n : path,
            });
            return;
        }

        // Same registry the file list uses (shared per shell), so the action set matches.
        var registry = FileSystemFeatureRegistry.For(shell, ai, configs);
        // Fire-and-forget: we're on the UI thread (a clicked link); no view to refresh here.
        _ = new DefaultFileOpener(registry).OpenAsync(path);
    }

    /// <summary>
    /// Whether <paramref name="path"/> should open as a file-browser tab rather than fall through to the
    /// default file opener. True for a real directory, a container (real or nested) that expands in place
    /// (a real archive), or a plain directory inside a container. A container that keeps its own viewer
    /// (<c>.docx</c>, <c>.eml</c>) is false — governed solely by <see cref="DefaultFileOpener.ExpandsInPlace"/>,
    /// never by the directory flag, because <see cref="IVirtualFileSystem.GetEntryInfo"/> reports a nested
    /// container's root as a directory and would otherwise fold a <c>.eml</c>/<c>.docx</c> attachment into a
    /// folder tab instead of its viewer.
    /// </summary>
    internal static bool OpensAsFolder(IVirtualFileSystem vfs, string path)
    {
        if (Directory.Exists(path)) return true;
        bool containerLike =
            vfs.IsContainer(path) ||
            (vfs.IsContainerName(Path.GetFileName(path)) && vfs.Exists(path));
        return containerLike
            ? DefaultFileOpener.ExpandsInPlace(path)
            : vfs.GetEntryInfo(path) is { IsDirectory: true };
    }

    /// <summary>Accepts either a raw path or a <c>file:</c> URI (post-it links carry the latter),
    /// returning the local file-system path; null for anything else (e.g. http URLs).</summary>
    private static string? ToLocalPath(object obj)
    {
        if (obj is not string s || string.IsNullOrWhiteSpace(s)) return null;
        if (Uri.TryCreate(s, UriKind.Absolute, out var u))
            return u.IsFile ? u.LocalPath : null;   // a real URL (http, etc.) is not ours
        return s;                                   // a bare path
    }
}
