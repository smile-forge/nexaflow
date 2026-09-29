using System.IO;
using Nexaflow.IO.Common;

namespace Nexaflow.Features.Hdf5;

/// <summary>
/// A path that may continue past an HDF5 file into it — <c>D:\data\run.h5\measurements\temperature</c> — split into
/// the file and the object, so a tab can be opened on a file at one of its objects.
/// </summary>
internal static class Hdf5Location
{
    public static readonly string[] Extensions = [".h5", ".hdf5", ".he5"];

    public static bool HasHdf5Extension(string path) =>
        Extensions.Any(e => path.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The file and the object path inside it (null when the path is the file itself). The first segment named
    /// like an HDF5 file that is a file — on disk or inside an archive — is the boundary, so a group that happens
    /// to be called <c>x.h5</c> stays inside its file.
    /// </summary>
    public static (string File, string? Object) Split(string path, IVirtualFileSystem vfs)
    {
        if (string.IsNullOrEmpty(path)) return (path, null);
        var parts = path.Split('\\', '/');
        for (int i = 0; i < parts.Length; i++)
        {
            if (!HasHdf5Extension(parts[i])) continue;
            if (i == parts.Length - 1) return (path, null);
            var file = string.Join('\\', parts[..(i + 1)]);
            if (IsFile(file, vfs)) return (file, "/" + string.Join('/', parts[(i + 1)..].Where(p => p.Length > 0)));
        }
        return (path, null);
    }

    private static bool IsFile(string path, IVirtualFileSystem vfs)
    {
        if (File.Exists(path)) return true;
        if (Directory.Exists(path)) return false;
        try { return vfs.GetEntryInfo(path) is { IsDirectory: false }; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { return false; }
    }
}
