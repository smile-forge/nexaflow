using Nexaflow.Features.Common;
using Nexaflow.Features.WindowsFileSystem.Services;
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
        => ToLocalPath(obj) is { } p && (File.Exists(p) || Directory.Exists(p));

    public void Handle(object obj)
    {
        if (ToLocalPath(obj) is not { } path) return;

        if (Directory.Exists(path))
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
